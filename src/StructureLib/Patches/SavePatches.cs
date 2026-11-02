using System;
using System.Collections.Generic;
using HarmonyLib;
using MoonCleaners;

namespace StructureLib.Patches
{
    // Save and load of modded depots through structurelib.json (see Sidecar). Every hook is guarded: a sidecar failure
    // is logged and never stops the vanilla save or load.
    [HarmonyPatch]
    internal static class SavePatches
    {
        [ThreadStatic] static List<MoonDepotSnapshot> strippedDepots;

        [HarmonyPatch(typeof(MoonDepotSave), nameof(MoonDepotSave.Write)), HarmonyPrefix]
        static void DepotWritePre(MoonLooseDustSnapshot state)
        {
            strippedDepots = null;
            try { strippedDepots = Sidecar.StripDepots(state); }
            catch (Exception e) { CoreLog.Error("Save: stripping modded structures failed: " + e); }
        }

        [HarmonyPatch(typeof(MoonDepotSave), nameof(MoonDepotSave.Write)), HarmonyFinalizer]
        static void DepotWriteFin(MoonLooseDustSnapshot state)
        {
            try { Sidecar.PutBackDepots(state, strippedDepots); }
            catch (Exception e) { CoreLog.Error("Save: putting modded structures back failed: " + e); }
            strippedDepots = null;
        }

        // After FreezePatches.RestoreDepots (normal priority), which freezes the registry and builds this world's cloned
        // definitions: the injected records then restore and spawn like any saved depot.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreDepots), new[] { typeof(MoonLooseDustSnapshot) }), HarmonyPrefix, HarmonyPriority(Priority.Low)]
        static void RestoreDepots(MoonWorld __instance, MoonLooseDustSnapshot snapshot)
        {
            try { Sidecar.InjectDepots(__instance, snapshot); }
            catch (Exception e) { CoreLog.Error("Load: injecting modded structures failed: " + e); }
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreDepots), new[] { typeof(MoonLooseDustSnapshot) }), HarmonyPostfix]
        static void RestoreDepotsPost(MoonWorld __instance)
        {
            try { Sidecar.CheckRestored(__instance); }
            catch (Exception e) { CoreLog.Error("Load: checking restored modded structures failed: " + e); }
        }
    }
}
