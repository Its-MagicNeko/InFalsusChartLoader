using MelonLoader;

[assembly: MelonInfo(typeof(InFalsusChartLoader.ChartLoaderMod), "InFalsusChartLoader",
                     InFalsusChartLoader.ModCore.Version, "MagicNeko",
                     "https://github.com/Its-MagicNeko/InFalsusChartLoader/releases/latest")]
[assembly: MelonGame("lowiro", "infalsus")]

namespace InFalsusChartLoader
{
    /// <summary>MelonLoader's entry point; every callback hands over to <see cref="ModCore"/>.</summary>
    public class ChartLoaderMod : MelonMod
    {
        public override void OnInitializeMelon() => ModCore.Loaded();

        public override void OnLateInitializeMelon() => ModCore.Initialize();

        public override void OnUpdate() => ModCore.Update();

        public override void OnDeinitializeMelon() => ModCore.Shutdown();
    }
}
