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
        // Latest stripped records (what the world holds now).
        static List<StructureRecord> live = new();

        /// <summary>Path of the sidecar for a save root, or null when the root is unknown.</summary>
        internal static string PathFor(string root) => string.IsNullOrEmpty(root) ? null : Path.Combine(root, FileName);

        /// <summary>Structure ids in the file loaded for this session (orphans included).</summary>
        internal static List<string> LoadedIds() { lock (gate) return loaded.Structures.Select(s => s.Id).ToList(); }

        /// <summary>Structure ids kept as orphans at the last inject.</summary>
        internal static readonly List<string> Orphans = new();

        /// <summary>What the last MoonDepotSave.Write saw. Read by the dev self-test.</summary>
        internal sealed class WriteStats
        {
            internal int Seq;
            internal int Before, Modded, Stripped, VanillaAfter, PutBack = -1;
            internal readonly List<int> StrippedIds = new(), StrippedKinds = new();
            internal bool MainThread;
        }

        /// <summary>A sidecar written to its .tmp by the Write prefix, committed by the finalizer.</summary>
        internal sealed class PendingWrite
        {
            internal string Tmp, Path;
            internal int Structures, Kept;
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
                sessionLoaded = false;
            }
        }

        // Session entered the game. Without a load since the last session (a new game), nothing of the previous save
        // may leak into this one: forget its root, records and live list.
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
                live = new();
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
                live = new();
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

        // After RestoreDepots: every injected record must now be in world.depots. One that is not would be lost on the
        // next write (the world no longer holds it), so it stops counting as in the world and is kept verbatim.
        internal static void CheckRestored(MoonWorld world)
        {
            lock (gate)
            {
                if (inWorld.Count == 0) return;
                var depots = world?.depots;
                foreach (var (r, id) in inWorld.ToList())
                {
                    var d = MoonStructures.Definition(r.Id);
                    bool there = d != null && depots != null && depots.ContainsKey(id) && depots.get_Item(id)?.kind == d.Kind;
                    if (there) continue;
                    inWorld.Remove(r);
                    CoreLog.Error($"Sidecar: '{r.Id}' depot {id} was injected but the world did not restore it; kept in the sidecar untouched");
                }
                CoreLog.Info($"Sidecar: world restored {inWorld.Count} modded structures; world has {depots?.Count ?? 0} depots");
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
        internal static PendingWrite StripDepots(MoonLooseDustSnapshot state, List<MoonDepotSnapshot> removed)
        {
            var list = state?.depots;
            if (list == null) return null;
            var stats = new WriteStats { Before = list.Count, MainThread = Thread.CurrentThread.ManagedThreadId == mainThreadId };

            // No Frozen/Disabled guard on the strip itself: before the registry freezes no modded kind can be in a world,
            // so stripping every kind in the modded range is equivalent, and it can never let one reach the vanilla save.
            var take = new List<(int index, MoonDepotSnapshot s)>();
            var records = new List<StructureRecord>();
            for (int i = 0; i < list.Count; i++)
            {
                var s = list.get_Item(i);
                if (s == null || s.kind < KindAllocator.FirstModdedKind) continue;
                take.Add((i, s));
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
            CoreLog.Info($"Save: moved {removed.Count} modded structures out of the vanilla save ({stats.VanillaAfter} vanilla depots stay; write #{stats.Seq}, {(stats.MainThread ? "main thread" : "thread " + Thread.CurrentThread.ManagedThreadId)})");

            try
            {
                lock (gate) live = records;
                return WriteTmp();
            }
            catch (Exception e)
            {
                CoreLog.Error($"Save: preparing the sidecar failed; the vanilla save goes ahead WITHOUT the {records.Count} modded structure(s): {e}");
                return null;
            }
        }

        // Called by the finalizer. Pass 2 removed from the highest index down, so walking `removed` backwards appends the
        // records in their original relative order.
        internal static void PutBackDepots(MoonLooseDustSnapshot state, List<MoonDepotSnapshot> removed)
        {
            if (removed == null) return;
            int n = 0;
            if (removed.Count > 0)
            {
                if (state?.depots == null) { CoreLog.Error($"Save: cannot put {removed.Count} modded structures back: the snapshot has no depot list"); return; }
                for (int i = removed.Count - 1; i >= 0; i--) { state.depots.Add(removed[i]); n++; }
            }
            var stats = LastWrite;
            if (stats != null) stats.PutBack = n;
        }

        // The file is committed only when the vanilla Write went through; otherwise the .tmp is dropped and the previous
        // sidecar stays, matching the vanilla save the game keeps.
        internal static void CommitWrite(PendingWrite p, Exception vanillaFailed)
        {
            if (p == null) return;
            lock (gate)
            {
                try
                {
                    if (vanillaFailed != null)
                    {
                        CoreLog.Error($"Save: MoonDepotSave.Write threw ({vanillaFailed.GetType().Name}: {vanillaFailed.Message}); the sidecar is NOT updated this time");
                        try { File.Delete(p.Tmp); } catch (Exception) { }
                        return;
                    }
                    if (File.Exists(p.Path)) File.Copy(p.Path, p.Path + ".bak", true);
                    File.Move(p.Tmp, p.Path, true);
                    CoreLog.Info($"Sidecar written: {p.Structures} structures ({p.Kept} kept untouched) -> {CoreLog.Redact(p.Path)}");
                }
                catch (Exception e)
                {
                    CoreLog.Error($"Save: committing sidecar {CoreLog.Redact(p.Path)} failed; the vanilla save went ahead WITHOUT the modded structures: {e}");
                }
            }
        }

        static PendingWrite WriteTmp()
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
                    CoreLog.Error($"Save: save root unknown; the vanilla save goes ahead WITHOUT the {live.Count} modded structure(s)");
                    return null;
                }
                if (loadedRoot != root)
                {
                    // A save root this session never loaded (a changed root): read its file first so its records are kept,
                    // not overwritten. Nothing of it is in the world, so all of it is written back.
                    CoreLog.Warn($"Save: writing the sidecar to {CoreLog.Redact(root)} but loaded from {CoreLog.Redact(loadedRoot) ?? "nothing"}; reading it first");
                    var keepLive = live;
                    LoadFor(root, IntPtr.Zero);
                    live = keepLive;
                }
                var current = new SidecarFile { Written = DateTime.Now.ToString("s"), Structures = live.ToList() };
                // Loaded records the world holds are replaced by the live list; every other one is kept as it was.
                var notInWorld = new SidecarFile
                {
                    Structures = loaded.Structures.Where(r => !inWorld.ContainsKey(r)).ToList(),
                    Production = loaded.Production,
                    Canisters = loaded.Canisters,
                };
                var data = SidecarCodec.Merge(current, notInWorld, _ => false);
                int kept = data.Structures.Count - live.Count;
                if (data.Structures.Count == 0 && data.Production.Count == 0 && data.Canisters.Count == 0 && !File.Exists(path)) return null;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, SidecarCodec.Serialize(data));
                return new PendingWrite { Tmp = tmp, Path = path, Structures = data.Structures.Count, Kept = kept };
            }
        }
    }
}
