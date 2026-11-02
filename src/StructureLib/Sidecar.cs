using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MoonCleaners;
using StructureLib.Patches;

namespace StructureLib
{
    // The vanilla save never sees a modded kind: the game re-reads every save it writes and refuses (keeps the old one)
    // when MoonDepotSave.Read meets a kind above its range. So modded depot records are cut out of the save snapshot while
    // MoonDepotSave.Write runs, put back right after, and written to structurelib.json beside the save instead. On load
    // they go back into the load snapshot before RestoreDepots, so the world restores (and spawns) them like any depot.
    // Records whose structure mod is missing are kept in the file verbatim until it is installed again.
    // Canisters holding modded cargo go the same way: cut out (with their state.cargo entry) while
    // MoonProgressStore.Serialize runs, recorded by cargo id, put back into the load snapshot before RestoreCanisters and
    // given their cargo again after RestoreProduction.
    internal static class Sidecar
    {
        internal const string FileName = "structurelib.json";

        static readonly object gate = new();
        static string loadedRoot;
        static IntPtr loadedSnapshot;
        static SidecarFile loaded = new();
        // Loaded records that are in the world now (injected, and restored by RestoreDepots), with the depot id they got.
        // Only these are replaced by the live records on write; every other loaded record is written back verbatim, so a
        // skipped, failed or partial inject can never drop or duplicate a record.
        static readonly Dictionary<StructureRecord, int> inWorld = new(ReferenceEqualityComparer.Instance);
        static IntPtr injectedFor;
        // True once a load (RestoreDepots) happened since the last session start; see BeginSession.
        static bool sessionLoaded;

        /// <summary>Path of the sidecar for a save root, or null when the root is unknown.</summary>
        internal static string PathFor(string root) => string.IsNullOrEmpty(root) ? null : Path.Combine(root, FileName);

        /// <summary>Structure ids in the file loaded for this session (orphans included).</summary>
        internal static List<string> LoadedIds() { lock (gate) return loaded.Structures.Select(s => s.Id).ToList(); }

        /// <summary>Structure ids of the production records in the file loaded for this session (orphans included).</summary>
        internal static List<string> LoadedProductionIds() { lock (gate) return loaded.Production.Select(p => p.Id).ToList(); }

        /// <summary>A production record the last load restored: where, and each slot as saved and as the world held it right after RestoreProduction.</summary>
        internal sealed class ProductionRestore
        {
            internal string StructureId;
            internal int Depot;
            // Kilograms per slot as saved, and in the world right after RestoreProduction.
            internal double[] Saved, Loaded;
        }

        /// <summary>Production records restored by the last load (read by the dev self-test).</summary>
        internal static readonly List<ProductionRestore> RestoredProduction = new();

        /// <summary>Structure ids kept as orphans at the last inject.</summary>
        internal static readonly List<string> Orphans = new();

        /// <summary>What the last MoonDepotSave.Write saw. Read by the dev self-test.</summary>
        internal sealed class WriteStats
        {
            internal int Seq;
            internal int Before, Modded, Stripped, VanillaAfter, PutBack = -1;
            // Stripped depots placed with a free pose (placedPose: the build menu path), and the per-depot entries that left
            // with them (see StripDepotRefs).
            internal int Posed, AnchorsStripped, RobotsOffStripped, SupportRefs;
            internal readonly List<int> StrippedIds = new(), StrippedKinds = new();
            internal bool MainThread;
        }

        /// <summary>
        /// A sidecar written to its own .tmp by the depot Write prefix (and rewritten with this save's production records by
        /// the MoonProductionSave.Write prefix), committed once the whole vanilla write is through.
        /// </summary>
        internal sealed class PendingWrite
        {
            internal string Tmp, Path;
            /// <summary>Order of the save snapshot it was built from (see NoteSaveSnapshot); commits never go backwards.</summary>
            internal long Order;
            internal int Structures, Kept, Canisters, KeptCanisters, Production = -1, KeptProduction;
            // This save's live records, kept so the production prefix can rewrite the .tmp with its own records added.
            internal List<StructureRecord> LiveStructures;
            internal List<CanisterRecord> LiveCanisters;
        }

        // Save snapshot order: every save snapshot is numbered on the main thread when SnapshotCanisters fills it, so
        // overlapping writes can be committed in snapshot order. A snapshot that was not numbered gets the next number at write.
        static long saveOrder;
        static readonly Dictionary<IntPtr, long> snapshotOrder = new();
        static readonly Dictionary<string, long> lastCommitted = new(StringComparer.OrdinalIgnoreCase);
        static long tmpSeq;

        /// <summary>SnapshotCanisters postfix (main thread): numbers a save snapshot.</summary>
        internal static void NoteSaveSnapshot(MoonLooseDustSnapshot snapshot)
        {
            if (snapshot == null) return;
            lock (gate)
            {
                if (snapshotOrder.Count > 64) snapshotOrder.Clear();
                snapshotOrder[snapshot.Pointer] = Interlocked.Increment(ref saveOrder);
            }
        }

        static long OrderOf(MoonLooseDustSnapshot state)
        {
            lock (gate)
                return state != null && snapshotOrder.TryGetValue(state.Pointer, out long o) ? o : Interlocked.Increment(ref saveOrder);
        }

        static int writeSeq;
        internal static int WriteSeq => Volatile.Read(ref writeSeq);
        internal static volatile WriteStats LastWrite;
        static int mainThreadId = -1;

        /// <summary>Called every frame from CoreDriver (main thread) so save hooks can tell which thread they run on.</summary>
        internal static void NoteMainThread() => mainThreadId = Thread.CurrentThread.ManagedThreadId;

        // ---- session boundaries ----

        // Session left the game: the next load must read and inject afresh even if its snapshot reuses an old address.
        // Loaded records and the root are kept, because the game may still be writing this session's last save.
        internal static void EndSession()
        {
            lock (gate)
            {
                loadedSnapshot = IntPtr.Zero;
                injectedFor = IntPtr.Zero;
                canistersInjectedFor = IntPtr.Zero;
                productionInjectedFor = IntPtr.Zero;
                sessionLoaded = false;
            }
        }

        // Session entered the game. Without a load since the last session (a new game), nothing of the previous save
        // may leak into this one: forget its root and records.
        internal static void BeginSession()
        {
            lock (gate)
            {
                if (sessionLoaded) return;
                CoreLog.Info("Sidecar: in game without a save load (new game?); starting with an empty sidecar state");
                loadedRoot = null;
                loaded = new SidecarFile();
                inWorld.Clear();
                Orphans.Clear();
                canistersInWorld.Clear();
                CanisterOrphans.Clear();
                cargoPending.Clear();
                ForgetProduction();
            }
        }

