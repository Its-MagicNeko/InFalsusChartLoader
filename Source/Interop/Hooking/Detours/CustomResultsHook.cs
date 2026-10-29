using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`TryGetResult` — a song's record.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte ReadResultFn(IntPtr results, IntPtr songInfo, byte difficulty,
                                           IntPtr result, IntPtr methodInfo);

        /// <summary>The wide `TryGetResult` overload.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte ReadResultFullFn(IntPtr results, IntPtr songInfo, byte difficulty,
                                               IntPtr score, IntPtr shiny, IntPtr perfect, IntPtr far,
                                               IntPtr miss, IntPtr lamp, IntPtr rank, IntPtr clear,
                                               IntPtr methodInfo);

        /// <summary>`TryUpdate` — the merge and the only record write.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int UpdateResultFn(IntPtr results, IntPtr songInfo, IntPtr newResult,
                                            IntPtr existingScore, IntPtr methodInfo);

        private static NativeHook<ReadResultFn> _readResult;
        private static ReadResultFn _readResultTramp;
        private static NativeHook<ReadResultFullFn> _readResultFull;
        private static ReadResultFullFn _readResultFullTramp;
        private static NativeHook<UpdateResultFn> _updateResult;
        private static UpdateResultFn _updateResultTramp;

        /// <summary>`TryUpdate`'s "nothing was recorded" answer.</summary>
        private const int NoChange = 2;

        /// <summary>`UpdateEncounterResult` — the encounter result's only writer.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte UpdateEncounterFn(IntPtr results, IntPtr newResult, IntPtr methodInfo);

        private static NativeHook<UpdateEncounterFn> _updateEncounter;
        private static UpdateEncounterFn _updateEncounterTramp;

        private static bool InstallCustomResults()
        {
            try
            {
                IntPtr klass = FieldResolver.ClassPointer("GameResultsV4");

                IntPtr read = CustomResults.MethodOn(klass, "TryGetResult", 3);
                IntPtr readFull = CustomResults.MethodOn(klass, "TryGetResult", 10);
                IntPtr update = CustomResults.MethodOn(klass, "TryUpdate", 3);

                IntPtr readCode = read == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)read;
                IntPtr readFullCode = readFull == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)readFull;
                IntPtr updateCode = update == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)update;

                if (readCode == IntPtr.Zero || readFullCode == IntPtr.Zero || updateCode == IntPtr.Zero)
                {
                    Diagnostics.Error("GameResultsV4.TryGetResult/TryUpdate could not be found in this " +
                                      "build; custom scores will go into the game's own save file");
                    return false;
                }

                byte[] readPrologue = Prologue(readCode);
                _readResult = new NativeHook<ReadResultFn>
                {
                    Target = readCode,
                    Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, IntPtr, byte>)
                        &ReadResultDetour,
                };
                _readResult.Attach();
                _readResultTramp = _readResult.Trampoline;
                Diagnostics.Info("GameResultsV4.TryGetResult(song) hooked");

                byte[] readFullPrologue = Prologue(readFullCode);
                _readResultFull = new NativeHook<ReadResultFullFn>
                {
                    Target = readFullCode,
                    Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, IntPtr, IntPtr,
                        IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, byte>)&ReadResultFullDetour,
                };
                _readResultFull.Attach();
                _readResultFullTramp = _readResultFull.Trampoline;
                Diagnostics.Info("GameResultsV4.TryGetResult(song, full) hooked");

                byte[] updatePrologue = Prologue(updateCode);
                _updateResult = new NativeHook<UpdateResultFn>
                {
                    Target = updateCode,
                    Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int>)
                        &UpdateResultDetour,
                };
                _updateResult.Attach();
                _updateResultTramp = _updateResult.Trampoline;
                Diagnostics.Info("GameResultsV4.TryUpdate hooked");

                IntPtr encounterClass = FieldResolver.ClassPointer("EncounterResults");

                IntPtr encounter = CustomResults.MethodOn(encounterClass, "UpdateEncounterResult", 1);
                IntPtr encounterCode = encounter == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)encounter;
                bool encounterOk = false;

                if (encounterCode == IntPtr.Zero)
                {
                    Diagnostics.Error("EncounterResults.UpdateEncounterResult could not be found in this " +
                                      "build; a custom song's encounter result will be written into the " +
                                      "game's own save");
                }
                else
                {
                    byte[] encounterPrologue = Prologue(encounterCode);
                    _updateEncounter = new NativeHook<UpdateEncounterFn>
                    {
                        Target = encounterCode,
                        Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>)
                            &UpdateEncounterDetour,
                    };
                    _updateEncounter.Attach();
                    _updateEncounterTramp = _updateEncounter.Trampoline;
                    Diagnostics.Info("EncounterResults.UpdateEncounterResult hooked");
                    encounterOk = Landed(Hook.EncounterResult, encounterCode, encounterPrologue);
                }

                return Landed(Hook.ResultRead, readCode, readPrologue)
                     & Landed(Hook.ResultReadFull, readFullCode, readFullPrologue)
                     & Landed(Hook.ResultUpdate, updateCode, updatePrologue)
                     & encounterOk;
            }
            catch (Exception e)
            {
                Diagnostics.Warn("the custom result hooks could not be installed: " + Diagnostics.Describe(e));
                return false;
            }
        }

        private static void DetachCustomResults()
        {
            _updateEncounter?.Detach();
            _updateEncounter = null;
            _updateEncounterTramp = null;

            _updateResult?.Detach();
            _updateResult = null;
            _updateResultTramp = null;

            _readResultFull?.Detach();
            _readResultFull = null;
            _readResultFullTramp = null;

            _readResult?.Detach();
            _readResult = null;
            _readResultTramp = null;
        }

        /// <summary>Answers a song's record from the mod's table when the song is ours.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte ReadResultDetour(IntPtr results, IntPtr songInfo, byte difficulty,
                                             IntPtr result, IntPtr methodInfo)
        {
            CustomResults.Reads++;

            IntPtr table = results;
            if (!Faulted)
            {
#if DEBUG
                long t0 = Now;
#endif
                try
                {
                    if (CustomResults.IsOurs(songInfo))
                    {
                        if (!CustomResults.Ready) return 0;
                        CustomResults.Served++;
                        table = CustomResults.Table;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.ResultRead, e);
                }
#if DEBUG
                TicksRead += Now - t0;
#endif
            }

            return _readResultTramp(table, songInfo, difficulty, result, methodInfo);
        }

        /// <summary>The wide read, substituted the same way.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte ReadResultFullDetour(IntPtr results, IntPtr songInfo, byte difficulty,
                                                 IntPtr score, IntPtr shiny, IntPtr perfect, IntPtr far,
                                                 IntPtr miss, IntPtr lamp, IntPtr rank, IntPtr clear,
                                                 IntPtr methodInfo)
        {
            CustomResults.Reads++;

            IntPtr table = results;
            if (!Faulted)
            {
#if DEBUG
                long t0 = Now;
#endif
                try
                {
                    if (CustomResults.IsOurs(songInfo))
                    {
                        if (!CustomResults.Ready) return 0;
                        CustomResults.Served++;
                        table = CustomResults.Table;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.ResultReadFull, e);
                }
#if DEBUG
                TicksRead += Now - t0;
#endif
            }

            return _readResultFullTramp(table, songInfo, difficulty, score, shiny, perfect, far, miss,
                                        lamp, rank, clear, methodInfo);
        }

        /// <summary>Merges a custom song's result into the mod's table and saves it.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int UpdateResultDetour(IntPtr results, IntPtr songInfo, IntPtr newResult,
                                              IntPtr existingScore, IntPtr methodInfo)
        {
            CustomResults.Updates++;

            bool ours = false;
            IntPtr table = results;
            if (!Faulted)
            {
                try
                {
                    if (CustomResults.IsOurs(songInfo))
                    {
                        if (!CustomResults.Ready) return NoChange;
                        ours = true;
                        table = CustomResults.Table;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.ResultUpdate, e);
                }
            }

            int outcome = _updateResultTramp(table, songInfo, newResult, existingScore, methodInfo);

            if (ours)
            {
                CustomResults.Merged++;
                CustomResults.Save();
            }

            return outcome;
        }

        /// <summary>Drops the encounter result of a custom play.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte UpdateEncounterDetour(IntPtr results, IntPtr newResult, IntPtr methodInfo)
        {
            CustomResults.EncounterWrites++;

            if (!Faulted)
            {
                try
                {
                    bool ours = false;
                    IntPtr playing = IntPtr.Zero;
#if DEBUG
                    string seen = "no payload";
#endif
                    if (Selection.TryReadSong(out playing))
                    {
                        ours = CustomResults.IsOurs(playing);
#if DEBUG
                        seen = $"song {Memory.U16(playing + Offsets.Song.Id)}, {(ours ? "ours" : "not ours")}";
#endif
                    }

                    if (ours)
                    {
                        CustomResults.EncounterDropped++;
#if DEBUG
                        Diagnostics.Info($"the encounter result of '{JacketCatalog.NameOf(playing) ?? "?"}' " +
                                         "was not written (a custom song keeps no encounter record)");
#endif
                        return 0;
                    }

#if DEBUG
                    Diagnostics.Info($"the encounter result was written; the settlement's song: {seen}");
#endif
                }
                catch (Exception e)
                {
                    Fault(Hook.EncounterResult, e);
                }
            }

            return _updateEncounterTramp(results, newResult, methodInfo);
        }
    }
}
