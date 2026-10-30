using System;
using System.Collections.Generic;

namespace InFalsusChartLoader
{
    /// <summary>The mod: imports chart folders, installs hooks, and registers the songs.</summary>
    internal static class ModCore
    {
        /// <summary>The mod's version, shared by both loader entries.</summary>
        internal const string Version = "0.3.0";

        /// <summary>What was imported, waiting for the game's data to exist so it can be registered.</summary>
        private static List<ChartInfo> _imported = new List<ChartInfo>();

        /// <summary>The pack roots the last import found.</summary>
        private static List<PackManifest> _importedPacks = new List<PackManifest>();

        private static bool _registered;

        /// <summary>Whether the first registration has been attempted.</summary>
        internal static bool Registered => _registered;

        /// <summary>Whether the first import found anything to register.</summary>
        internal static bool HasImported => _imported.Count > 0;

        /// <summary>Marks the tables as ready after a reload registered them.</summary>
        internal static void MarkRegistered() => _registered = true;

        /// <summary>Keeps the current model in step with what the reloader applied.</summary>
        internal static void Adopt(List<ChartInfo> charts, List<PackManifest> packs)
        {
            _imported = charts;
            _importedPacks = packs;
        }

#if DEBUG
        /// <summary>Frames seen, for the probe's timer.</summary>
        private static int _frames;
#endif

        /// <summary>Reports that the mod loaded.</summary>
        internal static void Loaded()
        {
            Diagnostics.Load("InFalsusChartLoader loading...");
        }

        /// <summary>Imports the chart folders and installs the hooks.</summary>
        internal static void Initialize()
        {
            try
            {
                bool codecReady = ChartCodec.Resolve();
                if (!codecReady)
                {
                    Diagnostics.Error("the game's chart decoder could not be reached; no charts will load");
                    return;
                }

                bool assetsReady = StreamingAssets.Resolve();

                string root = ChartLibrary.Locate();
                Diagnostics.Info($"charts folder: {root}");

                ChartLibrary.ImportResult imported = ChartLibrary.Import(root, Validate);
                _imported = imported.Songs;
                _importedPacks = imported.Packs;
                Diagnostics.Load($"charts: {_imported.Count} imported, {_importedPacks.Count} pack(s)");

                Reload.Start(root);

                Hooks.Install();

#if DEBUG
                Probe.Startup(root, codecReady, assetsReady, Hooks.Installed, Hooks.Attempted);
#endif
            }
            catch (Exception e)
            {
                Diagnostics.Error($"chart loading failed: {Diagnostics.Describe(e)}");
            }
        }

        /// <summary>Keeps the pack screen's live alpha mask on after the game's material pass.</summary>
        internal static void LateUpdate() => Hooks.TickPackMask();

        /// <summary>Registers the songs once the game's tables exist.</summary>
        internal static void Update()
        {
            VideoBackground.Follow();
            Reload.Poll();

#if DEBUG
            if (++_frames % 600 == 0) Diagnostics.Info("probe: counters       " + Probe.Status());
#endif

            if (_registered) return;

            try
            {
                if (!SongCatalog.TryGetAssets(out IntPtr songData, out IntPtr packData))
                {
                    if (SongCatalog.Unusable) _registered = true;
                    return;
                }

                if (_imported.Count == 0 && _importedPacks.Count == 0)
                {
                    _registered = true;
                    return;
                }

                if (_imported.Count == 0)
                {
                    _registered = true;
                    return;
                }

#if DEBUG
                PackSetup.TryGetVisuals(out IntPtr packAssetsForProbe);
                Probe.Before(songData, packData, packAssetsForProbe);
#endif

                Diagnostics.Load(FieldResolver.Stats());

                List<ChartInfo> named = SongCatalog.SettleNames(_imported, songData);
                if (named.Count == 0)
                {
                    _registered = true;
                    Diagnostics.Error("no custom song could be registered");
                    return;
                }

                List<ChartInfo> ready = RegisterAudio(named);
                if (ready.Count == 0)
                {
                    _registered = true;
                    Diagnostics.Error("no custom song could have its audio registered");
                    return;
                }

                int added = SongCatalog.Inject(songData, packData, ready, _importedPacks);
                _registered = true;

                if (added == 0)
                {
                    Diagnostics.Error("the custom songs could not be added to the game's song list");
                    return;
                }

                CustomResults.Prepare();

#if DEBUG
                PackSetup.TryGetVisuals(out IntPtr packAssetsAfter);
                Probe.After(songData, packData, packAssetsAfter, ready);
#endif

                Diagnostics.Load($"charts: {added} registered");
            }
            catch (Exception e)
            {
                _registered = true;
                Diagnostics.Error($"registering the custom songs failed: {Diagnostics.Describe(e)}");
            }
        }

        /// <summary>Stops every detour. The loader calls this once, on the way out.</summary>
        internal static void Shutdown()
        {
            PackVisuals.ReleaseAll();
            Hooks.Uninstall();
        }

        /// <summary>Registers audio for the songs and returns the ones that got it.</summary>
        private static List<ChartInfo> RegisterAudio(List<ChartInfo> charts)
        {
            if (!StreamingAssets.Resolve())
            {
                Diagnostics.Error("the game's asset manager could not be reached; no custom audio can " +
                                  "be registered, so no custom song can be played");
                return new List<ChartInfo>();
            }

            var ready = new List<ChartInfo>(charts.Count);
            foreach (ChartInfo info in charts)
            {
                if (AudioCatalog.Add(info.BaseName, info.SongPath, out string reason)) ready.Add(info);
                else Diagnostics.Error($"'{info.Name}' was left out: {reason}");
            }

            Diagnostics.Info($"audio registered for {ready.Count} of {charts.Count} songs");
            return ready;
        }

        /// <summary>Checks the charts a folder imports.</summary>
        internal static string Validate(ChartInfo info)
        {
            byte[] scratch = new byte[32];
            for (int d = 0; d < ChartInfo.Difficulties; d++)
            {
                if (info.NoChart[d]) continue;

                string path = info.ChartPaths[d];

                string seed = info.ChartNames[d];

                byte[] bytes;
                try
                {
                    bytes = System.IO.File.ReadAllBytes(path);
                }
                catch (Exception e)
                {
                    return $"difficulty {d} could not be read: {Diagnostics.Describe(e)}";
                }

                unsafe
                {
                    fixed (byte* tuple = scratch)
                    {
                        if (!ChartCodec.TryDecode(bytes, seed, (IntPtr)tuple, out string reason))
                            return $"difficulty {d}: {reason}";
                    }
                }
            }

            if (!JacketCatalog.Add(info, out string jacketReason))
                Diagnostics.Warn($"'{info.Name}' has no jacket of its own: {jacketReason}");

            if (!BackgroundCatalog.Add(info, out string backgroundReason))
                Diagnostics.Warn($"'{info.Name}' has no background of its own: {backgroundReason}");

#if DEBUG
            Probe.Imported(info, AudioCatalog.IndexOf(info.BaseName), AudioCatalog.BytesOf(info.BaseName),
                           JacketCatalog.Count);
#endif

            return null;
        }
    }
}
