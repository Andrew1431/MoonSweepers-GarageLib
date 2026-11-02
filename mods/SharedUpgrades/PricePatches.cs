using System;
using HarmonyLib;
using MoonCleaners;

namespace SharedUpgrades
{
    // Shared prices. TryGetVehicleUpgradePrice is what the purchase charges and the garage menu shows; Price covers
    // any other caller. Both compute from the base price, so it doesn't matter whether one calls the other.
    [HarmonyPatch]
    internal static class PricePatches
    {
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryGetVehicleUpgradePrice)), HarmonyPostfix]
        static void TryGetPost(MoonOwnedVehicle v, MoonVehicleUpgrades.Kind kind, ref Il2CppSystem.Decimal price, bool __result)
        {
            if (!__result || v == null || !Shared.Active) return;
            try { price = Money.To(Shared.SharedPrice(MoonVehicleUpgrades.Find(kind), v.vehicle, MoonWorld.VehicleUpgradeLevel(v, kind))); }
            catch (Exception e) { Plugin.L.LogError("TryGetVehicleUpgradePrice: " + e); }
        }

        [HarmonyPatch(typeof(MoonVehicleUpgrades), nameof(MoonVehicleUpgrades.Price)), HarmonyPostfix]
        static void Price(MoonVehicleUpgrades.Def def, int vehicle, int level, ref Il2CppSystem.Decimal __result)
        {
            if (def == null || !Shared.Active) return;
            try { __result = Money.To(Shared.SharedPrice(def, vehicle, level)); }
            catch (Exception e) { Plugin.L.LogError("Price: " + e); }
        }

        // Equalize right after a purchase instead of on the next check, so the menu shows the new level everywhere.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryUpgradeVehicle)), HarmonyPostfix]
        static void Bought(MoonWorld __instance, bool __result, MoonVehicleUpgrades.Kind kind, int expectedLevel)
        {
            if (!__result || !Shared.Active) return;
            Plugin.L.LogInfo($"Bought {kind} level {expectedLevel + 1}: sharing it");
            try { Shared.Check(__instance); } catch (Exception e) { Plugin.L.LogError("After purchase: " + e); }
        }
    }
}
