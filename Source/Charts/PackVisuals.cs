using System;
using System.Collections.Generic;

namespace InFalsusChartLoader
{
    /// <summary>The custom packs' five pictures, cloned from the game's own material rows.</summary>
    internal static unsafe class PackVisuals
    {
        /// <summary>The material slots fed by each picture, in the format's slot order.</summary>
        private static readonly string[][] SlotGroups =
        {
            new[]
            {
                "PackSelectLargeNonCompleted",
                "PackSelectLargeNonCompletedHover",
                "PackSelectLargeCompleted",
                "PackSelectLargeCompletedHover",
            },
            new[]
            {
                "BackingImageNonCompleted",
                "BackingImageCompleted",
            },
            new[]
            {
                "OutlineNonCompleted",
                "OutlineCompleted",
            },
            new[]
            {
                "PackSelectNonCompleted",
                "PackSelectNonCompletedHover",
                "PackSelectCompleted",
                "PackSelectCompletedHover",
            },
            new[]
            {
                "SongSelectLargeNonCompleted",
                "SongSelectLargeNonCompletedHover",
                "SongSelectLargeCompleted",
                "SongSelectLargeCompletedHover",
            },
        };

        private static int[][] _slotOffsets;
        private static int _firstSlot = Offsets.Unresolved;

        /// <summary>One pack's built clones and the resolved picture paths they came from.</summary>
        private sealed class Entry
        {
            internal string[] Paths;
            internal readonly List<IntPtr> Materials = new List<IntPtr>();
        }

        /// <summary>One picture texture and how many live packs show it.</summary>
        private sealed class TextureRef
        {
            internal IntPtr Texture;
            internal int Count;
        }

        private static readonly Dictionary<int, Entry> ByPack = new Dictionary<int, Entry>();
        private static readonly Dictionary<string, TextureRef> Textures =
            new Dictionary<string, TextureRef>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<IntPtr> RetiredMaterials = new List<IntPtr>();
        private static readonly List<IntPtr> RetiredTextures = new List<IntPtr>();

        /// <summary>Changes when a pack picture has to be rebuilt, so the screen knows to redraw.</summary>
        internal static int Revision;

        /// <summary>Builds a pack row's five picture groups from the game's own materials.</summary>
        internal static bool Rebuild(IntPtr packAssets, int packId, string[] jacketPaths, out string reason)
        {
            reason = null;

            if (!Memory.LooksLikeObject(packAssets) || packId < 0)
            {
                reason = "the pack's visual row is not there";
                return false;
            }
            if (jacketPaths == null || jacketPaths.Length == 0 || string.IsNullOrEmpty(jacketPaths[0]))
            {
                reason = "the pack names no cover";
                return false;
            }

            if (!PackSetup.ExtendVisuals(packAssets, packId))
            {
                reason = "the pack's visual row could not be grown";
                return false;
            }

            IntPtr table = Memory.Ptr(packAssets + PackSetup.VisualsTableOffset);
            if (!Memory.LooksLikeObject(table))
            {
                reason = "the pack visual table is not there";
                return false;
            }

            int count = Memory.I32(table + Offsets.Runtime.ArrayLength);
            int stride = count > 0
                ? (int)(Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(table) / count)
                : 0;
            if (stride <= 0 || packId >= count)
            {
                reason = "the pack's visual row is not in the table";
                return false;
            }

            int sourceRow = Hooks.PackVisualRow(table, count, stride);
            if (sourceRow < 0)
            {
                reason = "no shipped pack row has its own large card and a backing picture to copy";
                return false;
            }

            if (!ResolveSlots(out reason)) return false;

            var resolved = new string[SlotGroups.Length];
            resolved[0] = jacketPaths[0];
            for (int i = 1; i < resolved.Length; i++)
                resolved[i] = i < jacketPaths.Length && !string.IsNullOrEmpty(jacketPaths[i])
                    ? jacketPaths[i]
                    : resolved[0];

            IntPtr source = table + Offsets.Runtime.ArrayDataOffset + sourceRow * stride;
            IntPtr destination = table + Offsets.Runtime.ArrayDataOffset + packId * stride;

            if (ByPack.TryGetValue(packId, out Entry existing) &&
                SamePaths(existing.Paths, resolved) &&
                existing.Materials.Count > 0 &&
                Memory.Ptr(destination + _firstSlot) == existing.Materials[0])
                return true;

            bool hadEntry = ByPack.ContainsKey(packId);
            Retire(packId);
            if (hadEntry) Revision++;

            var textures = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in resolved)
            {
                if (textures.ContainsKey(path)) continue;

                IntPtr texture = AcquireTexture(path, out reason);
                if (texture == IntPtr.Zero)
                {
                    ReleasePaths(textures.Keys);
                    return false;
                }
                textures[path] = texture;
            }

            var entry = new Entry { Paths = resolved };
            for (int group = 0; group < SlotGroups.Length; group++)
            {
                IntPtr texture = textures[resolved[group]];
                int[] offsets = _slotOffsets[group];

                for (int i = 0; i < offsets.Length; i++)
                {
                    int offset = offsets[i];
                    IntPtr origin = Memory.Ptr(source + offset);
                    if (!Memory.LooksLikeObject(origin))
                    {
                        reason = $"the shipped pack row has no '{SlotGroups[group][i]}' material to copy";
                        RetireEntry(entry);
                        return false;
                    }

                    if (CloneInto(origin, destination, offset, texture, entry, out reason))
                        continue;

                    RetireEntry(entry);
                    return false;
                }
            }

