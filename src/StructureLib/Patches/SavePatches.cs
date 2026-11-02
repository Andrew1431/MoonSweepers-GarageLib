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
        // Owned here, so the finalizer puts back whatever the prefix took out, even if the prefix threw halfway.
        [ThreadStatic] static List<MoonDepotSnapshot> strippedDepots;
        [ThreadStatic] static Sidecar.PendingWrite pendingWrite;

        [HarmonyPatch(typeof(MoonDepotSave), nameof(MoonDepotSave.Write)), HarmonyPrefix]
        static void DepotWritePre(MoonLooseDustSnapshot state)
        {
            strippedDepots = new List<MoonDepotSnapshot>();
            pendingWrite = null;
            try { pendingWrite = Sidecar.StripDepots(state, strippedDepots); }
            catch (Exception e) { CoreLog.Error($"Save: stripping modded structures failed ({strippedDepots.Count} taken out so far, put back after the write): {e}"); }
        }

        // Puts the stripped records back, then commits the sidecar only if the vanilla Write did not throw.
        [HarmonyPatch(typeof(MoonDepotSave), nameof(MoonDepotSave.Write)), HarmonyFinalizer]
        static void DepotWriteFin(MoonLooseDustSnapshot state, Exception __exception)
        {
            try { Sidecar.PutBackDepots(state, strippedDepots); }
            catch (Exception e) { CoreLog.Error("Save: putting modded structures back failed: " + e); }
            try { Sidecar.CommitWrite(pendingWrite, __exception); }
            catch (Exception e) { CoreLog.Error("Save: committing the sidecar failed: " + e); }
            strippedDepots = null;
            pendingWrite = null;
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
