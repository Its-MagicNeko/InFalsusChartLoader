using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    /// <summary>The play-gate and scene-switch probes.</summary>
    internal static unsafe partial class Hooks
    {
        /// <summary>Play-gate calls and how many said yes.</summary>
        internal static long PlayGateCalls, PlayGateYes;

        /// <summary>The song-select scene the gate was last called on.</summary>
        internal static IntPtr SongSelect;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte PlayGateFn(IntPtr self, IntPtr methodInfo);

        private static NativeHook<PlayGateFn> _gate;
        private static PlayGateFn _gateTramp;

        private static bool InstallPlayGate()
        {
            IntPtr target = MethodResolver.ByRuntime("SongSelectScene", "_sN");
            if (target == IntPtr.Zero)
            {
                Diagnostics.Warn("SongSelectScene._sN could not be resolved; whether a song may be " +
                                 "started will not be reported");
                return false;
            }

            byte[] prologue = Prologue(target);
            _gate = new NativeHook<PlayGateFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte>)&PlayGateDetour,
            };
            _gate.Attach();
            _gateTramp = _gate.Trampoline;
            Diagnostics.Info("SongSelectScene._sN hooked");
            return Landed(Hook.PlayGate, target, prologue);
        }

        private static void DetachPlayGate()
        {
            _gate?.Detach();
            _gate = null;
            _gateTramp = null;
        }

        /// <summary>Answers exactly as the game would, and says what the answer was.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte PlayGateDetour(IntPtr self, IntPtr methodInfo)
        {
            byte answer = _gateTramp(self, methodInfo);

            PlayGateCalls++;
            if (answer != 0) PlayGateYes++;
            if (Memory.LooksLikeObject(self)) SongSelect = self;

            Diagnostics.Info($"play gate: _sN -> {answer != 0}");

            return answer;
        }

        /// <summary>`CoreScene._CB.MoveNext` — the scene switch.</summary>
        internal static long SceneSwitchCalls;
        internal static int SceneSwitchLastState = int.MinValue;

        private static bool _releaseReported;

        /// <summary>Says once that the audio a finished scene switch should have released is still held.</summary>
        private static void ReleaseFailed(Exception e)
        {
            if (_releaseReported) return;
            _releaseReported = true;
            Diagnostics.Error("the audio a finished scene switch should have released could not be cleared: " +
                              Diagnostics.Describe(e));
        }

        /// <summary>The object the console switch is waiting on, and its class name.</summary>
        internal static IntPtr SceneSwitchWait;
        internal static string SceneSwitchWaitName;

        /// <summary>The class of an object, by name, or null.</summary>
        private static string ClassNameOf(IntPtr instance)
        {
            try
            {
                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(instance);
                if (klass == IntPtr.Zero) return null;
                return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(klass);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The wait's progress when the switch is waiting on `_Ig`.</summary>
        internal static string WaitProgress()
        {
            if (SceneSwitchWait == IntPtr.Zero) return null;
            if (SceneSwitchWaitName != "_Ig") return SceneSwitchWaitName;

            if (_igDoneByte == int.MinValue) _igDoneByte = FieldResolver.Field("_Ig", "_TwA");
            if (_igDoneByte < 0) return SceneSwitchWaitName;    // reported where it was asked

            return $"_Ig/+0x{_igDoneByte:X}={Memory.U8(SceneSwitchWait + _igDoneByte)}";
        }

        /// <summary>`_J._Ig._TwA` — the byte `keepWaiting` reads, asked once.</summary>
        private static int _igDoneByte = int.MinValue;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte MoveNextFn(IntPtr self, IntPtr methodInfo);

        private static NativeHook<MoveNextFn> _moveNext;
        private static MoveNextFn _moveNextTramp;

        private static bool InstallSceneSwitch()
        {
            IntPtr target = MethodResolver.ByRuntime("_CB", "MoveNext");
            if (target == IntPtr.Zero)
            {
                Diagnostics.Warn("CoreScene._CB.MoveNext could not be resolved; where the scene switch " +
                                 "stops will not be reported");
                return false;
            }

            byte[] prologue = Prologue(target);
            _moveNext = new NativeHook<MoveNextFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte>)&MoveNextDetour,
            };
            _moveNext.Attach();
            _moveNextTramp = _moveNext.Trampoline;
            Diagnostics.Info("CoreScene._CB.MoveNext hooked");
            return Landed(Hook.SceneSwitch, target, prologue);
        }

        private static void DetachSceneSwitch()
        {
            _moveNext?.Detach();
            _moveNext = null;
            _moveNextTramp = null;
        }

        /// <summary>Runs the step, then says which state it is now parked in.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte MoveNextDetour(IntPtr self, IntPtr methodInfo)
        {
            byte more = _moveNextTramp(self, methodInfo);
            SceneSwitchCalls++;

            if (more == 0)
            {
                PackVisual = IntPtr.Zero;
                PackScene = IntPtr.Zero;
                try { AudioCatalog.ReleaseRetired(); }
                catch (Exception e) { ReleaseFailed(e); }
                try { PackVisuals.ReleaseRetired(); }
                catch (Exception e) { ReleaseFailed(e); }
            }

            if (!Memory.LooksLikeObject(self)) return more;

            int state = Memory.I32(self + StateField);
            if (state != SceneSwitchLastState)
            {
                SceneSwitchLastState = state;
                Diagnostics.Info($"scene switch: parked at state {state}{(more == 0 ? " (finished)" : "")}");
            }

            IntPtr current = more == 0 ? IntPtr.Zero : Memory.Ptr(self + CurrentField);
            if (current == IntPtr.Zero)
            {
                SceneSwitchWait = IntPtr.Zero;
                SceneSwitchWaitName = null;
            }
            else if (current != SceneSwitchWait)
            {
                SceneSwitchWait = current;
                SceneSwitchWaitName = ClassNameOf(current);
                Diagnostics.Info($"scene switch: waiting on {SceneSwitchWaitName ?? "(unnamed)"} " +
                                 $"at 0x{current.ToInt64():X}");
            }

            return more;
        }

        /// <summary>`&lt;&gt;2__current` — the object a generated coroutine is yielding.</summary>
        private const int CurrentField = Offsets.Runtime.Coroutine.Current;

        /// <summary>`&lt;&gt;1__state` — a generated coroutine's state.</summary>
        private const int StateField = Offsets.Runtime.Coroutine.State;

    }
}
