using System;

namespace InFalsusChartLoader
{
    /// <summary>Where things are inside the game's objects.</summary>
    internal static class Offsets
    {
        /// <summary>Layout of the IL2CPP runtime's containers.</summary>
        internal static class Runtime
        {
            /// <summary>Where an IL2CPP array's elements start.</summary>
            internal const int ArrayDataOffset = 0x20;

            /// <summary>An IL2CPP array's element count.</summary>
            internal const int ArrayLength = 0x18;

            /// <summary>List&lt;T&gt;._items.</summary>
            internal const int ListItems = 0x10;

            /// <summary>List&lt;T&gt;._size.</summary>
            internal const int ListSize = 0x18;

            /// <summary>Dictionary&lt;K,V&gt;._count.</summary>
            internal const int DictCount = 0x20;

            /// <summary>HashSet&lt;T&gt;._count.</summary>
            internal const int HashSetCount = 0x20;

            /// <summary>Il2CppClass.static_fields.</summary>
            internal const int ClassStatics = 0xB8;

            /// <summary>The class and monitor pointers every IL2CPP object starts with.</summary>
            internal const int ObjectHeader = 0x10;

            /// <summary>Where a boxed value type's payload starts.</summary>
            internal const int BoxedData = 0x10;

            /// <summary>The size of a Span&lt;byte&gt;.</summary>
            internal const int SpanSize = 0x10;

            /// <summary>A span's length field.</summary>
            internal const int SpanLength = 8;

            /// <summary>The note list inside the chart tuple.</summary>
            internal const int ChartTupleNotes = 0x10;

            /// <summary>The native read record and its info block.</summary>
            internal static class ReadRecord
            {
                internal const int Info = 0;          // the record's first word: the block the read works on
                internal const int Handle = 8;        // the second: index, length and kind packed together
                internal const int Size = 40;         // the whole record, copied into `_bGA`
                internal const int IndexMask = 0xFFFFFF;

                internal const int Offset = 8;        // in the info block: where in the file this read starts
                internal const int Length = 12;       // how many bytes it asks for
                internal const int Buffer = 32;       // where those bytes go
                internal const int BytesRead = 40;    // how many were put there
            }

            /// <summary>The state and current fields of a generated coroutine.</summary>
            internal static class Coroutine
            {
                internal const int State = 0x10;
                internal const int Current = 0x18;
            }

            /// <summary>BitArray's array and length fields.</summary>
            internal const int BitArrayArray = 0x10;
            internal const int BitArrayLength = 0x18;
        }

        /// <summary>`_fA`, the note record the decoder produces.</summary>
        internal static class Note
        {
            /// <summary>`_Ae` — the note's plane.</summary>
            internal static int Side = Unresolved;

            /// <summary>`_be` — the note's type.</summary>
            internal static int Type = Unresolved;

            /// <summary>`_Be` — judgement start.</summary>
            internal static int StartMs = Unresolved;

            /// <summary>`_ce` — judgement end.</summary>
            internal static int EndMs = Unresolved;
        }

        /// <summary>-1: a field that has not been asked yet.</summary>
        internal const int Unresolved = -1;

        /// <summary>`SongData` — the class holding every song.</summary>
        internal static class SongData
        {
            /// <summary>`allSongInfo`, `SongInfo[]`. The game's own song table.</summary>
            internal static int AllSongs = Unresolved;

            /// <summary>`_Efb`, `Dictionary&lt;string, SongId&gt;` — the name index.</summary>
            internal static int NameIndex = Unresolved;

            /// <summary>`_Ffb`, the `BitArray` with one bit per song index.</summary>
            internal static int Availability = Unresolved;
        }

        /// <summary>`PackData` — the class holding every pack.</summary>
        internal static class PackData
        {
            /// <summary>`PackInfo[]`; the array order is the order the UI shows them in.</summary>
            internal static int Packs = Unresolved;
        }

        /// <summary>`SongInfo`, one song.</summary>
        internal static class Song
        {
            /// <summary>Stride of a SongInfo element.</summary>
            internal static int Size = Unresolved;

            /// <summary>`Id`, a `SongId`.</summary>
            internal static int Id = Unresolved;

            /// <summary>`BaseName`.</summary>
            internal static int BaseName = Unresolved;

            /// <summary>`ChartInfos`, `SongChartInfo[]` — the four difficulties.</summary>
            internal static int Charts = Unresolved;

            /// <summary>`PreviewStartSeconds` / `PreviewEndSeconds`, floats in seconds.</summary>
            internal static int PreviewStart = Unresolved;
            internal static int PreviewEnd = Unresolved;

            /// <summary>`LocalizationToTitleReadingOverride` / `ArtistReadingOverride` — per-locale readings.</summary>
            internal static int TitleReading = Unresolved;
            internal static int ArtistReading = Unresolved;

            /// <summary>`RewardStyle`.</summary>
            internal static int RewardStyle = Unresolved;
        }

        /// <summary>`SongChartInfo`, one difficulty of one song.</summary>
        internal static class Chart
        {
            /// <summary>Stride of a SongInfo element.</summary>
            internal static int Size = Unresolved;

