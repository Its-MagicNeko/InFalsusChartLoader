using System;
using System.Collections.Generic;

namespace InFalsusChartLoader
{
    /// <summary>Debug-only reporting for one run.</summary>
    internal static class Probe
    {
        private static int SongInfoSize => Offsets.Song.Size;
        private static int SongInfoId => Offsets.Song.Id;
        private static int SongInfoBaseName => Offsets.Song.BaseName;
        private static int SongInfoCharts => Offsets.Song.Charts;
        private static int SongInfoTitleReading => Offsets.Song.TitleReading;
        private static int SongInfoArtistReading => Offsets.Song.ArtistReading;

        // ---------------------------------------------------------------- one: reaching the game

        /// <summary>What the mod found when it went looking for the game.</summary>
        internal static void Startup(string chartsFolder, bool codecReady, bool assetsReady,
                                     int installed, int attempted)
        {
            line($"charts folder   {chartsFolder ?? "<none>"}");
            line($"charts folder exists? {chartsFolder != null && System.IO.Directory.Exists(chartsFolder)}");
            line($"chart decoder   {(codecReady ? "reached" : "NOT REACHED")}");
            line($"asset manager   {(assetsReady ? "reached" : "NOT REACHED")}");
            line($"hooks           {installed}/{attempted}");
            line($"offsets         {FieldResolver.Stats()}");

            long baseAddress = GameAssembly.Base.ToInt64();
            line($"game assembly   0x{baseAddress:X}" +
                 (baseAddress == 0 ? "  <-- NOT FOUND; no hook can work" : ""));
        }

        // ---------------------------------------------------------------- two: what was imported

        /// <summary>What each folder decided, and what the ones that were kept produced.</summary>
        internal static void Imported(ChartInfo info, int audioIndex, int audioBytes, int jacketCount)
        {
            line($"kept '{Path(info)}' as '{info.BaseName}', audio index {audioIndex} ({audioBytes} bytes), " +
                 $"jackets built {jacketCount}");
            line($"  name='{info.Name}' composer='{info.Composer}' illust={Slots(info.Illustrators)}");
            line($"  levels={Slots(info.Level)} charters={Slots(info.Charters)}");
            line($"  preview={info.PreviewStart}..{info.PreviewEnd}s");
            line($"  audio={System.IO.Path.GetFileName(info.SongPath)} jackets={Jackets(info)} " +
                 $"background={Backgrounds(info)}");
        }

        /// <summary>The four jacket file names and whether they are one file or several.</summary>
        private static string Jackets(ChartInfo info)
        {
            var names = new List<string>(ChartInfo.Difficulties);
            foreach (string path in info.JacketPaths) names.Add(System.IO.Path.GetFileName(path));

            int distinct = new HashSet<string>(names, StringComparer.Ordinal).Count;
            return string.Join(", ", names) + (distinct == 1 ? "  (one file)" : $"  ({distinct} files)");
        }

        /// <summary>The four backgrounds by file name, with <c>-</c> where none is named.</summary>
        private static string Backgrounds(ChartInfo info)
        {
            if (info.BackgroundPaths == null) return "(none; the game's own)";

            var names = new string[ChartInfo.Difficulties];
            for (int d = 0; d < names.Length; d++)
            {
                string path = info.BackgroundPaths[d];
                names[d] = path == null ? "-" : System.IO.Path.GetFileName(path);
            }
            return string.Join("/", names);
        }

        /// <summary>One per-difficulty key's four values, with <c>-</c> for one left out.</summary>
        private static string Slots(string[] values)
        {
            if (values == null) return "(none)";

            var text = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
                text[i] = string.IsNullOrEmpty(values[i]) ? "-" : values[i];
            return string.Join("/", text);
        }

        /// <summary>As above, for the difficulty ratings.</summary>
        private static string Slots(int?[] values)
        {
            if (values == null) return "(none)";

            var text = new string[values.Length];
            for (int i = 0; i < values.Length; i++) text[i] = values[i]?.ToString() ?? "-";
            return string.Join("/", text);
        }

        // ---------------------------------------------------------------- three: the tables

        /// <summary>The sizes everything is about to be written into.</summary>
        internal static void Before(IntPtr songData, IntPtr packData, IntPtr packAssets)
        {
            line("--- before ---");
            line($"resolve         DataAccess ok, SongData=0x{songData.ToInt64():X} " +
                 $"PackData=0x{packData.ToInt64():X} PackAssets=0x{packAssets.ToInt64():X}");
            line($"sizes           {Sizes(songData, packData, packAssets)}");
        }

