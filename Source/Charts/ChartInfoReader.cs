using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace InFalsusChartLoader
{
    /// <summary>Reads and vets one chart folder's <c>if</c> file.</summary>
    internal static class ChartInfoReader
    {
        /// <summary>The audio containers this mod accepts.</summary>
        private static readonly string[] AudioExtensions = { ".wav", ".mp3", ".ogg" };

        /// <summary>The image formats a jacket may be in.</summary>
        private static readonly string[] ImageExtensions = { ".png" };

        /// <summary>The in-play background formats.</summary>
        private static readonly string[] BackgroundExtensions = { ".png", ".mp4" };

        /// <summary>The file that carries a folder's metadata. Lower case, and matched as written.</summary>
        internal const string InfoFileName = "if";

        /// <summary>How the file is read.</summary>
        private static readonly JsonDocumentOptions JsonOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>A JSON null, for the slots a one-value form leaves empty.</summary>
        private static readonly JsonElement Null = NullElement();

        private static JsonElement NullElement()
        {
            using (JsonDocument document = JsonDocument.Parse("null"))
                return document.RootElement.Clone();
        }

        /// <summary>Reads <paramref name="folder"/>'s <c>if</c> file.</summary>
        internal static bool TryRead(string folder, out ChartInfo info, out string reason)
        {
            info = null;
            reason = null;

            string ifPath = Path.Combine(folder, InfoFileName);
            if (!File.Exists(ifPath))
            {
#if DEBUG
                reason = $"no {InfoFileName} file";
#endif
                return false;
            }

            string text;
            try
            {
                text = File.ReadAllText(ifPath);
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"{InfoFileName} could not be read: {Diagnostics.Describe(e)}";
#else
                _ = e;
#endif
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text, JsonOptions);
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"{InfoFileName} is not valid JSON: {Diagnostics.Describe(e)}";
#else
                _ = e;
#endif
                return false;
            }

            using JsonDocument owned = document;
            JsonElement root = owned.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
#if DEBUG
                reason = $"{InfoFileName} is not a JSON object";
#endif
                return false;
            }

            string folderName = Path.GetFileName(folder);
            var built = new ChartInfo { Folder = folder, FolderName = folderName };

            if (!Text(root, "id", out string id, out reason)) return false;
            built.BaseName = id;

            if (!LocalizedName(root, out built.Names, out reason)) return false;
            built.Name = built.Names[0];
            if (!Text(root, "composer", out built.Composer, out reason)) return false;

            if (!Illustrators(root, out built.Illustrators, out reason)) return false;

            if (!LocalFile(root, folder, "song", AudioExtensions, out built.SongPath, out reason)) return false;

            if (!Jackets(root, folder, out built.JacketPaths, out reason)) return false;

            if (!Backgrounds(root, folder, out built.BackgroundPaths, out reason)) return false;

            if (!Preview(root, out built.PreviewStart, out built.PreviewEnd, out reason)) return false;

            if (!DifficultyValue(root, "chart", out JsonElement charts, out reason)) return false;
            if (!DifficultyValue(root, "charter", out JsonElement charters, out reason)) return false;
            if (!DifficultyValue(root, "lv", out JsonElement levels, out reason)) return false;

            JsonElement[] chartSlots = Slots(charts);
            JsonElement[] charterSlots = Slots(charters);
            JsonElement[] levelSlots = Slots(levels);
            if (!Aligned(chartSlots, charterSlots, levelSlots, out reason)) return false;

            if (!Texts(charterSlots, "charter", out built.Charters, out reason)) return false;
            if (!Levels(levelSlots, "lv", out built.Level, out reason)) return false;
            if (!ChartFiles(chartSlots, folder, out built.ChartPaths, out built.ChartNames, out built.NoChart,
                            out reason)) return false;

            info = built;
            return true;
        }

        /// <summary>Whether a folder's <c>if</c> file says the folder is a pack root.</summary>
        internal static bool IsPackFolder(string folder)
        {
            string ifPath = Path.Combine(folder, InfoFileName);
            if (!File.Exists(ifPath)) return false;

            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(ifPath), JsonOptions);
                JsonElement root = document.RootElement;
                return root.ValueKind == JsonValueKind.Object &&
                       root.TryGetProperty("pack", out JsonElement flag) &&
                       flag.ValueKind == JsonValueKind.True;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Reads a pack root's <c>if</c>; <paramref name="isPack"/> says whether it was a pack.</summary>
        internal static bool TryReadPack(string folder, out PackManifest pack, out bool isPack, out string reason)
        {
            pack = null;
            isPack = false;
            reason = null;

            string ifPath = Path.Combine(folder, InfoFileName);
            if (!File.Exists(ifPath)) return false;

            string text;
            try
            {
                text = File.ReadAllText(ifPath);
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"{InfoFileName} could not be read: {Diagnostics.Describe(e)}";
#else
                _ = e;
#endif
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text, JsonOptions);
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"{InfoFileName} is not valid JSON: {Diagnostics.Describe(e)}";
#else
                _ = e;
#endif
                return false;
            }

            using JsonDocument owned = document;
            JsonElement root = owned.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (!root.TryGetProperty("pack", out JsonElement flag) || flag.ValueKind != JsonValueKind.True)
                return false;

            isPack = true;
            var built = new PackManifest
            {
                Folder = folder,
                FolderName = Path.GetFileName(folder),
            };

            if (!Text(root, "id", out built.Id, out reason)) return false;
            if (!LocalizedName(root, out built.Names, out reason)) return false;
            built.Name = built.Names[0];
            if (!PackJackets(root, folder, out built.JacketPaths, out reason)) return false;

            pack = built;
            return true;
        }

        /// <summary>The five pack pictures, in the format's slot order.</summary>
        private static bool PackJackets(JsonElement root, string folder, out string[] paths, out string reason)
        {
            paths = new string[5];
            reason = null;

            if (!root.TryGetProperty("jacket", out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null)
            {
#if DEBUG
                reason = "\"jacket\" is missing";
#endif
                return false;
            }

            if (token.ValueKind == JsonValueKind.String)
            {
                if (!LocalFileAt(folder, "jacket", token.GetString(), ImageExtensions, out paths[0], out reason))
                    return false;
                return true;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = "\"jacket\" is not a string or an array";
#endif
                return false;
            }

            int at = 0;
            foreach (JsonElement entry in token.EnumerateArray())
            {
                if (at >= paths.Length) break;

                if (entry.ValueKind == JsonValueKind.Null)
                {
                    at++;
                    continue;
                }

                if (entry.ValueKind != JsonValueKind.String)
                {
#if DEBUG
                    reason = $"\"jacket\"[{at}] is not a string or null";
#endif
                    return false;
                }

                if (!LocalFileAt(folder, "jacket", entry.GetString(), ImageExtensions, out paths[at], out reason))
                    return false;

                at++;
            }

            if (string.IsNullOrEmpty(paths[0]))
            {
#if DEBUG
                reason = "\"jacket\"[0] is missing";
#endif
                return false;
            }

            return true;
        }

        /// <summary>The name: one string or five language strings.</summary>
        private static bool LocalizedName(JsonElement root, out string[] names, out string reason)
        {
            names = null;
            reason = null;

            if (!root.TryGetProperty("name", out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null)
            {
#if DEBUG
                reason = "\"name\" is missing";
#endif
                return false;
            }

            if (token.ValueKind == JsonValueKind.String)
            {
                string single = token.GetString();
                if (string.IsNullOrWhiteSpace(single))
                {
#if DEBUG
                    reason = "\"name\" is empty";
#endif
                    return false;
                }

                names = SameName(single);
                return true;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = "\"name\" is not a string or an array";
#endif
                return false;
            }

            var values = new List<string>(5);
            foreach (JsonElement entry in token.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(entry.GetString()))
                {
#if DEBUG
                    reason = $"\"name\"[{values.Count}] is not a non-empty string";
#endif
                    return false;
                }

                values.Add(entry.GetString());
            }

            if (values.Count == 1)
            {
                names = SameName(values[0]);
                return true;
            }

            if (values.Count == 5)
            {
                names = values.ToArray();
                return true;
            }

#if DEBUG
            reason = $"\"name\" has {values.Count} entries, not 1 or 5";
#endif
            return false;
        }

        /// <summary>The same name in all five language slots.</summary>
        private static string[] SameName(string name)
        {
            var names = new string[5];
            for (int i = 0; i < names.Length; i++) names[i] = name;
            return names;
        }

        /// <summary>A required, non-empty string field.</summary>
        private static bool Text(JsonElement root, string key, out string value, out string reason)
        {
            value = null;
            reason = null;

            if (!root.TryGetProperty(key, out JsonElement token) || token.ValueKind == JsonValueKind.Null)
            {
#if DEBUG
                reason = $"\"{key}\" is missing";
#endif
                return false;
            }

            if (token.ValueKind != JsonValueKind.String)
            {
#if DEBUG
                reason = $"\"{key}\" is not a string";
#endif
                return false;
            }

            value = token.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
#if DEBUG
                reason = $"\"{key}\" is empty";
#endif
                return false;
            }

            return true;
        }

        /// <summary>The file-name rules for one name in an `if` file.</summary>
        private static bool LocalFile(JsonElement root, string folder, string key, string[] allowed,
                                      out string path, out string reason)
        {
            path = null;
            if (!Text(root, key, out string name, out reason)) return false;

            return LocalFileAt(folder, key, name, allowed, out path, out reason);
        }

        /// <summary>The file-name rules shared by every file key.</summary>
        private static bool LocalFileAt(string folder, string key, string name, string[] allowed,
                                        out string path, out string reason)
        {
            path = null;
            reason = null;

            if (Path.GetFileName(name) != name)
            {
#if DEBUG
                reason = $"\"{key}\" names something outside the folder: {name}";
#endif
                return false;
            }

            string extension = Path.GetExtension(name).ToLowerInvariant();
            if (Array.IndexOf(allowed, extension) < 0)
            {
#if DEBUG
                reason = $"\"{key}\" is {extension}, which the game cannot decode " +
                         $"(accepted: {string.Join(", ", allowed)})";
#endif
                return false;
            }

            path = Path.Combine(folder, name);
            if (!File.Exists(path))
            {
#if DEBUG
                reason = $"\"{key}\" points at {name}, which is not in the folder";
#endif
                path = null;
                return false;
            }

            return true;
        }

        /// <summary>The jacket images.</summary>
        private static bool Jackets(JsonElement root, string folder, out string[] paths, out string reason) =>
            OneOrFour(root, folder, "jacket", ImageExtensions, false, out paths, out reason);

        /// <summary>The in-play backgrounds.</summary>
        private static bool Backgrounds(JsonElement root, string folder, out string[] paths, out string reason)
        {
            paths = null;
            reason = null;

            if (!root.TryGetProperty("background", out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null) return true;   // absent: the game's own

            return OneOrFour(root, folder, "background", BackgroundExtensions, true, out paths, out reason);
        }

        /// <summary>A key that is one file name or four.</summary>
        private static bool OneOrFour(JsonElement root, string folder, string key, string[] allowed,
                                      bool entriesMayBeNull, out string[] paths, out string reason)
        {
            paths = null;
            reason = null;

            if (!root.TryGetProperty(key, out JsonElement token) || token.ValueKind == JsonValueKind.Null)
            {
#if DEBUG
                reason = $"\"{key}\" is missing";
#endif
                return false;
            }

            if (token.ValueKind == JsonValueKind.String)
            {
                if (!LocalFile(root, folder, key, allowed, out string single, out reason)) return false;

                var same = new string[ChartInfo.Difficulties];
                for (int i = 0; i < same.Length; i++) same[i] = single;
                paths = same;
                return true;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = $"\"{key}\" is neither a name nor a list of names";
#endif
                return false;
            }

            int count = token.GetArrayLength();
            if (count != ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"{key}\" has {count} entries, not {ChartInfo.Difficulties}";
#endif
                return false;
            }

            var result = new string[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JsonElement item = token[i];
                if (item.ValueKind == JsonValueKind.Null && entriesMayBeNull) continue;   // this one: none

                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
#if DEBUG
                    reason = $"\"{key}\"[{i}] is not a name";
#endif
                    return false;
                }

                if (!LocalFileAt(folder, key, item.GetString(), allowed, out result[i], out reason))
                    return false;
            }

            paths = result;
            return true;
        }

        /// <summary>The illustrators: one name, four names, or nothing.</summary>
        private static bool Illustrators(JsonElement root, out string[] names, out string reason)
        {
            names = null;
            reason = null;

            if (!root.TryGetProperty("illust", out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null) return true;   // absent: the game's own

            if (token.ValueKind == JsonValueKind.String)
            {
                string single = token.GetString();
                if (string.IsNullOrWhiteSpace(single))
                {
#if DEBUG
                    reason = "\"illust\" is empty";
#endif
                    return false;
                }

                names = new string[ChartInfo.Difficulties];
                for (int i = 0; i < names.Length; i++) names[i] = single;
                return true;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = "\"illust\" is neither a name nor a list of names";
#endif
                return false;
            }

            int count = token.GetArrayLength();
            if (count != ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"illust\" has {count} entries, not {ChartInfo.Difficulties}";
#endif
                return false;
            }

            var result = new string[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JsonElement item = token[i];
                if (item.ValueKind == JsonValueKind.Null)
                {
                    result[i] = string.Empty;
                    continue;
                }

                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
#if DEBUG
                    reason = $"\"illust\"[{i}] is not a name";
#endif
                    return false;
                }
                result[i] = item.GetString();
            }

            names = result;
            return true;
        }

        /// <summary>The preview window, in seconds.</summary>
        private static bool Preview(JsonElement root, out float start, out float end, out string reason)
        {
            start = 0f;
            end = 0f;
            reason = null;

            if (!root.TryGetProperty("preview_seconds", out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null)
            {
#if DEBUG
                reason = "\"preview_seconds\" is missing";
#endif
                return false;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = "\"preview_seconds\" is not a list";
#endif
                return false;
            }

            int count = token.GetArrayLength();
            if (count != 2)
            {
#if DEBUG
                reason = $"\"preview_seconds\" has {count} entries, not 2";
#endif
                return false;
            }

            if (!Seconds(token[0], 0, out start, out reason)) return false;
            if (!Seconds(token[1], 1, out end, out reason)) return false;
            return true;
        }

        /// <summary>One of the two preview values.</summary>
        private static bool Seconds(JsonElement item, int index, out float value, out string reason)
        {
            value = 0f;
            reason = null;

            if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out double number))
            {
                value = (float)number;
                return true;
            }

            if (item.ValueKind == JsonValueKind.String &&
                float.TryParse(item.GetString(), NumberStyles.Float,
                               CultureInfo.InvariantCulture, out value)) return true;

