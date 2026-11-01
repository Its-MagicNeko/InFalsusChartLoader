using System;

namespace InFalsusChartLoader
{
    /// <summary>The in-play background video: one <c>VideoPlayer</c> rendering into a material.</summary>
    internal static unsafe class VideoBackground
    {
        /// <summary>The render texture size.</summary>
        private const int TextureWidth = 1920;

        private const int TextureHeight = 1080;

        /// <summary>Where the video types live.</summary>
        private const string CoreAssembly = "UnityEngine.CoreModule.dll";

        private const string VideoAssembly = "UnityEngine.VideoModule.dll";

        private const string CoreNamespace = "UnityEngine";

        private const string VideoNamespace = "UnityEngine.Video";

        private static IntPtr _object;      // the GameObject the player lives on
        private static IntPtr _player;      // the VideoPlayer component
        private static IntPtr _texture;     // the RenderTexture it renders into
        private static IntPtr _material;    // the material that shows the texture
        private static string _playing;     // the file the player was last pointed at

        private static bool _armed;
        private static bool _running;
        private static bool _held;
        private static bool _ended;

        /// <summary>Whether the prepared clip still needs its opening frame positioned.</summary>
        private static bool _firstFramePending;

        private static double _heldClock = double.NaN;
        private static bool _rewound;

        private static IntPtr _gameSceneClass;
        private static int _engineField = Offsets.Unresolved;   // GameScene._DN — the live _VD
        private static int _clockField = Offsets.Unresolved;    // _VD._WEA — seconds on the chart
        private static bool _clockMissing;

        private static IntPtr _objectHandle, _playerHandle, _textureHandle, _materialHandle;

        private static bool _unavailable;   // asked once; the answer cannot change

        /// <summary>Prepares the clip and answers the material that shows it.</summary>
        internal static IntPtr Material(string path, out string reason)
        {
            reason = null;

            if (_unavailable) return IntPtr.Zero;

            if (_playing == path && _material != IntPtr.Zero)
            {
                _firstFramePending = true;
                Park(out string parked);

                if (!Show(_texture, out reason)) return IntPtr.Zero;

                _armed = false;
                _running = false;
                _held = false;
                _ended = false;
                return _material;
            }

            try
            {
                if (!Ensure(out reason)) { _unavailable = true; return IntPtr.Zero; }

                if (_playing != null) Stop();

                if (!Set("set_source", 1, 1 /* VideoSource.Url */, out reason)) return IntPtr.Zero;   // a file
                if (!Set("set_url", 1, FileUrl(path), out reason)) return IntPtr.Zero;
                if (!Set("set_renderMode", 1, 2 /* VideoRenderMode.RenderTexture */, out reason)) return IntPtr.Zero;
                if (!Set("set_targetTexture", 1, _texture, out reason)) return IntPtr.Zero;

                if (!Set("set_isLooping", 1, false, out reason)) return IntPtr.Zero;

                if (!Set("set_playOnAwake", 1, false, out reason)) return IntPtr.Zero;

                if (!Set("set_audioOutputMode", 1, 0 /* VideoAudioOutputMode.None */, out reason)) return IntPtr.Zero;

                _firstFramePending = true;
                if (!Call("Prepare", 0, _player, out reason)) return IntPtr.Zero;

                if (!Show(_texture, out reason)) return IntPtr.Zero;

                _playing = path;
                _length = -1.0;                          // the previous clip's length is not this one's
                _armed = false;
                _running = false;
                _held = false;
                _ended = false;
                Diagnostics.Info($"background video '{System.IO.Path.GetFileName(path)}' ready; it starts with the song");
                return _material;
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"the background video could not be prepared: {Diagnostics.Describe(e)}";
#else
                _ = e;
#endif
                return IntPtr.Zero;
            }
        }

        /// <summary>Whether this is the material the clip built.</summary>
        internal static bool Owns(IntPtr material) => material != IntPtr.Zero && material == _material;

        /// <summary>Drops the prepared clip for a file whose bytes changed.</summary>
        internal static void Forget(string path)
        {
            if (_playing != path) return;

            Stop();
            _playing = null;
            _length = -1.0;
        }

