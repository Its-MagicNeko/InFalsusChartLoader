using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    /// <summary>The custom songs' audio: name to index, and index to bytes.</summary>
    internal static unsafe class AudioCatalog
    {
        private const string Extension = ".wav";

        /// <summary>One song's audio, and the index it answers to.</summary>
        private sealed class Entry
        {
            internal int Index;
            internal AudioCodec.Prepared Audio;
            internal bool Retired;
        }

        private static readonly Dictionary<string, Entry> ByBase =
            new Dictionary<string, Entry>(StringComparer.Ordinal);

        private static readonly Dictionary<int, Entry> ByIndex = new Dictionary<int, Entry>();

        /// <summary>Entries an open reader may still be serving; released when the scene switch finishes.</summary>
        private static readonly List<Entry> Retired = new List<Entry>();

#if DEBUG
        /// <summary>Reads served and bytes handed over.</summary>
        internal static long Served, ServedBytes;

        /// <summary>The index a song's audio answers to, or -1.</summary>
        internal static int IndexOf(string baseName) =>
            ByBase.TryGetValue(baseName, out Entry entry) ? entry.Index : -1;

        /// <summary>The byte length of a song's prepared audio, or -1.</summary>
        internal static int BytesOf(string baseName) =>
            ByBase.TryGetValue(baseName, out Entry entry) && entry.Audio != null
                ? entry.Audio.Length
                : -1;
#endif

        /// <summary>Prepares a song's audio and assigns it an index.</summary>
        internal static bool Add(string baseName, string path, out string reason)
        {
            reason = null;

            if (!Prepare(path, out AudioCodec.Prepared audio, out reason)) return false;
            return Adopt(baseName, audio, out reason);
        }

        /// <summary>Reads and transforms a file without registering it.</summary>
        internal static bool Prepare(string path, out AudioCodec.Prepared audio, out string reason) =>
            (audio = AudioCodec.Load(path, out reason)) != null;

        /// <summary>Puts prepared audio on a song's index, giving changed audio an index of its own.</summary>
        internal static bool Adopt(string baseName, AudioCodec.Prepared audio, out string reason)
        {
            reason = null;

            if (ByBase.TryGetValue(baseName, out Entry existing))
            {
                if (existing.Audio != null && Same(existing.Audio, audio)) return true;

                int oldIndex = existing.Index;
                int oldLength = existing.Audio != null ? existing.Audio.Length : -1;

                int fresh = StreamingAssets.Add(audio.Length);
                if (fresh < 0)
                {
                    reason = "the game's asset manager would not take another file";
                    return false;
                }

                Retire(existing);
                var entry = new Entry { Index = fresh, Audio = audio };
                ByBase[baseName] = entry;
                ByIndex[fresh] = entry;
                Diagnostics.Info($"audio '{baseName}' -> index {fresh}, {audio.Length} bytes " +
                                 $"(was {oldIndex}, {oldLength} bytes)");
                return true;
            }

            int index = StreamingAssets.Add(audio.Length);
            if (index < 0)
            {
                reason = "the game's asset manager would not take another file";
                return false;
            }

            var added = new Entry { Index = index, Audio = audio };
            ByBase[baseName] = added;
            ByIndex[index] = added;
            Diagnostics.Info($"audio '{baseName}' -> index {index}, {audio.Length} bytes");
            return true;
        }

        /// <summary>Drops the songs that are no longer in the model.</summary>
        internal static void Prune(ICollection<string> keep)
        {
            foreach (string name in new List<string>(ByBase.Keys))
            {
                if (keep.Contains(name)) continue;

                Entry entry = ByBase[name];
                ByBase.Remove(name);
                Retire(entry);
            }
        }

        /// <summary>Releases the audio the finished scene switch no longer has open.</summary>
        internal static void ReleaseRetired()
        {
            if (Retired.Count == 0) return;

            foreach (Entry entry in Retired)
            {
                ByIndex.Remove(entry.Index);
                entry.Audio = null;
                StreamingAssets.Release(entry.Index);
                Diagnostics.Info($"probe: audio released index={entry.Index}");
            }

            Retired.Clear();
        }

        /// <summary>Keeps a superseded entry's index and bytes for any open reader.</summary>
        private static void Retire(Entry entry)
        {
            if (entry.Retired) return;

            entry.Retired = true;
            Retired.Add(entry);
        }

        /// <summary>Whether two prepared audio buffers hold the same bytes.</summary>
        private static bool Same(AudioCodec.Prepared left, AudioCodec.Prepared right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            if (left.Length != right.Length) return false;
            return left.Bytes.AsSpan().SequenceEqual(right.Bytes);
        }

        /// <summary>Resolves a custom song's audio name to its index.</summary>
        internal static bool TryResolve(IntPtr namePtr, IntPtr indexPtr)
        {
            if (!CouldBeAudio(namePtr)) return false;

            string name = Memory.Text(namePtr, 128);
            if (name == null) return false;
            if (!name.EndsWith(Extension, StringComparison.Ordinal)) return false;

            string baseName = name.Substring(0, name.Length - Extension.Length);
            if (baseName.Length == 0) return false;
            if (!ByBase.TryGetValue(baseName, out Entry entry)) return false;

            Memory.WriteI32(indexPtr, entry.Index);
            return true;
        }

        /// <summary>Whether a name could be an audio file, without materialising it.</summary>
        private static unsafe bool CouldBeAudio(IntPtr s)
        {
            if (!Memory.LooksLikeObject(s)) return false;

            try
            {
                int length = Il2CppInterop.Runtime.IL2CPP.il2cpp_string_length(s);
                if (length < Extension.Length + 1 || length > 128) return false;

                char* chars = Il2CppInterop.Runtime.IL2CPP.il2cpp_string_chars(s);
                if (chars == null) return false;

                return chars[length - 1] == 'v';
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Serves one read of a custom song's audio.</summary>
        internal static bool TryServe(IntPtr record)
        {
            if (Hooks.CompleteRead == null) return false;

            long handle = Memory.I64(record + Offsets.Runtime.ReadRecord.Handle);
            int index = (int)(handle & Offsets.Runtime.ReadRecord.IndexMask);
            if (!ByIndex.TryGetValue(index, out Entry entry)) return false;
            if (entry.Audio == null) return false;

            IntPtr info = Memory.Ptr(record + Offsets.Runtime.ReadRecord.Info);
            if (info == IntPtr.Zero) return false;

            int offset = Memory.I32(info + Offsets.Runtime.ReadRecord.Offset);
            int size = Memory.I32(info + Offsets.Runtime.ReadRecord.Length);
            IntPtr buffer = Memory.Ptr(info + Offsets.Runtime.ReadRecord.Buffer);
            if (buffer == IntPtr.Zero || size <= 0) return false;

            byte[] bytes = entry.Audio.Bytes;
            int available = offset >= 0 && offset < bytes.Length ? bytes.Length - offset : 0;
            int count = Math.Min(size, available);

            if (count > 0) Marshal.Copy(bytes, offset, buffer, count);

#if DEBUG
            Served++;
            ServedBytes += count;
#endif

            Memory.WriteI32(info + Offsets.Runtime.ReadRecord.BytesRead, count);

            PendingReads.Finished(record);

            byte* copy = stackalloc byte[Offsets.Runtime.ReadRecord.Size];
            Buffer.MemoryCopy((void*)record, copy, Offsets.Runtime.ReadRecord.Size,
                              Offsets.Runtime.ReadRecord.Size);
            Hooks.CompleteRead((IntPtr)copy, 0);
            return true;
        }
    }
}