#if DEBUG
            reason = $"\"preview_seconds\"[{index}] is not a number of seconds";
#endif
            return false;
        }

        /// <summary>One of the three per-difficulty keys, as its token.</summary>
        private static bool DifficultyValue(JsonElement root, string key, out JsonElement token, out string reason)
        {
            token = default;
            reason = null;

            if (!root.TryGetProperty(key, out JsonElement found))
            {
#if DEBUG
                reason = $"\"{key}\" is missing";
#endif
                return false;
            }

            if (found.ValueKind == JsonValueKind.Array && found.GetArrayLength() != ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"{key}\" has {found.GetArrayLength()} entries, not {ChartInfo.Difficulties} "
                       + "and not one";
#endif
                return false;
            }

            token = found;
            return true;
        }

        /// <summary>One of those tokens as its four difficulties.</summary>
        private static JsonElement[] Slots(JsonElement token)
        {
            var slots = new JsonElement[ChartInfo.Difficulties];
            if (token.ValueKind != JsonValueKind.Array)
            {
                for (int i = 0; i < slots.Length - 1; i++) slots[i] = Null;
                slots[slots.Length - 1] = token;
                return slots;
            }

            for (int i = 0; i < slots.Length; i++) slots[i] = token[i];
            return slots;
        }

        /// <summary>Whether the three keys agree for every difficulty.</summary>
        private static bool Aligned(JsonElement[] charts, JsonElement[] charters, JsonElement[] levels,
                                    out string reason)
        {
            reason = null;

            for (int d = 0; d < ChartInfo.Difficulties; d++)
            {
                if (charts[d].ValueKind == JsonValueKind.Null) continue;

                if (charters[d].ValueKind == JsonValueKind.Null || levels[d].ValueKind == JsonValueKind.Null)
                {
#if DEBUG
                    reason = $"difficulty {d} has a chart but no "
                           + (charters[d].ValueKind == JsonValueKind.Null ? "\"charter\"" : "\"lv\"");
#endif
                    return false;
                }
            }

            return true;
        }

        /// <summary>The four chart designers.</summary>
        private static bool Texts(JsonElement[] slots, string key, out string[] values, out string reason)
        {
            values = null;
            reason = null;

            var result = new string[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JsonElement item = slots[i];
                if (item.ValueKind == JsonValueKind.Null) continue;

                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
#if DEBUG
                    reason = $"\"{key}\"[{i}] is not a name";
#endif
                    return false;
                }
                result[i] = item.GetString();
            }

            values = result;
            return true;
        }

        /// <summary>The four difficulty ratings.</summary>
        private static bool Levels(JsonElement[] slots, string key, out int?[] values, out string reason)
        {
            values = null;
            reason = null;

            var result = new int?[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JsonElement item = slots[i];
                if (item.ValueKind == JsonValueKind.Null) continue;

                if (!Whole(item, out int level))
                {
#if DEBUG
                    reason = $"\"{key}\"[{i}] is not a whole number";
#endif
                    return false;
                }

                if (level <= 0)
                {
#if DEBUG
                    reason = $"\"{key}\"[{i}] is {level}";
#endif
                    return false;
                }
                result[i] = level;
            }

            values = result;
            return true;
        }

        /// <summary>Whether a JSON number is a whole one, and its value.</summary>
        private static bool Whole(JsonElement item, out int value)
        {
            value = 0;
            if (item.ValueKind != JsonValueKind.Number) return false;

            string raw = item.GetRawText();
            if (raw.IndexOf('.') >= 0 || raw.IndexOf('e') >= 0 || raw.IndexOf('E') >= 0) return false;

            return item.TryGetInt32(out value);
        }

        /// <summary>The chart files, in difficulty order.</summary>
        private static bool ChartFiles(JsonElement[] slots, string folder, out string[] paths,
                                       out string[] chartNames, out bool[] noChart, out string reason)
        {
            paths = null;
            chartNames = null;
            noChart = null;
            reason = null;

            var result = new string[ChartInfo.Difficulties];
            var names = new string[ChartInfo.Difficulties];
            var absent = new bool[ChartInfo.Difficulties];

            for (int i = 0; i < ChartInfo.Difficulties; i++)
            {
                JsonElement item = slots[i];
                if (item.ValueKind == JsonValueKind.Null)
                {
                    absent[i] = true;
                    continue;
                }
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
#if DEBUG
                    reason = $"\"chart\"[{i}] is not a name";
#endif
                    return false;
                }

                string name = item.GetString();
                if (Path.GetFileName(name) != name)
                {
#if DEBUG
                    reason = $"\"chart\"[{i}] names something outside the folder: {name}";
#endif
                    return false;
                }

                string path = Path.Combine(folder, name);
                if (!File.Exists(path))
                {
#if DEBUG
                    reason = $"\"chart\"[{i}] points at {name}, which is not in the folder";
#endif
                    return false;
                }

                result[i] = path;
                names[i] = name;
            }

            paths = result;
            chartNames = names;
            noChart = absent;
            return true;
        }
    }
}