        /// <summary>Moves the clip's picture onto a clone of the game's background material.</summary>
        internal static IntPtr Rebase(IntPtr gameMaterial, out string reason)
        {
            reason = null;
            if (_material == IntPtr.Zero)
            {
                reason = "no clip material is loaded";
                return IntPtr.Zero;
            }

            IntPtr clone = JacketFactory.CloneFrom(gameMaterial, out reason);
            if (clone == IntPtr.Zero) return IntPtr.Zero;

            if (!Show(clone, _texture, out reason)) return IntPtr.Zero;

            _material = clone;
            return clone;
        }

        /// <summary>The game has handed the chart over: park the clip on its first frame and arm it.</summary>
        internal static void Restart()
        {
            if (_player == IntPtr.Zero || _playing == null) return;

            _firstFramePending = true;
            string reason = null;
            Park(out reason);

            _armed = true;
            _running = false;
            _held = false;
            _ended = false;
            Diagnostics.Info($"background video '{System.IO.Path.GetFileName(_playing)}' waits for the song");
        }

        /// <summary>Puts the clip back on its first frame and holds it there.</summary>
        private static void Park(out string reason)
        {
            reason = null;
            if (!Flag("get_isPrepared")) return;

            if (!Call("Pause", 0, _player, out reason))
            {
                Diagnostics.Warn($"the background video could not be held for the song: {reason}");
                return;
            }

            if (!Set("set_time", 1, 0.0, out reason))
            {
                Diagnostics.Warn($"the background video could not be placed for the song: {reason}");
                return;
            }

            _firstFramePending = false;
        }

        /// <summary>How far the picture may sit off the chart's clock.</summary>
        private const double ClockSlack = 0.3;

        /// <summary>Beyond this the two are not the same picture.</summary>
        private const double Chase = 6.0;

        /// <summary>How long a placement is given to land.</summary>
        private const long PlaceCooldownMs = 250;

        /// <summary>Said when the clip cannot be placed on the chart clock.</summary>
        private const string PlaceFailed =
            "the background video could not be placed on the chart clock; it will run out of step with the chart";

        private static bool _placeWarned;

        private static double _lastClock = double.NaN;
        private static long _lastPlace = long.MinValue;

        private static double _length = -1.0;

        /// <summary>Said when the clip cannot be started with the song.</summary>
        private const string StartFailed =
            "the background video could not be started with the song; the background will not move with it";

        /// <summary>Keeps the picture on the game's own chart clock.</summary>
        internal static void Follow()
        {
            if (_player == IntPtr.Zero || _playing == null) return;

            if (_firstFramePending)
            {
                Park(out _);
                if (_firstFramePending) return;
            }

            double clock = ChartClock();
            if (double.IsNaN(clock))
            {
                if (_held) _heldClock = double.NaN;
                return;
            }

            bool moved = clock != _lastClock;
            _lastClock = clock;

            if (!_running && !_held && clock < 0.0 && Flag("get_isPlaying"))
            {
                string parked = null;
                Park(out parked);
                return;
            }

            if (!_armed) return;

            if (_held)
            {
                if (double.IsNaN(_heldClock)) return;
                if (!_rewound)
                {
                    if (clock < _heldClock - ClockSlack) _rewound = true;
                    return;
                }

                double held = Time();
                if (held < 0.0) return;
                if (clock < held) return;

                Resume();
                return;
            }

            if (_ended) return;

            if (!_running)
            {
                if (clock >= 0.0) Start(clock);
                return;
            }

            if (FilmOver()) return;

            Place(clock, moved);
        }

        /// <summary>Whether the clip has played out.</summary>
        private static bool FilmOver()
        {
            if (_ended) return true;
            if (_running && !Flag("get_isPlaying"))
            {
                double length = Length();
                double clock = ChartClock();
                if (length > 0.0 && !double.IsNaN(clock) && clock >= length)
                {
                    _ended = true;
                    _running = false;
                    Diagnostics.Info("background video played out; its last frame stays for the song");
                    return true;
                }
            }

            return false;
        }

