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
        // True once InjectDepots ran for the loaded file. Until then every loaded record is kept as-is on write, so a
        // failed or skipped inject can never drop records the world does not hold.
        static bool injected;
        static IntPtr injectedFor;
        // Records injected into the load snapshot that RestoreDepots did not keep: written back verbatim, like orphans.
        static List<StructureRecord> notRestored = new();
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

        static int writeSeq;
        internal static int WriteSeq => Volatile.Read(ref writeSeq);
        internal static volatile WriteStats LastWrite;
        static int mainThreadId = -1;

        /// <summary>Called every frame from CoreDriver (main thread) so save hooks can tell which thread they run on.</summary>
        internal static void NoteMainThread() => mainThreadId = Thread.CurrentThread.ManagedThreadId;

        // Reloads whenever a new load snapshot shows up, so returning to the menu and loading again reads fresh data.
        internal static void LoadFor(string root, IntPtr snapshot)
        {
            lock (gate)
            {
                if (root == loadedRoot && snapshot == loadedSnapshot && loaded != null) return;
                loadedRoot = root;
                loadedSnapshot = snapshot;
                loaded = new SidecarFile();
                injected = false;
                notRestored = new();
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
                    CoreLog.Error($"Sidecar at {CoreLog.Redact(path)} is unreadable, moving it to {Path.GetFileName(keep)} and starting empty: {e.Message}");
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
            if (snapshot == null || snapshot.Pointer == injectedFor) return;
            if (!MoonStructures.Frozen || MoonStructures.Disabled)
            {
                lock (gate) CoreLog.Warn($"Sidecar: registry not working (frozen={MoonStructures.Frozen}, disabled={MoonStructures.Disabled}); {loaded.Structures.Count} structure records kept in the sidecar untouched");
                return;
            }
            injectedFor = snapshot.Pointer;
            snapshot.depots ??= new Il2CppSystem.Collections.Generic.List<MoonDepotSnapshot>();
            var list = snapshot.depots;
            var taken = new HashSet<int>();
            for (int i = 0; i < list.Count; i++) { var s = list.get_Item(i); if (s != null) taken.Add(s.id); }
            int added = 0, orphaned = 0;
            lock (gate)
            {
                Orphans.Clear();
                foreach (var r in loaded.Structures)
                {
                    var d = MoonStructures.Definition(r.Id);
                    if (d == null || !MoonStructures.IsModded(d.Kind))
                    {
                        orphaned++;
                        Orphans.Add(r.Id);
                        CoreLog.Warn($"Sidecar: structure '{r.Id}' (depot {r.DepotId}, owner {CoreLog.Redact(r.Owner)}) has no registered mod; kept in the sidecar untouched until it is installed again");
                        continue;
                    }
                    int id = r.DepotId;
                    if (taken.Contains(id))
                    {
                        id = snapshot.nextDepotId++;
                        while (taken.Contains(id)) id = snapshot.nextDepotId++;
                        CoreLog.Warn($"Sidecar: depot id {r.DepotId} of '{r.Id}' is already taken in the save; restoring it as depot {id}");
                    }
                    if (id >= snapshot.nextDepotId) snapshot.nextDepotId = id + 1;
                    taken.Add(id);
                    list.Add(ToSnapshot(r, id, d.Kind));
                    r.DepotId = id;
                    added++;
                    CoreLog.Info($"Sidecar: restored '{r.Id}' as depot {id} kind {d.Kind} at ({r.X:F1},{r.Y:F1},{r.Z:F1}) owner {CoreLog.Redact(r.Owner)}");
                }
                injected = true;
            }
            CoreLog.Info($"Sidecar: injected {added} modded structures into the load snapshot ({orphaned} orphaned), nextDepotId={snapshot.nextDepotId}");
        }

        // After RestoreDepots: every injected record must now be in world.depots. One that is not would be dropped by
        // the next write (the world no longer holds it), so it is kept verbatim instead.
        internal static void CheckRestored(MoonWorld world)
        {
            lock (gate)
            {
                if (!injected) return;
                var depots = world?.depots;
                var missing = new List<StructureRecord>();
                foreach (var r in loaded.Structures)
                {
                    var d = MoonStructures.Definition(r.Id);
                    if (d == null || !MoonStructures.IsModded(d.Kind)) continue;
                    bool there = depots != null && depots.ContainsKey(r.DepotId) && depots.get_Item(r.DepotId)?.kind == d.Kind;
                    if (!there) missing.Add(r);
                }
                notRestored = missing;
                foreach (var r in missing)
                    CoreLog.Error($"Sidecar: '{r.Id}' depot {r.DepotId} was injected but the world did not restore it; kept in the sidecar untouched");
                CoreLog.Info($"Sidecar: world restored {loaded.Structures.Count - Orphans.Count - missing.Count} modded structures; world has {depots?.Count ?? 0} depots");
            }
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
            {
                if (share == null) continue;
                var w = new MoonWalletSnapshot();
                w.player = share.Owner;
                w.dollars = new Il2CppSystem.Decimal(share.Amount);
                w.pendingKilograms = new Il2CppSystem.Decimal(share.Pending);
                c.Add(w);
            }
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
                    if (w == null) continue;
                    r.Contributions.Add(new ShareRecord { Owner = w.player, Amount = (double)w.dollars, Pending = (double)w.pendingKilograms });
                }
            return r;
        }

        // ---- save ----

        // May run off the main thread: touches only the save snapshot, the frozen registry and the file.
        internal static List<MoonDepotSnapshot> StripDepots(MoonLooseDustSnapshot state)
        {
            var removed = new List<MoonDepotSnapshot>();
            var list = state?.depots;
            if (list == null) return removed;
            var stats = new WriteStats { Before = list.Count, MainThread = Thread.CurrentThread.ManagedThreadId == mainThreadId };
            var records = new List<StructureRecord>();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var s = list.get_Item(i);
                if (s == null || s.kind < KindAllocator.FirstModdedKind) continue;
                stats.Modded++;
                // Any kind above the vanilla range would make the game refuse the save, registered or not.
                var d = Remap.Live(s.kind);
                if (d == null) CoreLog.Warn($"Save: depot {s.id} has unknown modded kind {s.kind}; left out of the vanilla save and not recorded");
                else records.Add(ToRecord(d.Id, s));
                removed.Add(s);
                list.RemoveAt(i);
                stats.StrippedIds.Add(s.id);
                stats.StrippedKinds.Add(s.kind);
            }
            records.Reverse();
            stats.Stripped = removed.Count;
            stats.VanillaAfter = list.Count;
            stats.Seq = Interlocked.Increment(ref writeSeq);
            LastWrite = stats;
            CoreLog.Info($"Save: moved {removed.Count} modded structures out of the vanilla save ({stats.VanillaAfter} vanilla depots stay; write #{stats.Seq}, {(stats.MainThread ? "main thread" : "thread " + Thread.CurrentThread.ManagedThreadId)})");
            lock (gate) live = records;
            WriteFile();
            return removed;
        }

        internal static void PutBackDepots(MoonLooseDustSnapshot state, List<MoonDepotSnapshot> removed)
        {
            int n = 0;
            if (removed != null && removed.Count > 0 && state?.depots != null)
                for (int i = removed.Count - 1; i >= 0; i--) { state.depots.Add(removed[i]); n++; }
            var stats = LastWrite;
            if (stats != null && removed != null) stats.PutBack = n;
        }

        static bool LiveOwned(string id)
        {
            // Called under gate. Before the inject ran, nothing in the file is the world's: keep it all.
            if (!injected) return false;
            var d = MoonStructures.Definition(id);
            return d != null && MoonStructures.IsModded(d.Kind);
        }

        static void WriteFile()
        {
            string root = CoreState.SaveRoot;
            string path = PathFor(root);
            if (path == null) { CoreLog.Error("Save: save root unknown, modded structures NOT written to the sidecar this time"); return; }
            lock (gate)
            {
                if (loadedRoot != root)
                {
                    // A save root this session never loaded (a new game, a changed root): read its file first so its
                    // records are kept, not overwritten.
                    CoreLog.Warn($"Save: writing the sidecar to {CoreLog.Redact(root)} but loaded from {CoreLog.Redact(loadedRoot) ?? "nothing"}; reading it first");
                    var keepLive = live;
                    LoadFor(root, IntPtr.Zero);
                    live = keepLive;
                }
                var current = new SidecarFile { Written = DateTime.Now.ToString("s"), Structures = live.ToList() };
                var data = SidecarCodec.Merge(current, loaded, LiveOwned);
                // Records injected but not restored are the world's by id, so Merge dropped them; keep them as they were.
                foreach (var r in notRestored) if (!data.Structures.Contains(r)) data.Structures.Add(r);
                int kept = data.Structures.Count - live.Count;
                if (data.Structures.Count == 0 && data.Production.Count == 0 && data.Canisters.Count == 0 && !File.Exists(path)) return;
                try
                {
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, SidecarCodec.Serialize(data));
                    if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                    File.Move(tmp, path, true);
                    CoreLog.Info($"Sidecar written: {data.Structures.Count} structures ({kept} kept untouched) -> {CoreLog.Redact(path)}");
                }
                catch (Exception e) { CoreLog.Error($"Writing sidecar {CoreLog.Redact(path)} failed: {e}"); }
            }
        }
    }
}
