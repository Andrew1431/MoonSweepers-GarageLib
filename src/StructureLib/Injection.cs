using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MoonCleaners;
using StructureLib.Patches;
using UnityEngine;

namespace StructureLib
{
    // One cloned MoonDepotBuildDefinition per modded structure per world: the donor's definition copied with the
    // structure's name and price. A new world gets new clones. Ensure() is idempotent and runs at world load and every
    // few seconds; DefinitionFor() also builds a missing clone on demand. Ensure() also keeps the build menu's static
    // catalog complete (EnsureCatalog).
    internal static class Injection
    {
        static IntPtr cacheWorld = IntPtr.Zero;
        static readonly Dictionary<string, MoonDepotBuildDefinition> clones = new(StringComparer.Ordinal);

        internal static void Ensure(MoonWorld world)
        {
            if (!MoonStructures.Frozen || MoonStructures.Disabled || world == null || world.Pointer == IntPtr.Zero || !world) return;
            foreach (var d in MoonStructures.All)
                if (d.Kind > 0) DefinitionFor(d, world);
            EnsureCatalog();
        }

        static bool catalogReady;

        // The build menu's static catalog: one entry per modded structure (image "structurelib:<id>", which the CardArt
        // prefix answers) and any category the vanilla tabs lack, appended in registration order. Idempotent: only
        // kinds and categories not already present are added, so a repeat call (or another world) changes nothing.
        internal static void EnsureCatalog()
        {
            if (!MoonStructures.Frozen || MoonStructures.Disabled) return;
            var live = MoonStructures.All.Where(d => d.Kind > 0 && MoonStructures.IsModded(d.Kind)).ToList();
            if (live.Count == 0) return;

            var entries = MoonDepotBuilder.baseStructures;
            if (entries == null) { CoreLog.Once("catalog-null", "MoonDepotBuilder.baseStructures is null, will retry", 1); return; }
            var missing = live.Where(d => !entries.Any(e => e != null && e.kind == d.Kind)).ToList();
            if (missing.Count > 0)
            {
                var grown = new Il2CppReferenceArray<MoonDepotBuilder.CatalogEntry>(entries.Length + missing.Count);
                for (int i = 0; i < entries.Length; i++) grown[i] = entries[i];
                int k = entries.Length;
                foreach (var d in missing)
                {
                    grown[k++] = new MoonDepotBuilder.CatalogEntry(d.Kind, d.Name, d.Category, CatalogPatches.ImagePrefix + d.Id);
                    CoreLog.Info($"Catalog entry added: kind {d.Kind} '{d.Name}' in '{d.Category}', image '{CatalogPatches.ImagePrefix}{d.Id}'");
                }
                MoonDepotBuilder.baseStructures = grown;
                var check = MoonDepotBuilder.baseStructures;
                CoreLog.Info($"Catalog grown {entries.Length} -> {check?.Length} (setter {(check?.Length == grown.Length ? "worked" : "DID NOT stick")})");
            }

            var categories = MoonDepotBuilder.catalogCategories;
            if (categories == null) { CoreLog.Once("categories-null", "MoonDepotBuilder.catalogCategories is null, will retry", 1); return; }
            var known = new HashSet<string>(categories, StringComparer.Ordinal);
            var newCategories = new List<string>();
            foreach (var d in live)
                if (!string.IsNullOrEmpty(d.Category) && known.Add(d.Category)) newCategories.Add(d.Category);
            if (newCategories.Count > 0)
            {
                var grown = new Il2CppStringArray(categories.Length + newCategories.Count);
                for (int i = 0; i < categories.Length; i++) grown[i] = categories[i];
                for (int i = 0; i < newCategories.Count; i++) grown[categories.Length + i] = newCategories[i];
                MoonDepotBuilder.catalogCategories = grown;
                var check = MoonDepotBuilder.catalogCategories;
                CoreLog.Info($"Catalog categories grown {categories.Length} -> {check?.Length}: added {string.Join(", ", newCategories.Select(c => "'" + c + "'"))} (setter {(check?.Length == grown.Length ? "worked" : "DID NOT stick")})");
            }

            // First time the catalog is complete: render the card pictures (no-op if already queued at session start).
            var now = MoonDepotBuilder.baseStructures;
            if (!catalogReady && now != null && live.All(d => now.Any(e => e != null && e.kind == d.Kind)))
            {
                catalogReady = true;
                IconRenderer.QueueAll();
            }
        }

