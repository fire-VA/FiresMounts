using FiresCore.Logging;

namespace FiresMounts
{
    // FiresMounts' tagged logger. Thin wrapper over the shared FiresCore FiresLog engine
    // so lines carry the mod's own tag instead of Core's.
    public static class FiresLogger
    {
        private static readonly FiresLog Log = new FiresLog(
            "FiresMounts",
            () => MountConfig.VerboseLog?.Value ?? false);

        public static bool VerboseEnabled => Log.VerboseEnabled;

        public static void LogInfo(string message) => Log.Info(message);

        public static void LogVerbose(string message) => Log.Verbose(message);

        public static void LogWarning(string message) => Log.Warning(message);

        public static void LogError(string message) => Log.Error(message);
    }
}