        /// <summary>The clip's length in seconds, or zero when it cannot be read.</summary>
        private static double Length()
        {
            if (_length >= 0.0) return _length;

            IntPtr method = Entry(_player, "get_length", 0);
            if (method == IntPtr.Zero) return 0.0;

            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, _player, null, ref raised);
            if (raised != IntPtr.Zero || boxed == IntPtr.Zero) return 0.0;

            return _length = *(double*)(boxed + Offsets.Runtime.BoxedData);
        }

        /// <summary>Places the picture on the clock when it has slipped off it.</summary>
        private static void Place(double clock, bool moved = true)
        {
            double at = Time();
            if (at < 0.0) return;

            double off = at - clock;
            if (off <= ClockSlack && off >= -ClockSlack) return;
            if (off > Chase || off < -Chase) return;
            if (!moved) return;

            long now = System.Environment.TickCount64;
            if (now - _lastPlace < PlaceCooldownMs) return;

            string reason = null;
            if (!Set("set_time", 1, clock, out reason))
            {
                _lastPlace = now;
#if DEBUG
                Diagnostics.Warn($"the background video could not be placed on the chart clock: {reason}");
#endif
                if (!_placeWarned)
                {
                    _placeWarned = true;
                    Diagnostics.WarnRelease(PlaceFailed);
                }
                return;
            }

            _lastPlace = now;
            Diagnostics.Info($"background video placed {off:+0.000;-0.000} s onto the chart clock ({at:F3} -> {clock:F3} s)");
        }

        /// <summary>Starts the clip at its first frame, once per chart.</summary>
        private static bool Start(double clock)
        {
            string reason = null;

            Park(out reason);
            if (!Show(_texture, out reason) || !Call("Play", 0, _player, out reason))
            {
                Diagnostics.Warn($"the background video could not be started with the song: {reason}");
                Diagnostics.WarnRelease(StartFailed);
                _armed = false;
                return false;
            }

            _running = true;
#if DEBUG
            Starts++;
#endif
            Diagnostics.Info($"background video started with the song ({clock:F3} s on the chart clock)");
            return true;
        }

        /// <summary>The game's chart clock, or NaN when it cannot be read.</summary>
        private static double ChartClock()
        {
            if (_clockMissing) return double.NaN;

            if (_gameSceneClass == IntPtr.Zero)
            {
                _gameSceneClass = FieldResolver.ClassPointer("GameScene");
                if (_gameSceneClass == IntPtr.Zero) return double.NaN;
            }

            if (_engineField == Offsets.Unresolved)
            {
                _engineField = FieldResolver.Field("GameScene", "_DN");
                _clockField = FieldResolver.Field("_VD", "_WEA");
                if (_engineField < 0 || _clockField < 0)
                {
                    _clockMissing = true;
                    Diagnostics.Error("the chart's clock could not be read by name in this build; " +
                                      "the in-play background video will not play");
                    return double.NaN;
                }
            }

            IntPtr statics = FieldResolver.Statics(_gameSceneClass);
            if (statics == IntPtr.Zero) return double.NaN;

            IntPtr engine = Memory.Ptr(statics + _engineField);
            if (!Memory.LooksLikeObject(engine)) return double.NaN;

            return Memory.F64(engine + _clockField);
        }

#if DEBUG
        /// <summary>How far the picture sits off the chart clock, for the probe.</summary>
        internal static double Skew
        {
            get
            {
                if (_player == IntPtr.Zero || _playing == null) return double.NaN;

                double clock = ChartClock();
                double at = Time();
                return double.IsNaN(clock) || at < 0.0 ? double.NaN : at - clock;
            }
        }
#endif

        /// <summary>Holds the clip where it is when the game pauses its music.</summary>
        internal static void Hold(double at)
        {
            if (_player == IntPtr.Zero || !_running || _held) return;

            string reason = null;
            if (!Call("Pause", 0, _player, out reason))
            {
                Diagnostics.Warn($"the background video could not be held for the pause: {reason}");
                return;
            }

            _held = true;
            _heldClock = ChartClock();
            _rewound = false;
#if DEBUG
            Holds++;
#endif
            Diagnostics.Info("background video held for the pause");

#if DEBUG
            double held = Time();
            Diagnostics.Info(held < 0.0
                ? $"the clip would not say where it was; the song was held at {at:F3} s"
                : $"the clip is at {held:F3} s while the song was held at {at:F3} s");
#endif
        }

