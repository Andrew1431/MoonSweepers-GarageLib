using System;
using System.Collections.Generic;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    // One cloned MoonDepotBuildDefinition per modded structure per world: the donor's definition copied with the
    // structure's name and price. A new world gets new clones. Ensure() is idempotent and runs at world load and every
    // few seconds; DefinitionFor() also builds a missing clone on demand.
    internal static class Injection
    {
        static IntPtr cacheWorld = IntPtr.Zero;
        static readonly Dictionary<string, MoonDepotBuildDefinition> clones = new(StringComparer.Ordinal);

        internal static void Ensure(MoonWorld world)
        {
            if (!MoonStructures.Frozen || MoonStructures.Disabled || world == null || world.Pointer == IntPtr.Zero || !world) return;
            foreach (var d in MoonStructures.All)
                if (d.Kind > 0) DefinitionFor(d, world);
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
