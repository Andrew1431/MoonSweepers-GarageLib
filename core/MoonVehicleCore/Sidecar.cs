using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using MoonCleaners;
using UnityEngine;

namespace MoonVehicleCore
{
    /// <summary>Storage ids look like "garage/&lt;garage&gt;/&lt;vehicle index&gt;/&lt;owner&gt;".</summary>
    public static class StorageIds
    {
        public static bool TryParse(string storage, out int garage, out int vehicle, out string owner)
        {
            garage = vehicle = -1; owner = null;
            if (string.IsNullOrEmpty(storage) || !storage.StartsWith("garage/", StringComparison.Ordinal)) return false;
            var parts = storage.Split('/', 4);
            if (parts.Length != 4 || !int.TryParse(parts[1], out garage) || !int.TryParse(parts[2], out vehicle)) return false;
            owner = parts[3];
            return true;
        }

        /// <summary>Index-free key for a modded vehicle's storage ("garage/g/&lt;def id&gt;/owner"), or null if not modded.</summary>
        public static string Stable(string storage)
        {
            if (!TryParse(storage, out int g, out int v, out string o)) return null;
            var d = MoonVehicles.Definition(v);
            return d == null ? null : $"garage/{g}/{d.Id}/{o}";
        }

        /// <summary>Stable key back to a live storage id, or null if the vehicle id isn't registered.</summary>
        public static string Live(string stable)
        {
            if (string.IsNullOrEmpty(stable)) return null;
            var parts = stable.Split('/', 4);
            if (parts.Length != 4) return null;
            var d = MoonVehicles.Definition(parts[2]);
            return d == null ? null : $"garage/{parts[1]}/{d.Index}/{parts[3]}";
        }
    }

    // The vanilla save never sees a modded index: modded owned vehicles and their hopper storage are cut out of the
    // snapshot while it is serialized and written to moonvehiclecore.json beside the save instead. On load they are
    // put back into the snapshot before the world restores it. Records whose vehicle mod is missing are kept as-is.
    internal static class Sidecar
    {
        internal const string FileName = "moonvehiclecore.json";
        internal const int Version = 1;

        internal sealed class FileData
        {
            public int Version { get; set; } = Sidecar.Version;
            public string Written { get; set; }
            public List<VehicleRecord> Vehicles { get; set; } = new();
            public List<StorageRecord> Storage { get; set; } = new();
            public Dictionary<string, Dictionary<string, int>> CustomLevels { get; set; } = new();
        }

        internal sealed class VehicleRecord
        {
            public string Id { get; set; }
            public int Garage { get; set; }
            public string Owner { get; set; }
            public string OwnerName { get; set; }
            public bool InWorld { get; set; }
            public float[] Position { get; set; }
            public float[] Rotation { get; set; }
            public string Holder { get; set; }
            public double Odometer { get; set; }
            public int[] Upgrades { get; set; }
        }

        internal sealed class StorageRecord
        {
            public string Key { get; set; }
            public double Kilograms { get; set; }
            public double Capacity { get; set; }
        }

        static readonly object gate = new();
        static readonly JsonSerializerOptions json = new() { WriteIndented = true };

        /// <summary>Custom upgrade levels by stable storage key.</summary>
        internal static Dictionary<string, Dictionary<string, int>> CustomLevels = new();
        static List<VehicleRecord> orphanVehicles = new();
        static List<StorageRecord> orphanStorage = new();
        // Latest stripped records per kind, so each of the two Write hooks can rewrite the whole file.
        static List<VehicleRecord> liveVehicles = new();
        static List<StorageRecord> liveStorage = new();
        static string loadedRoot;
        static IntPtr loadedSnapshot;
        static FileData loaded;
        static IntPtr restoredVehiclesFor, restoredStorageFor;

        static string PathFor(string root) => string.IsNullOrEmpty(root) ? null : Path.Combine(root, FileName);

