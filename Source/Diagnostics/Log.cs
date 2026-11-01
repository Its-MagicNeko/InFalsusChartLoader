using System.Diagnostics;

namespace InFalsusChartLoader
{
    /// <summary>The mod's logging: Debug-only narration, and the lines a Release keeps.</summary>
    internal static class Diagnostics
    {
        /// <summary>The loader's log sink.</summary>
        private static class Sink
        {
#if MELONLOADER
            internal static void Msg(string message) => MelonLoader.MelonLogger.Msg(message);
            internal static void Error(string message) => MelonLoader.MelonLogger.Error(message);
            internal static void Warning(string message) => MelonLoader.MelonLogger.Warning(message);
#elif BEPINEX
            private static readonly BepInEx.Logging.ManualLogSource Source =
                BepInEx.Logging.Logger.CreateLogSource("InFalsusChartLoader");

            internal static void Msg(string message) => Source.LogInfo(message);
            internal static void Error(string message) => Source.LogError(message);
            internal static void Warning(string message) => Source.LogWarning(message);
#else
#error Define MELONLOADER or BEPINEX.
#endif
        }

        /// <summary>Debug-only narration about a run.</summary>
        [Conditional("DEBUG")]
        internal static void Info(string message)
        {
            Sink.Msg(message);
        }

        /// <summary>The startup state every build reports.</summary>
        internal static void Load(string message) => Sink.Msg(message);

        /// <summary>A failure the mod has stopped doing its work for.</summary>
        internal static void Error(string message) => Sink.Error(message);

        /// <summary>A warning a Release build keeps for the player.</summary>
        internal static void WarnRelease(string message) => Sink.Warning(message);

        /// <summary>Debug-only warning about a failure the mod works around.</summary>
        [Conditional("DEBUG")]
        internal static void Warn(string message)
        {
            Sink.Warning(message);
        }

        /// <summary>An exception chain flattened into one line.</summary>
        internal static string Describe(System.Exception e)
        {
            if (e == null) return "<null>";

#if DEBUG
            var sb = new System.Text.StringBuilder();
            for (System.Exception x = e; x != null; x = x.InnerException)
            {
                if (sb.Length > 0) sb.Append(" <- ");
                sb.Append(x.GetType().Name).Append(": ").Append(x.Message);
            }
            return sb.ToString();
#else
            return e.GetType().Name;
#endif
        }
    }
}