        /// <summary>Sets the clip running again after a pause.</summary>
        internal static void Resume(bool atMusicReturn = false)
        {
            if (_player == IntPtr.Zero || !_running || !_held) return;
            if (_ended) return;
            if (atMusicReturn && !double.IsNaN(_heldClock)) return;

            _held = false;

            double clock = ChartClock();
            if (!double.IsNaN(clock)) Place(clock);

            string reason = null;
            if (!Call("Play", 0, _player, out reason))
            {
                Diagnostics.Warn($"the background video could not be resumed: {reason}");
                return;
            }

#if DEBUG
            Resumes++;
#endif
            Diagnostics.Info("background video resumed with the song");
        }

#if DEBUG
        /// <summary>How many times a chart's start set the clip running.</summary>
        internal static long Starts;

        /// <summary>How many times the pause held and resumed the clip.</summary>
        internal static long Holds, Resumes;

        /// <summary>What the player is doing, for the probe.</summary>
        internal static string State
        {
            get
            {
                if (_unavailable) return "fail";
                if (_player == IntPtr.Zero) return "none";
                if (_ended) return "end";

                if (Flag("get_isPlaying")) return "play";
                if (Flag("get_isPrepared")) return "ready";
                return "load";
            }
        }

#endif
        /// <summary>A boolean property of the player.</summary>
        private static bool Flag(string name)
        {
            IntPtr method = Entry(_player, name, 0);
            if (method == IntPtr.Zero) return false;

            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, _player, null, ref raised);
            return raised == IntPtr.Zero && boxed != IntPtr.Zero &&
                   *(byte*)(boxed + Offsets.Runtime.BoxedData) != 0;
        }

        /// <summary>Where the clip is in its own picture, or -1 when it cannot be read.</summary>
        private static double Time()
        {
            IntPtr method = Entry(_player, "get_time", 0);
            if (method == IntPtr.Zero) return -1.0;

            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, _player, null, ref raised);
            if (raised != IntPtr.Zero || boxed == IntPtr.Zero) return -1.0;

            return *(double*)(boxed + Offsets.Runtime.BoxedData);
        }

        /// <summary>Stops the player.</summary>
        internal static void Stop()
        {
            if (_player == IntPtr.Zero) return;

            string reason = null;
            if (!Call("Stop", 0, _player, out reason))
                Diagnostics.Warn($"the background video could not be stopped: {reason}");

            // Clears the source so the file is not held open between songs.
            if (!Set("set_url", 1, string.Empty, out reason))
                Diagnostics.Warn($"the background video's source could not be cleared: {reason}");

            _playing = null;
            _firstFramePending = false;
            _armed = false;
            _running = false;
            _held = false;
            _ended = false;
        }

        /// <summary>Builds the object, player, texture and material, once.</summary>
        private static bool Ensure(out string reason)
        {
            reason = null;
            if (_player != IntPtr.Zero) return true;

            IntPtr gameObjectClass = Require(CoreAssembly, CoreNamespace, "GameObject", out reason);
            if (gameObjectClass == IntPtr.Zero) return false;

            IntPtr videoClass = Require(VideoAssembly, VideoNamespace, "VideoPlayer", out reason);
            if (videoClass == IntPtr.Zero) return false;

            IntPtr textureClass = Require(CoreAssembly, CoreNamespace, "RenderTexture", out reason);
            if (textureClass == IntPtr.Zero) return false;

            IntPtr objectClass = Require(CoreAssembly, CoreNamespace, "Object", out reason);
            if (objectClass == IntPtr.Zero) return false;

            IntPtr go = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(gameObjectClass);
            if (go == IntPtr.Zero)
            {
#if DEBUG
                reason = "the background video's object could not be created";
#endif
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            IntPtr name = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp("InFalsusChartLoader background video");
            args[0] = name;
            if (!Invoke(Entry(go, ".ctor", 1), go, args, 1, out reason)) return false;

            args[0] = go;
            if (!Invoke(EntryOn(objectClass, "DontDestroyOnLoad", 1), IntPtr.Zero, args, 1, out reason)) return false;

            IntPtr type = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_type(videoClass);
            if (type == IntPtr.Zero)
            {
#if DEBUG
                reason = "the background video's type could not be read";
#endif
                return false;
            }

            args[0] = Il2CppInterop.Runtime.IL2CPP.il2cpp_type_get_object(type);
            IntPtr player;
            if (!Invoke(Entry(go, "AddComponent", 1), go, args, 1, out reason)) return false;
            player = LastResult;
            if (!Memory.LooksLikeObject(player))
            {
#if DEBUG
                reason = "the background video's player could not be added";
#endif
                return false;
            }

            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(player);
            if (!IsVideoPlayer(klass, videoClass))
            {
#if DEBUG
                reason = "the component added was not a VideoPlayer";
#endif
                return false;
            }

            IntPtr texture = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(textureClass);
            if (texture == IntPtr.Zero)
            {
#if DEBUG
                reason = "the background video's texture could not be created";
#endif
                return false;
            }

            int width = TextureWidth, height = TextureHeight, depth = 0;
            IntPtr* textureArgs = stackalloc IntPtr[3];
            textureArgs[0] = (IntPtr)(&width);
            textureArgs[1] = (IntPtr)(&height);
            textureArgs[2] = (IntPtr)(&depth);
            if (!Invoke(Entry(texture, ".ctor", 3), texture, textureArgs, 3, out reason)) return false;

            if (!Call("Create", 0, texture, out reason)) return false;

            int built = WidthOf(texture);
            if (built != TextureWidth)
            {
#if DEBUG
                reason = $"the background video's texture reports {built} pixels wide instead of {TextureWidth}";
#endif
                return false;
            }

            IntPtr material = JacketFactory.BuildFromTexture(texture, "background", out reason);
            if (material == IntPtr.Zero) return false;

            _object = go;
            _player = player;
            _texture = texture;
            _material = material;
            Root(ref _objectHandle, go);
            Root(ref _playerHandle, player);
            Root(ref _textureHandle, texture);
            Root(ref _materialHandle, material);

            Diagnostics.Info("the background video player is ready");
            return true;
        }

        /// <summary>How wide a <c>RenderTexture</c> says it is, or -1.</summary>
        private static int WidthOf(IntPtr texture)
        {
            IntPtr getter = Entry(texture, "get_width", 0);
            if (getter == IntPtr.Zero) return -1;

            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(getter, texture, null, ref raised);
            if (raised != IntPtr.Zero || boxed == IntPtr.Zero) return -1;

            return *(int*)(boxed + Offsets.Runtime.BoxedData);
        }

        /// <summary>Whether the component returned is the player that was asked for.</summary>
        private static bool IsVideoPlayer(IntPtr klass, IntPtr wanted)
        {
            for (IntPtr at = klass; at != IntPtr.Zero; at = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_parent(at))
            {
                if (at == wanted) return true;
            }
            return false;
        }

        /// <summary>Holds an object against the collector for the rest of the run.</summary>
        private static void Root(ref IntPtr handle, IntPtr obj)
        {
            if (handle != IntPtr.Zero || obj == IntPtr.Zero) return;
            handle = Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_new(obj, false);
        }

        /// <summary>The last value an invoked call answered with.</summary>
        private static IntPtr LastResult;

        /// <summary>Calls a no-argument method that answers with an object.</summary>
        private static bool Call(string name, int parameters, IntPtr instance, out string reason)
        {
            reason = null;

            IntPtr method = Entry(instance, name, parameters);
            if (method == IntPtr.Zero)
            {
#if DEBUG
                reason = $"VideoPlayer.{name} could not be found";
#endif
                return false;
            }

            IntPtr raised = IntPtr.Zero;
            IntPtr result = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, instance, null, ref raised);
            LastResult = result;
            if (raised == IntPtr.Zero) return true;

#if DEBUG
            reason = $"VideoPlayer.{name} raised: {Raised.Text(raised)}";
#endif
            return false;
        }

        /// <summary>Sets one property of the player.</summary>
        private static unsafe bool Set(string name, int parameters, int value, out string reason)
        {
            int storage = value;
            return SetArgument(name, (IntPtr)(&storage), out reason);
        }

        private static unsafe bool Set(string name, int parameters, double value, out string reason)
        {
            double storage = value;
            return SetArgument(name, (IntPtr)(&storage), out reason);
        }

        private static unsafe bool Set(string name, int parameters, bool value, out string reason)
        {
            byte storage = value ? (byte)1 : (byte)0;
            return SetArgument(name, (IntPtr)(&storage), out reason);
        }

        private static bool Set(string name, int parameters, IntPtr reference, out string reason) =>
            SetArgument(name, reference, out reason);

        private static bool Set(string name, int parameters, string value, out string reason) =>
            SetArgument(name, Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(value), out reason);

        private static unsafe bool SetArgument(string name, IntPtr argument, out string reason)
        {
            reason = null;

            if (_player == IntPtr.Zero)
            {
#if DEBUG
                reason = "there is no background video player";
#endif
                return false;
            }

            IntPtr method = Entry(_player, name, 1);
            if (method == IntPtr.Zero)
            {
#if DEBUG
                reason = $"VideoPlayer.{name} could not be found";
#endif
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = argument;

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, _player, (void**)args, ref raised);
            if (raised == IntPtr.Zero) return true;

#if DEBUG
            reason = $"VideoPlayer.{name} raised: {Raised.Text(raised)}";
#endif
            return false;
        }

        /// <summary>The picture the clip shows, put on the material.</summary>
        private static bool Show(IntPtr material, IntPtr texture, out string reason)
        {
            reason = null;

            if (material == IntPtr.Zero) return false;

            IntPtr method = Entry(material, "set_mainTexture", 1);
            if (method == IntPtr.Zero)
            {
                reason = "Material.set_mainTexture could not be found";
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = texture;

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, material, (void**)args, ref raised);
            if (raised == IntPtr.Zero) return true;

            reason = $"Material.set_mainTexture raised: {Raised.Text(raised)}";
            return false;
        }

        private static bool Show(IntPtr texture, out string reason) => Show(_material, texture, out reason);

        /// <summary>An absolute Windows path as a <c>VideoPlayer</c> URL.</summary>
        private static string FileUrl(string path) => "file:///" + path.Replace('\\', '/');

        /// <summary>A method of an instance's own class.</summary>
        private static IntPtr Entry(IntPtr instance, string name, int parameters)
        {
            if (!Memory.LooksLikeObject(instance)) return IntPtr.Zero;
            return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(instance), name, parameters);
        }

        /// <summary>A method of a class the mod resolved itself.</summary>
        private static IntPtr EntryOn(IntPtr klass, string name, int parameters)
        {
            if (klass == IntPtr.Zero) return IntPtr.Zero;
            return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, name, parameters);
        }

        /// <summary>A Unity class, by assembly and name.</summary>
        private static IntPtr Require(string assembly, string namespaze, string typeName, out string reason)
        {
            reason = null;

            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(assembly, namespaze, typeName);
            if (klass == IntPtr.Zero)
            {
#if DEBUG
                reason = $"the game's {typeName} type could not be found";
#endif
                return IntPtr.Zero;
            }

            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);
            return klass;
        }

        /// <summary>Calls a method through <c>il2cpp_runtime_invoke</c>.</summary>
        private static unsafe bool Invoke(IntPtr method, IntPtr instance, IntPtr* args, int count, out string reason)
        {
            reason = null;

            if (method == IntPtr.Zero)
            {
#if DEBUG
                reason = "a background video method could not be found";
#endif
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (args[i] != IntPtr.Zero) continue;
#if DEBUG
                reason = $"argument {i} could not be built, so the call was not made";
#endif
                return false;
            }

            IntPtr raised = IntPtr.Zero;
            LastResult = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, instance, (void**)args, ref raised);
            if (raised == IntPtr.Zero) return true;

#if DEBUG
            reason = $"a call into the game's video types raised: {Raised.Text(raised)}";
#endif
            return false;
        }
    }
}
