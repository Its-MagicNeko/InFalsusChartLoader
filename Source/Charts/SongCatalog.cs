using System;
using System.Collections.Generic;

namespace InFalsusChartLoader
{
    /// <summary>Adds the custom songs to the game's song and pack tables.</summary>
    internal static unsafe class SongCatalog
    {
        private static int SongDataAllSongs => Offsets.SongData.AllSongs;
        private static int SongDataAvailability => Offsets.SongData.Availability;

        /// <summary>`BitArray.m_array` and `m_length`.</summary>
        private const int BitArrayArray = Offsets.Runtime.BitArrayArray;
        private const int BitArrayLength = Offsets.Runtime.BitArrayLength;

        /// <summary>Grows the game's per-song `BitArray` to cover <paramref name="count"/> songs.</summary>
        private static bool GrowAvailability(IntPtr songData, int count)
        {
            IntPtr bits = Memory.Ptr(songData + SongDataAvailability);
            if (!Memory.LooksLikeObject(bits))
            {
                Diagnostics.Warn("SongData has no availability bits");
                return false;
            }

            int oldLength = Memory.I32(bits + BitArrayLength);
            if (oldLength >= count) return true;

            int oldWords = (oldLength + 31) / 32;
            int newWords = (count + 31) / 32;

            IntPtr words = Memory.Ptr(bits + BitArrayArray);
            if (!Memory.LooksLikeObject(words))
            {
                Diagnostics.Warn($"the availability bits have no backing array (0x{words.ToInt64():x})");
                return false;
            }

            IntPtr grown = NewArray(words, newWords);
            if (grown == IntPtr.Zero)
            {
                Diagnostics.Warn($"no room for {newWords} availability word(s)");
                return false;
            }

            long bytes = (long)Memory.I32(words + Offsets.Runtime.ArrayLength) * sizeof(int);
            Buffer.MemoryCopy((void*)(words + Offsets.Runtime.ArrayDataOffset),
                              (void*)(grown + Offsets.Runtime.ArrayDataOffset), bytes, bytes);

            for (int i = 0; i < newWords; i++)
            {
                int mask = i < oldWords ? Memory.I32(words + Offsets.Runtime.ArrayDataOffset + i * sizeof(int))
                                        : 0;
                for (int b = 0; b < 32; b++)
                {
                    int index = i * 32 + b;
                    if (index < oldLength || index >= count) continue;
                    mask |= 1 << b;
                }
                Memory.WriteI32(grown + Offsets.Runtime.ArrayDataOffset + i * sizeof(int), mask);
            }

            Memory.WritePtr(bits + BitArrayArray, grown);
            Memory.WriteI32(bits + BitArrayLength, count);
            Diagnostics.Info($"availability bits grown from {oldLength} to {count}");
            return true;
        }
        private static int SongDataNameIndex => Offsets.SongData.NameIndex;

        /// <summary>The one prefix the game's rebuild leaves out.</summary>
        private const string FilteredPrefix = "tutorial";

        /// <summary>A ceiling on a title-reading array's length.</summary>
        private const int LocaleLimit = 64;

        private static int PackDataPacks => Offsets.PackData.Packs;

        private static int SongInfoSize => Offsets.Song.Size;
        private static int SongInfoId => Offsets.Song.Id;                  // SongId (ushort)
        private static int SongInfoBaseName => Offsets.Song.BaseName;      // string
        private static int SongInfoCharts => Offsets.Song.Charts;          // SongChartInfo[]
        private static int SongInfoPreviewStart => Offsets.Song.PreviewStart;
        private static int SongInfoPreviewEnd => Offsets.Song.PreviewEnd;
        private static int SongInfoTitleReading => Offsets.Song.TitleReading;
        private static int SongInfoArtistReading => Offsets.Song.ArtistReading;
        private static int SongInfoRewardStyle => Offsets.Song.RewardStyle;

        /// <summary>`RewardStyle.Supress`.</summary>
        private const int RewardStyleSupress = 0;

        // ---- SongChartInfo ----
        private static int ChartInfoSize => Offsets.Chart.Size;
        private static int ChartInfoId => Offsets.Chart.Id;                       // string
        private static int ChartInfoAvailable => Offsets.Chart.Available;         // bool
        private static int ChartInfoDifficulty => Offsets.Chart.Difficulty;       // ChartDifficultyFlag
        private static int ChartInfoDesigner => Offsets.Chart.Designer;           // string
        private static int ChartInfoJacketDesigner => Offsets.Chart.JacketDesigner;
        private static int ChartInfoRating => Offsets.Chart.Rating;               // int
        private static int ChartInfoSection => Offsets.Chart.Section;             // string

        // ---- PackInfo ----
        private static int PackInfoSize => Offsets.Pack.Size;
        private static int PackInfoId => Offsets.Pack.Id;        // PackId (ushort)
        private static int PackInfoSlug => Offsets.Pack.Slug;    // string
        private static int PackInfoSongs => Offsets.Pack.Songs;  // SongId[]