        // Reloads whenever a new load snapshot shows up, so returning to the menu and loading again reads fresh data.
        internal static void LoadFor(string root, IntPtr snapshot)
        {
            lock (gate)
            {
                if (root == loadedRoot && snapshot == loadedSnapshot && loaded != null) return;
                loadedRoot = root;
                loadedSnapshot = snapshot;
                loaded = new SidecarFile();
                inWorld.Clear();
                Orphans.Clear();
                canistersInWorld.Clear();
                CanisterOrphans.Clear();
                cargoPending.Clear();
                ForgetProduction();
                string path = PathFor(root);
                if (path == null) { CoreLog.Warn("Sidecar load: save root unknown, starting empty"); return; }
                if (!File.Exists(path)) { CoreLog.Info($"Sidecar: none at {CoreLog.Redact(path)} (no modded structures saved yet)"); return; }
                try
                {
                    loaded = SidecarCodec.Deserialize(File.ReadAllText(path));
                    CoreLog.Info($"Sidecar loaded: {loaded.Structures.Count} structures, {loaded.Production.Count} production, {loaded.Canisters.Count} canisters (v{loaded.Version}, written {loaded.Written}) from {CoreLog.Redact(path)}");
                    if (loaded.Version > new SidecarFile().Version)
                        CoreLog.Warn($"Sidecar version {loaded.Version} is newer than this library ({new SidecarFile().Version}); unknown fields will be dropped on save");
                }
                catch (Exception e)
                {
                    string keep = path + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    CoreLog.Error($"Sidecar at {CoreLog.Redact(path)} is unreadable, moving it to {System.IO.Path.GetFileName(keep)} and starting empty: {e.Message}");
                    try { File.Move(path, keep, true); }
                    catch (Exception) { try { File.Copy(path, keep, true); } catch (Exception) { } }
                    loaded = new SidecarFile();
                }
            }
        }

        // ---- load ----

        internal static void InjectDepots(MoonWorld world, MoonLooseDustSnapshot snapshot)
        {
            CoreState.World = world;
            CoreState.CaptureRoot(world);
            LoadFor(CoreState.SaveRoot, snapshot?.Pointer ?? IntPtr.Zero);
            lock (gate) sessionLoaded = true;
            if (snapshot == null || snapshot.Pointer == injectedFor) return;
            if (!MoonStructures.Frozen || MoonStructures.Disabled)
            {
                lock (gate) CoreLog.Warn($"Sidecar: registry not working (frozen={MoonStructures.Frozen}, disabled={MoonStructures.Disabled}); {loaded.Structures.Count} structure records kept in the sidecar untouched");
                return;
            }
            injectedFor = snapshot.Pointer;
            snapshot.depots ??= new Il2CppSystem.Collections.Generic.List<MoonDepotSnapshot>();
            var list = snapshot.depots;
            lock (gate)
            {
                // Pass 1: decide ids and build every snapshot without touching the load snapshot. A throw here injects
                // nothing, and every record stays in the file as it was.
                var taken = new HashSet<int>();
                for (int i = 0; i < list.Count; i++) { var s = list.get_Item(i); if (s != null) taken.Add(s.id); }
                int next = snapshot.nextDepotId;
                var plan = new List<(StructureRecord r, MoonDepotSnapshot s)>();
                Orphans.Clear();
                foreach (var r in loaded.Structures)
                {
                    var d = MoonStructures.Definition(r.Id);
                    if (d == null || !MoonStructures.IsModded(d.Kind))
                    {
                        Orphans.Add(r.Id);
                        CoreLog.Warn($"Sidecar: structure '{r.Id}' (depot {r.DepotId}, owner {CoreLog.Redact(r.Owner)}) has no registered mod; kept in the sidecar untouched until it is installed again");
                        continue;
                    }
                    int id = r.DepotId;
                    if (taken.Contains(id))
                    {
                        do id = next++; while (taken.Contains(id));
                        CoreLog.Warn($"Sidecar: depot id {r.DepotId} of '{r.Id}' is already taken in the save; restoring it as depot {id}");
                    }
                    if (id >= next) next = id + 1;
                    taken.Add(id);
                    plan.Add((r, ToSnapshot(r, id, d.Kind)));
                }
                // Pass 2: add them. A record counts as in the world only once it is in the snapshot.
                snapshot.nextDepotId = next;
                foreach (var (r, s) in plan)
                {
                    list.Add(s);
                    inWorld[r] = s.id;
                    CoreLog.Info($"Sidecar: restored '{r.Id}' as depot {s.id} kind {s.kind} at ({r.X:F1},{r.Y:F1},{r.Z:F1}) owner {CoreLog.Redact(r.Owner)}, {r.Contributions?.Count ?? 0} contribution(s)");
                }
                CoreLog.Info($"Sidecar: injected {plan.Count} modded structures into the load snapshot ({Orphans.Count} orphaned), nextDepotId={snapshot.nextDepotId}");
            }
        }

        // RestoreDepots finalizer, whether it returned or threw: every injected record must now be in world.depots. One that
        // is not would be lost on the next write (the world no longer holds it), so it stops counting as in the world and
        // is kept verbatim (SaveRefs.Unrestored).
        internal static void CheckRestored(MoonWorld world, Exception vanillaFailed)
        {
            lock (gate)
            {
                if (vanillaFailed != null)
                    CoreLog.Error($"Load: RestoreDepots threw ({vanillaFailed.GetType().Name}: {vanillaFailed.Message}); {inWorld.Count} injected modded structure(s) are checked against what the world holds");
                if (inWorld.Count == 0) return;
                Il2CppSystem.Collections.Generic.Dictionary<int, MoonDepotSnapshot> depots = null;
                try { depots = world?.depots; } catch (Exception) { }
                int? KindOf(int id)
                {
                    try { return depots != null && depots.ContainsKey(id) ? depots.get_Item(id)?.kind : null; }
                    catch (Exception) { return null; }
                }
                var injected = inWorld.Select(kv => (kv.Key, kv.Value, MoonStructures.Definition(kv.Key.Id)?.Kind ?? int.MinValue)).ToList();
                foreach (var r in SaveRefs.Unrestored(injected, KindOf))
                {
                    int id = inWorld[r];
                    inWorld.Remove(r);
                    CoreLog.Error($"Sidecar: '{r.Id}' depot {id} was injected but the world did not restore it; kept in the sidecar untouched");
                }
                CoreLog.Info($"Sidecar: world restored {inWorld.Count} modded structures; world has {depots?.Count ?? 0} depots");
            }
        }

        /// <summary>A copy of the first loaded structure record with this id (null if none). Read by the dev self-test.</summary>
        internal static StructureRecord LoadedStructure(string id)
        {
            lock (gate)
            {
                var r = loaded.Structures.FirstOrDefault(s => s.Id == id);
                return r == null ? null : SidecarCodec.Deserialize(SidecarCodec.Serialize(new SidecarFile { Structures = { r } })).Structures[0];
            }
        }

        /// <summary>MoonWalletSnapshot -> ShareRecord (dollars and pending kilograms as doubles).</summary>
        internal static ShareRecord ShareOf(MoonWalletSnapshot w) =>
            new() { Owner = w.player, Amount = (double)w.dollars, Pending = (double)w.pendingKilograms };

        /// <summary>ShareRecord -> MoonWalletSnapshot.</summary>
        internal static MoonWalletSnapshot WalletOf(ShareRecord share)
        {
            var w = new MoonWalletSnapshot();
            w.player = share.Owner;
            w.dollars = new Il2CppSystem.Decimal(share.Amount);
            w.pendingKilograms = new Il2CppSystem.Decimal(share.Pending);
            return w;
        }

        static MoonDepotSnapshot ToSnapshot(StructureRecord r, int id, int kind)
        {
            var s = new MoonDepotSnapshot();
            s.id = id;
            s.kind = kind;
            s.owner = r.Owner;
            s.x = r.X; s.y = r.Y; s.z = r.Z;
            s.yaw = r.Yaw;
            s.placedPose = r.PlacedPose;
            s.qx = r.Qx; s.qy = r.Qy; s.qz = r.Qz; s.qw = r.Qw;
            s.support = r.Support;
            s.stored = r.Stored;
            s.vehicles = r.Vehicles;
            s.style = r.Style;
            var c = new Il2CppSystem.Collections.Generic.List<MoonWalletSnapshot>();
            foreach (var share in r.Contributions ?? new())
                if (share != null) c.Add(WalletOf(share));
            s.contributions = c;
            return s;
        }

