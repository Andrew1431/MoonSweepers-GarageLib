using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace __NAME__
{
    [BepInPlugin(Guid, "__TITLE__", Version)]
    public class Plugin : BasePlugin
    {
        // Mods list this in [BepInDependency(__NAME__.Plugin.Guid)]. Never change it after release.
        public const string Guid = "com.__AUTHOR__.__LOWER__";
        // A library is a released package: bump Version and add a README changelog line for every change.
        public const string Version = "0.1.0";

        internal static ManualLogSource L;

        public override void Load()
        {
            L = Log;
            // See StructureLib and GarageLib (src/) for how a library exposes registration APIs to mods.
            L.LogInfo($"__TITLE__ {Version} loaded");
        }
    }
}