        /// <summary>This mod's IFCL pack id, taken from the live pack table.</summary>
        internal static int CustomPackId = -1;

        /// <summary>The pack manifest each live custom pack id belongs to.</summary>
        private static readonly Dictionary<ushort, PackManifest> LivePacks =
            new Dictionary<ushort, PackManifest>();

        /// <summary>Whether a pack id is one of this mod's live packs, IFCL included.</summary>
        internal static bool IsCustomPack(ushort id) =>
            (CustomPackId >= 0 && id == (ushort)CustomPackId) || LivePacks.ContainsKey(id);

        /// <summary>Whether a pack id is in the live pack table.</summary>
        internal static bool LivePackExists(ushort id) => LivePackSongCount(id, out _);

        /// <summary>Whether a pack id is a live pack that this mod did not make.</summary>
        internal static bool IsOfficialPack(ushort id) => !IsCustomPack(id) && LivePackExists(id);

        /// <summary>How many custom packs are live, IFCL included.</summary>
        internal static int CustomPackCount => LivePacks.Count + (CustomPackId >= 0 ? 1 : 0);

        /// <summary>The pack ids the mod has live, IFCL first.</summary>
        internal static IEnumerable<int> LiveCustomPackIds()
        {
            if (CustomPackId >= 0) yield return CustomPackId;

            var ids = new List<ushort>(LivePacks.Keys);
            ids.Sort();
            foreach (ushort id in ids) yield return id;
        }

        /// <summary>The song ids this run handed out.</summary>
        internal static readonly HashSet<ushort> CustomSongIds = new HashSet<ushort>();

        /// <summary>The same songs, by id, with their `if` file data.</summary>
        internal static readonly Dictionary<ushort, ChartInfo> ById =
            new Dictionary<ushort, ChartInfo>();

        /// <summary>Which difficulty each song's illustrator entry currently shows.</summary>
        private static readonly Dictionary<ushort, int> IllustratorShown = new Dictionary<ushort, int>();

        /// <summary>The pack's name in the pack list. The game shows this text.</summary>
        internal const string PackName = "IFCL";

        /// <summary>
        /// The difficulty flags, in the order <c>chart[0..3]</c> is written: Minimal, Evolved,
        /// Ultimate, Forbidden.
        /// </summary>
        private static readonly byte[] DifficultyFlags = { 1, 2, 4, 8 };

        /// <summary>The index <c>chart[0..3]</c> a difficulty flag names, or -1 for anything else.</summary>
        internal static int DifficultyIndexOf(byte flag)
        {
            for (int i = 0; i < DifficultyFlags.Length; i++)
                if (DifficultyFlags[i] == flag) return i;
            return -1;
        }

        private static int _songDataField = int.MinValue;
        private static int _packDataField = int.MinValue;
        private static bool _unusable;

        /// <summary>Whether this run has to give up on the game's tables.</summary>
        internal static bool Unusable => _unusable;

        /// <summary>The two game data objects, or false while they are not loaded.</summary>
        internal static bool TryGetAssets(out IntPtr songData, out IntPtr packData)
        {
            songData = IntPtr.Zero;
            packData = IntPtr.Zero;

            if (_unusable) return false;

            IntPtr klass = FieldResolver.ClassPointer("DataAccess");
            if (klass == IntPtr.Zero) return false;

            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return false;

            if (_songDataField == int.MinValue)
            {
                _songDataField = FieldResolver.Field("DataAccess", "_JAb");
                _packDataField = FieldResolver.Field("DataAccess", "_lAb");
            }

            if (_songDataField < 0 || _packDataField < 0)
            {
                _unusable = true;
                Diagnostics.Error("the game's data assets could not be found by name in this build; " +
                                  "no custom song will be registered");
                return false;
            }

            songData = Memory.Ptr(statics + _songDataField);
            packData = Memory.Ptr(statics + _packDataField);

            if (!Memory.LooksLikeObject(songData) || !Memory.LooksLikeObject(packData)) return false;

            if (!Offsets.Resolve(songData, packData))
            {
                _unusable = true;
                return false;
            }

            return true;
        }

        /// <summary>The names the shipped songs already carry.</summary>
        internal static HashSet<string> ShippedNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            if (!TryGetAssets(out IntPtr songData, out _)) return names;

            IntPtr songs = Memory.Ptr(songData + SongDataAllSongs);
            if (!Memory.LooksLikeObject(songs)) return names;

            int count = Memory.I32(songs + Offsets.Runtime.ArrayLength) - ById.Count;
            if (count < 0) count = 0;

            for (int i = 0; i < count; i++)
            {
                IntPtr slot = songs + Offsets.Runtime.ArrayDataOffset + i * SongInfoSize;
                string text = Memory.Text(Memory.Ptr(slot + SongInfoBaseName));
                if (text != null) names.Add(text);
            }

            return names;
        }

