using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>Hides the results screen's unlock-progress container for custom songs.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void UnlockFillFn(IntPtr self, IntPtr before, IntPtr after, IntPtr methodInfo);

        private static NativeHook<UnlockFillFn> _unlockFill;
        private static UnlockFillFn _unlockFillTramp;

        /// <summary>Results-screen unlock fills seen, and how many of them were about a custom song.</summary>
#if DEBUG
        internal static long UnlockFills, UnlockHidden;
#endif

        /// <summary>The difficulty field, the enum member to hide on, and the record size.</summary>
        private static int _unlockDifficulty = -1;
        private static int _unlockNone = -1;
        private static int _unlockSize = -1;

        /// <summary>A bound on the snapshot copied.</summary>
        private const int UnlockSnapshotLimit = 64;

        private static bool InstallResultsUnlock()
        {
            IntPtr target = MethodResolver.ByName("DifficultyUnlockContainer", "_GB");
            if (target == IntPtr.Zero)
            {
                Diagnostics.Error("the results screen's unlock progress will keep showing for custom songs");
                return false;
            }

            _unlockDifficulty = FieldResolver.Field("_lH", "_leb");
            _unlockNone = FieldResolver.EnumValue("_lH._MH", "_Ueb");
            _unlockSize = FieldResolver.ValueSize("_lH");

            if (_unlockDifficulty < 0 || _unlockDifficulty >= _unlockSize ||
                _unlockNone < 0 || _unlockNone > byte.MaxValue ||
                _unlockSize <= 0 || _unlockSize > UnlockSnapshotLimit)
            {
                Diagnostics.Error("the results screen's unlock progress will keep showing for custom songs");
                return false;
            }

            byte[] prologue = Prologue(target);
            _unlockFill = new NativeHook<UnlockFillFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&UnlockFillDetour,
            };
            _unlockFill.Attach();
            _unlockFillTramp = _unlockFill.Trampoline;
            Diagnostics.Info("DifficultyUnlockContainer._GB hooked");
            return Landed(Hook.ResultsUnlock, target, prologue);
        }

        private static void DetachResultsUnlock()
        {
            _unlockFill?.Detach();
            _unlockFill = null;
            _unlockFillTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void UnlockFillDetour(IntPtr self, IntPtr before, IntPtr after, IntPtr methodInfo)
        {
#if DEBUG
            UnlockFills++;
#endif

            if (!Faulted)
            {
                try
                {
                    if (after != IntPtr.Zero &&
                        Selection.TryReadSong(out IntPtr playing) && CustomResults.IsOurs(playing))
                    {
#if DEBUG
                        UnlockHidden++;
#endif

                        byte* hidden = stackalloc byte[UnlockSnapshotLimit];
                        for (int i = 0; i < _unlockSize; i++) hidden[i] = ((byte*)after)[i];
                        hidden[_unlockDifficulty] = (byte)_unlockNone;

                        _unlockFillTramp(self, before, (IntPtr)hidden, methodInfo);
                        return;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.ResultsUnlock, e);
                }
            }

            _unlockFillTramp(self, before, after, methodInfo);
        }
    }
}
