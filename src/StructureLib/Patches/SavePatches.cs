using System;
using System.Collections.Generic;
using HarmonyLib;
using MoonCleaners;

namespace StructureLib.Patches
{
    // Save and load of modded depots and modded-cargo canisters through structurelib.json (see Sidecar). Every hook is
    // guarded: a sidecar failure is logged and never stops the vanilla save or load.
    //
    // Save order (one worker thread): MoonProgressStore.Serialize writes the core sections, the canister list among them,
    // then calls the per-section savers, MoonDepotSave.Write first. So the canister strip (Serialize prefix) runs before the
    // depot prefix writes the sidecar .tmp, which therefore carries both; the depot finalizer commits it; the Serialize
    // finalizer puts the canisters back.
    [HarmonyPatch]
    internal static class SavePatches
    {
        // Owned here, so the finalizers put back whatever the prefixes took out, even if a prefix threw halfway.
        [ThreadStatic] static List<MoonDepotSnapshot> strippedDepots;
        [ThreadStatic] static Sidecar.PendingWrite pendingWrite;
        [ThreadStatic] static Sidecar.StrippedCanisters strippedCanisters;

        [HarmonyPatch(typeof(MoonProgressStore), nameof(MoonProgressStore.Serialize)), HarmonyPrefix]
        static void SerializePre(MoonLooseDustSnapshot dust)
        {
            strippedCanisters = new Sidecar.StrippedCanisters();
            try { Sidecar.StripCanistersForSave(dust, strippedCanisters); }
            catch (Exception e) { CoreLog.Error($"Save: stripping modded-cargo canisters failed ({strippedCanisters.Canisters.Count} taken out so far, put back after the write): {e}"); }
        }

        [HarmonyPatch(typeof(MoonProgressStore), nameof(MoonProgressStore.Serialize)), HarmonyFinalizer]
        static void SerializeFin(MoonLooseDustSnapshot dust)
        {
            try { Sidecar.PutBackCanistersAfterSave(dust, strippedCanisters); }
            catch (Exception e) { CoreLog.Error("Save: putting modded-cargo canisters back failed: " + e); }
            strippedCanisters = null;
        }

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

        // After FreezePatches.RestoreCanisters (normal priority), which freezes the registry: the injected canisters then
        // restore like any saved canister.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreCanisters), new[] { typeof(MoonLooseDustSnapshot) }), HarmonyPrefix, HarmonyPriority(Priority.Low)]
        static void RestoreCanisters(MoonWorld __instance, MoonLooseDustSnapshot snapshot)
        {
            try { Sidecar.InjectCanisters(__instance, snapshot); }
            catch (Exception e) { CoreLog.Error("Load: injecting modded-cargo canisters failed: " + e); }
        }

        // The last step of a load: injected canisters get their modded cargo (see Sidecar.ApplyCanisterCargo).
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreProduction), new[] { typeof(MoonLooseDustSnapshot) }), HarmonyPostfix]
        static void RestoreProductionPost(MoonWorld __instance)
        {
            try { Sidecar.ApplyCanisterCargo(__instance); }
            catch (Exception e) { CoreLog.Error("Load: giving restored canisters their modded cargo failed: " + e); }
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