        /// <summary>Settles every song's name and refuses the ones the game already has.</summary>
        internal static List<ChartInfo> SettleNames(List<ChartInfo> charts, IntPtr songData)        {
            var admitted = new List<ChartInfo>(charts.Count);

            IntPtr songs = Memory.Ptr(songData + SongDataAllSongs);
            int count = Memory.LooksLikeObject(songs) ? Memory.I32(songs + Offsets.Runtime.ArrayLength) : 0;

            var taken = new HashSet<string>(StringComparer.Ordinal);

            const int SlotsToCheck = 4;
            int stride = SongInfoSize;
            long byteLength = Memory.ArrayBytes(songs);

            for (int i = 0; i < count && byteLength > 0 && (long)(i + 1) * stride <= byteLength; i++)
            {
                IntPtr slot = songs + Offsets.Runtime.ArrayDataOffset + i * stride;

                ushort id = Memory.U16(slot + SongInfoId);
                if (i < SlotsToCheck && id != 0 && id != (ushort)i)
                {
                    Diagnostics.Error($"the song table does not index the way this build expects " +
                                      $"(slot {i} carries id {id}, not {i}); no custom song was " +
                                      "registered, because their names cannot be checked against the game's");
                    return admitted;
                }

                string text = Memory.Text(Memory.Ptr(slot + SongInfoBaseName));
                if (text != null) taken.Add(text);
            }

            foreach (ChartInfo chart in charts)
            {
                if (taken.Contains(chart.BaseName))
                {
                    Diagnostics.WarnRelease($"'{chart.BaseName}' is already the name of a song this game " +
                                            $"has, so '{chart.FolderName}' was not loaded. Change the id " +
                                            "in its if file.");
                    continue;
                }

                taken.Add(chart.BaseName);
                admitted.Add(chart);

                ChartCatalog.Adopt(chart);
            }

            return admitted;
        }

        /// <summary>Adds every chart folder to the song and pack tables.</summary>
        internal static int Inject(IntPtr songData, IntPtr packData, List<ChartInfo> charts,
                                   List<PackManifest> packs) =>
            ReplaceCustoms(songData, packData, charts, packs);

        /// <summary>Rebuilds the custom tail of the song table in id order.</summary>
        internal static int ReplaceCustoms(IntPtr songData, IntPtr packData, List<ChartInfo> charts,
                                           List<PackManifest> packs)
        {
            IntPtr oldSongs = Memory.Ptr(songData + SongDataAllSongs);
            if (!Memory.LooksLikeObject(oldSongs)) { Diagnostics.Warn("SongData has no song array"); return 0; }

            int oldCount = Memory.I32(oldSongs + Offsets.Runtime.ArrayLength);
            if (oldCount <= 0) { Diagnostics.Warn("SongData has no songs to take a template from"); return 0; }

            int shipped = oldCount - ById.Count;
            if (shipped < 0) shipped = 0;

            int newCount = shipped + charts.Count;
            if (newCount <= 0) return 0;

            IntPtr chartTemplate = FirstCharts(oldSongs, oldCount);
            if (chartTemplate == IntPtr.Zero) return 0;

            IntPtr titleTemplate = FirstReadings(oldSongs, oldCount);

            List<PackGroup> groups = BuildGroups(packData, charts, packs);
            if (groups == null) return 0;

            if (!PackSetup.ApplyPackAssets(packData, packs))
            {
                Diagnostics.Error("the custom packs could not be given their rows; the custom songs were " +
                                  "not added");
                return 0;
            }

            var placement = new Dictionary<ChartInfo, (PackGroup Group, int Index)>();
            foreach (PackGroup group in groups)
                for (int i = 0; i < group.Songs.Count; i++)
                    placement[group.Songs[i]] = (group, i);

            if (!GrowAvailability(songData, newCount)) return 0;

            IntPtr songs = ResizeArray(oldSongs, newCount);
            if (songs == IntPtr.Zero) return 0;

            int stride = SongInfoSize;
            long shippedBytes = (long)shipped * stride;
            if (shippedBytes > 0)
            {
                Buffer.MemoryCopy((void*)(oldSongs + Offsets.Runtime.ArrayDataOffset),
                                  (void*)(songs + Offsets.Runtime.ArrayDataOffset),
                                  shippedBytes, shippedBytes);
            }

            ChartCatalog.Reset();
            JacketCatalog.BeginRebuild();

            for (int i = 0; i < charts.Count; i++)
            {
                int id = shipped + i;
                IntPtr slot = (IntPtr)((long)songs + Offsets.Runtime.ArrayDataOffset + (long)id * stride);

                Buffer.MemoryCopy((void*)(oldSongs + Offsets.Runtime.ArrayDataOffset), (void*)slot,
                                  stride, stride);

                Memory.WritePtr(slot + SongInfoTitleReading, BuildReadings(titleTemplate, charts[i].Names));
                Memory.WritePtr(slot + SongInfoArtistReading, Str(charts[i].Composer));

                Memory.WriteU16(slot + SongInfoId, (ushort)id);
                Memory.WritePtr(slot + SongInfoBaseName, Str(charts[i].BaseName));

                Memory.WriteF32(slot + SongInfoPreviewStart, charts[i].PreviewStart);
                Memory.WriteF32(slot + SongInfoPreviewEnd, charts[i].PreviewEnd);

                Memory.WriteI32(slot + SongInfoRewardStyle, RewardStyleSupress);

                IntPtr chartInfos = BuildCharts(chartTemplate, charts[i]);
                Memory.WritePtr(slot + SongInfoCharts, chartInfos);
                JacketCatalog.RegisterCharts(chartInfos, charts[i]);

                Diagnostics.Info($"probe: rebuild slot={id} songId={Memory.U16(slot + SongInfoId)} " +
                                 $"charts=0x{chartInfos.ToInt64():X} base='{charts[i].BaseName}'");

                if (placement.TryGetValue(charts[i], out (PackGroup group, int index) at))
                    WriteSongId(at.group.SongIds, at.index, (ushort)id);
            }

            Memory.WritePtr(songData + SongDataAllSongs, songs);
            Root(songs);

            CustomSongIds.Clear();
            ById.Clear();
            IllustratorShown.Clear();

            for (int i = 0; i < charts.Count; i++)
            {
                int id = shipped + i;
                CustomSongIds.Add((ushort)id);
                ById[(ushort)id] = charts[i];
                ChartCatalog.Adopt(charts[i]);
            }

            if (!WritePackTable(packData, groups)) return 0;

            for (int i = 0; i < charts.Count; i++)
            {
                int id = shipped + i;
                PackSetup.SetSongText(PackSetup.SongText.Title, id, charts[i].Names);
                PackSetup.SetSongText(PackSetup.SongText.Artist, id, charts[i].Composer);
                PackSetup.SetSongText(PackSetup.SongText.Illustrator, id, IllustratorOf(charts[i], 0));
            }

            Refresh(songData);
            return charts.Count;
        }

