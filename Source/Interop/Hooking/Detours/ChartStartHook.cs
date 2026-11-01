using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`GameScene._rk` — the game hands play over to the running chart.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr RkFn(IntPtr self, IntPtr methodInfo);

        private static NativeHook<RkFn> _rk;
        private static RkFn _rkTramp;

        private static bool InstallChartStart()
        {
            IntPtr target = MethodResolver.ByName("GameScene", "_rk");
            if (target == IntPtr.Zero) return false;

            byte[] prologue = Prologue(target);
            _rk = new NativeHook<RkFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr>)&RkDetour,
            };
            _rk.Attach();
            _rkTramp = _rk.Trampoline;
            Diagnostics.Info("GameScene._rk hooked");
            return Landed(Hook.ChartStart, target, prologue);
        }

        private static void DetachChartStart()
        {
            _rk?.Detach();
            _rk = null;
            _rkTramp = null;
        }

        /// <summary>`GameScene.OnDestroy` — the run is over.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void EndFn(IntPtr self, IntPtr methodInfo);

        private static NativeHook<EndFn> _end;
        private static EndFn _endTramp;

        private static bool InstallGameEnd()
        {
            IntPtr target = MethodResolver.ByRuntime("GameScene", "OnDestroy");
            if (target == IntPtr.Zero) return false;

            byte[] prologue = Prologue(target);
            _end = new NativeHook<EndFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&EndDetour,
            };
            _end.Attach();
            _endTramp = _end.Trampoline;
            Diagnostics.Info("GameScene.OnDestroy hooked");
            return Landed(Hook.GameEnd, target, prologue);
        }

        private static void DetachGameEnd()
        {
            _end?.Detach();
            _end = null;
            _endTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void EndDetour(IntPtr self, IntPtr methodInfo)
        {
            _endTramp(self, methodInfo);

            try
            {
                VideoBackground.Stop();
            }
            catch (Exception e)
            {
                Fault(Hook.GameEnd, e);
            }

            Playing = false;
        }

        private static IntPtr _gameSceneClass;
        private static int _gameSceneField = Offsets.Unresolved;

        /// <summary>Whether the game still has a live <c>GameScene</c> singleton.</summary>
        internal static bool GameSceneAlive()
        {
            if (_gameSceneClass == IntPtr.Zero) _gameSceneClass = FieldResolver.ClassPointer("GameScene");
            if (_gameSceneClass == IntPtr.Zero) return true;

            if (_gameSceneField == Offsets.Unresolved)
            {
                _gameSceneField = FieldResolver.Lookup("GameScene", "_xn");
                if (_gameSceneField < 0) _gameSceneField = -2;
            }
            if (_gameSceneField < 0) return true;

            IntPtr statics = FieldResolver.Statics(_gameSceneClass);
            if (statics == IntPtr.Zero) return true;

            return JacketFactory.IsAlive(Memory.Ptr(statics + _gameSceneField));
        }

        /// <summary>Starts this mod's background clip with the chart.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr RkDetour(IntPtr self, IntPtr methodInfo)
        {
#if DEBUG
            ChartStarts++;
#endif
            Playing = true;
            SongSelect = IntPtr.Zero;

            if (!Faulted)
            {
                try
                {
                    VideoBackground.Restart();
                }
                catch (Exception e)
                {
                    Fault(Hook.ChartStart, e);
                }
            }

            return _rkTramp(self, methodInfo);
        }
    }
}
