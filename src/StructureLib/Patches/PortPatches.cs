using HarmonyLib;
using MoonCleaners;

namespace StructureLib.Patches
{
    // The cargo a port carries. MoonWorld.PortCargo is static and takes the definition's port struct
    // { name, position, direction, output } (no depot, no kind), and vanilla answers by name: IN_SAND is sand, any other
    // port dust. StructureLib writes a modded structure's ports into its cloned definition as "sl:<cargo byte>:<Name>"
    // (Injection.ApplyPorts), so this prefix answers from the name; any other name runs vanilla. Prefixing this static is
    // safe (recon, "Interop hazards found": a log-only prefix through two clean loads, then this one).
    [HarmonyPatch]
    internal static class PortPatches
    {
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.PortCargo)), HarmonyPrefix]
        static bool PortCargo(MoonDepotBuildDefinition.ConveyorPort port, ref MoonCargo __result)
        {
            if (port == null || !MoonStructures.Frozen || MoonStructures.Disabled) return true;
            if (!PortRules.TryDecodeCargo(port.name, out byte cargo)) return true;
            __result = (MoonCargo)cargo;
            return false;
        }
    }
}