            internal static int Id = Unresolved;              // string — the chart's file name
            internal static int Available = Unresolved;       // bool
            internal static int Difficulty = Unresolved;      // ChartDifficultyFlag
            internal static int Designer = Unresolved;        // string
            internal static int JacketDesigner = Unresolved;  // string
            internal static int Rating = Unresolved;          // int
            internal static int Section = Unresolved;         // string, LevelSectionIndicator
        }

        /// <summary>`PackInfo`, one pack (a row of the pack select screen).</summary>
        internal static class Pack
        {
            /// <summary>Stride of a PackInfo element.</summary>
            internal static int Size = Unresolved;

            internal static int Id = Unresolved;     // PackId (ushort)
            internal static int Slug = Unresolved;   // string
            internal static int Songs = Unresolved;  // SongId[]
            internal static int ExternalURL = Unresolved; // string, DLC link
        }

        private static bool _resolved;

        private static bool _failed;

        /// <summary>Whether every offset has been resolved.</summary>
        internal static bool Ready => _resolved;

        /// <summary>Resolves every game field offset, once.</summary>
        internal static bool Resolve(IntPtr songData, IntPtr packData)
        {
            if (_failed) return false;
            if (_resolved) return true;

            bool ok = true;

            ok &= (SongData.AllSongs = FieldResolver.Field("SongData", "allSongInfo")) >= 0;
            ok &= (SongData.NameIndex = FieldResolver.Field("SongData", "_Efb")) >= 0;
            ok &= (SongData.Availability = FieldResolver.Field("SongData", "_Ffb")) >= 0;
            ok &= (PackData.Packs = FieldResolver.Field("PackData", "PackInfo")) >= 0;

            ok &= (Song.Id = FieldResolver.Field("SongInfo", "Id")) >= 0;
            ok &= (Song.BaseName = FieldResolver.Field("SongInfo", "BaseName")) >= 0;
            ok &= (Song.Charts = FieldResolver.Field("SongInfo", "ChartInfos")) >= 0;
            ok &= (Song.PreviewStart = FieldResolver.Field("SongInfo", "PreviewStartSeconds")) >= 0;
            ok &= (Song.PreviewEnd = FieldResolver.Field("SongInfo", "PreviewEndSeconds")) >= 0;
            ok &= (Song.TitleReading = FieldResolver.Field("SongInfo", "LocalizationToTitleReadingOverride")) >= 0;
            ok &= (Song.ArtistReading = FieldResolver.Field("SongInfo", "ArtistReadingOverride")) >= 0;
            ok &= (Song.RewardStyle = FieldResolver.Field("SongInfo", "RewardStyle")) >= 0;

            ok &= (Chart.Id = FieldResolver.Field("SongChartInfo", "Id")) >= 0;
            ok &= (Chart.Available = FieldResolver.Field("SongChartInfo", "Available")) >= 0;
            ok &= (Chart.Difficulty = FieldResolver.Field("SongChartInfo", "Difficulty")) >= 0;
            ok &= (Chart.Designer = FieldResolver.Field("SongChartInfo", "DisplayChartDesigner")) >= 0;
            ok &= (Chart.JacketDesigner = FieldResolver.Field("SongChartInfo", "DisplayJacketDesigner")) >= 0;
            ok &= (Chart.Rating = FieldResolver.Field("SongChartInfo", "Rating")) >= 0;
            ok &= (Chart.Section = FieldResolver.Field("SongChartInfo", "LevelSectionIndicator")) >= 0;

            ok &= (Pack.Id = FieldResolver.Field("PackInfo", "Id")) >= 0;
            ok &= (Pack.Slug = FieldResolver.Field("PackInfo", "Slug")) >= 0;
            ok &= (Pack.Songs = FieldResolver.Field("PackInfo", "SongIds")) >= 0;
            Pack.ExternalURL = FieldResolver.Lookup("PackInfo", "ExternalURL");

            if (ok)
            {
                IntPtr songs = Memory.Ptr(songData + SongData.AllSongs);

                Song.Size = FieldResolver.ElementSize(songs, "SongInfo.Size");
                ok &= Song.Size > 0;

                Chart.Size = FieldResolver.ElementSize(ChartTemplate(songs), "SongChartInfo.Size");
                ok &= Chart.Size > 0;

                Pack.Size = FieldResolver.ElementSize(Memory.Ptr(packData + PackData.Packs), "PackInfo.Size");
                ok &= Pack.Size > 0;
            }

            if (!ok)
            {
                _failed = true;
                Diagnostics.Error("the game's song and pack tables could not be read by name in this build; " +
                                  "no custom song will be registered");
                return false;
            }

            _resolved = true;
            return true;
        }

        /// <summary>The first song element with a live ChartInfos array.</summary>
        private static IntPtr ChartTemplate(IntPtr songs)
        {
            if (!Memory.LooksLikeObject(songs)) return IntPtr.Zero;

            int count = Memory.I32(songs + Runtime.ArrayLength);
            long bytes = Memory.ArrayBytes(songs);
            int stride = Song.Size;

            for (int i = 0; i < count && stride > 0 && (long)(i + 1) * stride <= bytes; i++)
            {
                IntPtr charts = Memory.Ptr(songs + Runtime.ArrayDataOffset + i * stride + Song.Charts);
                if (Memory.LooksLikeObject(charts) && Memory.I32(charts + Runtime.ArrayLength) > 0)
                    return charts;
            }

            return IntPtr.Zero;
        }
    }
}