        /// <summary>The sizes afterwards, and what was written.</summary>
        internal static void After(IntPtr songData, IntPtr packData, IntPtr packAssets,
                                   List<ChartInfo> charts)
        {
            line("--- after ---");
            line($"sizes           {Sizes(songData, packData, packAssets)}");

            IntPtr songs = ArrayAt(songData, "SongData", "allSongInfo");
            int count = Length(songs);
            line($"allSongInfo     {count} entries");

            for (int i = 0; i < charts.Count; i++)
            {
                int index = count - charts.Count + i;
                if (index < 0) break;
                DumpSong(songs, index, charts[i]);
            }

            DumpPack(packData);
            DumpPackVisuals(packAssets);
        }

        /// <summary>The counters, printed on a timer.</summary>
        internal static string Status() =>
            $"va={Hooks.VaCalls}/{Hooks.VaServed} dja={Hooks.DjaCalls}/{Hooks.DjaHits} " +
            $"zga={Hooks.ZgaCalls}/{Hooks.ZgaServed} apa={Hooks.ApACalls}/{Hooks.ApAArmed} " +
            $"askedNoPicture={Hooks.ApaAsked} mia={Hooks.MiaCalls}/{Hooks.MiaClaimed} " +
            $"zoa={Hooks.ZoaCalls}/{Hooks.ZoaArmed} lia={Hooks.LiaCalls}/{Hooks.LiaClaimed} " +
            $"uma={Hooks.UmaCalls}/{Hooks.UmaArmed} " +
            $"gate={Hooks.PlayGateCalls}/{Hooks.PlayGateYes} " +
            $"state={Hooks.DifficultyAsked}/{Hooks.DifficultyOurs} " +
            $"unlock={Hooks.UnlockFills}/{Hooks.UnlockHidden} " +
            $"apply={Hooks.ApplyCalls}/{Hooks.ApplyReloaded} " +
            $"diff={Selection.AppliedDifficulty} song={Selection.AppliedSong} " +
            $"pack={Hooks.PackDifficultyCalls}/{Hooks.PackCardsRebuilt} pv={Hooks.PackVisualRebuilds} " +
            $"packs={SongCatalog.CustomPackCount} " +
            $"ps={Hooks.PackSelectCalls}/{Hooks.PackViewSwaps} " +
            $"switch={Hooks.SceneSwitchCalls}@{Hooks.SceneSwitchLastState}" +
            (Hooks.WaitProgress() is string waiting ? $" wait={waiting}" : "") + " " +
            $"jackets={JacketCatalog.Asked}/{JacketCatalog.Claimed} " +
            $"backgrounds={BackgroundCatalog.Count} cs={Hooks.ChartStarts} " +
            $"vid={VideoBackground.State} vidN={VideoBackground.Starts} " +
            $"vidskew={VideoBackground.Skew:F3} " +
            $"hold={VideoBackground.Holds}/{VideoBackground.Resumes} " +
            $"reads={AudioCatalog.Served}/{AudioCatalog.ServedBytes} " +
            $"ifcl={CustomResults.Reads}/{CustomResults.Served} " +
            $"upd={CustomResults.Updates}/{CustomResults.Merged} saved={CustomResults.Saves} " +
            $"late={CustomResults.Late} " +
            $"enc={CustomResults.EncounterWrites}/{CustomResults.EncounterDropped} " +
            $"keyfix={ResultKeys.Refiled} " +
            $"outstanding={PendingReads.Outstanding()} skip={PendingReads.Skipped} " +
            $"released={PendingReads.Released} noread={PendingReads.NotTaken} " +
            $"ourms row={Hooks.Ms(Hooks.TicksRow):F1} apa={Hooks.Ms(Hooks.TicksApa):F1} " +
            $"mia={Hooks.Ms(Hooks.TicksMia):F1} vc={Hooks.Ms(Hooks.TicksVc):F1} " +
            $"read={Hooks.Ms(Hooks.TicksRead):F1} rowN={Hooks.RowCalls}/{Hooks.RowRedirects}";

        // ---------------------------------------------------------------- reading it back

        private static string Sizes(IntPtr songData, IntPtr packData, IntPtr packAssets)
        {
            return $"allSongInfo={Length(ArrayAt(songData, "SongData", "allSongInfo"))} " +
                   $"_ffb={Length(ArrayAt(songData, "SongData", "_ffb"))} " +
                   $"_Efb={Count(DictAt(songData, "SongData", "_Efb"))} " +
                   $"packInfo={Length(ArrayAt(packData, "PackData", "PackInfo"))} " +
                   $"_fYA={Count(DictAt(packData, "PackData", "_fYA"))} " +
                   $"packToAssets={Length(ArrayAt(packAssets, "PackSelectSceneAssets", "packToAssets"))}";
        }

