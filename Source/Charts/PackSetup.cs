using System;
using System.Collections.Generic;

namespace InFalsusChartLoader
{
    /// <summary>The pack's row of visuals and its reverse lookup.</summary>
    internal static unsafe class PackSetup
    {
        /// <summary>`PackSelectSceneAssets.packToAssets`.</summary>
        private static int PackAssetsTable = Offsets.Unresolved;

        /// <summary>`DataAccess._NAb`.</summary>
        private static int PackAssetsField = Offsets.Unresolved;

        /// <summary>`DataAccess._mAb` — the localisation table.</summary>
        private static int StringMappingField = Offsets.Unresolved;

        /// <summary>The pack visuals, or false while the game has not loaded them.</summary>
        internal static bool TryGetVisuals(out IntPtr packAssets)
        {
            packAssets = IntPtr.Zero;

            if (!ResolveFields()) return false;

            IntPtr klass = FieldResolver.ClassPointer("DataAccess");
            if (klass == IntPtr.Zero) return false;

            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return false;

            packAssets = Memory.Ptr(statics + PackAssetsField);
            return Memory.LooksLikeObject(packAssets);
        }

        /// <summary>Gives the new pack a row in the visual table.</summary>
        internal static bool ExtendVisuals(IntPtr packAssets, int packIndex)
        {
            IntPtr table = Memory.Ptr(packAssets + PackAssetsTable);
            if (!Memory.LooksLikeObject(table))
            {
                Diagnostics.Warn("the pack visual table is missing");
                NoRow();
                return false;
            }

            int count = Memory.I32(table + Offsets.Runtime.ArrayLength);
            if (packIndex < count) return true;   // already room; nothing to do

            long bytes = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(table);
            int stride = count > 0 ? (int)(bytes / count) : 0;
            if (stride <= 0)
            {
                Diagnostics.Warn("the pack visual table has no element size");
                NoRow();
                return false;
            }

            IntPtr grown = SongCatalog.GrowArray(table, packIndex + 1);
            if (grown == IntPtr.Zero)
            {
                Diagnostics.Warn("the pack visual table could not be grown");
                NoRow();
                return false;
            }

            int from = Hooks.SourceRow(table, count, stride);
            if (from < 0)
            {
                Diagnostics.Warn("no shipped pack row has a material to copy");
                NoRow();
                return false;
            }

            unsafe
            {
                Buffer.MemoryCopy(
                    (void*)(table + Offsets.Runtime.ArrayDataOffset + from * stride),
                    (void*)(grown + Offsets.Runtime.ArrayDataOffset + packIndex * stride),
                    stride, stride);
            }

            int backingFrom = Hooks.BackingRow(table, count, stride);
            if (backingFrom < 0)
            {
                Diagnostics.Error("no shipped pack row has a backing picture; the custom pack would have no " +
                                  "background");
                NoRow();
                return false;
            }

            int backingSlot = FieldResolver.Field("SongSelectPackAssets", "BackingImageNonCompleted");
            int backingDoneSlot = FieldResolver.Field("SongSelectPackAssets", "BackingImageCompleted");
            if (backingSlot < 0 || backingDoneSlot < 0)
            {
                Diagnostics.Error("the pack backing slots could not be read by name in this build; the custom " +
                                  "pack would have no background");
                NoRow();
                return false;
            }

            IntPtr sourceBacking = table + Offsets.Runtime.ArrayDataOffset + backingFrom * stride;
            IntPtr destination = grown + Offsets.Runtime.ArrayDataOffset + packIndex * stride;
            Memory.WritePtr(destination + backingSlot, Memory.Ptr(sourceBacking + backingSlot));
            Memory.WritePtr(destination + backingDoneSlot, Memory.Ptr(sourceBacking + backingDoneSlot));

            Memory.WritePtr(packAssets + PackAssetsTable, grown);
            Diagnostics.Info($"pack visuals extended to {packIndex + 1} rows, " +
                             $"row {packIndex} copying row {from}, {stride} bytes");
            return true;
        }