        static StructureRecord ToRecord(string structureId, MoonDepotSnapshot s)
        {
            var r = new StructureRecord
            {
                Id = structureId, DepotId = s.id, Owner = s.owner,
                X = s.x, Y = s.y, Z = s.z, Yaw = s.yaw, PlacedPose = s.placedPose,
                Qx = s.qx, Qy = s.qy, Qz = s.qz, Qw = s.qw,
                Support = s.support, Stored = s.stored, Vehicles = s.vehicles, Style = s.style,
            };
            var c = s.contributions;
            if (c != null)
                for (int i = 0; i < c.Count; i++)
                {
                    var w = c.get_Item(i);
                    if (w != null) r.Contributions.Add(ShareOf(w));
                }
            return r;
        }

        // ---- save ----

        // Runs inside the MoonDepotSave.Write prefix, possibly off the main thread: touches only the save snapshot, the
        // frozen registry and the file. `removed` belongs to the caller, so whatever was taken out of state.depots is put
        // back by the finalizer even if this throws halfway.
        // Pass 1 builds every record without changing the snapshot: a throw there leaves the save untouched (the game's
        // read-back then refuses it, which loses nothing). Pass 2 only removes. The .tmp is written last, in its own try.
        // `canisters` are the records the canister strip of this same save produced (null: that strip failed or did not
        // run, so every loaded canister record is written back verbatim).
        internal static PendingWrite StripDepots(MoonLooseDustSnapshot state, List<MoonDepotSnapshot> removed, List<CanisterRecord> canisters, out WriteStats written)
        {
            written = null;
            var list = state?.depots;
            if (list == null) return null;
            var stats = new WriteStats { Before = list.Count, MainThread = Thread.CurrentThread.ManagedThreadId == mainThreadId };
            written = stats;

            // No Frozen/Disabled guard on the strip itself: before the registry freezes no modded kind can be in a world,
            // so stripping every kind in the modded range is equivalent, and it can never let one reach the vanilla save.
            var take = new List<(int index, MoonDepotSnapshot s)>();
            var records = new List<StructureRecord>();
            for (int i = 0; i < list.Count; i++)
            {
                var s = list.get_Item(i);
                if (s == null || s.kind < KindAllocator.FirstModdedKind) continue;
                take.Add((i, s));
                if (s.placedPose) stats.Posed++;
                var d = Remap.Live(s.kind);
                // Any kind in the modded range would make the game refuse the save, registered or not.
                if (d == null) CoreLog.Warn($"Save: depot {s.id} has unknown modded kind {s.kind}; left out of the vanilla save and not recorded");
                else records.Add(ToRecord(d.Id, s));
            }
            stats.Modded = take.Count;

            for (int k = take.Count - 1; k >= 0; k--)
            {
                var (index, s) = take[k];
                list.RemoveAt(index);
                removed.Add(s);
                stats.StrippedIds.Insert(0, s.id);
                stats.StrippedKinds.Insert(0, s.kind);
            }
            stats.Stripped = removed.Count;
            stats.VanillaAfter = list.Count;
            stats.Seq = Interlocked.Increment(ref writeSeq);
            LastWrite = stats;
            CoreLog.Info($"Save: moved {removed.Count} modded structures ({stats.Posed} placed with a free pose) out of the vanilla save ({stats.VanillaAfter} vanilla depots stay; write #{stats.Seq}, {(stats.MainThread ? "main thread" : "thread " + Thread.CurrentThread.ManagedThreadId)})");

            try
            {
                return WriteTmp(records, canisters, null, OrderOf(state));
            }
            catch (Exception e)
            {
                CoreLog.Error($"Save: preparing the sidecar failed; the vanilla save goes ahead WITHOUT the {records.Count} modded structure(s): {e}");
                return null;
            }
        }

        // Called by the Serialize finalizer (or the depot Write finalizer outside a Serialize). Pass 2 removed from the
        // highest index down, so walking `removed` backwards appends the records in their original relative order.
        // `stats` is the WriteStats of the same strip (null when it failed before making one).
        internal static void PutBackDepots(MoonLooseDustSnapshot state, List<MoonDepotSnapshot> removed, WriteStats stats)
        {
            if (removed == null) return;
            int n = 0;
            if (removed.Count > 0)
            {
                if (state?.depots == null) { CoreLog.Error($"Save: cannot put {removed.Count} modded structures back: the snapshot has no depot list"); return; }
                for (int i = removed.Count - 1; i >= 0; i--) { state.depots.Add(removed[i]); n++; }
            }
            if (stats != null) stats.PutBack = n;
        }

        // ---- per-depot entries ----
        // Sections written after MoonDepotSave that name depots (see the save audit in research/STRUCTURES_RECON.md):
        // MoonStructurePoseSave derives its records from state.depots (the depots stay stripped, so it never sees a modded
        // one); MoonStructureAnchorSave writes state.anchors (more MoonDepotSnapshots) and MoonRobotSettingsSave writes
        // state.robotsOff (depot ids). A robots-off entry naming a stripped depot, or an anchor of a modded kind,
        // leaves the vanilla save with it and comes back after the write. They are not kept in the sidecar: a modded
        // structure has not been seen with either (both log a warning when one turns up).

        /// <summary>Anchor and robots-off entries a strip took out, with their original indexes. Owned by the SaveRun.</summary>
        internal sealed class StrippedRefs
        {
            internal readonly List<(int index, MoonDepotSnapshot s)> Anchors = new();
            internal readonly List<(int index, int id)> RobotsOff = new();
            internal int Count => Anchors.Count + RobotsOff.Count;
        }

        // Pass 1 decides (SaveRefs), pass 2 only removes, from the highest index down. Vanilla depots standing on a stripped
        // depot (support) are only counted and logged: the depots they stand on are refused as donors, so none is expected.
        internal static void StripDepotRefs(MoonLooseDustSnapshot state, ICollection<int> strippedIds, StrippedRefs into, WriteStats stats)
        {
            if (state == null) return;
            var anchors = state.anchors;
            if (anchors != null && anchors.Count > 0)
            {
                var entries = new List<(int, int)>();
                for (int i = 0; i < anchors.Count; i++) { var a = anchors.get_Item(i); entries.Add(a == null ? (int.MinValue, 0) : (a.id, a.kind)); }
                // By kind only: whether anchor ids share the depot id space is not known, so an id match is only logged.
                var take = SaveRefs.IndexesToStrip(entries, null, KindAllocator.FirstModdedKind);
                int idMatches = entries.Count(e => strippedIds.Contains(e.Item1)) - take.Count(i => strippedIds.Contains(entries[i].Item1));
                if (idMatches > 0) CoreLog.Warn($"Save: {idMatches} vanilla-kind anchor entr(ies) carry the id of a stripped modded structure; kept in the vanilla save");
                for (int k = take.Count - 1; k >= 0; k--)
                {
                    int i = take[k];
                    var a = anchors.get_Item(i);
                    anchors.RemoveAt(i);
                    into.Anchors.Insert(0, (i, a));
                }
            }
            var off = state.robotsOff;
            if (off != null && off.Count > 0 && strippedIds.Count > 0)
            {
                var entries = new List<(int, int)>();
                for (int i = 0; i < off.Count; i++) entries.Add((off.get_Item(i), 0));
                var take = SaveRefs.IndexesToStrip(entries, strippedIds, KindAllocator.FirstModdedKind);
                for (int k = take.Count - 1; k >= 0; k--)
                {
                    int i = take[k];
                    int id = off.get_Item(i);
                    off.RemoveAt(i);
                    into.RobotsOff.Insert(0, (i, id));
                }
            }
            int supportRefs = 0;
            var depots = state.depots;
            for (int i = 0; depots != null && strippedIds.Count > 0 && i < depots.Count; i++)
            {
                var d = depots.get_Item(i);
                if (d != null && strippedIds.Contains(d.support)) supportRefs++;
            }
            if (stats != null) { stats.AnchorsStripped = into.Anchors.Count; stats.RobotsOffStripped = into.RobotsOff.Count; stats.SupportRefs = supportRefs; }
            if (into.Count > 0)
                CoreLog.Warn($"Save: {into.Anchors.Count} anchor and {into.RobotsOff.Count} robots-off entr(ies) of modded structures left out of the vanilla save (put back after the write; not kept across a reload)");
            if (supportRefs > 0)
                CoreLog.Warn($"Save: {supportRefs} vanilla structure(s) stand on a modded one; the vanilla save holds them without the structure they stand on");
        }

