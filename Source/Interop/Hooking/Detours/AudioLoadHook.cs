using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`_ZgA` — the game's per-read handler.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ZgAFn(IntPtr record, IntPtr unused);

        /// <summary>`_DJA` — the name-to-index lookup.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte DjaFn(IntPtr self, IntPtr name, IntPtr index);

        /// <summary>`_hF._bGA(record, result)` — finishes a read this mod served.</summary>
        internal static delegate* unmanaged[Cdecl]<IntPtr, uint, void> CompleteRead;

        private static NativeHook<ZgAFn> _zga;
        private static ZgAFn _zgaTramp;
        private static NativeHook<DjaFn> _dja;
        private static DjaFn _djaTramp;

        private static bool InstallAudioLoad()
        {
            IntPtr zgA = MethodResolver.ByName("_hF", "_ZgA");
            if (zgA == IntPtr.Zero) return false;

            IntPtr bga = MethodResolver.ByName("_hF", "_bGA");
            if (bga == IntPtr.Zero) return false;

            IntPtr dja = MethodResolver.ByName("_BG", "_DJA");
            if (dja == IntPtr.Zero) return false;

            CompleteRead = (delegate* unmanaged[Cdecl]<IntPtr, uint, void>)bga;

            byte[] zgAPrologue = Prologue(zgA);
            _zga = new NativeHook<ZgAFn>
            {
                Target = zgA,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&ZgADetour,
            };
            _zga.Attach();
            _zgaTramp = _zga.Trampoline;
            Diagnostics.Info("_J._hF._ZgA hooked");

            byte[] djaPrologue = Prologue(dja);
            _dja = new NativeHook<DjaFn>
            {
                Target = dja,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>)&DjaDetour,
            };
            _dja.Attach();
            _djaTramp = _dja.Trampoline;
            Diagnostics.Info("_BG._DJA hooked");
            return Landed(Hook.AudioRead, zgA, zgAPrologue) & Landed(Hook.AudioName, dja, djaPrologue);
        }

        private static void DetachAudioLoad()
        {
            _dja?.Detach();
            _dja = null;
            _djaTramp = null;

            _zga?.Detach();
            _zga = null;
            _zgaTramp = null;
            CompleteRead = null;
        }

        /// <summary>Serves this mod's audio reads and passes every other read through untouched.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void ZgADetour(IntPtr record, IntPtr unused)
        {
#if DEBUG
            ZgaCalls++;
#endif

            if (!Faulted)
            {
                try
                {
#if DEBUG
                    PendingReads.Observe(record);
#endif
                    if (AudioCatalog.TryServe(record))
                    {
#if DEBUG
                        ZgaServed++;
#endif
                        return;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.AudioRead, e);
                }
            }

            _zgaTramp(record, unused);
        }

        /// <summary>Answers for this mod's audio names.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte DjaDetour(IntPtr self, IntPtr name, IntPtr index)
        {
#if DEBUG
            DjaCalls++;
#endif

            if (!Faulted)
            {
                try
                {
                    if (AudioCatalog.TryResolve(name, index))
                    {
#if DEBUG
                        DjaHits++;
#endif
                        return 1;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.AudioName, e);
                }
            }

            return _djaTramp(self, name, index);
        }
    }
}