        /// <summary>One pack row while the custom pack table is being rebuilt.</summary>
        private sealed class PackGroup
        {
            internal ushort PackId;
            internal string Name;
            internal List<ChartInfo> Songs;
            internal IntPtr SongIds;
        }

        /// <summary>The pack id a live pack root's <c>if</c> id was given.</summary>
        private static readonly Dictionary<string, ushort> PackIds =
            new Dictionary<string, ushort>(StringComparer.Ordinal);

        /// <summary>Groups the songs by their pack and gives every non-empty pack its id.</summary>
        private static List<PackGroup> BuildGroups(IntPtr packData, List<ChartInfo> charts,
                                                   List<PackManifest> packs)
        {
            IntPtr oldPacks = Memory.Ptr(packData + PackDataPacks);
            if (!Memory.LooksLikeObject(oldPacks)) { Diagnostics.Warn("PackData has no pack array"); return null; }

            int oldCount = Memory.I32(oldPacks + Offsets.Runtime.ArrayLength);
            int next = NextPackId(oldPacks, oldCount);

            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (PackManifest pack in packs) wanted.Add(pack.Id);
            foreach (string key in new List<string>(PackIds.Keys))
                if (!wanted.Contains(key)) PackIds.Remove(key);

            var groups = new List<PackGroup>();
            var ifclSongs = new List<ChartInfo>();
            foreach (ChartInfo chart in charts)
                if (chart.Pack == null) ifclSongs.Add(chart);

            if (ifclSongs.Count > 0)
            {
                if (CustomPackId < 0) CustomPackId = next++;
                else if (CustomPackId >= next)
                {
                    Diagnostics.Error("PackData.PackInfo and SongCatalog.PackIds are out of step; " +
                                      "the custom packs were not written");
                    return null;
                }

                groups.Add(new PackGroup
                {
                    PackId = (ushort)CustomPackId,
                    Name = PackName,
                    Songs = ifclSongs,
                });
            }
            else
            {
                CustomPackId = -1;
            }

            var byManifest = new Dictionary<PackManifest, List<ChartInfo>>();
            foreach (PackManifest pack in packs) byManifest[pack] = new List<ChartInfo>();

            foreach (ChartInfo chart in charts)
            {
                if (chart.Pack == null) continue;
                if (!byManifest.TryGetValue(chart.Pack, out List<ChartInfo> songs))
                {
                    Diagnostics.Error("a custom song names a pack that was not imported; the custom packs " +
                                      "were not written");
                    return null;
                }

                songs.Add(chart);
            }

            LivePacks.Clear();
            foreach (PackManifest pack in packs)
            {
                List<ChartInfo> songs = byManifest[pack];
                if (songs.Count == 0)
                {
                    pack.PackId = -1;
                    PackIds.Remove(pack.Id);
                    continue;
                }

                if (!PackIds.TryGetValue(pack.Id, out ushort packId))
                {
                    packId = (ushort)next++;
                    PackIds[pack.Id] = packId;
                }
                else if (packId >= next)
                {
                    Diagnostics.Error("PackData.PackInfo and SongCatalog.PackIds are out of step; " +
                                      "the custom packs were not written");
                    return null;
                }

                pack.PackId = packId;
                groups.Add(new PackGroup
                {
                    PackId = packId,
                    Name = pack.Name,
                    Songs = songs,
                });
                LivePacks[packId] = pack;
            }

            foreach (PackGroup group in groups)
            {
                group.SongIds = AllocateSongIds(packData, group.Songs.Count);
                if (group.SongIds == IntPtr.Zero) return null;
            }

            return groups;
        }

