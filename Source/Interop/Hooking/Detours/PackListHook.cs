using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`PackSelectScene.Awake` — the game builds its pack list, entries and card windows.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PackListFn(IntPtr self, IntPtr methodInfo);

        private static NativeHook<PackListFn> _packListHook;
        private static PackListFn _packListTramp;

        /// <summary>Pack screens built.</summary>
        internal static long PackListsBuilt;

        private static bool InstallPackList()
        {
            IntPtr info = MethodResolver.MethodInfoByRuntime("PackSelectScene", "Awake");
            if (info == IntPtr.Zero || *(IntPtr*)info == IntPtr.Zero ||
                Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(info) != 0)
            {
                Diagnostics.Error(SongSelectRefresh.PackListUnavailable);
                return false;
            }

            IntPtr target = *(IntPtr*)info;
            byte[] prologue = Prologue(target);

            _packListHook = new NativeHook<PackListFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&PackListDetour,
            };
            _packListHook.Attach();
            _packListTramp = _packListHook.Trampoline;
            Diagnostics.Info("PackSelectScene.Awake hooked");
            PackScreenRebuild.Prepare(info);
            return Landed(Hook.PackList, target, prologue);
        }

        private static void DetachPackList()
        {
            _packListHook?.Detach();
            _packListHook = null;
            _packListTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PackListDetour(IntPtr self, IntPtr methodInfo)
        {
            Diagnostics.Info("probe: pack list detour begin");
            if (PackSetup.TryGetVisuals(out IntPtr dataAssets))
            {
                int field = FieldResolver.Field("PackSelectScene", "packSelectSceneAssets");
                IntPtr sceneAssets = field >= 0 ? Memory.Ptr(self + field) : IntPtr.Zero;
                Diagnostics.Info($"probe: pack assets data=0x{dataAssets.ToInt64():X} " +
                                 $"scene=0x{sceneAssets.ToInt64():X}");
            }

            if (!Faulted && !PackSetup.SyncSceneVisuals(self))
            {
                Diagnostics.Error("the scene's own visual rows could not be filled; the category layer " +
                                  "was not built");
                return;
            }

            if (PackScreenRebuild.InProgress)
            {
                _packListTramp(self, methodInfo);
                PackListsBuilt++;
                if (!Faulted) PackScreenRebuild.SnapScrolls(self);
                Diagnostics.Info("probe: pack list detour done (rebuild)");
                return;
            }

            if (!Faulted)
            {
                try { SongSelectRefresh.DeselectIfGonePackSelected(); }
                catch (Exception e) { Fault(Hook.PackList, e); }
            }
            Diagnostics.Info("probe: pack list detour deselect done");

            _packListTramp(self, methodInfo);
            PackListsBuilt++;
            Diagnostics.Info("probe: pack list detour awake done");

            if (!Faulted) SongSelectRefresh.NotePackScreenBuilt(SongCatalog.PackScreenSignature());
            Diagnostics.Info("probe: pack list detour note done");

            if (!Faulted) PackScreenRebuild.SnapScrolls(self);
            Diagnostics.Info("probe: pack list detour snap done");
        }
    }
}