        private static void DumpSong(IntPtr songs, int index, ChartInfo expected)
        {
            IntPtr song = songs + Offsets.Runtime.ArrayDataOffset + index * SongInfoSize;

            int id = Memory.I32(song + SongInfoId) & 0xFFFF;
            string baseName = Memory.Text(Memory.Ptr(song + SongInfoBaseName), 128);
            IntPtr chartArray = Memory.Ptr(song + SongInfoCharts);
            IntPtr title = Memory.Ptr(song + SongInfoTitleReading);
            IntPtr artist = Memory.Ptr(song + SongInfoArtistReading);

            line($"song[{index}]       id={id} base='{baseName}' charts={Length(chartArray)} " +
                 $"readings={(title == IntPtr.Zero ? "null" : "set")} " +
                 $"artist(ptr)={(artist == IntPtr.Zero ? "null" : "set")} " +
                 $"preview={Memory.F32(song + Offsets.Song.PreviewStart)}.." +
                 $"{Memory.F32(song + Offsets.Song.PreviewEnd)}s " +
                 $"reward={Memory.I32(song + Offsets.Song.RewardStyle)}");

            if (baseName != expected.BaseName)
                line($"  !! base name is '{baseName}', expected '{expected.BaseName}'");

            // One difficulty is enough to prove the shape; the other three are copies of it.
            if (chartArray != IntPtr.Zero && Length(chartArray) > 0)
            {
                IntPtr chart = chartArray + Offsets.Runtime.ArrayDataOffset;
                line($"  chart[0]      id='{Memory.Text(Memory.Ptr(chart + Offsets.Chart.Id), 128)}' " +
                     $"available={Memory.U8(chart + Offsets.Chart.Available)} " +
                     $"difficulty={Memory.U8(chart + Offsets.Chart.Difficulty)} " +
                     $"designer='{Memory.Text(Memory.Ptr(chart + Offsets.Chart.Designer), 128)}' " +
                     $"rating={Memory.I32(chart + Offsets.Chart.Rating)} " +
                     $"section='{Memory.Text(Memory.Ptr(chart + Offsets.Chart.Section), 16)}'");
            }
        }

        private static void DumpPack(IntPtr packData)
        {
            IntPtr packs = ArrayAt(packData, "PackData", "PackInfo");
            int count = Length(packs);
            int stride = Stride(packs, count);

            for (int i = 0; i < count && i < 12; i++)
            {
                IntPtr pack = packs + Offsets.Runtime.ArrayDataOffset + i * stride;
                line($"pack[{i}]         id={Memory.U16(pack + Offsets.Pack.Id)} " +
                     $"slug='{Memory.Text(Memory.Ptr(pack + Offsets.Pack.Slug), 64)}' " +
                     $"songs={Length(Memory.Ptr(pack + Offsets.Pack.Songs))}");
            }
        }

        private static void DumpPackVisuals(IntPtr packAssets)
        {
            if (!Memory.LooksLikeObject(packAssets)) { line("pack visuals    NOT REACHED"); return; }

            IntPtr table = ArrayAt(packAssets, "PackSelectSceneAssets", "packToAssets");
            int count = Length(table);
            int stride = Stride(table, count);

            line($"pack visuals    assets=0x{packAssets.ToInt64():X} table=0x{table.ToInt64():X} " +
                 $"{count} rows of {stride} bytes");

            int backing = FieldResolver.Lookup("SongSelectPackAssets", "BackingImageNonCompleted");

            for (int i = 0; i < count; i++)
            {
                IntPtr row = table + Offsets.Runtime.ArrayDataOffset + i * stride;
                line($"  row[{i}]        at=0x{row.ToInt64():X} first=0x{Memory.Ptr(row).ToInt64():X} " +
                     $"backing={(backing < 0 ? "(no field)" : $"0x{Memory.Ptr(row + backing).ToInt64():X}")}");
            }
        }

        /// <summary>An array's element size, or 0.</summary>
        private static int Stride(IntPtr array, int count)
        {
            if (!Memory.LooksLikeObject(array) || count <= 0) return 0;

            try
            {
                return (int)(Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(array) / count);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static IntPtr ArrayAt(IntPtr obj, string type, string field)
        {
            if (!Memory.LooksLikeObject(obj)) return IntPtr.Zero;
            int offset = FieldResolver.Lookup(type, field);
            if (offset < 0) { line($"  !! {type}.{field} was not found by name"); return IntPtr.Zero; }
            return Memory.Ptr(obj + offset);
        }

        private static IntPtr DictAt(IntPtr obj, string type, string field) => ArrayAt(obj, type, field);

        private static int Length(IntPtr array) =>
            Memory.LooksLikeObject(array) ? Memory.I32(array + Offsets.Runtime.ArrayLength) : -1;

        private static int Count(IntPtr dictionary) =>
            Memory.LooksLikeObject(dictionary) ? Memory.I32(dictionary + Offsets.Runtime.DictCount) : -1;

        private static string Path(ChartInfo info) => System.IO.Path.GetFileName(info.Folder);

        private static void line(string text) => Diagnostics.Info("probe: " + text);
    }
}