        internal static void PutBackDepotRefs(MoonLooseDustSnapshot state, StrippedRefs refs)
        {
            if (refs == null || refs.Count == 0) return;
            if (refs.Anchors.Count > 0)
            {
                if (state?.anchors == null) CoreLog.Error($"Save: cannot put {refs.Anchors.Count} anchor entries back: the snapshot has no anchor list");
                else foreach (var (i, a) in refs.Anchors) state.anchors.Insert(Math.Min(i, state.anchors.Count), a);
            }
            if (refs.RobotsOff.Count > 0)
            {
                if (state?.robotsOff == null) CoreLog.Error($"Save: cannot put {refs.RobotsOff.Count} robots-off entries back: the snapshot has no robots-off list");
                else foreach (var (i, id) in refs.RobotsOff) state.robotsOff.Insert(Math.Min(i, state.robotsOff.Count), id);
            }
            refs.Anchors.Clear();
            refs.RobotsOff.Clear();
        }

        // ---- refused saves ----
        static int vanillaFailures;

        /// <summary>Vanilla saves that threw (the game's read-back refusal among them) since the plugin loaded.</summary>
        internal static int VanillaFailures => Volatile.Read(ref vanillaFailures);

        /// <summary>Called by the save finalizers once per vanilla save that threw (the outermost Write, or a Serialize outside one).</summary>
        internal static void NoteVanillaFailure() => Interlocked.Increment(ref vanillaFailures);

        // ---- canisters ----
        // A canister's cargo is not in MoonCanisterSnapshot. In a save snapshot SnapshotProduction lists it in state.cargo
        // (MoonCargoSnapshot: canister id + cargo byte, one entry per non-dust canister, modded values included). The
        // canister list itself is written inline by MoonProgressStore.Serialize, before the per-section savers it calls
        // (MoonDepotSave.Write first, then the others; state.cargo is written by one of them). So the canister strip runs in
        // the Serialize prefix, before the depot prefix writes the sidecar .tmp, and the put-back in the Serialize finalizer.

        /// <summary>What a canister strip took out, with the original indexes. Owned by the caller (the Serialize prefix).</summary>
        internal sealed class StrippedCanisters
        {
            internal readonly List<(int index, MoonCanisterSnapshot s)> Canisters = new();
            internal readonly List<(int index, MoonCargoSnapshot c)> Cargo = new();
        }

        /// <summary>What the last canister strip of a real save saw (diagnostics).</summary>
        internal sealed class CanisterStats
        {
            internal int Before, Stripped, CargoStripped, Recorded, PutBack = -1;
        }

        internal static volatile CanisterStats LastCanisterStrip;

        // Loaded canister records that are in the world now (injected into the load snapshot), with their id. Only these are
        // replaced by the live records on write; every other loaded canister record is written back verbatim.
        static readonly Dictionary<CanisterRecord, int> canistersInWorld = new(ReferenceEqualityComparer.Instance);
        static IntPtr canistersInjectedFor;

        /// <summary>Cargo ids of loaded canister records kept as orphans at the last inject.</summary>
        internal static readonly List<string> CanisterOrphans = new();

        /// <summary>
        /// Takes every canister whose cargo is in the modded range out of <paramref name="state"/>, with its state.cargo
        /// entry (and any other state.cargo entry holding a modded value), into <paramref name="stripped"/>, and returns the
        /// records of the registered ones. Canisters whose id is in <paramref name="pendingIds"/> (injected at load but never
        /// given their cargo, so dust now) are taken out too, without a record: their loaded record is still unclaimed and
        /// is written back verbatim. Pass 1 builds every record without touching the snapshot; pass 2 only removes.
        /// Touches nothing but the snapshot and the frozen registry, so it may run off the main thread.
        /// </summary>
        internal static List<CanisterRecord> StripCanisters(MoonLooseDustSnapshot state, StrippedCanisters stripped, ICollection<int> pendingIds = null)
        {
            var records = new List<CanisterRecord>();
            var canisters = state?.canisters;
            var cargo = state?.cargo;
            bool anyPending = pendingIds != null && pendingIds.Count > 0;
            if ((cargo == null || cargo.Count == 0) && !anyPending) return records;

            // No Frozen/Disabled guard on the strip itself (as for depots): a value in the modded range can only come
            // from a working registry, and stripping every one of them can never let one reach the vanilla save.
            var cargoOf = new Dictionary<int, byte>();
            var takeCargo = new List<int>();
            for (int i = 0; cargo != null && i < cargo.Count; i++)
            {
                var c = cargo.get_Item(i);
                if (c == null || c.cargo < Rules.FirstModdedCargoValue) continue;
                takeCargo.Add(i);
                cargoOf.TryAdd(c.id, c.cargo);
            }
            if (takeCargo.Count == 0 && !anyPending) return records;

            var takeCanisters = new List<int>();
            var unknown = new List<(int id, byte value)>();
            var pendingTaken = new List<int>();
            var matched = new HashSet<int>();
            for (int i = 0; canisters != null && i < canisters.Count; i++)
            {
                var s = canisters.get_Item(i);
                if (s == null) continue;
                if (cargoOf.TryGetValue(s.id, out byte value))
                {
                    takeCanisters.Add(i);
                    matched.Add(s.id);
                    var d = CargoPatches.Live((MoonCargo)value);
                    if (d == null) unknown.Add((s.id, value));
                    else records.Add(ToCanisterRecord(d.Id, s));
                }
                else if (anyPending && pendingIds.Contains(s.id))
                {
                    takeCanisters.Add(i);
                    pendingTaken.Add(s.id);
                }
            }

            for (int k = takeCanisters.Count - 1; k >= 0; k--)
            {
                int i = takeCanisters[k];
                var s = canisters.get_Item(i);
                canisters.RemoveAt(i);
                stripped.Canisters.Insert(0, (i, s));
            }
            for (int k = takeCargo.Count - 1; k >= 0; k--)
            {
                int i = takeCargo[k];
                var c = cargo.get_Item(i);
                cargo.RemoveAt(i);
                stripped.Cargo.Insert(0, (i, c));
            }
            foreach (var (id, value) in unknown)
                CoreLog.Warn($"Save: canister {id} holds unregistered modded cargo {value}; left out of the vanilla save and not recorded");
            foreach (var id in cargoOf.Keys.Where(id => !matched.Contains(id)))
                CoreLog.Warn($"Save: modded cargo entry {id}:{cargoOf[id]} has no canister in the save snapshot; left out of the vanilla save");
            foreach (var id in pendingTaken)
                CoreLog.Error($"Save: canister {id} was restored from the sidecar but never got its modded cargo (no RestoreProduction after the load?); left out of the vanilla save, its sidecar record is kept as it was");
            return records;
        }