        /// <summary>Gives every live custom pack its row, name, cover and backing.</summary>
        internal static bool ApplyPackAssets(IntPtr packData, List<PackManifest> packs)
        {
            if (SongCatalog.CustomPackId < 0 && packs.Count == 0) return true;

            if (!TryGetVisuals(out IntPtr packAssets))
            {
                Diagnostics.Error("the pack visual table could not be reached; the custom packs were not applied");
                return false;
            }

            var live = new List<int>();
            if (SongCatalog.CustomPackId >= 0)
            {
                live.Add(SongCatalog.CustomPackId);
                if (!ExtendVisuals(packAssets, SongCatalog.CustomPackId)) return false;
                if (!SetPackName(SongCatalog.CustomPackId, SongCatalog.PackName))
                {
                    Diagnostics.Error("the custom pack's name could not be written; the custom packs were " +
                                      "not applied");
                    return false;
                }
            }

            foreach (PackManifest pack in packs)
            {
                if (pack.PackId < 0) continue;

                live.Add(pack.PackId);
                if (!ExtendVisuals(packAssets, pack.PackId)) return false;
                if (!SetPackName(pack.PackId, pack.Names))
                {
                    Diagnostics.Error("the custom pack's name could not be written; the custom packs were " +
                                      "not applied");
                    return false;
                }

                if (!FillPackVisuals(packAssets, pack.PackId, pack.JacketPaths, out string reason))
                {
                    Diagnostics.Error($"the pack '{pack.Name}' could not be given its pictures: {reason}");
                    return false;
                }
            }

            PackVisuals.Prune(live);
            return true;
        }

        /// <summary>Puts a pack's five pictures on its row, using clones of the game's own materials.</summary>
        internal static bool FillPackVisuals(IntPtr packAssets, int packId, string[] jacketPaths,
                                             out string reason) =>
            PackVisuals.Rebuild(packAssets, packId, jacketPaths, out reason);

        /// <summary>Gives the pack screen's own visual tables the rows the reload built.</summary>
        internal static bool SyncSceneVisuals(IntPtr scene)
        {
            if (!Memory.LooksLikeObject(scene)) return false;
            if (SongCatalog.CustomPackCount == 0) return true;
            if (!TryGetVisuals(out IntPtr dataAssets)) return false;

            int sceneField = FieldResolver.Field("PackSelectScene", "packSelectSceneAssets");
            if (sceneField < 0)
            {
                Diagnostics.Error("PackSelectScene.packSelectSceneAssets could not be read by name in this " +
                                  "build; the custom rows were not given to the scene's visuals");
                return false;
            }

            var seen = new HashSet<IntPtr>();
            IntPtr sceneAssets = Memory.Ptr(scene + sceneField);

            int prefabField = FieldResolver.Field("PackSelectScene", "packOutlineMemberPrefab");
            if (prefabField < 0)
            {
                Diagnostics.Error("PackSelectScene.packOutlineMemberPrefab could not be read by name in this " +
                                  "build; the custom rows were not given to the scene's visuals");
                return false;
            }

            IntPtr prefab = Memory.Ptr(scene + prefabField);
            int prefabAssetsField = FieldResolver.Field("PackOutlineMember", "packSelectSceneAssets");
            if (!Memory.LooksLikeObject(prefab) || prefabAssetsField < 0)
            {
                Diagnostics.Error("PackOutlineMember.packSelectSceneAssets could not be read in this build; " +
                                  "the custom rows were not given to the scene's visuals");
                return false;
            }

            IntPtr prefabAssets = Memory.Ptr(prefab + prefabAssetsField);

            int visualField = FieldResolver.Field("PackSelectScene", "packVisualMemberLarge");
            if (visualField < 0)
            {
                Diagnostics.Error("PackSelectScene.packVisualMemberLarge could not be read by name in this " +
                                  "build; the custom rows were not given to the scene's visuals");
                return false;
            }

            IntPtr visual = Memory.Ptr(scene + visualField);
            int largeAssetsField = FieldResolver.Field("PackVisualMemberLarge", "packSelectSceneAssets");
            if (!Memory.LooksLikeObject(visual) || largeAssetsField < 0)
            {
                Diagnostics.Error("PackVisualMemberLarge.packSelectSceneAssets could not be read in this " +
                                  "build; the custom rows were not given to the scene's visuals");
                return false;
            }

            IntPtr largeAssets = Memory.Ptr(visual + largeAssetsField);

            if (!SyncOne(sceneAssets, dataAssets, seen)) return false;
            if (!SyncOne(prefabAssets, dataAssets, seen)) return false;
            if (!SyncOne(largeAssets, dataAssets, seen)) return false;

            return true;
        }

