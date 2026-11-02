using System;
using System.Collections.Generic;
using HarmonyLib;
using MoonCleaners;

namespace StructureLib.Patches
{
    // Save and load of modded depots and modded-cargo canisters through structurelib.json (see Sidecar). Every hook is
    // guarded: a sidecar failure is logged and never stops the vanilla save or load.
    //
    // Save order, all on one worker thread per save (saves can overlap on different threads): MoonProgressStore.Write ->
    // Serialize, which writes the core sections (the canister list among them) and then calls the per-section savers,
    // MoonDepotSave.Write first; then Write writes the envelope and reads the bytes back. So:
    //   Serialize prefix: strip modded-cargo canisters; their records stay with this thread's SaveRun.
    //   MoonDepotSave.Write prefix: strip modded depots and write this save's own sidecar .tmp (depots + canisters).
    //   MoonDepotSave.Write finalizer: put the depots back.
    //   Serialize finalizer: put the canisters back.
    //   The .tmp is committed when the outermost of Write / Serialize on this thread finishes without an exception, so a
    //   saver after MoonDepotSave, or the envelope write, that throws leaves the previous sidecar in place.
    // A MoonDepotSave.Write outside any Serialize (not seen in game) commits in its own finalizer and keeps every loaded
    // canister record verbatim.
    [HarmonyPatch]
    internal static class SavePatches
    {
        // State of the save running on this thread. Owned here, so the finalizers put back whatever the prefixes took
        // out, even if a prefix threw halfway.
        sealed class SaveRun
        {
            internal readonly Sidecar.StrippedCanisters Canisters = new();
            // Records of this save's canister strip; null when it failed (the sidecar write then keeps the loaded ones).
            internal List<CanisterRecord> CanisterRecords;
            internal List<MoonDepotSnapshot> Depots;
            internal Sidecar.PendingWrite Pending;
            internal Exception Failed;
        }

        [ThreadStatic] static SaveRun run;
        [ThreadStatic] static int storeWriteDepth;
        [ThreadStatic] static Sidecar.PendingWrite deferred;
        [ThreadStatic] static List<MoonDepotSnapshot> looseDepots;
        [ThreadStatic] static Sidecar.PendingWrite loosePending;

        // Numbers each save snapshot on the main thread, so overlapping saves commit in snapshot order.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.SnapshotCanisters), new[] { typeof(MoonLooseDustSnapshot) }), HarmonyPostfix]
        static void SnapshotCanistersPost(MoonLooseDustSnapshot snapshot)
        {
            try { Sidecar.NoteSaveSnapshot(snapshot); }
            catch (Exception e) { CoreLog.Once("snaporder", "Save: numbering the save snapshot failed: " + e.Message, 2); }
        }

        [HarmonyPatch(typeof(MoonProgressStore), nameof(MoonProgressStore.Write)), HarmonyPrefix]
        static void StoreWritePre() => storeWriteDepth++;

        [HarmonyPatch(typeof(MoonProgressStore), nameof(MoonProgressStore.Write)), HarmonyFinalizer]
        static void StoreWriteFin(Exception __exception)
        {
            storeWriteDepth--;
            if (storeWriteDepth > 0 || deferred == null) return;
            var p = deferred;
            deferred = null;
            try { Sidecar.CommitWrite(p, __exception); }
            catch (Exception e) { CoreLog.Error("Save: committing the sidecar failed: " + e); }
        }

        [HarmonyPatch(typeof(MoonProgressStore), nameof(MoonProgressStore.Serialize)), HarmonyPrefix]
        static void SerializePre(MoonLooseDustSnapshot dust)
        {
            var r = new SaveRun();
            run = r;
            try { r.CanisterRecords = Sidecar.StripCanistersForSave(dust, r.Canisters); }
            catch (Exception e)
            {
                r.CanisterRecords = null;
                CoreLog.Error($"Save: stripping modded-cargo canisters failed ({r.Canisters.Canisters.Count} taken out so far, put back after the write); this save keeps the loaded canister records as they were: {e}");
            }
        }

        [HarmonyPatch(typeof(MoonProgressStore), nameof(MoonProgressStore.Serialize)), HarmonyFinalizer]
        static void SerializeFin(MoonLooseDustSnapshot dust, Exception __exception)
        {
            var r = run;
            run = null;
            if (r == null) return;
            try { Sidecar.PutBackCanistersAfterSave(dust, r.Canisters); }
            catch (Exception e) { CoreLog.Error("Save: putting modded-cargo canisters back failed: " + e); }
            if (r.Pending == null) return;
            var failed = __exception ?? r.Failed;
            if (storeWriteDepth > 0 && failed == null)
            {
                // Inside MoonProgressStore.Write: commit once the envelope is written too.
                deferred = r.Pending;
                return;
            }
            try { Sidecar.CommitWrite(r.Pending, failed); }
            catch (Exception e) { CoreLog.Error("Save: committing the sidecar failed: " + e); }
        }

        [HarmonyPatch(typeof(MoonDepotSave), nameof(MoonDepotSave.Write)), HarmonyPrefix]
        static void DepotWritePre(MoonLooseDustSnapshot state)
        {
            var r = run;
            var removed = new List<MoonDepotSnapshot>();
            if (r != null) { r.Depots = removed; r.Pending = null; }
            else { looseDepots = removed; loosePending = null; }
            Sidecar.PendingWrite pending = null;
            try { pending = Sidecar.StripDepots(state, removed, r?.CanisterRecords); }
            catch (Exception e) { CoreLog.Error($"Save: stripping modded structures failed ({removed.Count} taken out so far, put back after the write): {e}"); }
            if (r != null) r.Pending = pending;
            else loosePending = pending;
        }

        // Puts the stripped depots back. Inside a Serialize the commit waits for the rest of the save; a depot Write outside
        // one commits here, only if it did not throw.
        [HarmonyPatch(typeof(MoonDepotSave), nameof(MoonDepotSave.Write)), HarmonyFinalizer]
        static void DepotWriteFin(MoonLooseDustSnapshot state, Exception __exception)
        {
            var r = run;
            try { Sidecar.PutBackDepots(state, r != null ? r.Depots : looseDepots); }
            catch (Exception e) { CoreLog.Error("Save: putting modded structures back failed: " + e); }
            if (r != null)
            {
                r.Depots = null;
                if (__exception != null) r.Failed = __exception;
                return;
            }
            try { Sidecar.CommitWrite(loosePending, __exception); }
            catch (Exception e) { CoreLog.Error("Save: committing the sidecar failed: " + e); }
            looseDepots = null;
            loosePending = null;
        }

        // After FreezePatches.RestoreCanisters (normal priority), which freezes the registry: the injected canisters then
        // restore like any saved canister (as dust; see RestoreProductionPost).
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