        /// <summary>Puts stripped canisters and cargo entries back at their original indexes; returns how many canisters.</summary>
        internal static int PutBackCanisters(MoonLooseDustSnapshot state, StrippedCanisters stripped)
        {
            if (stripped == null) return 0;
            int n = 0;
            if (stripped.Canisters.Count > 0)
            {
                if (state?.canisters == null) CoreLog.Error($"Save: cannot put {stripped.Canisters.Count} modded-cargo canisters back: the snapshot has no canister list");
                else
                    foreach (var (i, s) in stripped.Canisters) { state.canisters.Insert(Math.Min(i, state.canisters.Count), s); n++; }
            }
            if (stripped.Cargo.Count > 0)
            {
                if (state?.cargo == null) CoreLog.Error($"Save: cannot put {stripped.Cargo.Count} modded cargo entries back: the snapshot has no cargo list");
                else
                    foreach (var (i, c) in stripped.Cargo) state.cargo.Insert(Math.Min(i, state.cargo.Count), c);
            }
            return n;
        }

        // The Serialize prefix: strip, and return the records for the sidecar write of this same save (the
        // MoonDepotSave.Write prefix inside this Serialize, on this thread). Throws if the strip fails; the caller then
        // passes null to that write, which keeps every loaded canister record verbatim.
        internal static List<CanisterRecord> StripCanistersForSave(MoonLooseDustSnapshot state, StrippedCanisters stripped)
        {
            var stats = new CanisterStats { Before = state?.canisters?.Count ?? 0 };
            List<int> pendingIds;
            lock (gate) pendingIds = cargoPending.Select(p => p.id).ToList();
            if (pendingIds.Count > 0)
                CoreLog.Error($"Save: {pendingIds.Count} canister(s) restored from the sidecar still wait for their modded cargo ({string.Join(", ", pendingIds)}); they are kept out of this save");
            var records = StripCanisters(state, stripped, pendingIds);
            stats.Stripped = stripped.Canisters.Count;
            stats.CargoStripped = stripped.Cargo.Count;
            stats.Recorded = records.Count;
            LastCanisterStrip = stats;
            if (stats.Stripped > 0 || stats.CargoStripped > 0)
                CoreLog.Info($"Save: moved {stats.Stripped} modded-cargo canisters ({stats.CargoStripped} cargo entries, {stats.Recorded} recorded) out of the vanilla save; {stats.Before - stats.Stripped} canisters stay");
            return records;
        }

        internal static void PutBackCanistersAfterSave(MoonLooseDustSnapshot state, StrippedCanisters stripped)
        {
            int n = PutBackCanisters(state, stripped);
            var stats = LastCanisterStrip;
            if (stats != null) stats.PutBack = n;
        }

        // RestoreCanisters prefix: loaded canister records go into the load snapshot, so the world restores them like any
        // canister (as dust: no load step gives a canister a cargo value above Stone, see ApplyCanisterCargo). Same two
        // passes and per-record ownership as InjectDepots. No state.cargo entry is added: vanilla never applies a modded one.
        internal static void InjectCanisters(MoonWorld world, MoonLooseDustSnapshot snapshot)
        {
            CoreState.World = world;
            CoreState.CaptureRoot(world);
            LoadFor(CoreState.SaveRoot, snapshot?.Pointer ?? IntPtr.Zero);
            lock (gate) sessionLoaded = true;
            if (snapshot == null || snapshot.Pointer == canistersInjectedFor) return;
            lock (gate)
            {
                if (loaded.Canisters.Count == 0) { canistersInjectedFor = snapshot.Pointer; return; }
                if (!MoonStructures.Frozen || MoonStructures.Disabled)
                {
                    CoreLog.Warn($"Sidecar: registry not working (frozen={MoonStructures.Frozen}, disabled={MoonStructures.Disabled}); {loaded.Canisters.Count} canister records kept in the sidecar untouched");
                    return;
                }
                canistersInjectedFor = snapshot.Pointer;
                snapshot.canisters ??= new Il2CppSystem.Collections.Generic.List<MoonCanisterSnapshot>();
                var list = snapshot.canisters;

                // Pass 1: ids and snapshots, without touching the load snapshot.
                var taken = new HashSet<int>();
                for (int i = 0; i < list.Count; i++) { var s = list.get_Item(i); if (s != null) taken.Add(s.id); }
                int next = snapshot.nextCanisterId;
                var plan = new List<(CanisterRecord r, MoonCanisterSnapshot s, byte value)>();
                CanisterOrphans.Clear();
                foreach (var r in loaded.Canisters)
                {
                    var d = MoonStructures.CargoDefinition(r.CargoId);
                    if (d == null || CargoPatches.Live((MoonCargo)d.Value) != d)
                    {
                        CanisterOrphans.Add(r.CargoId);
                        CoreLog.Warn($"Sidecar: canister {r.Id} holds cargo '{r.CargoId}' (owner {CoreLog.Redact(r.Owner)}, {r.Kilograms} kg), which no installed mod registers; kept in the sidecar untouched until it is installed again");
                        continue;
                    }
                    int id = r.Id;
                    if (taken.Contains(id))
                    {
                        do id = next++; while (taken.Contains(id));
                        CoreLog.Warn($"Sidecar: canister id {r.Id} ('{r.CargoId}') is already taken in the save; restoring it as canister {id}");
                    }
                    if (id >= next) next = id + 1;
                    taken.Add(id);
                    plan.Add((r, ToCanisterSnapshot(r, id), d.Value));
                }
                // Pass 2: add them. A record is not claimed yet (it stays "not in the world" and would be written back
                // verbatim) until ApplyCanisterCargo has given its canister the modded cargo.
                snapshot.nextCanisterId = next;
                cargoPending.Clear();
                foreach (var (r, s, value) in plan)
                {
                    list.Add(s);
                    cargoPending.Add((r, s.id, value));
                    CoreLog.Info($"Sidecar: restoring a '{r.CargoId}' canister as canister {s.id} ({r.Kilograms}/{r.Capacity} kg) owner {CoreLog.Redact(r.Owner)}");
                }
                CoreLog.Info($"Sidecar: injected {plan.Count} modded-cargo canisters into the load snapshot ({CanisterOrphans.Count} orphaned), nextCanisterId={snapshot.nextCanisterId}");
            }
        }

        // Injected canisters whose modded cargo is not set yet (by ApplyCanisterCargo). Every save keeps these out of the
        // vanilla save (they are dust until then); their records stay unclaimed, so the sidecar keeps them verbatim.
        static readonly List<(CanisterRecord r, int id, byte value)> cargoPending = new();