        /// <summary>Copies the mod's rows into one pack-screen visual table.</summary>
        private static bool SyncOne(IntPtr sceneAssets, IntPtr dataAssets, HashSet<IntPtr> seen)
        {
            if (!Memory.LooksLikeObject(sceneAssets)) return true;
            if (sceneAssets == dataAssets) return true;
            if (!seen.Add(sceneAssets)) return true;

            IntPtr dataTable = Memory.Ptr(dataAssets + PackAssetsTable);
            if (!Memory.LooksLikeObject(dataTable))
            {
                Diagnostics.Error("the pack visual table could not be read; the custom rows were not given " +
                                  "to the scene's visuals");
                return false;
            }

            int dataCount = Memory.I32(dataTable + Offsets.Runtime.ArrayLength);
            int stride = dataCount > 0
                ? (int)(Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(dataTable) / dataCount)
                : 0;
            if (stride <= 0)
            {
                Diagnostics.Error("the pack visual table has no element size; the custom rows were not given " +
                                  "to the scene's visuals");
                return false;
            }

            foreach (int id in SongCatalog.LiveCustomPackIds())
            {
                if (id < 0 || id >= dataCount)
                {
                    Diagnostics.Error("a custom pack row could not be taken from the reloaded visual table; " +
                                      "the custom rows were not given to the scene's visuals");
                    return false;
                }

                if (!ExtendVisuals(sceneAssets, id))
                {
                    Diagnostics.Error("a row of the scene's visuals could not be grown");
                    return false;
                }

                IntPtr sceneTable = Memory.Ptr(sceneAssets + PackAssetsTable);
                int sceneCount = Memory.LooksLikeObject(sceneTable)
                    ? Memory.I32(sceneTable + Offsets.Runtime.ArrayLength)
                    : 0;
                int sceneStride = sceneCount > 0
                    ? (int)(Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(sceneTable) / sceneCount)
                    : 0;
                if (sceneStride != stride || id >= sceneCount)
                {
                    Diagnostics.Error("a row of the scene's visuals does not match the reloaded visual table");
                    return false;
                }

                Buffer.MemoryCopy(
                    (void*)(dataTable + Offsets.Runtime.ArrayDataOffset + id * stride),
                    (void*)(sceneTable + Offsets.Runtime.ArrayDataOffset + id * stride),
                    stride, stride);
            }

            return true;
        }

        private static bool _noRowReported;

        /// <summary>Reports that the custom pack has no row of visuals, once.</summary>
        internal static void NoRow()
        {
            if (_noRowReported) return;
            _noRowReported = true;
            Diagnostics.WarnRelease("the custom pack has no row of visuals of its own; it will wear " +
                                    "whatever this build draws for it");
        }

        /// <summary>The pack visuals table's offset.</summary>
        internal static int VisualsTableOffset => PackAssetsTable;

        /// <summary>Rebuilds the game's song-to-pack lookup.</summary>
        internal static void RefreshLookup(IntPtr packData)
        {
            IntPtr klass = FieldResolver.ClassPointer("PackData");
            if (klass == IntPtr.Zero) { Diagnostics.Warn("PackData could not be found; the pack lookup is stale"); return; }

            IntPtr method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, "_SkA", 0);
            if (method == IntPtr.Zero)
            {
                Diagnostics.Warn("PackData._SkA could not be found; the pack lookup is stale");
                return;
            }

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, packData, null, ref raised);
            if (raised != IntPtr.Zero)
                Diagnostics.Warn($"the game's pack lookup rebuild raised: {Raised.Text(raised)}");

