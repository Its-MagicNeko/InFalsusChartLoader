using System;
using System.Collections.Generic;

namespace InFalsusChartLoader
{
    /// <summary>The custom jackets and the reference-to-material handshake.</summary>
    internal static class JacketCatalog
    {
        /// <summary>`SongInfo.BaseName` — the one resolved set is <see cref="Offsets.Song"/>.</summary>
        private static int SongInfoBaseName => Offsets.Song.BaseName;

        /// <summary>Per song, one material per difficulty.</summary>
        private static readonly Dictionary<string, IntPtr[]> ByBase =
            new Dictionary<string, IntPtr[]>(StringComparer.Ordinal);

        /// <summary>The songs this mod built a jacket for.</summary>
        private static readonly HashSet<string> Imported = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>What each song asked for, so a removed one can drop its cache entries.</summary>
        private static readonly Dictionary<string, ChartInfo> Folders =
            new Dictionary<string, ChartInfo>(StringComparer.Ordinal);

        /// <summary>Pictures a changed folder no longer points at.</summary>
        private static readonly List<IntPtr> Retired = new List<IntPtr>();

        /// <summary>Takes the pictures a changed folder left behind.</summary>
        internal static List<IntPtr> DrainRetired()
        {
            var copy = new List<IntPtr>(Retired);
            Retired.Clear();
            return copy;
        }

        /// <summary>The reference the game was just told to load, and its replacement material.</summary>
        private static IntPtr _armed;
        private static IntPtr _armedMaterial;

        /// <summary>Whether the armed load is the in-play background.</summary>
        private static bool _armedBackground;

#if DEBUG
        internal static int Count => ByBase.Count;

        /// <summary>Handshakes seen.</summary>
        internal static long Asked, Claimed;
#endif

        /// <summary>The `SongChartInfo` stride.</summary>
        private static int ChartInfoSize => Offsets.Chart.Size;

        /// <summary>Each custom song's `ChartInfos` array, for identifying `_ZOA` arguments.</summary>
        private static readonly List<(IntPtr Charts, ChartInfo Info)> ChartArrays =
            new List<(IntPtr, ChartInfo)>();

        /// <summary>Registers a custom song's `ChartInfos` array.</summary>
        internal static void RegisterCharts(IntPtr charts, ChartInfo info)
        {
            if (info == null || !Memory.LooksLikeObject(charts)) return;
            ChartArrays.Add((charts, info));
        }

        /// <summary>Drops the per-array list before the rebuilt records are registered.</summary>
        internal static void BeginRebuild() => ChartArrays.Clear();

        /// <summary>Every material the surviving songs still point at.</summary>
        internal static HashSet<IntPtr> LiveMaterials()
        {
            var live = new HashSet<IntPtr>();
            foreach (KeyValuePair<string, IntPtr[]> pair in ByBase)
                foreach (IntPtr material in pair.Value)
                    if (material != IntPtr.Zero) live.Add(material);
            return live;
        }

        /// <summary>Drops the songs that are no longer in the model, and answers what to release.</summary>
        internal static List<IntPtr> Prune(ICollection<string> keep)
        {
            var dropped = new List<IntPtr>();

            var live = new HashSet<IntPtr>();
            foreach (KeyValuePair<string, IntPtr[]> pair in ByBase)
            {
                if (!keep.Contains(pair.Key)) continue;
                foreach (IntPtr material in pair.Value)
                    if (material != IntPtr.Zero) live.Add(material);
            }

            foreach (string name in new List<string>(Folders.Keys))
            {
                if (keep.Contains(name)) continue;

                ChartInfo info = Folders[name];
                foreach (string path in info.JacketPaths)
                {
                    if (UsedByAnother(path, name)) continue;

                    IntPtr material = JacketFactory.Forget(path, "jacket");
                    if (material != IntPtr.Zero && !live.Contains(material)) dropped.Add(material);
                }

                if (ByBase.TryGetValue(name, out IntPtr[] materials))
                {
                    foreach (IntPtr material in materials)
                        if (material != IntPtr.Zero && !live.Contains(material)) dropped.Add(material);
                    ByBase.Remove(name);
                }

                Imported.Remove(name);
                Folders.Remove(name);
            }

            return dropped;
        }

        /// <summary>Finds the jacket material for a `_ZOA` argument.</summary>
        internal static bool FindByChart(IntPtr chartInfo, out IntPtr material)
        {
            material = IntPtr.Zero;
            if (!Memory.LooksLikeObject(chartInfo)) return false;

            foreach ((IntPtr charts, ChartInfo info) in ChartArrays)
            {
                IntPtr first = charts + Offsets.Runtime.ArrayDataOffset;
                for (int d = 0; d < ChartInfo.Difficulties; d++)
                {
                    if (first + d * ChartInfoSize != chartInfo) continue;

                    if (!ByBase.TryGetValue(info.BaseName, out IntPtr[] byDifficulty)) return false;

                    material = At(byDifficulty, d);
                    return material != IntPtr.Zero;
                }
            }

            string id = Memory.Text(Memory.Ptr(chartInfo), 128);
            if (id == null) return false;

            if (!Offsets.Ready) return false;

            int at = SongCatalog.DifficultyIndexOf(Memory.U8(chartInfo + Offsets.Chart.Difficulty));
            if (at < 0) return false;

            if (!Selection.TryReadSong(out IntPtr playing))
            {
                Diagnostics.Warn($"no song is selected to match the chart '{id}' against; the game's own " +
                                 "picture is used");
                return false;
            }

            if (!SongCatalog.ById.TryGetValue(Memory.U16(playing + Offsets.Song.Id), out ChartInfo owner))
                return false;

            if (owner.ChartNames[at] != id)
            {
                Diagnostics.Warn($"the song being played does not file a chart called '{id}' at this " +
                                 "difficulty; the game's own picture is used");
                return false;
            }

            if (!ByBase.TryGetValue(owner.BaseName, out IntPtr[] ownerMaterials)) return false;

            material = At(ownerMaterials, at);
            return material != IntPtr.Zero;
        }

