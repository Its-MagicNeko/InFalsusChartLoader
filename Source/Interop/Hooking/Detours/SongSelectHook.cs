using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
#if DEBUG
        /// <summary>Selections applied.</summary>
        internal static long ApplyCalls;

        /// <summary>Times a difficulty change or a chart reload forced the pictures to reload.</summary>
        internal static long ApplyReloaded;
#endif

        /// <summary>The `_MN` delegate.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte ApplyFn(IntPtr self, ushort songId, byte difficulty, int transition,
                                      IntPtr methodInfo);

        private static NativeHook<ApplyFn> _apply;
        private static ApplyFn _applyTramp;

        private static bool InstallSongSelect()
        {
            IntPtr target = MethodResolver.ByRuntime("SongSelectScene", "_MN");
            if (target == IntPtr.Zero)
            {
                Diagnostics.Warn("SongSelectScene._MN could not be resolved; a song with a picture per " +
                                 "difficulty will keep showing the one it was first drawn with");
                return false;
            }

            byte[] prologue = Prologue(target);
            _apply = new NativeHook<ApplyFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, ushort, byte, int, IntPtr, byte>)&MnDetour,
            };
            _apply.Attach();
            _applyTramp = _apply.Trampoline;
            Diagnostics.Info("SongSelectScene._MN hooked");
            return Landed(Hook.SongSelectApply, target, prologue);
        }

        private static void DetachSongSelect()
        {
            _apply?.Detach();
            _apply = null;
            _applyTramp = null;
        }

        /// <summary>Records the selection and widens a difficulty change to a picture reload.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte MnDetour(IntPtr self, ushort songId, byte difficulty, int transition,
                                     IntPtr methodInfo)
        {
#if DEBUG
            ApplyCalls++;
            SelectionRestoreProbe.Applied(self, $"apply before transition={transition}", songId, difficulty);
#endif

            bool shadowed = false;
            if (!Faulted)
            {
                try
                {
                    bool changed = Selection.Applying(self, songId, difficulty);

                    SongCatalog.ShowIllustrator(songId, difficulty);

                    shadowed = Selection.ShadowUnlessSongChanged(self, songId, changed);
#if DEBUG
                    if (shadowed) ApplyReloaded++;
#endif
                }
                catch (Exception e)
                {
                    Fault(Hook.SongSelectApply, e);
                }
            }

            byte applied = _applyTramp(self, songId, difficulty, transition, methodInfo);

            if (shadowed) Selection.Unshadow(self, songId);
#if DEBUG
            SelectionRestoreProbe.Applied(self, $"apply after result={applied}", songId, difficulty);
#endif

            return applied;
        }

        /// <summary>`_NH._UoA` — the state of one difficulty of one song.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte DifficultyStateFn(IntPtr save, IntPtr access, IntPtr songInfo,
                                                byte difficulty, byte flags, IntPtr methodInfo);

        private static NativeHook<DifficultyStateFn> _state;
        private static DifficultyStateFn _stateTramp;

        /// <summary>Difficulty questions asked, and how many of them were about this mod's songs.</summary>
#if DEBUG
        internal static long DifficultyAsked, DifficultyOurs;
#endif

        private static bool InstallDifficultyState()
        {
            IntPtr target = MethodResolver.ByRuntime("_NH", "_UoA");
            if (target == IntPtr.Zero)
            {
                Diagnostics.Error("_NH._UoA: no method by that name in this build; a custom song's " +
                                  "difficulties will follow the game's own locks, and a fresh save may " +
                                  "leave them locked");
                return false;
            }

            byte[] prologue = Prologue(target);
            _state = new NativeHook<DifficultyStateFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte, byte, IntPtr, byte>)&DifficultyDetour,
            };
            _state.Attach();
            _stateTramp = _state.Trampoline;
            Diagnostics.Info("_NH._UoA hooked");
            return Landed(Hook.DifficultyState, target, prologue);
        }

        private static void DetachDifficultyState()
        {
            _state?.Detach();
            _state = null;
            _stateTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte DifficultyDetour(IntPtr save, IntPtr access, IntPtr songInfo, byte difficulty,
                                             byte flags, IntPtr methodInfo)
        {
#if DEBUG
            DifficultyAsked++;
#endif

            if (!Faulted)
            {
                try
                {
                    if (Ours(songInfo, difficulty, out ChartInfo info, out int index))
                    {
#if DEBUG
                        DifficultyOurs++;
#endif

                        return info.NoChart[index] ? (byte)0 : (byte)2;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.DifficultyState, e);
                }
            }

            return _stateTramp(save, access, songInfo, difficulty, flags, methodInfo);
        }

        /// <summary>Whether the song is this mod's, and which difficulty the flag names.</summary>
        private static bool Ours(IntPtr songInfo, byte difficulty, out ChartInfo info, out int index)
        {
            info = null;
            index = -1;

            if (!Offsets.Ready || !Memory.LooksLikeObject(songInfo)) return false;
            if (!SongCatalog.ById.TryGetValue(Memory.U16(songInfo + Offsets.Song.Id), out info)) return false;

            index = SongCatalog.DifficultyIndexOf(difficulty);
            return index >= 0;
        }
    }
}
