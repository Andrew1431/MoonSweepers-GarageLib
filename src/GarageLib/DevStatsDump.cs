#if DEVBUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MoonCleaners;

namespace GarageLib
{
    // Dev build: writes every vanilla vehicle's catalog numbers, garage price and upgrade price table to
    // BepInEx/cache/garagelib/vanilla-stats.json, for balancing new vehicles against the base game.
    internal static class DevStatsDump
    {
        internal static void Write()
        {
            var vehicles = new List<object>();
            var catalog = MoonPrototypeCatalog.Vehicles;
            var offers = MoonWorld.GarageOffers;
            var defs = MoonVehicleUpgrades.All;
            for (int i = 0; i < Math.Min(catalog.Length, MoonVehicles.FirstModdedIndex); i++)
            {
                var it = catalog[i];
                if (it == null) continue;
                object offer = null;
                if (offers != null)
                    foreach (var o in offers)
                        if (o != null && o.vehicle == i) offer = new { price = Money.From(o.price), o.summary };
                // Unused prototypes (2-19) have no prefab; asking the game about them can fault natively (uncatchable).
                // Heft() faults natively when called from here even for sold vehicles, so it is left out.
                bool sold = offer != null;
                var upgrades = new List<object>();
                if (sold && defs != null)
                    foreach (var d in defs)
                    {
                        if (d == null) continue;
                        bool applies = Try(() => MoonVehicleUpgrades.Applies(d, i));
                        var prices = new List<decimal?>();
                        for (int l = 1; l <= d.maxLevel; l++) { int lv = l; prices.Add(TryN(() => Money.From(MoonVehicleUpgrades.Price(d, i, lv)))); }
                        upgrades.Add(new { kind = d.kind.ToString(), applies, prices });
                    }
                vehicles.Add(new
                {
                    index = i, it.name, it.hint, work = it.work.ToString(),
                    it.width, it.depth, it.reach, it.speed, it.turn, it.capacity,
                    storageKg = sold ? TryN(() => MoonPrototypeCatalog.StorageKg(i)) : null,
                    prefab = sold ? TryS(() => MoonPrototypeCatalog.AuthoredPrefab(i)) : null,
                    sweeper = Try(() => MoonVehicleUpgrades.Sweeper(it)),
                    upgradePriceScale = sold ? TryN(() => Money.From(MoonVehicleUpgrades.PriceScale(i))) : null,
                    offer, upgrades,
                });
            }

            var upgradeDefs = new List<object>();
            if (defs != null)
                foreach (var d in defs)
                {
                    if (d == null) continue;
                    var basePrices = new List<int>();
                    if (d.prices != null) foreach (var p in d.prices) basePrices.Add(p);
                    var effects = new List<string>();
                    for (int l = 1; l <= d.maxLevel; l++) { int lv = l; effects.Add(TryS(() => d.effect?.Invoke(lv))); }
                    upgradeDefs.Add(new { kind = d.kind.ToString(), d.title, d.summary, d.maxLevel, d.sweepersOnly, basePrices, effects });
                }

            var boosts = new List<float?>();
            for (int l = 0; l <= 7; l++) { int lv = l; boosts.Add(TryN(() => MoonVehicleUpgrades.Boost(lv))); }
            var tuning = new
            {
                speedStep = MoonVehicleUpgrades.SpeedStep, hopperStep = MoonVehicleUpgrades.HopperStep, boostByLevel = boosts,
                suctionNames = MoonVehicleUpgrades.SuctionNames, suctionWidth = MoonVehicleUpgrades.SuctionWidth,
                suctionSpeed = MoonVehicleUpgrades.SuctionSpeed, suctionNormal = MoonVehicleUpgrades.SuctionNormal,
                driftTurn = MoonPrototypeVehicle.DriftTurn, driftGrip = MoonPrototypeVehicle.DriftGrip,
                springHz = MoonPrototypeVehicle.SpringHz, dampingRatio = MoonPrototypeVehicle.DampingRatio,
                downforce = MoonPrototypeVehicle.Downforce,
            };

            var dir = Path.Combine(BepInEx.Paths.CachePath, "garagelib");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "vanilla-stats.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { vehicles, upgradeDefs, tuning }, new JsonSerializerOptions { WriteIndented = true }));
            CoreLog.Info($"[dev] vanilla stats: {vehicles.Count} vehicles, {upgradeDefs.Count} upgrade kinds -> {path}");
        }

        static bool Try(Func<bool> f) { try { return f(); } catch { return false; } }
        static T? TryN<T>(Func<T> f) where T : struct { try { return f(); } catch { return null; } }
        static string TryS(Func<string> f) { try { return f(); } catch (Exception e) { return "!" + e.GetType().Name; } }
    }
}
#endif
