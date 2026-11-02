using System;
using HarmonyLib;
using MoonCleaners;

namespace StructureLib.Patches
{
    // The registry freezes when a world loads: world.BuildDefinition (needed to vet donor kinds) only answers once a
    // world exists. A load runs MoonDepotSave.Read -> RestoreCanisters -> RestoreDepots in one frame, so the first of
    // RestoreCanisters / RestoreDepots freezes it: modded-cargo canisters need the frozen cargo table when they are
    // injected (RestoreCanisters), modded structures need it before they are restored.
    [HarmonyPatch]
    internal static class FreezePatches
    {
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreCanisters), new[] { typeof(MoonLooseDustSnapshot) }), HarmonyPrefix]
        static void RestoreCanisters(MoonWorld __instance) => FreezeFor(__instance, "RestoreCanisters");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreDepots), new[] { typeof(MoonLooseDustSnapshot) }), HarmonyPrefix]
        static void RestoreDepots(MoonWorld __instance)
        {
            FreezeFor(__instance, "RestoreDepots");
            // Every world load (the first and any later one): build this world's cloned definitions before saved
            // structures are restored. Ensure does nothing unless the registry is frozen and working.
            try { Injection.Ensure(__instance); }
            catch (Exception e) { CoreLog.Error("Building cloned structure definitions failed: " + e); }
        }

        static void FreezeFor(MoonWorld world, string hook)
        {
            if (MoonStructures.Frozen) return;
            try
            {
                CoreLog.Info($"World loading ({hook}): freezing the structure registry");
                MoonStructures.Freeze(kind => Donor(world, kind));
            }
            catch (Exception e) { CoreLog.Error("Freezing the structure registry failed: " + e); }
        }

        // The traits Rules.CheckDonor needs, read from the vanilla build definition of a donor kind. A read that throws
        // counts as "no such donor", which drops only the structures built on that kind.
        internal static DonorTraits Donor(MoonWorld world, int kind)
        {
            try
            {
                var def = world.BuildDefinition(kind);
                if (def == null) return new DonorTraits(false, false, false, false, false);
                var m = def.module;
                if (m == null) return new DonorTraits(true, false, false, false, false);
                return new DonorTraits(true, m.Deck, m.BuildingPart, m.CatwalkSupport, m.WorldGrid);
            }
            catch (Exception e)
            {
                CoreLog.Error($"Reading the build definition of donor kind {kind} threw; treating it as missing: {e.Message}");
                return new DonorTraits(false, false, false, false, false);
            }
        }
    }
}
