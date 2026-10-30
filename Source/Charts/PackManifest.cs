using System;

namespace InFalsusChartLoader
{
    /// <summary>One custom pack folder, as its root <c>if</c> file describes it.</summary>
    internal sealed class PackManifest
    {
        /// <summary>The pack's identity, from the root <c>if</c>.</summary>
        internal string Id;

        /// <summary>The name shown in the pack list.</summary>
        internal string Name;

        /// <summary>The five language slots of the name: Simplified, Traditional, English, Japanese, Korean.</summary>
        internal string[] Names;

        /// <summary>The five pictures: large card, background, outline, small card, song-select button.</summary>
        internal string[] JacketPaths = new string[5];

        /// <summary>The folder this came from.</summary>
        internal string Folder;

        /// <summary>The folder's own name, for log messages.</summary>
        internal string FolderName;

        /// <summary>The pack id the game's table gave this pack, or -1 before it is registered.</summary>
        internal int PackId = -1;
    }
}
