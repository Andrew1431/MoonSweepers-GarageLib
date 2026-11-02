using System;
using System.Collections.Generic;
using System.Linq;
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
    //   MoonDepotSave.Write prefix: strip modded depots (and the anchor and robots-off entries naming them) and write this
    //   save's own sidecar .tmp (depots + canisters). They stay out of the snapshot for the rest of the Serialize: the
    //   later savers derive per-depot sections from state.depots (MoonStructurePoseSave writes one pose record per depot
    //   placed with a free pose, which the build menu does), and the game's read-back refuses a section naming a depot
    //   the depot section did not hold ("Structure pose ID"). The pose itself is in the depot record, so the sidecar
    //   already keeps it.
    //   MoonDepotSave.Write finalizer: nothing inside a Serialize (the depots come back in the Serialize finalizer).
    //   MoonProductionSave.Write prefix: strip modded machines' production states (matched against the stripped depots)
    //   and rewrite this save's .tmp with them.
    //   MoonProductionSave.Write finalizer: put them back.
    //   Serialize finalizer: put the depots, their anchor and robots-off entries, and the canisters back.
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
            // Stripped by the depot prefix; out of the snapshot until the Serialize finalizer puts them back.
            internal List<MoonDepotSnapshot> Depots;
            internal Sidecar.WriteStats DepotStats;
            internal readonly Sidecar.StrippedRefs Refs = new();
            // Depot id -> kind of the stripped modded depots, for the production strip.
            internal Dictionary<int, int> ModdedKinds;
            internal Sidecar.PendingWrite Pending;
            internal Exception Failed;
        }

        [ThreadStatic] static SaveRun run;
        [ThreadStatic] static int storeWriteDepth;
        [ThreadStatic] static Sidecar.PendingWrite deferred;
        [ThreadStatic] static List<MoonDepotSnapshot> looseDepots;
        [ThreadStatic] static Sidecar.PendingWrite loosePending;
        [ThreadStatic] static Sidecar.WriteStats looseStats;
        [ThreadStatic] static Sidecar.StrippedProduction production;

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
            if (storeWriteDepth == 0 && __exception != null) Sidecar.NoteVanillaFailure();
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
            PutBackRunDepots(dust, r);
            try { Sidecar.PutBackCanistersAfterSave(dust, r.Canisters); }
            catch (Exception e) { CoreLog.Error("Save: putting modded-cargo canisters back failed: " + e); }
            var failed = __exception ?? r.Failed;
            if (storeWriteDepth == 0 && failed != null) Sidecar.NoteVanillaFailure();
            if (r.Pending == null) return;
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
            // A second depot Write in one Serialize (not seen in game): the first one's depots go back before a new strip.
            if (r?.Depots != null)
            {
                CoreLog.Warn("Save: MoonDepotSave.Write ran twice in one Serialize; putting the first strip back before the second");
                PutBackRunDepots(state, r);
            }
            var removed = new List<MoonDepotSnapshot>();
            if (r != null) { r.Depots = removed; r.Pending = null; r.ModdedKinds = null; }
            else { looseDepots = removed; loosePending = null; looseStats = null; }
            Sidecar.PendingWrite pending = null;
            Sidecar.WriteStats stats = null;
            try { pending = Sidecar.StripDepots(state, removed, r?.CanisterRecords, out stats); }
            catch (Exception e) { CoreLog.Error($"Save: stripping modded structures failed ({removed.Count} taken out so far, put back after the write): {e}"); }
            if (r == null) { loosePending = pending; looseStats = stats; return; }
            r.Pending = pending;
            r.DepotStats = stats;
            // The depots stay out until the Serialize finalizer, so the sections written after this one never name them.
            try
            {
                r.ModdedKinds = SaveRefs.ModdedKinds(removed.Where(s => s != null).Select(s => (s.id, s.kind)), KindAllocator.FirstModdedKind);
                Sidecar.StripDepotRefs(state, r.ModdedKinds.Keys, r.Refs, stats);
            }
            catch (Exception e) { CoreLog.Error($"Save: stripping anchor and robots-off entries of modded structures failed ({r.Refs.Count} taken out so far, put back after the write): {e}"); }
        }

        // Inside a Serialize: notes a failure only (the depots come back in the Serialize finalizer, the commit waits for the
        // rest of the save). A depot Write outside one puts the depots back and commits here, only if it did not throw.
        [HarmonyPatch(typeof(MoonDepotSave), nameof(MoonDepotSave.Write)), HarmonyFinalizer]
        static void DepotWriteFin(MoonLooseDustSnapshot state, Exception __exception)
        {
            var r = run;
            if (r != null)
            {
                if (__exception != null) r.Failed = __exception;
                return;
            }
            try { Sidecar.PutBackDepots(state, looseDepots, looseStats); }
            catch (Exception e) { CoreLog.Error("Save: putting modded structures back failed: " + e); }
            if (__exception != null && storeWriteDepth == 0) Sidecar.NoteVanillaFailure();
            try { Sidecar.CommitWrite(loosePending, __exception); }
            catch (Exception e) { CoreLog.Error("Save: committing the sidecar failed: " + e); }
            looseDepots = null;
            loosePending = null;
            looseStats = null;
        }

        // Serialize finalizer (or a second depot Write in one Serialize): the run's depots and the entries naming them go
        // back into the snapshot. Each in its own try, so one failure never keeps the other out.
        static void PutBackRunDepots(MoonLooseDustSnapshot state, SaveRun r)
        {
            try { Sidecar.PutBackDepotRefs(state, r.Refs); }
            catch (Exception e) { CoreLog.Error("Save: putting anchor and robots-off entries of modded structures back failed: " + e); }
            try { Sidecar.PutBackDepots(state, r.Depots, r.DepotStats); }
            catch (Exception e) { CoreLog.Error("Save: putting modded structures back failed: " + e); }
            r.Depots = null;
        }

        // After FreezePatches.RestoreCanisters (normal priority), which freezes the registry: the injected canisters then
        // restore like any saved canister (as dust; see RestoreProductionPost).
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreCanisters), new[] { typeof(MoonLooseDustSnapshot) }), HarmonyPrefix, HarmonyPriority(Priority.Low)]
        static void RestoreCanisters(MoonWorld __instance, MoonLooseDustSnapshot snapshot)
        {
            try { Sidecar.InjectCanisters(__instance, snapshot); }
            catch (Exception e) { CoreLog.Error("Load: injecting modded-cargo canisters failed: " + e); }
        }

        // Inside the same Serialize, after MoonDepotSave.Write: modded machines' production snapshots leave the vanilla
        // save and join this save's pending sidecar write. Outside a Serialize (not seen in game) they are only kept out of
        // the vanilla save, and the sidecar keeps its loaded production records.
        [HarmonyPatch(typeof(MoonProductionSave), nameof(MoonProductionSave.Write)), HarmonyPrefix]
        static void ProductionWritePre(MoonLooseDustSnapshot state)
        {
            var stripped = new Sidecar.StrippedProduction();
            production = stripped;
            List<ProductionRecord> records;
            // Inside a Serialize the modded depots are out of state.depots now; match against the ones the depot prefix took.
            try { records = Sidecar.StripProduction(state, stripped, run?.ModdedKinds); }
            catch (Exception e)
            {
                CoreLog.Error($"Save: stripping modded production states failed ({stripped.Items.Count} taken out so far, put back after the write); this save keeps the loaded production records as they were: {e}");
                return;
            }
            var r = run;
            if (r?.Pending == null)
            {
                if (records.Count > 0) CoreLog.Warn($"Save: {records.Count} modded production state(s) left out of the vanilla save with no sidecar write to join; the sidecar keeps its loaded production records");
                return;
            }
            try { r.Pending = Sidecar.AddProduction(r.Pending, records); }
            catch (Exception e) { CoreLog.Error($"Save: adding {records.Count} production record(s) to the sidecar failed; this save keeps the loaded production records as they were: {e}"); }
        }

        [HarmonyPatch(typeof(MoonProductionSave), nameof(MoonProductionSave.Write)), HarmonyFinalizer]
        static void ProductionWriteFin(MoonLooseDustSnapshot state, Exception __exception)
        {
            var stripped = production;
            production = null;
            try { Sidecar.PutBackProduction(state, stripped); }
            catch (Exception e) { CoreLog.Error("Save: putting modded production states back failed: " + e); }
            if (__exception != null && run != null) run.Failed = __exception;
        }

        // After RestoreDepots: the injected structures are in world.depots, so their saved production states go into the
        // load snapshot and restore like any machine's.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreProduction), new[] { typeof(MoonLooseDustSnapshot) }), HarmonyPrefix]
        static void RestoreProductionPre(MoonLooseDustSnapshot snapshot)
        {
            try { Sidecar.InjectProduction(snapshot); }
            catch (Exception e) { CoreLog.Error("Load: injecting modded production states failed: " + e); }
        }

        // The last step of a load: injected production states are claimed (see Sidecar.ClaimProduction) and injected
        // canisters get their modded cargo (see Sidecar.ApplyCanisterCargo).
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreProduction), new[] { typeof(MoonLooseDustSnapshot) }), HarmonyPostfix]
        static void RestoreProductionPost(MoonWorld __instance)
        {
            try { Sidecar.ClaimProduction(__instance); }
            catch (Exception e) { CoreLog.Error("Load: checking restored modded production states failed: " + e); }
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

        // A finalizer, so it runs even when RestoreDepots threw: injected records the world does not hold are released and
        // written back verbatim on the next save instead of being dropped. The game's exception still propagates.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreDepots), new[] { typeof(MoonLooseDustSnapshot) }), HarmonyFinalizer]
        static void RestoreDepotsFin(MoonWorld __instance, Exception __exception)
        {
            try { Sidecar.CheckRestored(__instance, __exception); }
            catch (Exception e) { CoreLog.Error("Load: checking restored modded structures failed: " + e); }
        }
    }
}
