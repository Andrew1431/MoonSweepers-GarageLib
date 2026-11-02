using BepInEx.Logging;

namespace StructureLib
{
    // Minimal stub; replaced by the real CoreLog in the plugin task.
    internal static class CoreLog
    {
        static readonly ManualLogSource Source = Logger.CreateLogSource("StructureLib");

        internal static void Info(string m) => Source.LogInfo(m);
        internal static void Warn(string m) => Source.LogWarning(m);
        internal static void Error(string m) => Source.LogError(m);
    }
}