        /// <summary>The first pack id above every live row's id.</summary>
        private static int NextPackId(IntPtr packs, int count)
        {
            int next = 0;

            for (int i = 0; i < count; i++)
            {
                int id = Memory.U16(packs + Offsets.Runtime.ArrayDataOffset + i * PackInfoSize + PackInfoId);
                if (id >= next) next = id + 1;
            }

            return next;
        }

        /// <summary>Whether a pack row is one this mod wrote.</summary>
        private static bool IsCustomRow(IntPtr row)
        {
            IntPtr slug = Memory.Ptr(row + PackInfoSlug);
            return string.Equals(Memory.Text(slug, 16), "custom", StringComparison.Ordinal);
        }

        /// <summary>Writes the custom packs, then the game's own rows, into the live pack table.</summary>
        private static bool WritePackTable(IntPtr packData, List<PackGroup> groups)
        {
            IntPtr oldPacks = Memory.Ptr(packData + PackDataPacks);
            if (!Memory.LooksLikeObject(oldPacks)) { Diagnostics.Warn("PackData has no pack array"); return false; }

            int oldCount = Memory.I32(oldPacks + Offsets.Runtime.ArrayLength);
            int stride = PackInfoSize;

            int official = 0;
            IntPtr template = IntPtr.Zero;
            var officialRows = new List<IntPtr>();
            for (int i = 0; i < oldCount; i++)
            {
                IntPtr row = oldPacks + Offsets.Runtime.ArrayDataOffset + i * stride;
                if (IsCustomRow(row)) continue;

                official++;
                officialRows.Add(row);
                if (template == IntPtr.Zero) template = row;
            }

            if (template == IntPtr.Zero)
            {
                Diagnostics.Error("the game's pack table has no shipped row to take a shape from; " +
                                  "the custom packs were not written");
                return false;
            }

            int total = groups.Count + official;
            IntPtr fresh = NewArray(oldPacks, total);
            if (fresh == IntPtr.Zero) return false;

            int at = 0;

            // Keeps the game's head row first, then the mod's packs, then the game's other packs.
            Buffer.MemoryCopy((void*)officialRows[0],
                              (void*)(fresh + Offsets.Runtime.ArrayDataOffset), stride, stride);
            at++;

            foreach (PackGroup group in groups)
            {
                IntPtr destination = fresh + Offsets.Runtime.ArrayDataOffset + at * stride;
                Buffer.MemoryCopy((void*)template, (void*)destination, stride, stride);

                Memory.WriteU16(destination + PackInfoId, group.PackId);
                Memory.WritePtr(destination + PackInfoSlug, Str("custom"));
                Memory.WritePtr(destination + PackInfoSongs, group.SongIds);
                if (Offsets.Pack.ExternalURL >= 0)
                    Memory.WritePtr(destination + Offsets.Pack.ExternalURL, IntPtr.Zero);
                at++;
            }

            for (int i = 1; i < officialRows.Count; i++)
            {
                IntPtr destination = fresh + Offsets.Runtime.ArrayDataOffset + at * stride;
                Buffer.MemoryCopy((void*)officialRows[i], (void*)destination, stride, stride);
                at++;
            }

            Memory.WritePtr(packData + PackDataPacks, fresh);
            Root(fresh);
            PackSetup.RefreshLookup(packData);
            return true;
        }

        /// <summary>How many songs the live table holds, or -1.</summary>
        internal static int AllSongCount()
        {
            if (!TryGetAssets(out IntPtr songData, out _)) return -1;

            IntPtr songs = Memory.Ptr(songData + SongDataAllSongs);
            return Memory.LooksLikeObject(songs) ? Memory.I32(songs + Offsets.Runtime.ArrayLength) : -1;
        }

        /// <summary>How many songs the rebuilt list holds, or -1.</summary>
        internal static int SongListLength()
        {
            if (!TryGetAssets(out IntPtr songData, out _)) return -1;

            int field = FieldResolver.Lookup("SongData", "_ffb");
            if (field < 0) return -1;

            IntPtr list = Memory.Ptr(songData + field);
            return Memory.LooksLikeObject(list) ? Memory.I32(list + Offsets.Runtime.ArrayLength) : -1;
        }

