using System;

namespace InFalsusChartLoader
{
    /// <summary>The song and difficulty the game is currently on.</summary>
    internal static class Selection
    {
        /// <summary>Where the payload sits inside `SongSelectScene`'s statics.</summary>
        private static int PayloadField = Offsets.Unresolved;

        /// <summary>The payload's song and difficulty fields.</summary>
        private static int PayloadSong = Offsets.Unresolved;
        private static int PayloadDifficulty = Offsets.Unresolved;

        /// <summary>`SongInfo.ChartInfos` — read only to tell a filled payload from a zeroed one.</summary>
        private static int SongInfoCharts => Offsets.Song.Charts;

        /// <summary>The four flags, in difficulty order. The same set the chart records are written with.</summary>
        private static readonly byte[] DifficultyFlags = { 1, 2, 4, 8 };

        private static IntPtr _payload;

        private static bool _fieldsAsked;

        private static bool _fieldsMissing;

        /// <summary>The selected song record and its difficulty, 0..3.</summary>
        internal static bool TryRead(out IntPtr songInfo, out int difficulty)
        {
            songInfo = IntPtr.Zero;
            difficulty = -1;

            if (!Offsets.Ready) return false;

            IntPtr payload = Payload();
            if (payload == IntPtr.Zero) return false;

            int index = IndexOf(Memory.U8(payload + PayloadDifficulty));
            if (index < 0)
            {
#if DEBUG
                Refused(payload, $"the difficulty byte is not one of the four flags");
#endif
                return false;
            }

            IntPtr song = payload + PayloadSong;

            if (!Memory.LooksLikeObject(Memory.Ptr(song + SongInfoCharts)))
            {
#if DEBUG
                Refused(payload, "the selected song has no chart array");
#endif
                return false;
            }

            songInfo = song;
            difficulty = index;
            return true;
        }

        /// <summary>The selected song, without the difficulty check.</summary>
        internal static bool TryReadSong(out IntPtr songInfo)
        {
            songInfo = IntPtr.Zero;

            if (!Offsets.Ready) return false;

            IntPtr payload = Payload();
            if (payload == IntPtr.Zero) return false;

            IntPtr song = payload + PayloadSong;

            if (Memory.U16(song + Offsets.Song.Id) == 0) return false;
            if (!Memory.LooksLikeObject(Memory.Ptr(song + SongInfoCharts))) return false;

            songInfo = song;
            return true;
        }

#if DEBUG
        /// <summary>Logs the payload when a read is refused, once.</summary>
        private static void Refused(IntPtr payload, string why)
        {
            if (_refused) return;
            _refused = true;

            var words = new System.Text.StringBuilder();
            for (int i = 0; i < 12; i++)
            {
                if (i > 0) words.Append(' ');
                words.Append(Memory.I64(payload + PayloadSong + i * 8).ToString("X16"));
            }

            Diagnostics.Info($"probe: selection payload 0x{payload.ToInt64():X} turned the read down: {why}");
            Diagnostics.Info($"probe:   from +0x{PayloadSong:X}  {words}");
            Diagnostics.Info($"probe:   +0x{PayloadSong:X} id={Memory.I64(payload + PayloadSong)} " +
                             $"name='{Memory.Text(Memory.Ptr(payload + PayloadSong + Offsets.Song.BaseName), 128) ?? "(not a string)"}' " +
                             $"charts=0x{Memory.Ptr(payload + PayloadSong + SongInfoCharts).ToInt64():X} " +
                             $"diff-bytes={Memory.U8(payload + PayloadDifficulty):X2} " +
                             $"{Memory.U8(payload + PayloadDifficulty + 1):X2} " +
                             $"{Memory.U8(payload + PayloadDifficulty + 2):X2} " +
                             $"{Memory.U8(payload + PayloadDifficulty + 3):X2} (as a byte, then as an int)");
        }

        private static bool _refused;
#endif

        private static int SelectedSong = Offsets.Unresolved;

