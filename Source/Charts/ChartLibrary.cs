using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace InFalsusChartLoader
{
    /// <summary>The <c>Charts/</c> folder: where it is, and what it contains.</summary>
    internal static class ChartLibrary
    {
        /// <summary>The folder name to look for, and the name in the "beside the mod folder" rule.</summary>
        private const string FolderName = "Charts";

        /// <summary>The folder the DLL was loaded from.</summary>
        internal static string ModFolder()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(location)) return Path.GetDirectoryName(location);
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"could not read this assembly's path: {Diagnostics.Describe(e)}");
            }
            return null;
        }

        /// <summary>Where the charts are, creating the folder if it is not there.</summary>
        internal static string Locate()
        {
            string mod = ModFolder();
            if (mod == null) return null;

            DirectoryInfo parent = Directory.GetParent(mod);
            string[] candidates =
            {
#if BEPINEX
                parent?.Parent == null
                    ? null
                    : Path.GetFullPath(Path.Combine(parent.Parent.FullName, FolderName)),
#endif
                parent == null ? null : Path.GetFullPath(Path.Combine(parent.FullName, FolderName)),
                Path.GetFullPath(Path.Combine(mod, FolderName)),
            };

            foreach (string candidate in candidates)
            {
                if (candidate == null) continue;
                if (Directory.Exists(candidate))
                {
                    Diagnostics.Info($"{FolderName} folder: {candidate}");
                    return candidate;
                }
            }

            string preferred = candidates[0] ?? candidates[1];
            if (preferred == null)
            {
                Diagnostics.Warn("there is nowhere to put a charts folder");
                return null;
            }

            try
            {
                Directory.CreateDirectory(preferred);
                Diagnostics.Info($"created the {FolderName} folder at {preferred}");
                Diagnostics.Info("drop one folder per song in there, each holding an `if` and its files");
                return preferred;
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"no {FolderName} folder, and one could not be created at {preferred}: " +
                                 Diagnostics.Describe(e));
                return preferred;
            }
        }

        /// <summary>The songs and the packs one scan of the charts folder produced.</summary>
        internal sealed class ImportResult
        {
            internal readonly List<ChartInfo> Songs = new List<ChartInfo>();
            internal readonly List<PackManifest> Packs = new List<PackManifest>();
        }

        /// <summary>Imports every complete chart folder under <paramref name="root"/>.</summary>
        internal static ImportResult Import(string root, Func<ChartInfo, string> validateCharts,
                                            List<string> skipped = null)
        {
            var result = new ImportResult();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                Diagnostics.Warn($"no {FolderName} folder at {root}; nothing to load");
                return result;
            }

            string[] folders;
            try
            {
                folders = Directory.GetDirectories(root);
                Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"{root} could not be listed: {Diagnostics.Describe(e)}");
                return result;
            }

            var packIds = new HashSet<string>(StringComparer.Ordinal);
            var packNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (string folder in folders)
            {
                string name = Path.GetFileName(folder);

                if (ChartInfoReader.TryReadPack(folder, out PackManifest pack, out bool isPack, out string reason))
                {
                    if (!packIds.Add(pack.Id))
                    {
                        Diagnostics.Warn($"pack folder '{name}' skipped: the pack id '{pack.Id}' is already used");
                        skipped?.Add(name);
                        continue;
                    }

                    if (!packNames.Add(pack.Name))
                    {
                        Diagnostics.Warn($"pack folder '{name}' skipped: the pack name '{pack.Name}' is already used");
                        skipped?.Add(name);
                        continue;
                    }

                    ImportPackSongs(pack, result, validateCharts, skipped);
                    result.Packs.Add(pack);
                    Diagnostics.Info($"pack folder '{name}': '{pack.Name}' ({pack.Id})");
                    continue;
                }

                if (isPack)
                {
                    Diagnostics.Warn($"pack folder '{name}' skipped: {reason}");
                    skipped?.Add(name);
                    continue;
                }

                if (!ChartInfoReader.TryRead(folder, out ChartInfo info, out reason))
                {
                    Diagnostics.Warn($"chart folder '{name}' skipped: {reason}");
                    skipped?.Add(name);
                    continue;
                }

                reason = validateCharts(info);
                if (reason != null)
                {
                    Diagnostics.Warn($"chart folder '{name}' skipped: {reason}");
                    skipped?.Add(name);
                    continue;
                }

                Diagnostics.Info($"chart folder '{name}': '{info.Name}' by {info.Composer}, " +
                                 $"{Levels(info)}");
                result.Songs.Add(info);
            }

            result.Songs.Sort((left, right) => StringComparer.Ordinal.Compare(left.BaseName, right.BaseName));
            result.Packs.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
            return result;
        }

        /// <summary>Imports the direct song folders under one pack root.</summary>
        private static void ImportPackSongs(PackManifest pack, ImportResult result,
                                            Func<ChartInfo, string> validateCharts, List<string> skipped)
        {
            string[] folders;
            try
            {
                folders = Directory.GetDirectories(pack.Folder);
                Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"pack folder '{pack.FolderName}' could not be listed: {Diagnostics.Describe(e)}");
                return;
            }

            foreach (string folder in folders)
            {
                string name = Path.GetFileName(folder);

                if (ChartInfoReader.IsPackFolder(folder))
                {
                    Diagnostics.Warn($"chart folder '{name}' in pack '{pack.FolderName}' skipped: " +
                                     "a pack root is not accepted inside a pack");
                    skipped?.Add(name);
                    continue;
                }

                if (!ChartInfoReader.TryRead(folder, out ChartInfo info, out string reason))
                {
                    Diagnostics.Warn($"chart folder '{name}' in pack '{pack.FolderName}' skipped: {reason}");
                    skipped?.Add(name);
                    continue;
                }

                reason = validateCharts(info);
                if (reason != null)
                {
                    Diagnostics.Warn($"chart folder '{name}' in pack '{pack.FolderName}' skipped: {reason}");
                    skipped?.Add(name);
                    continue;
                }

                info.Pack = pack;
                result.Songs.Add(info);
                Diagnostics.Info($"chart folder '{name}' in pack '{pack.FolderName}': '{info.Name}' by " +
                                 $"{info.Composer}, {Levels(info)}");
            }
        }

        /// <summary>The four ratings for a log line, with <c>-</c> for a difficulty left out.</summary>
        private static string Levels(ChartInfo info)
        {
            var text = new string[ChartInfo.Difficulties];
            for (int d = 0; d < text.Length; d++) text[d] = info.Level[d]?.ToString() ?? "-";
            return string.Join("/", text);
        }
    }
}
