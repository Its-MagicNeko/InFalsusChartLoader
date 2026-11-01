using System;

namespace InFalsusChartLoader
{
    /// <summary>The native hooks: which game functions are patched, and what happens when one faults.</summary>
    /// <remarks>Each detour lives in its own file under Detours/; every target is resolved by name.</remarks>
    internal static unsafe partial class Hooks
    {
        /// <summary>Set when a detour throws. Every detour stops calling into the mod after that.</summary>
        internal static bool Faulted { get; private set; }

        internal static int Faults { get; private set; }

#if DEBUG
        internal static long VaCalls, VaServed;
        internal static long DjaCalls, DjaHits;
        internal static long ZgaCalls, ZgaServed;
        internal static long ApACalls, ApAArmed;

        /// <summary>Jacket requests for a custom song that had no picture.</summary>
        internal static long ApaAsked;

        internal static long MiaCalls, MiaClaimed;

        internal static long ZoaCalls, ZoaArmed, LiaCalls, LiaClaimed;

        /// <summary>In-play background requests and how many were armed.</summary>
        internal static long UmaCalls, UmaArmed;

        /// <summary>Background requests for a custom song that could not be answered.</summary>
        internal static long UmaUnset;

        /// <summary>Chart starts.</summary>
        internal static long ChartStarts;
#endif

        /// <summary>Whether a `GameScene` is running; reloads wait until it is gone.</summary>
        internal static bool Playing;

#if DEBUG
        /// <summary>Pauses and resumes of the scene's music.</summary>
        internal static long MusicPauses, MusicResumes;

        internal static long RowCalls, RowRedirects;
        internal static long TicksRow, TicksApa, TicksMia, TicksVc, TicksRead;

        internal static long Now => System.Diagnostics.Stopwatch.GetTimestamp();

        internal static double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
#endif

        /// <summary>How many hooks went in, and how many were tried.</summary>
        internal static int Installed { get; private set; }
        internal static int Attempted { get; private set; }

        internal static void Install()
        {
            int installed = 0;
            int attempted = 0;

            void Count(bool ok) { attempted++; if (ok) installed++; }

            Count(InstallChartLoad());
            Count(InstallChartStart());
            Count(InstallGameEnd());
            Count(InstallPause());
            Count(InstallAudioLoad());
            Count(InstallJacket());
            Count(InstallSongSelect());
            Count(InstallDifficultyState());
            Count(InstallPackVisual());
            Count(InstallPackOwned());
            Count(InstallPackSelect());
            Count(InstallPackList());
            Count(InstallCustomResults());
            Count(InstallResultsUnlock());
            Count(InstallPlayGate());
            Count(InstallSceneSwitch());
            Count(InstallHub());

            Installed = installed;
            Attempted = attempted;

            Diagnostics.Load($"{installed}/{attempted} hooks installed");
            if (installed < attempted)
                Diagnostics.Warn("a hook is missing; whatever it drives will silently do nothing");
        }

        internal static void Uninstall()
        {
            DetachHub();
            DetachSceneSwitch();
            DetachPlayGate();
            DetachResultsUnlock();
            DetachCustomResults();
            DetachPackSelect();
            DetachPackList();
            DetachPackOwned();
            DetachPackVisual();
            DetachDifficultyState();
            DetachSongSelect();
            DetachJacket();
            DetachAudioLoad();
            DetachPause();
            DetachGameEnd();
            DetachChartStart();
            DetachChartLoad();
        }

        /// <summary>The first bytes at a hook target.</summary>
        internal static unsafe byte[] Prologue(IntPtr target, int count = 8)
        {
            if (target == IntPtr.Zero) return null;

            var bytes = new byte[count];
            for (int i = 0; i < count; i++) bytes[i] = *(byte*)(target + i);
            return bytes;
        }