        // RestoreProduction postfix, the last step of a load (RestoreCanisters -> RestoreDepots -> RestoreProduction): the
        // injected canisters are in world.canisters as dust; give each its modded cargo. world.canisters holds value types,
        // and a Dictionary set_Item from managed code does not stick (checked in game), so the record is rewritten through
        // the game's own MoveCanisterRecord, which stores the canister it is given (same pose). Only a canister that holds
        // its cargo afterwards claims its record. One that cannot be set is removed from the world again, so it never
        // reaches the vanilla save as dust; if even that fails it stays pending (kept out of every save).
        // The game calls run outside `gate`; the lock only guards the lists.
        internal static void ApplyCanisterCargo(MoonWorld world)
        {
            List<(CanisterRecord r, int id, byte value)> pending;
            lock (gate)
            {
                if (cargoPending.Count == 0) return;
                pending = cargoPending.ToList();
            }
            var canisters = world?.canisters;
            int ok = 0;
            foreach (var (r, id, value) in pending)
            {
                string why;
                try { why = SetCargo(world, id, value); }
                catch (Exception e) { why = "threw " + e.GetType().Name + ": " + e.Message; }
                bool resolved = why == null;
                if (resolved) ok++;
                else
                {
                    try
                    {
                        if (canisters == null || !canisters.ContainsKey(id)) resolved = true;
                        else { world.RemoveCanister(id); resolved = !canisters.ContainsKey(id); }
                    }
                    catch (Exception e) { CoreLog.Error($"Sidecar: removing canister {id} after a failed cargo set threw: {e.Message}"); }
                    CoreLog.Error($"Sidecar: canister {id} ('{r.CargoId}') could not be given its cargo ({why}); " +
                                  (resolved ? "not in the world now, " : "still in the world as dust and kept out of every save, ") + "its sidecar record is kept as it was");
                }
                lock (gate)
                {
                    if (why == null) canistersInWorld[r] = id;
                    if (resolved) cargoPending.RemoveAll(p => ReferenceEquals(p.r, r));
                }
            }
            CoreLog.Info($"Sidecar: {ok} of {pending.Count} restored canisters hold their modded cargo; world has {canisters?.Count ?? 0} canisters");
        }

        // Null on success, else why not.
        static string SetCargo(MoonWorld world, int id, byte value)
        {
            var canisters = world?.canisters;
            if (canisters == null || !canisters.ContainsKey(id)) return "not in world.canisters";
            var c = canisters.get_Item(id);
            var rotation = new Il2CppSystem.Nullable<UnityEngine.Quaternion>(c.rotation);
            var withCargo = new MoonDroppedCanister(c.id, c.owner, c.kilograms, c.capacity, c.position, c.up, c.createdAt, rotation, (MoonCargo)value);
            withCargo.physicalPose = c.physicalPose;
            world.MoveCanisterRecord(withCargo, c.position, c.up, rotation);
            if (!canisters.ContainsKey(id)) return "gone after MoveCanisterRecord";
            byte now = (byte)canisters.get_Item(id).cargo;
            return now == value ? null : $"cargo {now} after MoveCanisterRecord";
        }

        static CanisterRecord ToCanisterRecord(string cargoId, MoonCanisterSnapshot s) => new()
        {
            Id = s.id, Owner = s.owner, CargoId = cargoId, Kilograms = s.kilograms, Capacity = s.capacity,
            Position = new[] { s.x, s.y, s.z }, Up = new[] { s.ux, s.uy, s.uz }, Rotation = new[] { s.qx, s.qy, s.qz, s.qw },
            PhysicalPose = s.physicalPose,
        };

        static MoonCanisterSnapshot ToCanisterSnapshot(CanisterRecord r, int id)
        {
            static float At(float[] a, int i, float fallback) => a != null && a.Length > i ? a[i] : fallback;
            var s = new MoonCanisterSnapshot();
            s.id = id;
            s.owner = r.Owner;
            s.kilograms = r.Kilograms;
            s.capacity = r.Capacity;
            s.x = At(r.Position, 0, 0); s.y = At(r.Position, 1, 0); s.z = At(r.Position, 2, 0);
            s.ux = At(r.Up, 0, 0); s.uy = At(r.Up, 1, 1); s.uz = At(r.Up, 2, 0);
            s.qx = At(r.Rotation, 0, 0); s.qy = At(r.Rotation, 1, 0); s.qz = At(r.Rotation, 2, 0); s.qw = At(r.Rotation, 3, 1);
            s.physicalPose = r.PhysicalPose;
            return s;
        }

        // ---- production ----
        // A machine's production state is saved by MoonProductionSave.Write from state.production (MoonProductionSnapshot:
        // depot id, progress, running, charge, per-slot owner shares; no kind and no cargo). It runs inside the same
        // Serialize as MoonDepotSave.Write, after it, so modded machines' snapshots are cut out there and their records
        // join that save's pending sidecar write (its .tmp is rewritten with them); they are put back after. On load they
        // go back into the load snapshot before RestoreProduction, which creates each machine's state through
        // NewProduction (sized by the Processor(kind) prefix) and fills its stores, as for any machine.

        // Loaded production records that are in the world now (injected, and restored by RestoreProduction), with their
        // depot id. Only these are replaced by the live records on write; every other one is written back verbatim.
        static readonly Dictionary<ProductionRecord, int> productionInWorld = new(ReferenceEqualityComparer.Instance);
        // Injected into the load snapshot, not yet seen in the world (ClaimProduction decides).
        static readonly List<(ProductionRecord r, int id)> productionPending = new();
        // Loaded records superseded at this load (slots changed; the machine started empty), with the machine's depot id.
        // Kept verbatim only while that machine is still saved, so removing it lets its old record go too.
        static readonly Dictionary<ProductionRecord, int> productionSuperseded = new(ReferenceEqualityComparer.Instance);
        static IntPtr productionInjectedFor;

        // Under gate: a new load or session forgets the previous one's production bookkeeping.
        static void ForgetProduction()
        {
            productionInWorld.Clear();
            productionPending.Clear();
            productionSuperseded.Clear();
            RestoredProduction.Clear();
        }

        /// <summary>What a production strip took out, with the original indexes. Owned by the caller (the MoonProductionSave.Write prefix).</summary>
        internal sealed class StrippedProduction
        {
            internal readonly List<(int index, MoonProductionSnapshot s)> Items = new();
        }

        /// <summary>
        /// Takes the production snapshot of every modded depot out of state.production into <paramref name="stripped"/>, and
        /// returns the records of the registered processors. Inside a Serialize the modded depots are out of state.depots
        /// at this point, so <paramref name="moddedKinds"/> (depot id -> kind of the depots the depot Write prefix took) says
        /// which; null (a production Write outside a Serialize) reads them from state.depots. Pass 1 builds every record;
        /// pass 2 only removes. Touches nothing but the snapshot and the frozen registry, so it may run off the main thread.
        /// </summary>
        internal static List<ProductionRecord> StripProduction(MoonLooseDustSnapshot state, StrippedProduction stripped, IReadOnlyDictionary<int, int> moddedKinds)
        {
            var records = new List<ProductionRecord>();
            var list = state?.production;
            if (list == null || list.Count == 0) return records;
            var kindOf = moddedKinds;
            if (kindOf == null)
            {
                var depots = state.depots;
                if (depots == null) return records;
                var all = new List<(int, int)>();
                for (int i = 0; i < depots.Count; i++) { var s = depots.get_Item(i); if (s != null) all.Add((s.id, s.kind)); }
                kindOf = SaveRefs.ModdedKinds(all, KindAllocator.FirstModdedKind);
            }
            if (kindOf.Count == 0) return records;

            var take = new List<int>();
            var unrecorded = new List<(int id, int kind)>();
            for (int i = 0; i < list.Count; i++)
            {
                var p = list.get_Item(i);
                if (p == null || !kindOf.TryGetValue(p.id, out int kind)) continue;
                take.Add(i);
                var d = Remap.Live(kind);
                if (d?.Processor == null) unrecorded.Add((p.id, kind));
                else records.Add(ToProductionRecord(d, p));
            }
            for (int k = take.Count - 1; k >= 0; k--)
            {
                int i = take[k];
                var p = list.get_Item(i);
                list.RemoveAt(i);
                stripped.Items.Insert(0, (i, p));
            }
            foreach (var (id, kind) in unrecorded)
                CoreLog.Warn($"Save: production state of depot {id} (modded kind {kind}, no registered processor) left out of the vanilla save and not recorded");
            return records;
        }