            Diagnostics.Info("pack lookup rebuilt");
        }

        /// <summary>`DynamicStringMapping.packIdTypeMapping`.</summary>
        private static int PackIdTypeMapping = Offsets.Unresolved;

        /// <summary>The three song text mappings.</summary>
        private static int SongTitleTypeMapping = Offsets.Unresolved;
        private static int SongArtistTypeMapping = Offsets.Unresolved;
        private static int SongIllustratorTypeMapping = Offsets.Unresolved;

        private static bool _fieldsResolved;

        private static bool _fieldsMissing;

        /// <summary>Resolves the fields this file uses.</summary>
        private static bool ResolveFields()
        {
            if (_fieldsResolved) return true;
            if (_fieldsMissing) return false;

            if (FieldResolver.ClassPointer("DataAccess") == IntPtr.Zero) return false;

            PackAssetsTable = FieldResolver.Field("PackSelectSceneAssets", "packToAssets");

            PackIdTypeMapping = FieldResolver.Field("DynamicStringMapping", "packIdTypeMapping");
            SongTitleTypeMapping = FieldResolver.Field("DynamicStringMapping", "songIdTitleTypeMapping");
            SongArtistTypeMapping = FieldResolver.Field("DynamicStringMapping", "songIdArtistTypeMapping");
            SongIllustratorTypeMapping = FieldResolver.Field("DynamicStringMapping",
                                                             "jacketIllustratorNameTypeMapping");

            PackAssetsField = FieldResolver.Field("DataAccess", "_NAb");
            StringMappingField = FieldResolver.Field("DataAccess", "_mAb");

            if (PackAssetsTable < 0 || PackIdTypeMapping < 0 || SongTitleTypeMapping < 0 ||
                SongArtistTypeMapping < 0 || SongIllustratorTypeMapping < 0 ||
                PackAssetsField < 0 || StringMappingField < 0)
            {
                _fieldsMissing = true;
                Diagnostics.Error("the pack's fields could not be read by name in this build; the custom " +
                                  "pack keeps the game's own visuals and text");
                return false;
            }

            _fieldsResolved = true;
            return true;
        }

        /// <summary>The five language slots of a `TextMappingValues`.</summary>
        private static readonly int TextMappingValuesSize = 5 * IntPtr.Size;

        private static readonly int ValueEnglish = 0 * IntPtr.Size;
        private static readonly int ValueJapanese = 1 * IntPtr.Size;
        private static readonly int ValueKorean = 2 * IntPtr.Size;
        private static readonly int ValueTraditionalChinese = 3 * IntPtr.Size;
        private static readonly int ValueSimplifiedChinese = 4 * IntPtr.Size;

        /// <summary>
        /// `Mapping`'s offset once asked, `int.MinValue` before, -1 when it could not be asked.
        /// See <see cref="MappingOffset"/>.
        /// </summary>
        private static int _mappingOffset = int.MinValue;

        /// <summary>Which text write this is.</summary>
        private enum TextWhat { PackName, SongText }

        /// <summary>Gives the pack a name in all five language slots.</summary>
        internal static bool SetPackName(int packId, string name) => SetPackName(packId, SameName(name));

        /// <summary>Gives the pack a name in all five language slots.</summary>
        internal static bool SetPackName(int packId, string[] names)
        {
            // The field is read here, after `ResolveFields` — not at the call site, for the reason
            // `SongText` gives.
            if (!ResolveFields()) return false;

            return SetText(PackIdTypeMapping, packId, names, TextWhat.PackName);
        }

        /// <summary>Which text write this is.</summary>
        internal enum SongText { Title, Artist, Illustrator }

        /// <summary>Gives a song a title, artist, or illustrator.</summary>
        internal static bool SetSongText(SongText which, int songId, string text) =>
            SetSongText(which, songId, SameName(text));

        /// <summary>Gives a song a title in all five language slots.</summary>
        internal static bool SetSongText(SongText which, int songId, string[] names)
        {
            if (!ResolveFields()) return false;

            int mappingField = which == SongText.Title ? SongTitleTypeMapping
                             : which == SongText.Artist ? SongArtistTypeMapping
                             : SongIllustratorTypeMapping;

            return SetText(mappingField, songId, names, TextWhat.SongText);
        }

        /// <summary>The same string in all five language slots.</summary>
        private static string[] SameName(string text) => new[] { text, text, text, text, text };

        /// <summary>The one text write.</summary>
        private static bool SetText(int mappingField, int id, string[] names, TextWhat what)
        {
            if (!ResolveFields()) return false;

            if (names == null || names.Length != 5)
            {
                NotWritten(what, "the name does not have five language slots");
                return false;
            }

            IntPtr klass = FieldResolver.ClassPointer("DataAccess");
            if (klass == IntPtr.Zero) return false;

            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return false;

            IntPtr mapping = Memory.Ptr(statics + StringMappingField);
            if (!Memory.LooksLikeObject(mapping))
            {
                NotWritten(what, "the localisation table is missing");
                return false;
            }

            IntPtr typeMapping = Memory.Ptr(mapping + mappingField);
            if (!Memory.LooksLikeObject(typeMapping))
            {
                NotWritten(what, $"the localisation mapping at +0x{mappingField:X} is missing");
                return false;
            }

            int at = MappingOffset(typeMapping);
            if (at < 0) return false;      // asked and reported where it was asked

            IntPtr table = Memory.Ptr(typeMapping + at);
            if (!Memory.LooksLikeObject(table))
            {
                NotWritten(what, $"the localisation table at +0x{mappingField:X} is missing");
                return false;
            }

            if (!Insert(table, id, names)) return false;

            Diagnostics.Info($"localisation +0x{mappingField:X}: entry {id} set to " +
                             $"'{string.Join("/", names)}'");
            return true;
        }

        /// <summary>Says which write could not be made.</summary>
        [System.Diagnostics.Conditional("DEBUG")]
        private static void NotWritten(TextWhat what, string where) =>
            Diagnostics.Warn($"{where}; {Name(what)}");

        /// <summary>The two writes by name. Null in a Release build — see <see cref="TextWhat"/>.</summary>
        private static string Name(TextWhat what)
        {
#if DEBUG
            return what == TextWhat.PackName ? "the custom pack will have no name"
                                             : "the song will have no text of its own";
#else
            return null;
#endif
        }

        /// <summary>Where the mapping field sits.</summary>
        private static int MappingOffset(IntPtr typeMapping)
        {
            if (_mappingOffset != int.MinValue) return _mappingOffset;

            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(typeMapping);
            if (klass != IntPtr.Zero)
            {
                IntPtr field = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, "Mapping");
                if (field != IntPtr.Zero)
                {
                    int offset = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field);
                    if (offset > 0)
                    {
                        Diagnostics.Info($"StringTypeMapping.Mapping is at 0x{offset:X}");
                        return _mappingOffset = offset;
                    }
                }
            }

            foreach (var entry in InteropTypeIndex.ByName())
            {
                if (!entry.Key.Contains("StringTypeMapping")) continue;
                if (entry.Key.IndexOf('`') >= 0) continue;

                int offset = FieldResolver.Lookup(entry.Key, "Mapping");
                if (offset > 0)
                {
                    Diagnostics.Info($"StringTypeMapping.Mapping is at 0x{offset:X} (via {entry.Key})");
                    return _mappingOffset = offset;
                }
            }

            Diagnostics.Error("StringTypeMapping.Mapping could not be found on its own class in this " +
                              "build; the custom pack's texts will not be written");
            return _mappingOffset = -1;
        }

        /// <summary>Writes one entry in the text table.</summary>
        private static unsafe bool Insert(IntPtr table, int packId, string[] names)
        {
            ushort id = (ushort)packId;

            byte[] raw = new byte[TextMappingValuesSize];
            fixed (byte* values = raw)
            {
                IntPtr simplified = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(names[0]);
                IntPtr traditional = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(names[1]);
                IntPtr english = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(names[2]);
                IntPtr japanese = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(names[3]);
                IntPtr korean = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(names[4]);

                *(IntPtr*)(values + ValueEnglish) = english;
                *(IntPtr*)(values + ValueJapanese) = japanese;
                *(IntPtr*)(values + ValueKorean) = korean;
                *(IntPtr*)(values + ValueTraditionalChinese) = traditional;
                *(IntPtr*)(values + ValueSimplifiedChinese) = simplified;

                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)values;

                IntPtr dictClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(table);
                IntPtr setItem = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                    dictClass, "set_Item", 2);
                if (setItem == IntPtr.Zero)
                {
                    Diagnostics.Warn("the pack name table has no setter; the custom pack will have no name");
                    return false;
                }

                IntPtr raised = IntPtr.Zero;
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(setItem, table, (void**)args, ref raised);
                if (raised != IntPtr.Zero)
                {
                    Diagnostics.Warn($"writing the pack name raised: {Raised.Text(raised)}");
                    return false;
                }
            }

            return true;
        }
    }
}