        /// <summary>The live song-id array of one pack, or zero when it is not in the live table.</summary>
        internal static IntPtr LivePackSongs(ushort packId)
        {
            if (!TryGetAssets(out IntPtr _, out IntPtr packData)) return IntPtr.Zero;

            IntPtr packs = Memory.Ptr(packData + Offsets.PackData.Packs);
            if (!Memory.LooksLikeObject(packs)) return IntPtr.Zero;

            int count = Memory.I32(packs + Offsets.Runtime.ArrayLength);
            for (int i = 0; i < count; i++)
            {
                IntPtr pack = packs + Offsets.Runtime.ArrayDataOffset + i * PackInfoSize;
                if (Memory.U16(pack + PackInfoId) != packId) continue;

                return Memory.Ptr(pack + PackInfoSongs);
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// How many songs one pack holds in the live table. False when the table or the pack cannot
        /// be read, which is not the same statement as a pack that holds none.
        /// </summary>
        internal static bool LivePackSongCount(ushort packId, out int count)
        {
            count = 0;
            if (!TryGetAssets(out IntPtr _, out IntPtr packData)) return false;

            IntPtr packs = Memory.Ptr(packData + Offsets.PackData.Packs);
            if (!Memory.LooksLikeObject(packs)) return false;

            int rows = Memory.I32(packs + Offsets.Runtime.ArrayLength);
            for (int i = 0; i < rows; i++)
            {
                IntPtr pack = packs + Offsets.Runtime.ArrayDataOffset + i * PackInfoSize;
                if (Memory.U16(pack + PackInfoId) != packId) continue;

                IntPtr songs = Memory.Ptr(pack + PackInfoSongs);
                count = Memory.LooksLikeObject(songs) ? Memory.I32(songs + Offsets.Runtime.ArrayLength) : 0;
                return true;
            }

            return false;
        }

        /// <summary>The live row index of one pack, or -1 when it is not in the table.</summary>
        internal static int LivePackRow(ushort packId)
        {
            if (!TryGetAssets(out IntPtr _, out IntPtr packData)) return -1;

            IntPtr packs = Memory.Ptr(packData + Offsets.PackData.Packs);
            if (!Memory.LooksLikeObject(packs)) return -1;

            int count = Memory.I32(packs + Offsets.Runtime.ArrayLength);
            for (int i = 0; i < count; i++)
            {
                IntPtr pack = packs + Offsets.Runtime.ArrayDataOffset + i * PackInfoSize;
                if (Memory.U16(pack + PackInfoId) == packId) return i;
            }

            return -1;
        }

        /// <summary>The pack id in one live row, or 0xFFFF when there is none.</summary>
        internal static ushort LivePackIdAtRow(int row)
        {
            if (row < 0) return ushort.MaxValue;
            if (!TryGetAssets(out IntPtr _, out IntPtr packData)) return ushort.MaxValue;

            IntPtr packs = Memory.Ptr(packData + Offsets.PackData.Packs);
            if (!Memory.LooksLikeObject(packs)) return ushort.MaxValue;

            int count = Memory.I32(packs + Offsets.Runtime.ArrayLength);
            if (row >= count) return ushort.MaxValue;

            IntPtr pack = packs + Offsets.Runtime.ArrayDataOffset + row * PackInfoSize;
            return Memory.U16(pack + PackInfoId);
        }

        /// <summary>Copies the live row of a pack over a saved copy of it.</summary>
        internal static bool CopyPackTo(IntPtr destination, ushort packId)
        {
            if (!TryGetAssets(out IntPtr _, out IntPtr packData)) return false;
            if (!Memory.LooksLikeObject(destination)) return false;

            IntPtr packs = Memory.Ptr(packData + Offsets.PackData.Packs);
            if (!Memory.LooksLikeObject(packs)) return false;

            int count = Memory.I32(packs + Offsets.Runtime.ArrayLength);
            for (int i = 0; i < count; i++)
            {
                IntPtr pack = packs + Offsets.Runtime.ArrayDataOffset + i * PackInfoSize;
                if (Memory.U16(pack + PackInfoId) != packId) continue;

                Buffer.MemoryCopy((void*)pack, (void*)destination, PackInfoSize, PackInfoSize);
                return true;
            }

            return false;
        }

        /// <summary>Copies the live row of the scene's current pack over its saved copy.</summary>
        internal static bool CopyPackToScene(IntPtr scene, int field)
        {
            if (!Memory.LooksLikeObject(scene) || field <= 0) return false;

            ushort wanted = Memory.U16(scene + field + PackInfoId);
            return CopyPackTo(scene + field, wanted);
        }

        /// <summary>An array of the same element type, resized, keeping the elements that fit.</summary>
        private static IntPtr ResizeArray(IntPtr like, int count)
        {
            int oldCount = Memory.I32(like + Offsets.Runtime.ArrayLength);
            IntPtr resized = NewArray(like, count);
            if (resized == IntPtr.Zero) return IntPtr.Zero;

            int stride = ElementStride(like, oldCount);
            if (stride <= 0) return IntPtr.Zero;

            int copy = oldCount < count ? oldCount : count;
            long bytes = (long)copy * stride;
            if (bytes > 0)
            {
                Buffer.MemoryCopy((void*)(like + Offsets.Runtime.ArrayDataOffset),
                                  (void*)(resized + Offsets.Runtime.ArrayDataOffset), bytes, bytes);
            }

            return Root(resized);
        }

        /// <summary>The illustrator to show for one difficulty.</summary>
        private static string IllustratorOf(ChartInfo info, int difficulty) =>
            info.Illustrators == null ? string.Empty : info.Illustrators[difficulty];

        /// <summary>Whether the four illustrators are not all the same.</summary>
        private static bool VariesByDifficulty(ChartInfo info)
        {
            string[] names = info.Illustrators;
            if (names == null) return false;

            for (int i = 1; i < names.Length; i++)
                if (names[i] != names[0]) return true;
            return false;
        }

        /// <summary>Puts the applied difficulty's illustrator into the localisation entry.</summary>
        internal static void ShowIllustrator(ushort songId, byte difficultyFlag)
        {
            int difficulty = DifficultyIndexOf(difficultyFlag);

            if (difficulty < 0) return;
            if (!ById.TryGetValue(songId, out ChartInfo info)) return;
            if (!VariesByDifficulty(info)) return;
            if (IllustratorShown.TryGetValue(songId, out int shown) && shown == difficulty) return;

            if (PackSetup.SetSongText(PackSetup.SongText.Illustrator, songId, info.Illustrators[difficulty]))
                IllustratorShown[songId] = difficulty;
        }

        /// <summary>Makes the game re-derive the lists it builds from <c>allSongInfo</c>.</summary>
        private static void Refresh(IntPtr songData)
        {
            Memory.WritePtr(songData + SongDataNameIndex, IntPtr.Zero);

            IntPtr klass = FieldResolver.ClassPointer("SongData");
            if (klass == IntPtr.Zero) { Diagnostics.Error("SongData could not be found; the new songs will not show"); return; }

            IntPtr method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, "_yOA", 0);
            if (method == IntPtr.Zero)
            {
                Diagnostics.Error("SongData._yOA could not be found; the new songs will not show");
                return;
            }

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, songData, null, ref raised);

            if (raised != IntPtr.Zero)
                Diagnostics.Error($"the game's song list refresh raised: {Raised.Text(raised)}");

            Diagnostics.Info("song list rebuilt");
        }

