using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MoonCleaners;

namespace GarageLib
{
    /// <summary>
    /// Storage and purchase of custom upgrade levels. Mirrors the vanilla MoonWorld methods (VehicleUpgradeLevel,
    /// TryGetVehicleUpgradePrice, TryUpgradeVehicle) so an upgrades mod can redirect both systems the same way.
    /// <paramref name="storage"/> is always the live storage id of an owned vehicle.
    /// </summary>
    public interface IUpgradeResolver
    {
        int Level(string storage, string upgradeId);
        bool TryGetPrice(string storage, string upgradeId, out decimal price);
        bool TryBuy(string player, string storage, string upgradeId, int expectedLevel, out string message);
    }

    /// <summary>Default resolver: per owned vehicle, saved in the core sidecar, paid from the buyer's wallet.</summary>
    public class SidecarUpgradeResolver : IUpgradeResolver
    {
        public virtual int Level(string storage, string upgradeId)
        {
            string key = StorageIds.Stable(storage);
            return key != null && Sidecar.CustomLevels.TryGetValue(key, out var m) && m.TryGetValue(upgradeId, out int l) ? l : 0;
        }

        public virtual bool TryGetPrice(string storage, string upgradeId, out decimal price)
        {
            price = 0;
            var up = Find(storage, upgradeId, out _);
            int level = Level(storage, upgradeId);
            if (up == null || level >= up.MaxLevel) return false;
            price = up.Prices[level];
            return true;
        }

        public virtual bool TryBuy(string player, string storage, string upgradeId, int expectedLevel, out string message)
        {
            var up = Find(storage, upgradeId, out var def);
            if (up == null) { message = "Unknown upgrade"; return false; }
            int level = Level(storage, upgradeId);
            if (level != expectedLevel) { message = "Upgrade changed, try again"; return false; }
            if (level >= up.MaxLevel) { message = "Fully upgraded"; return false; }
            decimal price = up.Prices[level];
            var w = CoreState.World;
            if (w == null) { message = "No world"; return false; }
            decimal wallet = Money.From(w.WalletDollars(player));
            if (wallet < price) { message = "Not enough money"; return false; }
            w.ChargeBuild(player, Money.To(price));
            string key = StorageIds.Stable(storage);
            if (!Sidecar.CustomLevels.TryGetValue(key, out var m)) Sidecar.CustomLevels[key] = m = new Dictionary<string, int>();
            m[upgradeId] = level + 1;
            CoreLog.Info($"Custom upgrade '{def.Id}/{upgradeId}' -> level {level + 1} for ${price} (wallet was {wallet})");
            message = $"{up.Title} level {level + 1}";
            return true;
        }

        protected static CustomUpgrade Find(string storage, string upgradeId, out VehicleDefinition def)
        {
            def = StorageIds.TryParse(storage, out _, out int index, out _) ? MoonVehicles.Definition(index) : null;
            return def?.CustomUpgrades.FirstOrDefault(u => u.Id == upgradeId);
        }
    }

    internal static class Money
    {
        internal static decimal From(Il2CppSystem.Decimal d)
        {
            try { return decimal.Parse(d.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return (decimal)(long)d; }
        }
        internal static Il2CppSystem.Decimal To(decimal d) => new Il2CppSystem.Decimal((double)d);
    }

    // Vanilla upgrade rules for modded indices: answer from the definition or fall back to the base vehicle. Levels are
    // never touched here; they stay in MoonOwnedVehicle.upgrades and are read through MoonWorld.
    [HarmonyPatch]
    internal static class UpgradeRulePatches
    {
        [HarmonyPatch(typeof(MoonVehicleUpgrades), nameof(MoonVehicleUpgrades.Applies)), HarmonyPrefix]
        static bool Applies(MoonVehicleUpgrades.Def def, ref int vehicle, ref bool __result)
        {
            var d = MoonVehicles.Definition(vehicle);
            if (d == null) return true;
            if (d.UpgradeKinds != null)
            {
                __result = def != null && Array.IndexOf(d.UpgradeKinds, def.kind) >= 0;
                CoreLog.Once($"applies{vehicle}{def?.kind}", $"Upgrades.Applies({def?.kind}, '{d.Id}') = {__result}", 1);
                return false;
            }
            vehicle = d.BaseVehicle;
            return true;
        }

        [HarmonyPatch(typeof(MoonVehicleUpgrades), nameof(MoonVehicleUpgrades.PriceScale)), HarmonyPrefix]
        static bool PriceScale(ref int vehicle, ref Il2CppSystem.Decimal __result)
        {
            var d = MoonVehicles.Definition(vehicle);
            if (d == null) return true;
            if (!double.IsNaN(d.UpgradePriceScale)) { __result = new Il2CppSystem.Decimal(d.UpgradePriceScale); return false; }
            vehicle = d.BaseVehicle;
            return true;
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryUpgradeVehicle)), HarmonyPostfix]
        static void TryUpgrade(bool __result, string storage, MoonVehicleUpgrades.Kind kind, int expectedLevel, string message)
        {
            if (StorageIds.TryParse(storage, out _, out int index, out _) && MoonVehicles.IsModded(index))
                CoreLog.Info($"TryUpgradeVehicle modded #{index} {kind} expected={expectedLevel} -> {__result} '{message}'");
        }
    }
}
