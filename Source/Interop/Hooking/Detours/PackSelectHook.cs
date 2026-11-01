using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`PackVisualMemberLarge._Ef` — the pack screen's whole-screen rebuild.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PackViewFn(IntPtr self, IntPtr layer, IntPtr info, int index, int direction,
                                         IntPtr methodInfo);

        private static NativeHook<PackViewFn> _packView;
        private static PackViewFn _packViewTramp;

        /// <summary>Pack-screen rebuilds seen, and how many were given a fresh custom pack view.</summary>
        internal static long PackSelectCalls, PackViewSwaps;

        private static bool InstallPackSelect()
        {
            if (!SongSelectRefresh.PreparePackSwitch()) return false;

            IntPtr info = SongSelectRefresh.PackRebuildInfo;
            if (info == IntPtr.Zero || *(IntPtr*)info == IntPtr.Zero ||
                Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(info) != 4)
            {
                SongSelectRefresh.PackSwitchMissing();
                return false;
            }

            IntPtr target = *(IntPtr*)info;
            byte[] prologue = Prologue(target);

            _packView = new NativeHook<PackViewFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int, int, IntPtr, void>)&PackViewDetour,
            };
            _packView.Attach();
            _packViewTramp = _packView.Trampoline;
            Diagnostics.Info("PackVisualMemberLarge._Ef hooked");
            return Landed(Hook.PackSelect, target, prologue);
        }

        private static void DetachPackSelect()
        {
            _packView?.Detach();
            _packView = null;
            _packViewTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PackViewDetour(IntPtr self, IntPtr layer, IntPtr info, int index, int direction,
                                           IntPtr methodInfo)
        {
            PackSelectCalls++;

            if (!Faulted)
            {
                try
                {
                    IntPtr fresh = SongSelectRefresh.PackViewFor(layer, info);
                    if (fresh != IntPtr.Zero)
                    {
                        SongSelectRefresh.AdoptPackView(layer, fresh);
                        info = fresh;
                        PackViewSwaps++;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.PackSelect, e);
                }
            }

            _packViewTramp(self, layer, info, index, direction, methodInfo);
        }
    }
}