            ByPack[packId] = entry;
            return true;
        }

        /// <summary>Drops the packs that are no longer live.</summary>
        internal static void Prune(ICollection<int> keep)
        {
            var gone = new List<int>();
            foreach (int packId in ByPack.Keys)
                if (!keep.Contains(packId)) gone.Add(packId);

            foreach (int packId in gone) Retire(packId);
        }

        /// <summary>Drops a pack whose picture file changed, so the next pass builds it again.</summary>
        internal static void Forget(string path)
        {
            var gone = new List<int>();
            foreach (KeyValuePair<int, Entry> pair in ByPack)
            {
                foreach (string known in pair.Value.Paths)
                {
                    if (!string.Equals(known, path, StringComparison.OrdinalIgnoreCase)) continue;
                    gone.Add(pair.Key);
                    break;
                }
            }

            foreach (int packId in gone) Retire(packId);
            if (gone.Count > 0) Revision++;
        }

        /// <summary>Destroys every material and texture this file built.</summary>
        internal static void ReleaseAll()
        {
            foreach (Entry entry in ByPack.Values) RetireEntry(entry);
            ByPack.Clear();

            ReleaseRetired();
            Textures.Clear();
        }

        /// <summary>Destroys the materials and textures the live packs no longer show.</summary>
        internal static void ReleaseRetired()
        {
            foreach (IntPtr material in RetiredMaterials) JacketFactory.ReleaseMaterial(material);
            RetiredMaterials.Clear();

            foreach (IntPtr texture in RetiredTextures) JacketFactory.ReleaseTexture(texture);
            RetiredTextures.Clear();
        }

        /// <summary>Moves one pack's clones out of the live set.</summary>
        private static void Retire(int packId)
        {
            if (!ByPack.TryGetValue(packId, out Entry entry)) return;

            ByPack.Remove(packId);
            RetireEntry(entry);
        }

        /// <summary>Moves one entry's clones and texture references to the retired lists.</summary>
        private static void RetireEntry(Entry entry)
        {
            if (entry == null) return;

            RetiredMaterials.AddRange(entry.Materials);
            entry.Materials.Clear();

            ReleasePaths(entry.Paths);
        }

        /// <summary>Clones one source slot and puts one of the pack's pictures on the clone.</summary>
        private static bool CloneInto(IntPtr source, IntPtr destination, int offset, IntPtr texture,
                                      Entry entry, out string reason)
        {
            reason = null;

            IntPtr clone = JacketFactory.CloneFrom(source, out reason);
            if (clone == IntPtr.Zero) return false;

            if (!JacketFactory.SetMainTexture(clone, texture, out reason))
            {
                JacketFactory.ReleaseMaterial(clone);
                return false;
            }

            if (!JacketFactory.KeepSourceAlpha(source, clone, out reason))
            {
                JacketFactory.ReleaseMaterial(clone);
                return false;
            }

            Memory.WritePtr(destination + offset, clone);
            entry.Materials.Add(clone);
            return true;
        }

        /// <summary>Resolves every material slot offset once.</summary>
        private static bool ResolveSlots(out string reason)
        {
            reason = null;
            if (_slotOffsets != null) return true;

            var groups = new int[SlotGroups.Length][];
            for (int group = 0; group < SlotGroups.Length; group++)
            {
                string[] slots = SlotGroups[group];
                var offsets = new int[slots.Length];
                for (int i = 0; i < slots.Length; i++)
                {
                    offsets[i] = FieldResolver.Field("SongSelectPackAssets", slots[i]);
                    if (offsets[i] < 0)
                    {
                        reason = $"the pack's slot '{slots[i]}' could not be read";
                        return false;
                    }
                }
                groups[group] = offsets;
            }

            _slotOffsets = groups;
            _firstSlot = groups[0][0];
            return true;
        }

        /// <summary>Gets a cached texture, building it on first use.</summary>
        private static IntPtr AcquireTexture(string path, out string reason)
        {
            reason = null;

            if (Textures.TryGetValue(path, out TextureRef known))
            {
                known.Count++;
                return known.Texture;
            }

            IntPtr texture = JacketFactory.BuildTexture(path, out reason);
            if (texture == IntPtr.Zero) return IntPtr.Zero;

            Textures[path] = new TextureRef { Texture = texture, Count = 1 };
            return texture;
        }

        /// <summary>Drops one reference to each distinct path.</summary>
        private static void ReleasePaths(IEnumerable<string> paths)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (!seen.Add(path)) continue;
                ReleaseTextureRef(path);
            }
        }

        /// <summary>Drops one reference to a texture; its destruction waits for shutdown.</summary>
        private static void ReleaseTextureRef(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!Textures.TryGetValue(path, out TextureRef known)) return;

            known.Count--;
            if (known.Count > 0) return;

            Textures.Remove(path);
            RetiredTextures.Add(known.Texture);
        }

        /// <summary>Whether two resolved picture lists are the same.</summary>
        private static bool SamePaths(string[] left, string[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;

            for (int i = 0; i < left.Length; i++)
                if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        }
    }
}