        internal static MoonDepotBuildDefinition DefinitionFor(StructureDefinition d, MoonWorld world)
        {
            if (d == null || world == null || world.Pointer == IntPtr.Zero) return null;
            SwitchWorld(world);
            if (clones.TryGetValue(d.Id, out var cached) && Alive(cached)) return cached;

            // BaseStructure is a vanilla kind, so this goes straight to the game's own table.
            var donor = world.BuildDefinition(d.BaseStructure);
            if (donor == null)
            {
                CoreLog.Once("clone-nodonor:" + d.Id, $"'{d.Id}': donor kind {d.BaseStructure} has no build definition in this world; no definition for kind {d.Kind}", 2);
                return null;
            }
            var clone = UnityEngine.Object.Instantiate<MoonDepotBuildDefinition>(donor);
            if (clone == null)
            {
                CoreLog.Once("clone-null:" + d.Id, $"'{d.Id}': cloning donor definition {d.BaseStructure} returned null", 2);
                return null;
            }
            clone.displayName = d.Name;
            clone.priceDollars = (int)d.Price;
            clone.name = "StructureLib_" + d.Id;
            // Nothing in the scene references the clone until something is built from it; keep asset GC off it.
            clone.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            // StructureDefinition.Footprint (full width and depth) replaces the donor's half footprint, which placement,
            // spacing and the line layout read. Before the model, so a BuildModel sees the final shape.
            if (d.Footprint.HasValue)
            {
                var was = clone.halfFootprint;
                clone.halfFootprint = d.Footprint.Value / 2f;
                CoreLog.Info($"'{d.Id}': halfFootprint ({was.x:F2}, {was.y:F2}) -> ({clone.halfFootprint.x:F2}, {clone.halfFootprint.y:F2}) from Footprint {d.Footprint.Value.x:F2} x {d.Footprint.Value.y:F2}");
            }
            // A Model file with no BuildModel of the mod's own: the library's swap onto the donor template.
            var build = d.BuildModel ?? (d.ModelPath != null ? (ModelContext ctx) => ModelSwap.Build(d, ctx) : null);
            if (build != null) ApplyBuildModel(d, build, clone, donor);
            ApplyPorts(d, clone);
            clones[d.Id] = clone;
            CoreLog.Info($"Definition for '{d.Id}' (kind {d.Kind}) cloned from kind {d.BaseStructure} ('{donor.displayName}'): '{clone.displayName}', ${clone.priceDollars}");
            return clone;
        }

        // BuildModel runs once per world; its result becomes the clone's prefab (and every skin), so the game instantiates
        // the mod's model wherever it would have instantiated the donor's. The template lives under an inactive holder,
        // so its scripts never wake; the copies the game makes from it are active. A null result or a throw keeps the
        // donor's model.
        static GameObject modelHolder;