        /// <summary>A title-reading array with one text per game locale.</summary>
        private static IntPtr BuildReadings(IntPtr template, string[] names)
        {
            if (!Memory.LooksLikeObject(template)) return IntPtr.Zero;
            if (names == null || names.Length != 5)
            {
                Diagnostics.Error("the song name does not have five language slots");
                return IntPtr.Zero;
            }

            int locales = Memory.I32(template + Offsets.Runtime.ArrayLength);
            if (locales <= 0 || locales > LocaleLimit)
            {
                Diagnostics.Warn($"the shipped title readings hold no locale count ({locales}); " +
                                 $"'{names[0]}' keeps its name from the localisation table");
                return IntPtr.Zero;
            }

            IntPtr array = NewArray(template, locales);
            if (array == IntPtr.Zero) return IntPtr.Zero;

            for (int i = 0; i < locales; i++)
            {
                IntPtr value = Str(ReadingFor(names, i));
                Memory.WritePtr(array + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size, value);
            }

            return Root(array);
        }

        /// <summary>The name for one `Strings.Localization` index.</summary>
        private static string ReadingFor(string[] names, int locale)
        {
            switch (locale)
            {
                case 1: return names[2];   // English
                case 2: return names[3];   // Japanese
                case 3: return names[4];   // Korean
                case 4: return names[1];   // Traditional Chinese
                case 5: return names[0];   // Simplified Chinese
                default: return names[2];  // Unset, and anything past the enum
            }
        }

        /// <summary>The per-difficulty records for one song.</summary>
        private static IntPtr BuildCharts(IntPtr template, ChartInfo info)
        {
            IntPtr array = NewArray(template, ChartInfo.Difficulties);
            if (array == IntPtr.Zero) return IntPtr.Zero;

            IntPtr source = template + Offsets.Runtime.ArrayDataOffset;

            for (int d = 0; d < ChartInfo.Difficulties; d++)
            {
                IntPtr slot = array + Offsets.Runtime.ArrayDataOffset + d * ChartInfoSize;
                Buffer.MemoryCopy((void*)source, (void*)slot, ChartInfoSize, ChartInfoSize);

                if (info.NoChart[d])
                {
                    Memory.WritePtr(slot + ChartInfoId, Str(info.BaseName + "_" + (d + 1)));
                    Memory.WriteU8(slot + ChartInfoAvailable, 0);
                    Memory.WriteU8(slot + ChartInfoDifficulty, DifficultyFlags[d]);
                }
                else
                {
                    Memory.WritePtr(slot + ChartInfoId, Str(Path.GetFileName(info.ChartPaths[d])));
                    Memory.WriteU8(slot + ChartInfoAvailable, 1);
                    Memory.WriteU8(slot + ChartInfoDifficulty, DifficultyFlags[d]);
                }

                Memory.WritePtr(slot + ChartInfoDesigner, Str(info.Charters[d] ?? string.Empty));

                Memory.WritePtr(slot + ChartInfoJacketDesigner, Str(IllustratorOf(info, d)));

                int? level = info.Level[d];
                Memory.WriteI32(slot + ChartInfoRating, level ?? 0);
                Memory.WritePtr(slot + ChartInfoSection,
                                Str(level is int rated
                                    ? rated.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                    : string.Empty));
            }

            return Root(array);
        }