        /// <summary>Puts stripped production snapshots back at their original indexes; returns how many.</summary>
        internal static int PutBackProduction(MoonLooseDustSnapshot state, StrippedProduction stripped)
        {
            if (stripped == null || stripped.Items.Count == 0) return 0;
            if (state?.production == null) { CoreLog.Error($"Save: cannot put {stripped.Items.Count} modded production states back: the snapshot has no production list"); return 0; }
            foreach (var (i, p) in stripped.Items) state.production.Insert(Math.Min(i, state.production.Count), p);
            return stripped.Items.Count;
        }

        /// <summary>
        /// The MoonProductionSave.Write prefix: this save's production records join its pending sidecar write. The file is
        /// rebuilt into a new .tmp (same snapshot order) and the old .tmp deleted; if that throws, the caller keeps the old
        /// pending write, which holds every loaded production record verbatim.
        /// </summary>
        internal static PendingWrite AddProduction(PendingWrite pending, List<ProductionRecord> production)
        {
            if (pending == null) return null;
            var rewritten = WriteTmp(pending.LiveStructures, pending.LiveCanisters, production, pending.Order);
            if (rewritten == null) return pending;
            try { File.Delete(pending.Tmp); } catch (Exception) { }
            CoreLog.Info($"Save: moved {production.Count} modded production states out of the vanilla save; sidecar holds {rewritten.Production} production records ({rewritten.KeptProduction} kept untouched)");
            return rewritten;
        }

        static ProductionRecord ToProductionRecord(StructureDefinition d, MoonProductionSnapshot p)
        {
            var r = new ProductionRecord { Id = d.Id, DepotId = p.id, Progress = p.progress, Running = p.running, Charge = p.charge, SlotCargoIds = SpecBuilder.SlotCargoIds(d) };
            var stores = p.stores;
            for (int i = 0; stores != null && i < stores.Count; i++)
            {
                var shares = stores.get_Item(i);
                var slot = new List<ShareRecord>();
                for (int j = 0; shares != null && j < shares.Count; j++)
                {
                    var w = shares.get_Item(j);
                    if (w != null) slot.Add(ShareOf(w));
                }
                r.Stores.Add(slot);
            }
            return r;
        }

        static MoonProductionSnapshot ToProductionSnapshot(ProductionRecord r, int id)
        {
            var s = new MoonProductionSnapshot();
            s.id = id;
            s.progress = r.Progress;
            s.running = r.Running;
            s.charge = r.Charge;
            var stores = new Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<MoonWalletSnapshot>>();
            foreach (var slot in r.Stores)
            {
                var shares = new Il2CppSystem.Collections.Generic.List<MoonWalletSnapshot>();
                foreach (var share in slot) if (share != null) shares.Add(WalletOf(share));
                stores.Add(shares);
            }
            s.stores = stores;
            return s;
        }

        // RestoreProduction prefix (after RestoreDepots, so the injected structures are in world.depots). Per record
        // (ProductionPlan.Decide): it goes into the load snapshot when its structure was injected and restored (inWorld,
        // matched by structure id and saved depot id, so a depot restored under a new id takes its record along) and it
        // still fits the definition's slots. When the slots changed, the machine gets an empty snapshot instead
        // (RestoreProduction only creates states for snapshot entries, and NewProduction sizes it from the current
        // definition), and the old record is kept for its data, marked Superseded so it is never offered again. Anything
        // else stays in the file untouched. Same two passes as InjectDepots; the game list is filled outside `gate`.
        internal static void InjectProduction(MoonLooseDustSnapshot snapshot)
        {
            if (snapshot == null) return;
            var plan = new List<(ProductionRecord r, MoonProductionSnapshot s, int id, bool fresh)>();
            lock (gate)
            {
                if (snapshot.Pointer == productionInjectedFor) return;
                if (loaded.Production.Count == 0) { productionInjectedFor = snapshot.Pointer; return; }
                if (!MoonStructures.Frozen || MoonStructures.Disabled)
                {
                    CoreLog.Warn($"Sidecar: registry not working (frozen={MoonStructures.Frozen}, disabled={MoonStructures.Disabled}); {loaded.Production.Count} production records kept in the sidecar untouched");
                    return;
                }
                productionInjectedFor = snapshot.Pointer;
                var depotOf = new Dictionary<(string, int), int>();
                foreach (var (r, id) in inWorld) depotOf[(r.Id, r.DepotId)] = id;
                var taken = new HashSet<int>();
                var list = snapshot.production;
                for (int i = 0; list != null && i < list.Count; i++) { var s = list.get_Item(i); if (s != null) taken.Add(s.id); }
                foreach (var p in loaded.Production)
                {
                    var d = MoonStructures.Definition(p.Id);
                    bool registered = d?.Processor != null && MoonStructures.IsModded(d.Kind);
                    bool there = depotOf.TryGetValue((p.Id, p.DepotId), out int id);
                    string mismatch = registered ? ProcessorLayout.RecordMismatch(p.SlotCargoIds, p.Stores.Count, SpecBuilder.SlotCargoIds(d)) : null;
                    var action = ProductionPlan.Decide(registered, there, there && taken.Contains(id), p.Superseded, mismatch);
                    if (action == ProductionRestoreAction.Keep)
                    {
                        string why = !registered ? "no installed mod registers it as a processor"
                            : !there ? $"its structure (depot {p.DepotId}) is not in the world"
                            : p.Superseded ? "it was superseded by a fresh state after a slot change"
                            : $"depot {id} already has a production state";
                        CoreLog.Warn($"Sidecar: production record of '{p.Id}' (depot {p.DepotId}) kept in the sidecar untouched: {why}");
                        continue;
                    }
                    taken.Add(id);
                    if (action == ProductionRestoreAction.Restore) { plan.Add((p, ToProductionSnapshot(p, id), id, false)); continue; }
                    var empty = new MoonProductionSnapshot();
                    empty.id = id;
                    // One empty inner list per slot of the current layout, so RestoreProduction never indexes past it.
                    empty.stores = new Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<MoonWalletSnapshot>>();
                    for (int slot = 0, n = ProductionPlan.EmptySlotCount(SpecBuilder.SlotCargoIds(d)); slot < n; slot++)
                        empty.stores.Add(new Il2CppSystem.Collections.Generic.List<MoonWalletSnapshot>());
                    p.Superseded = true;
                    productionSuperseded[p] = id;
                    plan.Add((p, empty, id, true));
                    CoreLog.Warn($"Sidecar: production record of '{p.Id}' (depot {p.DepotId}) no longer fits its definition ({mismatch}); depot {id} starts empty and the old record is kept in the sidecar, marked superseded");
                }
                productionPending.Clear();
                foreach (var (p, _, id, fresh) in plan) if (!fresh) productionPending.Add((p, id));
            }
            snapshot.production ??= new Il2CppSystem.Collections.Generic.List<MoonProductionSnapshot>();
            foreach (var (p, s, id, fresh) in plan)
            {
                snapshot.production.Add(s);
                if (!fresh) CoreLog.Info($"Sidecar: restoring production of '{p.Id}' as depot {id}: stores [{string.Join(" ", p.Stores.Select((x, i) => $"{i}:{x.Sum(y => y.Pending)}"))} kg], progress {p.Progress:F3}, running {p.Running}");
            }
            CoreLog.Info($"Sidecar: injected {plan.Count} modded production states into the load snapshot ({plan.Count(x => x.fresh)} empty after a slot change)");
        }

