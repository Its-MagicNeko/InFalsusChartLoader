using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`_S._VA` — the chart loader.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr VaFn(IntPtr result, IntPtr name, IntPtr methodInfo);

        private static NativeHook<VaFn> _va;
        private static VaFn _vaTramp;

        private static bool InstallChartLoad()
        {
            IntPtr target = MethodResolver.ByName("_s", "_VA");
            if (target == IntPtr.Zero) return false;

            byte[] prologue = Prologue(target);
            _va = new NativeHook<VaFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr>)&VaDetour,
            };
            _va.Attach();
            _vaTramp = _va.Trampoline;
            Diagnostics.Info("_s._VA hooked");
            return Landed(Hook.ChartLoader, target, prologue);
        }

        private static void DetachChartLoad()
        {
            _va?.Detach();
            _va = null;
            _vaTramp = null;
        }

        /// <summary>Serves this mod's charts and passes everything else through.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr VaDetour(IntPtr result, IntPtr name, IntPtr methodInfo)
        {
#if DEBUG
            VaCalls++;
#endif

            if (!Faulted)
            {
                try
                {
                    if (ChartCatalog.TryServe(name, result))
                    {
#if DEBUG
                        VaServed++;
#endif
                        return result;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.ChartLoader, e);
                }
            }

            return _vaTramp(result, name, methodInfo);
        }
    }
}
