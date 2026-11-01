#if BEPINEX
extern alias UnityEngineCore;

using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngineCore::UnityEngine;

namespace InFalsusChartLoader
{
    /// <summary>BepInEx's entry point; load and unload hand over to <see cref="ModCore"/>.</summary>
    [BepInPlugin("MagicNeko-InFalsusChartLoader", "InFalsusChartLoader",
                 ModCore.Version)]
    [BepInProcess("infalsus.exe")]
    public class ChartLoaderPlugin : BasePlugin
    {
        public override void Load()
        {
            ModCore.Loaded();
            ModCore.Initialize();
            AddComponent<ChartLoaderTick>();
        }

        public override bool Unload()
        {
            ModCore.Shutdown();
            return true;
        }
    }

    /// <summary>Runs <see cref="ModCore.Update"/> once per frame for BepInEx.</summary>
    public sealed class ChartLoaderTick : MonoBehaviour
    {
        private void Update() => ModCore.Update();
    }
}
#endif
