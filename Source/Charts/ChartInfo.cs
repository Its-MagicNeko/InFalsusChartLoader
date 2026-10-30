using System;

namespace InFalsusChartLoader
{
    /// <summary>One song folder under <c>Charts/</c>, as its <c>if</c> file describes it.</summary>
    /// <remarks>The four difficulties are positional: 0 Minimal, 1 Evolved, 2 Ultimate, 3 Forbidden.</remarks>
    internal sealed class ChartInfo
    {
        /// <summary>The number of difficulties a chart folder describes.</summary>
        internal const int Difficulties = 4;

        /// <summary>The folder this came from.</summary>
        internal string Folder;

        /// <summary>Display name of the song.</summary>
        internal string Name;

        /// <summary>The five language slots of the name: Simplified, Traditional, English, Japanese, Korean.</summary>
        internal string[] Names;

        /// <summary>Composer, shown as the song's artist.</summary>
        internal string Composer;

        /// <summary>Illustrators, one per difficulty, or null when the `if` file names none.</summary>
        internal string[] Illustrators;

        /// <summary>Absolute path of the audio file.</summary>
        internal string SongPath;

        /// <summary>Absolute paths of the four jacket images, in difficulty order.</summary>
        internal string[] JacketPaths;

        /// <summary>Absolute paths of the four in-play backgrounds, in difficulty order, or null when none are named.</summary>
        internal string[] BackgroundPaths;

        /// <summary>The start of the song-select preview, in seconds.</summary>
        internal float PreviewStart;

        internal float PreviewEnd;

        /// <summary>Absolute paths of the four chart files, in difficulty order.</summary>
        internal string[] ChartPaths;

        /// <summary>The `if` file's names for the four charts, in difficulty order.</summary>
        internal string[] ChartNames;

        /// <summary>Whether a difficulty has no chart, in difficulty order.</summary>
        internal bool[] NoChart;

        /// <summary>Chart designer per difficulty, or null where the `if` file leaves it out.</summary>
        internal string[] Charters;

        /// <summary>Difficulty rating per difficulty, or null where the `if` file leaves it out.</summary>
        internal int?[] Level;

        /// <summary>The folder's own name, for log messages.</summary>
        internal string FolderName;

        /// <summary>The pack this song belongs to, or null for the IFCL pack.</summary>
        internal PackManifest Pack;

        /// <summary>The name the game requests this song's charts under.</summary>
        internal string BaseName;
    }
}
