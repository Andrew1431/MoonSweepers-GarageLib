using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace __NAME__
{
    [BepInPlugin(Guid, "__TITLE__", Version)]
    public class Plugin : BasePlugin
    {
        // Never change Guid after release: it names players' config files.
        public const string Guid = "com.__AUTHOR__.__LOWER__";
        public const string Version = "0.1.0";

        internal static ManualLogSource L;

        public override void Load()
        {
            L = Log;
            // To register content, reference StructureLib or GarageLib (see the commented ProjectReference lines in
            // __NAME__.csproj) and add [BepInDependency(<Library>.Plugin.Guid)] to this class.
            // A StructureLib structure with its own model (a .bin from shared/blender next to the csproj; the build copies
            // it beside the DLL):
            // MoonStructures.Register(new StructureDefinition { Id = "__AUTHOR__.my-machine", Name = "My machine", BaseStructure = 96,
            //     Model = "my_machine.bin" }, Guid);
            L.LogInfo($"__TITLE__ {Version} loaded");
        }
    }
}