        /// <summary>What the category screen was built from, so a structural change redraws it.</summary>
        internal static string PackScreenSignature()
        {
            var parts = new List<string>();
            if (CustomPackId >= 0) parts.Add($"i{CustomPackId}");

            var packs = new List<PackManifest>(LivePacks.Values);
            packs.Sort((left, right) => left.PackId.CompareTo(right.PackId));
            foreach (PackManifest pack in packs)
                parts.Add($"p{pack.PackId}:{pack.Id}:{string.Join(",", pack.Names)}:{string.Join(",", pack.JacketPaths)}");

            parts.Add($"v{PackVisuals.Revision}");
            return string.Join("|", parts);
        }

        /// <summary>A `SongId[]` holding the ids this mod handed out.</summary>
        private static IntPtr AllocateSongIds(IntPtr packData, int count)
        {
            IntPtr packs = Memory.Ptr(packData + PackDataPacks);
            int packCount = Memory.I32(packs + Offsets.Runtime.ArrayLength);
            if (packCount <= 0) { Diagnostics.Warn("PackData has no packs to take a template from"); return IntPtr.Zero; }

            IntPtr firstPack = packs + Offsets.Runtime.ArrayDataOffset;
            IntPtr template = Memory.Ptr(firstPack + PackInfoSongs);
            if (!Memory.LooksLikeObject(template)) { Diagnostics.Warn("a shipped pack has no song ids"); return IntPtr.Zero; }

            return Root(NewArray(template, count));
        }

        /// <summary>The first song's `ChartInfos` array object.</summary>
        private static IntPtr FirstCharts(IntPtr songs, int count)
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr charts = Memory.Ptr(songs + Offsets.Runtime.ArrayDataOffset + i * SongInfoSize + SongInfoCharts);
                if (Memory.LooksLikeObject(charts) &&
                    Memory.I32(charts + Offsets.Runtime.ArrayLength) >= ChartInfo.Difficulties)
                    return charts;
            }

            Diagnostics.Warn("no shipped song has four difficulty records to take a shape from");
            return IntPtr.Zero;
        }

        /// <summary>The first shipped title-reading array.</summary>
        private static IntPtr FirstReadings(IntPtr songs, int count)
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr readings = Memory.Ptr(songs + Offsets.Runtime.ArrayDataOffset +
                                             i * SongInfoSize + SongInfoTitleReading);
                if (Memory.LooksLikeObject(readings) &&
                    Memory.I32(readings + Offsets.Runtime.ArrayLength) > 0)
                    return readings;
            }

            Diagnostics.Warn("no shipped song carries a title-reading array to take a shape from");
            return IntPtr.Zero;
        }

        /// <summary>An array of the same element type, larger, with the old contents copied.</summary>
        internal static IntPtr GrowArray(IntPtr like, int count)
        {
            int oldCount = Memory.I32(like + Offsets.Runtime.ArrayLength);
            IntPtr grown = NewArray(like, count);
            if (grown == IntPtr.Zero) return IntPtr.Zero;

            int stride = ElementStride(like, oldCount);
            if (stride <= 0) return IntPtr.Zero;

            long bytes = (long)oldCount * stride;
            if (bytes > 0)
            {
                Buffer.MemoryCopy((void*)(like + Offsets.Runtime.ArrayDataOffset),
                                  (void*)(grown + Offsets.Runtime.ArrayDataOffset), bytes, bytes);
            }

            return Root(grown);
        }

        /// <summary>An array of the same element type as <paramref name="like"/>, empty.</summary>
        internal static IntPtr NewArray(IntPtr like, int count)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(like);
            IntPtr element = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_element_class(klass);
            if (element == IntPtr.Zero) { Diagnostics.Warn("an array has no element class"); return IntPtr.Zero; }

            IntPtr array = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(element, (ulong)count);
            if (array == IntPtr.Zero) Diagnostics.Warn($"could not allocate an array of {count}");
            return array;
        }

        private static int ElementStride(IntPtr array, int count)
        {
            if (count <= 0) return 0;
            long bytes = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(array);
            return (int)(bytes / count);
        }

        private static void WriteSongId(IntPtr array, int index, ushort value) =>
            Memory.WriteU16(array + Offsets.Runtime.ArrayDataOffset + index * 2, value);

        /// <summary>A managed string as an IL2CPP one.</summary>
        private static IntPtr Str(string text) =>
            Root(Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(text));

        /// <summary>Keeps a mod-created IL2CPP object alive while the game holds it.</summary>
        private static IntPtr Root(IntPtr value)
        {
            if (value != IntPtr.Zero)
                Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_new(value, false);
            return value;
        }
    }
}
