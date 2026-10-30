using System;
using System.Collections.Generic;
using System.IO;

namespace InFalsusChartLoader
{
    /// <summary>Watches <c>Charts/</c> and rebuilds the custom songs when its files change.</summary>
    internal static class Reload
    {
        private const string ChartsFolder = "Charts";

        /// <summary>One poll per quarter second; a change is applied after two polls see it alike.</summary>
        private const long IntervalMs = 250;

        private static string _root;
        private static Dictionary<string, (long Ticks, long Length)> _applied;
        private static Dictionary<string, (long Ticks, long Length)> _pending;
        private static long _next;

        /// <summary>Starts watching the folder the initial import used.</summary>
        internal static void Start(string root)
        {
            _root = root;
            _applied = Scan();
            _pending = null;
            _next = Environment.TickCount64 + IntervalMs;
        }

        /// <summary>Reads the folder's stamps and applies the first change it sees.</summary>
        internal static void Poll()
        {
            if (_root == null) return;

            long now = Environment.TickCount64;
            if (now < _next) return;
            _next = now + IntervalMs;

            if (Hooks.Playing && !Hooks.GameSceneAlive()) Hooks.Playing = false;

            if (!ModCore.Registered && ModCore.HasImported) return;

            Dictionary<string, (long Ticks, long Length)> current = Scan();
            if (current == null) return;

            if (Same(current, _applied))
            {
                _pending = null;
                return;
            }

            if (_pending == null || !Same(current, _pending))
            {
                _pending = current;
                return;
            }

            if (Hooks.Playing) return;
            if (!SongCatalog.TryGetAssets(out IntPtr songData, out IntPtr packData)) return;

            Apply(songData, packData, current);
        }

        /// <summary>Every folder and file under the charts root, with the stamps watched.</summary>
        private static Dictionary<string, (long Ticks, long Length)> Scan()
        {
            var map = new Dictionary<string, (long Ticks, long Length)>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(_root) || !Directory.Exists(_root)) return map;

            string[] folders;
            try
            {
                folders = Directory.GetDirectories(_root);
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"the {ChartsFolder} folder could not be listed: {Diagnostics.Describe(e)}");
                return map;
            }

            foreach (string folder in folders)
            {
                map[folder] = (-1, 0);
                AddFiles(map, folder);

                string[] songs;
                try
                {
                    songs = Directory.GetDirectories(folder);
                }
                catch (Exception)
                {
                    continue;
                }

                // A pack root that is currently empty still has to notice a song folder appearing.
                map[folder + "/*"] = (songs.Length, 0);
                if (songs.Length == 0 || !ChartInfoReader.IsPackFolder(folder)) continue;

                foreach (string song in songs)
                {
                    map[song] = (-1, 0);
                    AddFiles(map, song);
                }
            }