        /// <summary>Whether an attach changed the target.</summary>
        private static unsafe bool Patched(IntPtr target, byte[] prologue)
        {
            if (target == IntPtr.Zero || prologue == null) return false;

            for (int i = 0; i < prologue.Length; i++)
                if (*(byte*)(target + i) != prologue[i]) return true;
            return false;
        }

        /// <summary>The hooks, named for a detour that did not land.</summary>
        internal enum Hook
        {
            ChartLoader,
            AudioRead,
            AudioName,
            JacketReference,
            JacketMaterial,
            ChartReference,
            ChartMaterial,
            Background,
            ChartStart,
            GameEnd,
            MusicPause,
            MusicResume,
            SongSelectApply,
            DifficultyState,
            PackVisual,
            PackOwned,
            PackSelect,
            PackList,
            PackRow,
            ResultRead,
            ResultReadFull,
            ResultUpdate,
            EncounterResult,
            ResultsUnlock,
            PlayGate,
            SceneSwitch,
            HubRefresh,
        }

        /// <summary>Whether a detour landed.</summary>
        private static bool Landed(Hook hook, IntPtr target, byte[] prologue)
        {
            if (Patched(target, prologue)) return true;

#if DEBUG
            NotLanded(hook);
#endif
            return false;
        }

#if DEBUG
        /// <summary>Says which hook did not take.</summary>
        private static void NotLanded(Hook hook)
        {
            Diagnostics.Warn($"{Name(hook)} was attached but its first bytes are unchanged; the detour " +
                             "is not in place, and whatever it answers for will silently do nothing");
        }

        /// <summary>The hooks by name, for the log. Debug only — see <see cref="Hook"/>.</summary>
        private static string Name(Hook hook) => hook switch
        {
            Hook.ChartLoader => "_s._VA",
            Hook.AudioRead => "_J._hF._ZgA",
            Hook.AudioName => "_BG._DJA",
            Hook.JacketReference => "_apA",
            Hook.JacketMaterial => "_MIA",
            Hook.ChartReference => "SongData._ZOA",
            Hook.ChartMaterial => "AddressableHandleAutoReleaser._LIA",
            Hook.Background => "GameplayBackgrounds._UmA",
            Hook.ChartStart => "GameScene._rk",
            Hook.GameEnd => "GameScene.OnDestroy",
            Hook.MusicPause => "_Dg._hiA",
            Hook.MusicResume => "_Dg._KiA",
            Hook.SongSelectApply => "SongSelectScene._MN",
            Hook.DifficultyState => "_NH._UoA",
            Hook.PackVisual => "PackVisualMemberLarge._vc",
            Hook.PackOwned => "_NH._AOA",
            Hook.PackSelect => "PackVisualMemberLarge._Ef",
            Hook.PackList => "PackSelectScene.Awake",
            Hook.PackRow => "the pack row accessor (SongSelectPackAssets[])",
            Hook.ResultRead => "GameResultsV4.TryGetResult(song)",
            Hook.ResultReadFull => "GameResultsV4.TryGetResult(song, full)",
            Hook.ResultUpdate => "GameResultsV4.TryUpdate",
            Hook.EncounterResult => "EncounterResults.UpdateEncounterResult",
            Hook.ResultsUnlock => "DifficultyUnlockContainer._GB",
            Hook.PlayGate => "SongSelectScene._sN",
            Hook.SceneSwitch => "CoreScene._CB.MoveNext",
            Hook.HubRefresh => "HubScene._WK",
            _ => "(unknown hook)",
        };
#endif

        /// <summary>Reports a detour fault once, then stops calling into the mod.</summary>
        private static void Fault(Hook hook, Exception e)
        {
            Faulted = true;
            Faults++;

            if (Faults > 1) return;
            Diagnostics.Error($"{Which(hook)}detour faulted, custom charts disabled: {Diagnostics.Describe(e)}");
        }

        /// <summary>The hook's name in Debug, or an article in Release.</summary>
        private static string Which(Hook hook)
        {
#if DEBUG
            return Name(hook) + " ";
#else
            return "a ";
#endif
        }
    }
}
