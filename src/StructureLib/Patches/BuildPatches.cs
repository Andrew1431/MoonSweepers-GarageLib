using System;
using HarmonyLib;
using MoonCleaners;

namespace StructureLib.Patches
{
    // Build and removal of modded structures. Placement itself is vanilla (TryBuildDepot validates and charges the
    // cloned definition's price; StructureRefund reads the same price); these postfixes only log and raise Removed.
    [HarmonyPatch]
    internal static class BuildPatches
    {
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryBuildDepot)), HarmonyPostfix]
        static void TryBuildDepot(bool __result, int kind, string reason)
        {
            var d = Remap.Live(kind);
            if (d == null) return;
            CoreLog.Info($"Build '{d.Id}' (kind {kind}): ok={__result} reason='{reason}'");
        }

        // The record is gone after RemoveStructureNow, so the prefix notes whether the depot was modded.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RemoveStructureNow)), HarmonyPrefix]
        static void RemoveStructureNowPre(MoonWorld __instance, int id, out int __state)
        {
            __state = -1;
            if (!MoonStructures.Frozen || MoonStructures.Disabled) return;
            try
            {
                var depots = __instance.depots;
                if (depots == null || !depots.ContainsKey(id)) return;
                var rec = depots.get_Item(id);
                if (rec != null && Remap.Live(rec.kind) != null) __state = rec.kind;
            }
            catch (Exception e) { CoreLog.Once("remove-pre", $"RemoveStructureNow({id}) prefix threw: {e.Message}", 3); }
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RemoveStructureNow)), HarmonyPostfix]
        static void RemoveStructureNow(MoonWorld __instance, int id, int __state)
        {
            if (__state < 0) return;
            var d = Remap.Live(__state);
            if (d == null) return;
            bool gone;
            try { gone = __instance.depots == null || !__instance.depots.ContainsKey(id); }
            catch (Exception) { gone = true; }
            CoreLog.Info($"Removed modded structure '{d.Id}' depot={id}" + (gone ? "" : " (record still present)"));
            if (gone) MoonStructures.RaiseRemoved(id);
        }
    }
}