        // Reloads whenever a new load snapshot shows up, so returning to the menu and loading again reads fresh data.
        internal static void LoadFor(string root, IntPtr snapshot)
        {
            lock (gate)
            {
                if (root == loadedRoot && snapshot == loadedSnapshot && loaded != null) return;
                loadedRoot = root;
                loadedSnapshot = snapshot;
                loaded = new FileData();
                CustomLevels = new();
                orphanVehicles = new(); orphanStorage = new();
                liveVehicles = new(); liveStorage = new();
                string path = PathFor(root);
                if (path == null) { CoreLog.Warn("Sidecar load: save root unknown, starting empty"); return; }
                if (!File.Exists(path)) { CoreLog.Info($"Sidecar: none at {path} (no modded vehicles saved yet)"); return; }
                try
                {
                    loaded = JsonSerializer.Deserialize<FileData>(File.ReadAllText(path), json) ?? new FileData();
                    CustomLevels = loaded.CustomLevels ?? new();
                    CoreLog.Info($"Sidecar loaded: {loaded.Vehicles.Count} vehicles, {loaded.Storage.Count} storages, {CustomLevels.Count} custom-upgrade entries (v{loaded.Version}, written {loaded.Written})");
                    if (loaded.Version > Version) CoreLog.Warn($"Sidecar version {loaded.Version} is newer than this core ({Version}); unknown fields will be dropped on save");
                }
                catch (Exception e)
                {
                    string keep = path + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    CoreLog.Error($"Sidecar at {path} is unreadable, moving it to {keep} and starting empty: {e}");
                    try { File.Copy(path, keep, true); } catch { }
                    loaded = new FileData();
                }
            }
        }

        // ---- load ----

        internal static void InjectOwned(MoonWorld world, MoonLooseDustSnapshot snapshot)
        {
            CoreState.World = world;
            CoreState.CaptureRoot(world);
            LoadFor(CoreState.SaveRoot, snapshot?.Pointer ?? IntPtr.Zero);
            if (snapshot == null || snapshot.Pointer == restoredVehiclesFor) return;
            restoredVehiclesFor = snapshot.Pointer;
            if (snapshot.ownedVehicles == null) snapshot.ownedVehicles = new Il2CppSystem.Collections.Generic.List<MoonOwnedVehicle>();
            var list = snapshot.ownedVehicles;
            int added = 0;
            lock (gate)
            {
                orphanVehicles = new();
                foreach (var r in loaded.Vehicles)
                {
                    var d = MoonVehicles.Definition(r.Id);
                    if (d == null)
                    {
                        orphanVehicles.Add(r);
                        CoreLog.Warn($"Sidecar: vehicle '{r.Id}' (garage {r.Garage}, owner {Patches.PurchasePatches.Redact(r.Owner)}) has no registered mod; kept in the sidecar untouched until it is installed again");
                        continue;
                    }
                    if (Exists(list, r.Garage, d.Index, r.Owner)) { CoreLog.Warn($"Sidecar: '{r.Id}' garage {r.Garage} already in snapshot, skipped"); continue; }
                    var v = new MoonOwnedVehicle();
                    v.garage = r.Garage;
                    v.vehicle = d.Index;
                    v.owner = r.Owner;
                    v.ownerName = r.OwnerName;
                    v.inWorld = r.InWorld;
                    v.position = r.Position?.Length == 3 ? new Vector3(r.Position[0], r.Position[1], r.Position[2]) : Vector3.zero;
                    v.rotation = r.Rotation?.Length == 4 ? new Quaternion(r.Rotation[0], r.Rotation[1], r.Rotation[2], r.Rotation[3]) : Quaternion.identity;
                    v.holder = r.Holder;
                    v.odometer = r.Odometer;
                    var bytes = new byte[Math.Max(MoonVehicleUpgrades.Count, r.Upgrades?.Length ?? 0)];
                    if (r.Upgrades != null) for (int i = 0; i < r.Upgrades.Length; i++) bytes[i] = (byte)Mathf.Clamp(r.Upgrades[i], 0, 255);
                    v.upgrades = bytes;
                    list.Add(v);
                    added++;
                    string storage = null;
                    try { storage = v.Storage; } catch (Exception e) { storage = "ERR " + e.GetType().Name; }
                    CoreLog.Info($"Sidecar: restored '{r.Id}' as #{d.Index} garage={r.Garage} inWorld={r.InWorld} storage={Patches.PurchasePatches.Redact(storage)} upgrades=[{string.Join(",", bytes)}]");
                }
            }
            CoreLog.Info($"Sidecar: injected {added} modded vehicles into the load snapshot ({orphanVehicles.Count} orphaned)");
        }