        /// <summary>`SongSelectScene._Sr` — the difficulty selected on the scene.</summary>
        private static int SceneDifficulty = Offsets.Unresolved;

        private static int SaveContainer = Offsets.Unresolved;
        private static int SaveObject = Offsets.Unresolved;
        private static int SaveState = Offsets.Unresolved;
        private static int SaveDifficulty = Offsets.Unresolved;
        private static IntPtr _saveClass;
        private static bool _saveMissing;

        private static IntPtr _songSelectClass;

        // ---- the selection the game is applying, taken from the call that applies it ----

        private static int _applied = -1;

#if DEBUG
        private static ushort _appliedSong;
#endif

        /// <summary>Records the selection and answers whether its difficulty changed.</summary>
        internal static bool Applying(IntPtr scene, ushort songId, byte difficultyFlag)
        {
            if (IndexOf(difficultyFlag) < 0) return false;   // not one of the four: whatever was applied last stands

            bool changed = ApplyingDifficulty(difficultyFlag);
#if DEBUG
            _appliedSong = songId;
#endif

            if (Memory.LooksLikeObject(scene)) Hooks.SongSelect = scene;

            return changed;
        }

        /// <summary>Records a difficulty being applied and answers whether it changed.</summary>
        internal static bool ApplyingDifficulty(byte difficultyFlag)
        {
            int index = IndexOf(difficultyFlag);
            if (index < 0) return false;

            bool changed = index != _applied;
            _applied = index;
            return changed;
        }

        /// <summary>The value the selected-song field is given to force a reload.</summary>
        private static ushort Shadow(ushort songId) => (ushort)(songId ^ 0xFFFF);

        /// <summary>Makes the game reload the pictures for a difficulty change.</summary>
        internal static bool ShadowUnlessSongChanged(IntPtr scene, ushort songId, bool difficultyChanged) =>
            difficultyChanged && ForcePictureReload(scene, songId);

        /// <summary>Makes the game's next selection apply reload the pictures it loaded before.</summary>
        internal static bool ForcePictureReload(IntPtr scene, ushort songId)
        {
            if (!Memory.LooksLikeObject(scene)) return false;
            if (!Fields()) return false;
            if (Memory.U16(scene + SelectedSong) != songId) return false;

            Memory.WriteU16(scene + SelectedSong, Shadow(songId));
            return true;
        }

        /// <summary>Restores the selected-song field.</summary>
        internal static void Unshadow(IntPtr scene, ushort songId)
        {
            if (!Memory.LooksLikeObject(scene)) return;
            if (!Fields()) return;
            if (Memory.U16(scene + SelectedSong) != Shadow(songId)) return;

            Memory.WriteU16(scene + SelectedSong, songId);
        }

#if DEBUG
        /// <summary>The song the last applied selection named. Reported by the probe.</summary>
        internal static int AppliedSong => _appliedSong;

        /// <summary>The difficulty it named, 0..3, or -1 before the first one. Reported by the probe.</summary>
        internal static int AppliedDifficulty => _applied;
#endif

        /// <summary>The selected difficulty, 0..3, or -1.</summary>
        internal static int Difficulty()
        {
            if (_applied >= 0) return _applied;

            int saved = FromSave();
            if (saved >= 0) return saved;

            if (TryRead(out IntPtr _, out int difficulty)) return difficulty;

            return FromScene();
        }