            return map;
        }

        /// <summary>Adds one folder's files to the watched stamps.</summary>
        private static void AddFiles(Dictionary<string, (long Ticks, long Length)> map, string folder)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception)
            {
                return;
            }

            foreach (string file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    map[file] = (info.LastWriteTimeUtc.Ticks, info.Length);
                }
                catch (Exception)
                {
                }
            }
        }

        private static bool Same(Dictionary<string, (long Ticks, long Length)> left,
                                 Dictionary<string, (long Ticks, long Length)> right)
        {
            if (left == null || right == null) return false;
            if (left.Count != right.Count) return false;

            foreach (KeyValuePair<string, (long Ticks, long Length)> pair in left)
            {
                if (!right.TryGetValue(pair.Key, out (long Ticks, long Length) value)) return false;
                if (value != pair.Value) return false;
            }

            return true;
        }

        /// <summary>Rebuilds the model from disk and puts it into the live tables.</summary>
        private static void Apply(IntPtr songData, IntPtr packData,
                                  Dictionary<string, (long Ticks, long Length)> stamps)
        {
            try
            {
                if (!StreamingAssets.Resolve())
                {
                    Diagnostics.Error("the game's asset manager is not ready; the charts were not reloaded");
                    return;
                }

                List<IntPtr> retired = ForgetChangedPictures(stamps);

                var skipped = new List<string>();
                ChartLibrary.ImportResult imported = ChartLibrary.Import(_root, ModCore.Validate, skipped);
                List<ChartInfo> songs = imported.Songs;
                foreach (string folder in skipped)
                    Diagnostics.Error($"'{folder}' was not loaded");

                var seen = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> shippedNames = SongCatalog.ShippedNames();
                var final = new List<ChartInfo>(songs.Count);
                var audio = new Dictionary<string, AudioCodec.Prepared>(StringComparer.Ordinal);

                foreach (ChartInfo info in songs)
                {
                    if (shippedNames.Contains(info.BaseName) || !seen.Add(info.BaseName))
                    {
                        Diagnostics.WarnRelease($"'{info.BaseName}' is already the name of a song this game " +
                                                $"has, so '{info.FolderName}' was not loaded. Change the id " +
                                                "in its if file.");
                        continue;
                    }

                    if (!AudioCatalog.Prepare(info.SongPath, out AudioCodec.Prepared prepared, out _))
                    {
                        Diagnostics.Error($"'{info.Name}' was left out: the audio file could not be read");
                        continue;
                    }

                    audio[info.BaseName] = prepared;
                    final.Add(info);
                }

                var oldSlots = new List<KeyValuePair<ushort, ChartInfo>>(SongCatalog.ById);
                oldSlots.Sort((left, right) => left.Key.CompareTo(right.Key));

                var ready = new List<ChartInfo>(final.Count);
                foreach (ChartInfo info in final)
                {
                    if (!AudioCatalog.Adopt(info.BaseName, audio[info.BaseName], out _))
                    {
                        Diagnostics.Error($"'{info.Name}' was left out: its audio could not be registered");
                        continue;
                    }
                    ready.Add(info);
                }

                final = ready;

                int added = 0;
                int removed = 0;
                int updated = 0;

                var oldNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (KeyValuePair<ushort, ChartInfo> slot in oldSlots)
                    oldNames.Add(slot.Value.BaseName);

                var newNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (ChartInfo info in final)
                    newNames.Add(info.BaseName);

                foreach (ChartInfo info in final)
                {
                    if (oldNames.Contains(info.BaseName)) updated++;
                    else added++;
                }

                foreach (KeyValuePair<ushort, ChartInfo> slot in oldSlots)
                    if (!newNames.Contains(slot.Value.BaseName)) removed++;

                SongSelectRefresh.CaptureSelection(out _, out string selectedName);
                int rebuilt = SongCatalog.ReplaceCustoms(songData, packData, final, imported.Packs);
                if (final.Count > 0 && rebuilt != final.Count)
                {
                    Diagnostics.Error("the custom songs could not be added to the game's song list");
                    return;
                }

                SongSelectRefresh.RestoreSelection(selectedName);

                var keep = new HashSet<string>(StringComparer.Ordinal);
                foreach (ChartInfo info in final) keep.Add(info.BaseName);

                retired.AddRange(JacketCatalog.Prune(keep));
                retired.AddRange(BackgroundCatalog.Prune(keep));
                retired.AddRange(JacketCatalog.DrainRetired());
                retired.AddRange(BackgroundCatalog.DrainRetired());
                AudioCatalog.Prune(keep);

                CustomResults.Prepare();
                CustomResults.MigrateAndSave();

                SongSelectRefresh.Structure();
                SongSelectRefresh.RefreshDisplayedPack();
                SongSelectRefresh.RefreshPackOutline();
                Hooks.RefreshHub();

                HashSet<IntPtr> livePictures = JacketCatalog.LiveMaterials();
                livePictures.UnionWith(BackgroundCatalog.LiveMaterials());
                int released = 0;
                foreach (IntPtr material in retired)
                {
                    if (livePictures.Contains(material)) continue;
                    if (JacketFactory.Release(material)) released++;
                }
                if (released > 0) Diagnostics.Info($"probe: pictures released={released}");

                ModCore.Adopt(final, imported.Packs);
                ModCore.MarkRegistered();

                Diagnostics.Load($"charts reloaded: +{added} ~{updated} -{removed}");

                _applied = Scan();
                _pending = null;
            }
            catch (Exception e)
            {
                Diagnostics.Error($"the chart reload failed: {Diagnostics.Describe(e)}");
            }
        }

        /// <summary>Drops cached pictures whose files changed, and answers what to release.</summary>
        private static List<IntPtr> ForgetChangedPictures(Dictionary<string, (long Ticks, long Length)> stamps)
        {
            var retired = new List<IntPtr>();

            foreach (KeyValuePair<string, (long Ticks, long Length)> pair in stamps)
            {
                if (_applied != null && _applied.TryGetValue(pair.Key, out (long Ticks, long Length) old) &&
                    old == pair.Value)
                    continue;

                string extension = Path.GetExtension(pair.Key).ToLowerInvariant();
                if (extension == ".png")
                {
                    PackVisuals.Forget(pair.Key);

                    IntPtr jacket = JacketFactory.Forget(pair.Key, "jacket");
                    if (jacket != IntPtr.Zero) retired.Add(jacket);

                    IntPtr background = JacketFactory.Forget(pair.Key, "background");
                    if (background != IntPtr.Zero) retired.Add(background);
                }
                else if (extension == ".mp4")
                {
                    VideoBackground.Forget(pair.Key);
                }
            }

            return retired;
        }
    }
}