        internal static void InjectStorage(MoonWorld world, MoonLooseDustSnapshot snapshot)
        {
            CoreState.World = world;
            CoreState.CaptureRoot(world);
            LoadFor(CoreState.SaveRoot, snapshot?.Pointer ?? IntPtr.Zero);
            if (snapshot == null || snapshot.Pointer == restoredStorageFor) return;
            restoredStorageFor = snapshot.Pointer;
            if (snapshot.vehicles == null) snapshot.vehicles = new Il2CppSystem.Collections.Generic.List<MoonVehicleStorageSnapshot>();
            int added = 0;
            lock (gate)
            {
                orphanStorage = new();
                foreach (var r in loaded.Storage)
                {
                    string live = StorageIds.Live(r.Key);
                    if (live == null) { orphanStorage.Add(r); continue; }
                    var s = new MoonVehicleStorageSnapshot();
                    s.id = live; s.kilograms = r.Kilograms; s.capacity = r.Capacity;
                    snapshot.vehicles.Add(s);
                    added++;
                }
            }
            CoreLog.Info($"Sidecar: injected {added} modded vehicle storages ({orphanStorage.Count} orphaned)");
        }

        static bool Exists(Il2CppSystem.Collections.Generic.List<MoonOwnedVehicle> list, int garage, int vehicle, string owner)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var v = list.get_Item(i);
                if (v != null && v.garage == garage && v.vehicle == vehicle && v.owner == owner) return true;
            }
            return false;
        }

        // ---- save ----

        internal static List<MoonOwnedVehicle> StripOwned(MoonLooseDustSnapshot state)
        {
            var removed = new List<MoonOwnedVehicle>();
            var list = state?.ownedVehicles;
            if (list == null) return removed;
            var records = new List<VehicleRecord>();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var v = list.get_Item(i);
                if (v == null) continue;
                var d = MoonVehicles.Definition(v.vehicle);
                if (d == null)
                {
                    if (v.vehicle >= MoonVehicles.FirstModdedIndex)
                        CoreLog.Warn($"Save: owned vehicle with unknown index {v.vehicle} (garage {v.garage}) is going into the vanilla save");
                    continue;
                }
                removed.Add(v);
                list.RemoveAt(i);
                records.Add(new VehicleRecord
                {
                    Id = d.Id, Garage = v.garage, Owner = v.owner, OwnerName = v.ownerName, InWorld = v.inWorld,
                    Position = new[] { v.position.x, v.position.y, v.position.z },
                    Rotation = new[] { v.rotation.x, v.rotation.y, v.rotation.z, v.rotation.w },
                    Holder = v.holder, Odometer = v.odometer,
                    Upgrades = v.upgrades == null ? Array.Empty<int>() : v.upgrades.Select(b => (int)b).ToArray(),
                });
            }
            lock (gate) liveVehicles = records;
            CoreLog.Info($"Save: moved {removed.Count} modded owned vehicles out of the vanilla save");
            WriteFile();
            return removed;
        }

        internal static void PutBackOwned(MoonLooseDustSnapshot state, List<MoonOwnedVehicle> removed)
        {
            if (removed == null || removed.Count == 0 || state?.ownedVehicles == null) return;
            foreach (var v in removed) state.ownedVehicles.Add(v);
        }

        internal static List<MoonVehicleStorageSnapshot> StripStorage(MoonLooseDustSnapshot state)
        {
            var removed = new List<MoonVehicleStorageSnapshot>();
            var list = state?.vehicles;
            if (list == null) return removed;
            var records = new List<StorageRecord>();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var s = list.get_Item(i);
                string key = s == null ? null : StorageIds.Stable(s.id);
                if (key == null) continue;
                removed.Add(s);
                list.RemoveAt(i);
                records.Add(new StorageRecord { Key = key, Kilograms = s.kilograms, Capacity = s.capacity });
            }
            lock (gate) liveStorage = records;
            CoreLog.Info($"Save: moved {removed.Count} modded vehicle storages out of the vanilla save");
            WriteFile();
            return removed;
        }

        internal static void PutBackStorage(MoonLooseDustSnapshot state, List<MoonVehicleStorageSnapshot> removed)
        {
            if (removed == null || removed.Count == 0 || state?.vehicles == null) return;
            foreach (var s in removed) state.vehicles.Add(s);
        }

        static void WriteFile()
        {
            string root = CoreState.SaveRoot;
            if (root == null) { CoreState.CaptureRoot(CoreState.World); root = CoreState.SaveRoot; }
            string path = PathFor(root);
            if (path == null) { CoreLog.Error("Save: save root unknown, modded vehicles NOT written to the sidecar this time"); return; }
            lock (gate)
            {
                if (loadedRoot != root) CoreLog.Warn($"Save: writing sidecar to {root} but loaded from {loadedRoot ?? "nothing"}");
                var data = new FileData
                {
                    Written = DateTime.Now.ToString("s"),
                    Vehicles = liveVehicles.Concat(orphanVehicles).ToList(),
                    Storage = liveStorage.Concat(orphanStorage).ToList(),
                    CustomLevels = CustomLevels,
                };
                // Nothing to say and nothing on disk: don't create the file.
                if (data.Vehicles.Count == 0 && data.Storage.Count == 0 && data.CustomLevels.Count == 0 && !File.Exists(path)) return;
                try
                {
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(data, json));
                    if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                    File.Move(tmp, path, true);
                    CoreLog.Info($"Sidecar written: {data.Vehicles.Count} vehicles ({orphanVehicles.Count} orphaned), {data.Storage.Count} storages -> {path}");
                }
                catch (Exception e) { CoreLog.Error($"Writing sidecar {path} failed: {e}"); }
            }
        }
    }

    [HarmonyPatch]
    internal static class SavePatches
    {
        [ThreadStatic] static List<MoonOwnedVehicle> strippedOwned;
        [ThreadStatic] static List<MoonVehicleStorageSnapshot> strippedStorage;

        [HarmonyPatch(typeof(MoonOwnedVehicleSave), nameof(MoonOwnedVehicleSave.Write)), HarmonyPrefix]
        static void OwnedWritePre(MoonLooseDustSnapshot state)
        {
            try { strippedOwned = Sidecar.StripOwned(state); }
            catch (Exception e) { CoreLog.Error("Save: stripping owned vehicles failed: " + e); }
        }

        [HarmonyPatch(typeof(MoonOwnedVehicleSave), nameof(MoonOwnedVehicleSave.Write)), HarmonyFinalizer]
        static void OwnedWriteFin(MoonLooseDustSnapshot state)
        {
            try { Sidecar.PutBackOwned(state, strippedOwned); }
            catch (Exception e) { CoreLog.Error("Save: putting owned vehicles back failed: " + e); }
            strippedOwned = null;
        }

        [HarmonyPatch(typeof(MoonVehicleStorageSave), nameof(MoonVehicleStorageSave.Write)), HarmonyPrefix]
        static void StorageWritePre(MoonLooseDustSnapshot snapshot)
        {
            try { strippedStorage = Sidecar.StripStorage(snapshot); }
            catch (Exception e) { CoreLog.Error("Save: stripping vehicle storage failed: " + e); }
        }

        [HarmonyPatch(typeof(MoonVehicleStorageSave), nameof(MoonVehicleStorageSave.Write)), HarmonyFinalizer]
        static void StorageWriteFin(MoonLooseDustSnapshot snapshot)
        {
            try { Sidecar.PutBackStorage(snapshot, strippedStorage); }
            catch (Exception e) { CoreLog.Error("Save: putting vehicle storage back failed: " + e); }
            strippedStorage = null;
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreOwnedVehicles)), HarmonyPrefix]
        static void RestoreOwned(MoonWorld __instance, MoonLooseDustSnapshot snapshot)
        {
            try { Injection.Ensure(); Sidecar.InjectOwned(__instance, snapshot); }
            catch (Exception e) { CoreLog.Error("Load: injecting modded vehicles failed: " + e); }
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreOwnedVehicles)), HarmonyPostfix]
        static void RestoreOwnedPost(MoonWorld __instance)
        {
            try
            {
                int modded = 0, total = 0;
                foreach (var kv in __instance.ownedVehicles)
                {
                    total++;
                    var v = kv.Value;
                    if (v != null && MoonVehicles.IsModded(v.vehicle))
                    {
                        modded++;
                        CoreLog.Info($"Load: world owns modded #{v.vehicle} storage={Patches.PurchasePatches.Redact(v.Storage)} inWorld={v.inWorld}");
                    }
                }
                CoreLog.Info($"Load: world has {total} owned vehicles, {modded} modded");
            }
            catch (Exception e) { CoreLog.Error("Load: listing owned vehicles failed: " + e); }
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreVehicleStorage)), HarmonyPrefix]
        static void RestoreStorage(MoonWorld __instance, MoonLooseDustSnapshot snapshot)
        {
            try { Injection.Ensure(); Sidecar.InjectStorage(__instance, snapshot); }
            catch (Exception e) { CoreLog.Error("Load: injecting vehicle storage failed: " + e); }
        }

        [HarmonyPatch(typeof(MoonOwnedVehicleSave), nameof(MoonOwnedVehicleSave.ReadOne)), HarmonyPostfix]
        static void ReadOne(MoonOwnedVehicle __result)
        {
            if (__result != null && __result.vehicle >= MoonVehicles.FirstModdedIndex)
                CoreLog.Warn($"Load: vanilla save contains vehicle index {__result.vehicle} (garage {__result.garage}); it was saved without the core's sidecar");
        }
    }
}
