#if DEBUG
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    /// <summary>Observes saved selection restoration and custom-song registration.</summary>
    internal static unsafe class SelectionRestoreProbe
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StartFn(IntPtr self, IntPtr methodInfo);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ushort ReadFn(IntPtr self, ushort pack, IntPtr methodInfo);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void WriteFn(IntPtr self, ushort pack, ushort song, IntPtr methodInfo);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte ExistsFn(IntPtr self, ushort song, IntPtr methodInfo);

        private static NativeHook<StartFn> _startHook;
        private static NativeHook<ReadFn> _readHook;
        private static NativeHook<WriteFn> _writeHook;
        private static NativeHook<ExistsFn> _existsHook;
        private static StartFn _start;
        private static ReadFn _read;
        private static WriteFn _write;
        private static ExistsFn _exists;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PackFn(IntPtr self, ushort pack, IntPtr methodInfo);

        private static NativeHook<PackFn> _packHook;
        private static PackFn _pack;
        private static long _sequence;
        private static int _starting;

        /// <summary>Installs selection-restoration observers.</summary>
        internal static void Install()
        {
            Attach("SongSelectScene", "Start",
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&Start,
                out _startHook, out _start);
            Attach("_NH", "_zNA",
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, ushort, IntPtr, ushort>)&Read,
                out _readHook, out _read);
            Attach("_NH", "_ZNA",
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, ushort, ushort, IntPtr, void>)&Write,
                out _writeHook, out _write);
            Attach("_NH", "_hoA",
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, ushort, IntPtr, void>)&Pack,
                out _packHook, out _pack);
            Attach("SongData", "_CpA",
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, ushort, IntPtr, byte>)&Exists,
                out _existsHook, out _exists);
        }

        private static void Attach<T>(string type, string name, IntPtr detour,
                                      out NativeHook<T> hook, out T trampoline) where T : Delegate
        {
            hook = null;
            trampoline = null;
            try
            {
                IntPtr target = MethodResolver.ByRuntime(type, name);
                if (target == IntPtr.Zero)
                {
                    Report($"cannot observe {type}.{name}");
                    return;
                }
                hook = new NativeHook<T> { Target = target, Detour = detour };
                hook.Attach();
                trampoline = hook.Trampoline;
                Report($"observing {type}.{name} trampoline={trampoline != null}");
            }
            catch (Exception e) { Report($"install {type}.{name}: {Diagnostics.Describe(e)}"); }
        }

        /// <summary>Removes selection-restoration observers.</summary>
        internal static void Uninstall()
        {
            _packHook?.Detach();
            _existsHook?.Detach();
            _writeHook?.Detach();
            _readHook?.Detach();
            _startHook?.Detach();
        }

        /// <summary>Records a registration boundary and the custom identities assigned there.</summary>
        internal static void Snapshot(string boundary)
        {
            try
            {
                Report($"{boundary} registered={ModCore.Registered} imported={ModCore.HasImported} " +
                       $"offsets={Offsets.Ready} customSongs={SongCatalog.ById.Count} " +
                       $"customPacks={SongCatalog.CustomPackCount}");
                foreach (var pair in SongCatalog.ById)
                    Report($"mapping song={pair.Key} base='{pair.Value.BaseName}' " +
                           $"pack={pair.Value.Pack?.PackId} packIdentity='{pair.Value.Pack?.Id}'");
            }
            catch (Exception e) { Report($"snapshot failed: {Diagnostics.Describe(e)}"); }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Start(IntPtr self, IntPtr methodInfo)
        {
            _starting++;
            Snapshot("Start before");
            SaveState(IntPtr.Zero, "Start before");
            _start(self, methodInfo);
            SaveState(IntPtr.Zero, "Start after");
            try
            {
                int selected = FieldResolver.Field("SongSelectScene", "_sr");
                Report($"Start after sceneSong={(selected < 0 ? -1 : Memory.U16(self + selected))}");
                Snapshot("Start after");
            }
            catch (Exception e) { Report($"Start observation failed: {Diagnostics.Describe(e)}"); }
            _starting--;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static ushort Read(IntPtr self, ushort pack, IntPtr methodInfo)
        {
            Report($"saved song read before pack={pack} starting={_starting} registered={ModCore.Registered}");
            ushort song = _read(self, pack, methodInfo);
            Report($"saved song read after pack={pack} song={song} {Identity(song)}");
            return song;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Write(IntPtr self, ushort pack, ushort song, IntPtr methodInfo)
        {
            Report($"saved song write before pack={pack} song={song} starting={_starting} " +
                   $"registered={ModCore.Registered} {Identity(song)}");
            SaveState(self, $"song setter before pack={pack} song={song}");
            _write(self, pack, song, methodInfo);
            SaveState(self, $"song setter after pack={pack} song={song}");
            Report($"saved song write after pack={pack} song={song}");
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte Exists(IntPtr self, ushort song, IntPtr methodInfo)
        {
            if (_starting > 0)
                Report($"song exists before song={song} registered={ModCore.Registered} {Identity(song)}");
            byte answer = _exists(self, song, methodInfo);
            if (_starting > 0) Report($"song exists after song={song} answer={answer}");
            return answer;
        }

        /// <summary>Records the applied selection alongside the save's current selection.</summary>
        internal static void Applied(IntPtr scene, string boundary, ushort song, byte difficulty)
        {
            try
            {
                int selected = FieldResolver.Field("SongSelectScene", "_sr");
                Report($"{boundary} requestedSong={song} difficulty={difficulty} " +
                       $"sceneSong={(selected < 0 ? -1 : Memory.U16(scene + selected))} {Identity(song)}");
                SaveState(IntPtr.Zero, boundary);
            }
            catch (Exception e) { Report($"apply observation failed: {Diagnostics.Describe(e)}"); }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Pack(IntPtr self, ushort pack, IntPtr methodInfo)
        {
            SaveState(self, $"pack setter before requested={pack}");
            _pack(self, pack, methodInfo);
            SaveState(self, $"pack setter after requested={pack}");
        }

        /// <summary>Reads the selection dictionary without creating missing entries.</summary>
        private static void SaveState(IntPtr save, string boundary)
        {
            try
            {
                if (save == IntPtr.Zero)
                {
                    IntPtr klass = FieldResolver.ClassPointer("_NH");
                    IntPtr statics = klass == IntPtr.Zero ? IntPtr.Zero : FieldResolver.Statics(klass);
                    save = statics == IntPtr.Zero ? IntPtr.Zero : Memory.Ptr(statics);
                }
                int containerAt = FieldResolver.Field("_NH", "_DEb");
                int stateAt = FieldResolver.Field("SingleFileSaveDataV2", "GeneralSaveState");
                int packAt = FieldResolver.Field("GeneralSaveStateV5", "LastSelectedPackId");
                int mapAt = FieldResolver.Field("GeneralSaveStateV5", "PackIdToSelectedSongId");
                if (!Memory.LooksLikeObject(save) || containerAt < 0 || stateAt < 0 || packAt < 0 || mapAt < 0)
                {
                    Report($"{boundary} save selection unavailable");
                    return;
                }
                IntPtr container = Memory.Ptr(save + containerAt);
                if (!Memory.LooksLikeObject(container)) { Report($"{boundary} no save container"); return; }
                IntPtr containerClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(container);
                IntPtr field = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(containerClass, "_iEb");
                if (field == IntPtr.Zero) { Report($"{boundary} no save object field"); return; }
                int objectAt = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field);
                IntPtr obj = Memory.Ptr(container + objectAt);
                if (!Memory.LooksLikeObject(obj)) { Report($"{boundary} no save object"); return; }
                IntPtr state = obj + stateAt;
                ushort selectedPack = Memory.U16(state + packAt);
                IntPtr map = Memory.Ptr(state + mapAt);
                Report($"{boundary} savedPack={selectedPack} map=0x{map.ToInt64():X}");
                if (!Memory.LooksLikeObject(map)) return;
                IntPtr mapClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(map);
                IntPtr read = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(mapClass, "TryGetValue", 2);
                if (read == IntPtr.Zero) { Report($"{boundary} no dictionary reader"); return; }
                var packs = new System.Collections.Generic.HashSet<ushort> { selectedPack, ushort.MaxValue };
                foreach (int id in SongCatalog.LiveCustomPackIds()) packs.Add((ushort)id);
                IntPtr* args = stackalloc IntPtr[2];
                foreach (ushort id in packs)
                {
                    ushort key = id;
                    ushort value = 0;
                    args[0] = (IntPtr)(&key);
                    args[1] = (IntPtr)(&value);
                    IntPtr raised = IntPtr.Zero;
                    IntPtr result = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(read, map, (void**)args, ref raised);
                    if (raised != IntPtr.Zero) { Report($"{boundary} dictionary read raised: {Raised.Text(raised)}"); return; }
                    bool found = result != IntPtr.Zero && Memory.U8(result + Offsets.Runtime.BoxedData) != 0;
                    Report($"{boundary} stored pack={key} found={found} song={(found ? value : -1)}");
                }
            }
            catch (Exception e) { Report($"{boundary} save observation failed: {Diagnostics.Describe(e)}"); }
        }

        private static string Identity(ushort song)
        {
            try
            {
                if (SongCatalog.ById.TryGetValue(song, out ChartInfo info))
                    return $"custom='{info.BaseName}' pack={info.Pack?.PackId}";
                return $"not in custom map (count={SongCatalog.ById.Count})";
            }
            catch (Exception e) { return $"identity failed: {Diagnostics.Describe(e)}"; }
        }

        private static void Report(string text) =>
            Diagnostics.Info($"probe: selection restore #{++_sequence} {text}");
    }
}
#endif