        static void ApplyBuildModel(StructureDefinition d, Func<ModelContext, GameObject> build, MoonDepotBuildDefinition clone, MoonDepotBuildDefinition donor)
        {
            try
            {
                if (!Alive(modelHolder))
                {
                    modelHolder = new GameObject("StructureLib_Models");
                    modelHolder.SetActive(false);
                    modelHolder.hideFlags |= HideFlags.HideInHierarchy;
                }
                var model = build(new ModelContext { Definition = d, Parent = modelHolder.transform, Donor = donor });
                if (model == null) { CoreLog.Warn($"'{d.Id}': BuildModel returned null; using the model of base kind {d.BaseStructure}"); return; }
                if (model.transform.parent == null || model.transform.parent.Pointer != modelHolder.transform.Pointer)
                    model.transform.SetParent(modelHolder.transform, false);
                clone.prefab = model;
                var skins = clone.skinPrefabs;
                if (skins != null && skins.Length > 0)
                {
                    var own = new Il2CppReferenceArray<GameObject>(skins.Length);
                    for (int i = 0; i < skins.Length; i++) own[i] = model;
                    clone.skinPrefabs = own;
                }
                CoreLog.Info($"'{d.Id}': BuildModel gave '{model.name}'; it replaces the prefab of base kind {d.BaseStructure}" + (skins != null && skins.Length > 0 ? $" and its {skins.Length} skin(s)" : ""));
            }
            catch (Exception e) { CoreLog.Error($"'{d.Id}': BuildModel threw; using the model of base kind {d.BaseStructure}: {e}"); }
        }

        // The clone's conveyorPorts become the resolved ports (MoonStructures.PortsOf), each named "sl:<cargo byte>:<Name>"
        // so the PortCargo prefix can answer its cargo from the name alone. The game's belt snapping, joining and labels
        // read this array. A throw keeps the donor's ports.
        static void ApplyPorts(StructureDefinition d, MoonDepotBuildDefinition clone)
        {
            var ports = d.ResolvedPorts;
            if (ports == null)
            {
                // Unresolved (the base's ports could not be read at the freeze): never write an empty array over them.
                CoreLog.Warn($"'{d.Id}': no resolved ports ({d.PortRule ?? "registry not frozen"}); the clone keeps the conveyorPorts of base kind {d.BaseStructure} unchanged");
                return;
            }
            try
            {
                var arr = new Il2CppReferenceArray<MoonDepotBuildDefinition.ConveyorPort>(ports.Count);
                for (int i = 0; i < ports.Count; i++)
                {
                    var p = ports[i];
                    var cp = new MoonDepotBuildDefinition.ConveyorPort();
                    cp.name = PortRules.EncodeName(p.Cargo.Value, p.Name);
                    cp.position = p.Position;
                    cp.direction = p.Direction;
                    cp.output = p.Output;
                    arr[i] = cp;
                }
                clone.conveyorPorts = arr;
                var back = clone.conveyorPorts;
                var names = new List<string>();
                for (int i = 0; i < (back?.Length ?? 0); i++) names.Add(back[i] == null ? "null" : $"'{back[i].name}' {(back[i].output ? "out" : "in")} ({back[i].position.x:F2},{back[i].position.y:F2},{back[i].position.z:F2})");
                CoreLog.Info($"'{d.Id}': ports from {d.PortRule}: {PortResolver.Describe(ports)}; clone conveyorPorts now {back?.Length ?? -1} [{string.Join(", ", names)}]");
            }
            catch (Exception e) { CoreLog.Error($"'{d.Id}': writing the clone's conveyorPorts threw; it keeps the ports of base kind {d.BaseStructure}: {e}"); }
        }

        // Clones belong to one world; drop (and destroy) them, and any BuildModel templates, when a different world asks.
        static void SwitchWorld(MoonWorld world)
        {
            if (world.Pointer == cacheWorld) return;
            if (clones.Count > 0)
            {
                foreach (var c in clones.Values)
                    if (Alive(c)) UnityEngine.Object.Destroy(c);
                CoreLog.Info($"World changed: dropped {clones.Count} cloned definition(s)");
                clones.Clear();
            }
            if (Alive(modelHolder)) UnityEngine.Object.Destroy(modelHolder);
            modelHolder = null;
            cacheWorld = world.Pointer;
        }

        static bool Alive(UnityEngine.Object o) => o != null && o.Pointer != IntPtr.Zero && o;
    }
}