        /// <summary>One of a song's four materials, or zero.</summary>
        private static IntPtr At(IntPtr[] materials, int difficulty)
        {
            if (difficulty >= 0 && difficulty < materials.Length) return materials[difficulty];

            IntPtr only = IntPtr.Zero;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == IntPtr.Zero) return IntPtr.Zero;
                if (i == 0) only = materials[i];
                else if (materials[i] != only) return IntPtr.Zero;
            }

            return only;
        }

        /// <summary>Whether another song still asks for a jacket path.</summary>
        private static bool UsedByAnother(string path, string baseName)
        {
            foreach (KeyValuePair<string, ChartInfo> pair in Folders)
            {
                if (string.Equals(pair.Key, baseName, StringComparison.Ordinal)) continue;
                if (pair.Value.JacketPaths != null &&
                    Array.IndexOf(pair.Value.JacketPaths, path) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>Builds and keeps the jackets for one folder.</summary>
        internal static bool Add(ChartInfo info, out string reason)
        {
            Imported.Add(info.BaseName);

            if (Folders.TryGetValue(info.BaseName, out ChartInfo previous))
            {
                foreach (string path in previous.JacketPaths)
                {
                    if (Array.IndexOf(info.JacketPaths, path) >= 0) continue;
                    if (UsedByAnother(path, info.BaseName)) continue;

                    IntPtr material = JacketFactory.Forget(path, "jacket");
                    if (material != IntPtr.Zero) Retired.Add(material);
                }
            }

            Folders[info.BaseName] = info;
            reason = null;

            var materials = new IntPtr[ChartInfo.Difficulties];
            var built = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
            bool all = true;

            for (int d = 0; d < materials.Length; d++)
            {
                string path = info.JacketPaths[d];

                if (!built.TryGetValue(path, out IntPtr material))
                {
                    material = JacketFactory.Build(path, out string why);
                    built[path] = material;

                    if (material == IntPtr.Zero)
                    {
                        all = false;
#if DEBUG
                        if (reason == null) reason = $"difficulty {d}: {why}";
#else
                        _ = why;
#endif
                    }
                }

                materials[d] = material;
            }

            ByBase[info.BaseName] = materials;
            return all;
        }

        /// <summary>Whether a song record is one of this mod's.</summary>
        internal static bool IsOurs(IntPtr songInfo)
        {
            string baseName = NameOf(songInfo);
            return baseName != null && Imported.Contains(baseName);
        }

        /// <summary>The base name a song record carries, or null.</summary>
        internal static string NameOf(IntPtr songInfo)
        {
            if (!Offsets.Ready) return null;
            if (!Memory.LooksLikeObject(songInfo)) return null;

            return Memory.Text(Memory.Ptr(songInfo + SongInfoBaseName), 128);
        }

        /// <summary>Finds the jacket material for a song request.</summary>
        internal static bool Find(IntPtr songInfo, out IntPtr material)
        {
            material = IntPtr.Zero;

            string baseName = NameOf(songInfo);
            if (baseName == null) return false;

            if (ByBase.TryGetValue(baseName, out IntPtr[] materials))
            {
                material = At(materials, Selection.Difficulty());
                if (material != IntPtr.Zero)
                {
#if DEBUG
                    Asked++;
#endif
                    return true;
                }
            }

            if (Imported.Contains(baseName))
            {
#if DEBUG
                Hooks.ApaAsked++;
#endif
                Diagnostics.Warn($"jacket asked for '{baseName}' - ours, but no picture was available " +
                                 "for the selected difficulty");
            }

            return false;
        }

        /// <summary>Arms the handshake for the next load.</summary>
        internal static void Arm(IntPtr reference, IntPtr material, bool background)
        {
            _armed = reference;
            _armedMaterial = material;
            _armedBackground = background;
        }

        internal static void Disarm()
        {
            _armed = IntPtr.Zero;
            _armedMaterial = IntPtr.Zero;
            _armedBackground = false;
        }

        /// <summary>Claims the armed material for a load, if it is that load.</summary>
        internal static bool TryClaim(IntPtr reference, out IntPtr material, out bool background)
        {
            material = IntPtr.Zero;
            background = false;
            if (_armed == IntPtr.Zero || reference != _armed) return false;

            material = _armedMaterial;
            background = _armedBackground;
            Disarm();
#if DEBUG
            Claimed++;
#endif
            return true;
        }
    }
}
