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
            clones[d.Id] = clone;
            CoreLog.Info($"Definition for '{d.Id}' (kind {d.Kind}) cloned from kind {d.BaseStructure} ('{donor.displayName}'): '{clone.displayName}', ${clone.priceDollars}");
            return clone;
        }

        // Clones belong to one world; drop (and destroy) them when a different world asks.
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
            cacheWorld = world.Pointer;
        }

        static bool Alive(UnityEngine.Object o) => o != null && o.Pointer != IntPtr.Zero && o;
    }
}