        // RestoreProduction postfix: an injected record counts as in the world only once the machine's state is there with
        // the record's slots; otherwise it stays unclaimed and is written back verbatim.
        internal static void ClaimProduction(MoonWorld world)
        {
            List<(ProductionRecord r, int id)> pending;
            lock (gate)
            {
                if (productionPending.Count == 0) return;
                pending = productionPending.ToList();
                productionPending.Clear();
            }
            int ok = 0;
            foreach (var (r, id) in pending)
            {
                var s = world?.ProductionOf(id);
                int slots = s?.stores?.Length ?? -1;
                if (s == null || slots != r.Stores.Count)
                {
                    CoreLog.Error($"Sidecar: production of '{r.Id}' depot {id} was injected but the world did not restore it ({(s == null ? "no state" : slots + " slots")}); kept in the sidecar untouched");
                    continue;
                }
                var restored = new ProductionRestore { StructureId = r.Id, Depot = id, Saved = r.Stores.Select(x => x.Sum(y => y.Pending)).ToArray(), Loaded = Enumerable.Range(0, slots).Select(i => s.Stored(i)).ToArray() };
                lock (gate)
                {
                    productionInWorld[r] = id;
                    RestoredProduction.Add(restored);
                }
                ok++;
            }
            CoreLog.Info($"Sidecar: {ok} of {pending.Count} injected production states restored");
        }

        // Called once the whole vanilla write is through (see SavePatches): the file is committed only when nothing in it
        // threw; otherwise the .tmp is dropped and the previous sidecar stays, matching the vanilla save the game keeps.
        // Commits never go backwards: a write prepared from an older save snapshot than the last one committed to the same
        // file (overlapping saves) is dropped.
        internal static void CommitWrite(PendingWrite p, Exception vanillaFailed)
        {
            if (p == null) return;
            lock (gate)
            {
                try
                {
                    if (vanillaFailed != null)
                    {
                        CoreLog.Error($"Save: the vanilla write threw ({vanillaFailed.GetType().Name}: {vanillaFailed.Message}); the sidecar is NOT updated this time");
                        try { File.Delete(p.Tmp); } catch (Exception) { }
                        return;
                    }
                    long last = lastCommitted.TryGetValue(p.Path, out long l) ? l : -1;
                    if (!CommitOrder.ShouldCommit(p.Order, last))
                    {
                        CoreLog.Warn($"Save: a sidecar write from save snapshot #{p.Order} finished after one from #{last}; dropped (the newer one stays)");
                        try { File.Delete(p.Tmp); } catch (Exception) { }
                        return;
                    }
                    if (File.Exists(p.Path)) File.Copy(p.Path, p.Path + ".bak", true);
                    File.Move(p.Tmp, p.Path, true);
                    lastCommitted[p.Path] = p.Order;
                    CoreLog.Info($"Sidecar written: {p.Structures} structures ({p.Kept} kept untouched), {p.Canisters} canisters ({p.KeptCanisters} kept untouched), save snapshot #{p.Order} -> {CoreLog.Redact(p.Path)}");
                }
                catch (Exception e)
                {
                    CoreLog.Error($"Save: committing sidecar {CoreLog.Redact(p.Path)} failed; the vanilla save went ahead WITHOUT the modded structures: {e}");
                    try { File.Delete(p.Tmp); } catch (Exception) { }
                }
            }
        }

        // Builds the file for one save into its own .tmp. `structures`, `canisters` and `production` are what this save
        // stripped (the world now); `canisters` or `production` null keeps every loaded record of that list verbatim.
        static PendingWrite WriteTmp(List<StructureRecord> structures, List<CanisterRecord> canisters, List<ProductionRecord> production, long order)
        {
            string root = CoreState.SaveRoot;
            if (root == null) { CoreState.CaptureRoot(CoreState.World); root = CoreState.SaveRoot; }
            lock (gate)
            {
                // After the session left the game its root is cleared, but its last save may still be writing.
                root ??= loadedRoot;
                string path = PathFor(root);
                if (path == null)
                {
                    CoreLog.Error($"Save: save root unknown; the vanilla save goes ahead WITHOUT the {structures.Count} modded structure(s) and {canisters?.Count ?? 0} modded-cargo canister(s)");
                    return null;
                }
                if (loadedRoot != root)
                {
                    // A save root this session never loaded (a changed root): read its file first so its records are kept,
                    // not overwritten. Nothing of it is in the world, so all of it is written back.
                    CoreLog.Warn($"Save: writing the sidecar to {CoreLog.Redact(root)} but loaded from {CoreLog.Redact(loadedRoot) ?? "nothing"}; reading it first");
                    LoadFor(root, IntPtr.Zero);
                }
                var liveCanisters = canisters ?? new List<CanisterRecord>();
                var liveProduction = production ?? new List<ProductionRecord>();
                var current = new SidecarFile { Written = DateTime.Now.ToString("s"), Structures = structures.ToList(), Production = liveProduction.ToList(), Canisters = liveCanisters.ToList() };
                // Loaded records the world holds are replaced by the live lists; every other one is kept as it was.
                var notInWorld = new SidecarFile
                {
                    Structures = loaded.Structures.Where(r => !inWorld.ContainsKey(r)).ToList(),
                    // A superseded record stays only while its machine is still saved (its live record is in this write).
                    Production = production == null ? loaded.Production.ToList() : loaded.Production.Where(r => !productionInWorld.ContainsKey(r) &&
                        (!productionSuperseded.TryGetValue(r, out int machine) || liveProduction.Any(p => p.DepotId == machine))).ToList(),
                    Canisters = canisters == null ? loaded.Canisters.ToList() : loaded.Canisters.Where(r => !canistersInWorld.ContainsKey(r)).ToList(),
                };
                var data = SidecarCodec.Merge(current, notInWorld, _ => false);
                if (data.Structures.Count == 0 && data.Production.Count == 0 && data.Canisters.Count == 0 && !File.Exists(path)) return null;
                string tmp = CommitOrder.TmpPath(path, Interlocked.Increment(ref tmpSeq));
                File.WriteAllText(tmp, SidecarCodec.Serialize(data));
                return new PendingWrite
                {
                    Tmp = tmp, Path = path, Order = order,
                    Structures = data.Structures.Count, Kept = data.Structures.Count - structures.Count,
                    Canisters = data.Canisters.Count, KeptCanisters = data.Canisters.Count - liveCanisters.Count,
                    Production = production == null ? -1 : data.Production.Count, KeptProduction = data.Production.Count - liveProduction.Count,
                    LiveStructures = structures, LiveCanisters = canisters,
                };
            }
        }
    }
}