        /// <summary>The difficulty stored in the save.</summary>
        private static int FromSave()
        {
            if (_saveMissing) return -1;

            if (_saveClass == IntPtr.Zero) _saveClass = FieldResolver.ClassPointer("_NH");
            if (_saveClass == IntPtr.Zero) return -1;

            IntPtr statics = FieldResolver.Statics(_saveClass);
            if (statics == IntPtr.Zero) return -1;

            IntPtr save = Memory.Ptr(statics);
            if (!Memory.LooksLikeObject(save)) return -1;

            if (SaveContainer == Offsets.Unresolved)
            {
                SaveState = FieldResolver.Field("SingleFileSaveDataV2", "GeneralSaveState");
                SaveDifficulty = FieldResolver.Field("GeneralSaveStateV5", "LastSelectedDifficulty");
                SaveContainer = FieldResolver.Field("_NH", "_DEb");

                if (SaveState < 0 || SaveDifficulty < 0 || SaveContainer < 0)
                {
                    _saveMissing = true;
                    Diagnostics.Error("the save's difficulty could not be read by name in this build; " +
                                      "which difficulty is selected will not be known before the " +
                                      "song select has been visited");
                    return -1;
                }
            }

            IntPtr container = Memory.Ptr(save + SaveContainer);
            if (!Memory.LooksLikeObject(container)) return -1;

            if (SaveObject == Offsets.Unresolved)
            {
                SaveObject = FieldOn(container, "_iEb");
                if (SaveObject < 0) { _saveMissing = true; return -1; }
            }

            IntPtr data = Memory.Ptr(container + SaveObject);
            if (!Memory.LooksLikeObject(data)) return -1;

            return IndexOf(Memory.U8(data + SaveState + SaveDifficulty));
        }

        /// <summary>A field's offset on an instance's own class.</summary>
        private static int FieldOn(IntPtr instance, string name)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(instance);
            if (klass == IntPtr.Zero) { Diagnostics.Error($"{name}: no class to ask in this build"); return -1; }

            IntPtr field = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, name);
            if (field == IntPtr.Zero) { Diagnostics.Error($"{name}: no field by that name in this build"); return -1; }

            int offset = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field);
            if (offset <= 0) { Diagnostics.Error($"{name}: no field by that name in this build"); return -1; }

            return offset;
        }

        /// <summary>The difficulty on the song-select scene.</summary>
        private static int FromScene()
        {
            IntPtr scene = Hooks.SongSelect;
            if (scene == IntPtr.Zero) return -1;

            if (_songSelectClass == IntPtr.Zero)
                _songSelectClass = FieldResolver.ClassPointer("SongSelectScene");
            if (_songSelectClass == IntPtr.Zero) return -1;

            if (Memory.Ptr(scene) != _songSelectClass) return -1;

            if (!Fields()) return -1;

            return IndexOf(Memory.U8(scene + SceneDifficulty));
        }

        /// <summary>Resolves the five offsets this file reads.</summary>
        private static bool Fields()
        {
            if (_fieldsMissing) return false;
            if (_fieldsAsked) return true;

            IntPtr klass = FieldResolver.ClassPointer("SongSelectScene");
            if (klass == IntPtr.Zero) return false;

            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return false;

            PayloadSong = FieldResolver.Field("_nG", "_LYA");
            PayloadDifficulty = FieldResolver.Field("_nG", "_mYA");
            SelectedSong = FieldResolver.Field("SongSelectScene", "_sr");
            SceneDifficulty = FieldResolver.Field("SongSelectScene", "_Sr");
            PayloadField = FieldResolver.Field("SongSelectScene", "_xr");

            if (PayloadSong < 0 || PayloadDifficulty < 0 || SelectedSong < 0 ||
                SceneDifficulty < 0 || PayloadField < 0)
            {
                _fieldsMissing = true;
                Diagnostics.Error("SongSelectScene's payload could not be read by name in this build; " +
                                  "which difficulty is selected will not be known, so a song with a " +
                                  "picture per difficulty will not show one");
                return false;
            }

            _fieldsAsked = true;
            _payload = statics + PayloadField;
            return true;
        }

        /// <summary>The address of the payload, or zero.</summary>
        private static IntPtr Payload() => Fields() ? _payload : IntPtr.Zero;

        /// <summary>A `ChartDifficultyFlag` as an index into the four difficulty slots, or -1.</summary>
        private static int IndexOf(byte flag)
        {
            for (int i = 0; i < DifficultyFlags.Length; i++)
                if (DifficultyFlags[i] == flag) return i;
            return -1;
        }
    }
}
