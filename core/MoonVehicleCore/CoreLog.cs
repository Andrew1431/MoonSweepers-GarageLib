using System.Collections.Generic;
using BepInEx.Logging;
using MoonCleaners;
using UnityEngine;

namespace MoonVehicleCore
{
    internal static class CoreLog
    {
        internal static ManualLogSource Source;
        internal static bool Verbose;
        static readonly Dictionary<string, int> counts = new();

        internal static void Info(string m) => Source?.LogInfo(m);
        internal static void Warn(string m) => Source?.LogWarning(m);
        internal static void Error(string m) => Source?.LogError(m);
        internal static void Debug(string m) { if (Verbose) Source?.LogInfo("[v] " + m); }

        // Hot-path tracing: logs the first few hits of each key, then goes quiet.
        internal static void Once(string key, string m, int limit = 5)
        {
            counts.TryGetValue(key, out int n);
            if (n >= limit) return;
            counts[key] = n + 1;
            Source?.LogInfo(n + 1 == limit ? m + " (further hits muted)" : m);
        }
    }

    // Main-thread state shared by the patches.
    internal static class CoreState
    {
        internal static MoonWorld World;
        /// <summary>Save folder of the loaded session (MoonProgressStore.Root); the sidecar lives here.</summary>
        internal static string SaveRoot;

        internal static MoonWorld FindWorld()
        {
            if (World != null && World.Pointer != System.IntPtr.Zero && World) return World;
            var all = Object.FindObjectsByType<MoonWorld>(FindObjectsSortMode.None);
            World = all != null && all.Length > 0 ? all[0] : null;
            if (World != null) CaptureRoot(World);
            return World;
        }

        internal static void CaptureRoot(MoonWorld w)
        {
            try
            {
                var store = w?.progressStore;
                string root = store?.Root;
                if (!string.IsNullOrEmpty(root) && root != SaveRoot)
                {
                    SaveRoot = root;
                    CoreLog.Info($"Save root: {root}");
                }
            }
            catch (System.Exception e) { CoreLog.Once("root", "Reading progressStore.Root failed: " + e.Message); }
        }
    }
}
