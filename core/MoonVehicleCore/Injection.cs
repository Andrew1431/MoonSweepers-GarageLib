using System;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MoonCleaners;

namespace MoonVehicleCore
{
    // Grows the game's static vehicle tables with the registered vehicles. Idempotent: Ensure() is called every few
    // seconds and after world loads, in case the game rebuilds a table.
    internal static class Injection
    {
        static int vanillaCount = -1;
        static string vanillaNote;

        internal static void Ensure()
        {
            var vehicles = MoonPrototypeCatalog.Vehicles;
            if (vehicles == null) { CoreLog.Once("cat-null", "Catalog not ready yet (Vehicles null)"); return; }

            if (!MoonVehicles.Frozen)
            {
                vanillaCount = vehicles.Length;
                CoreLog.Info($"Vanilla catalog has {vanillaCount} vehicles; garage mask {MoonWorld.GarageVehicleMask} ({MaskBits(MoonWorld.GarageVehicleMask)})");
                MoonVehicles.Freeze(vanillaCount);
            }
            if (MoonVehicles.All.Count == 0) return;

            EnsureCatalog(vehicles);
            EnsureOffers();
            EnsureMask();
            EnsureNote();
        }

        static void EnsureCatalog(Il2CppReferenceArray<MoonPrototypeCatalog.Item> vehicles)
        {
            int needed = MoonVehicles.All.Max(d => d.Index) + 1;
            bool complete = vehicles.Length >= needed && MoonVehicles.All.All(d => vehicles[d.Index] != null && vehicles[d.Index].name == d.Name);
            if (complete) return;

            var grown = new Il2CppReferenceArray<MoonPrototypeCatalog.Item>(Math.Max(needed, vehicles.Length));
            for (int i = 0; i < vehicles.Length; i++) grown[i] = vehicles[i];
            foreach (var d in MoonVehicles.All)
            {
                var b = d.BaseVehicle >= 0 && d.BaseVehicle < vanillaCount ? vehicles[d.BaseVehicle] : null;
                if (b == null) CoreLog.Warn($"'{d.Id}': base vehicle {d.BaseVehicle} missing from catalog, using raw defaults");
                var item = new MoonPrototypeCatalog.Item(
                    d.Name,
                    d.Work ?? b?.work ?? MoonPrototypeCatalog.Work.Clean,
                    string.IsNullOrEmpty(d.Hint) ? b?.hint ?? "" : d.Hint,
                    Pick(d.Width, b?.width ?? 1f), Pick(d.Depth, b?.depth ?? 1f), Pick(d.Reach, b?.reach ?? 1f),
                    Pick(d.Speed, b?.speed ?? 5f), Pick(d.Turn, b?.turn ?? 60f),
                    d.Capacity >= 0 ? d.Capacity : b?.capacity ?? 0);
                grown[d.Index] = item;
                CoreLog.Info($"Catalog[{d.Index}] = '{item.name}' work={item.work} w={item.width} d={item.depth} reach={item.reach} speed={item.speed} turn={item.turn} cap={item.capacity}");
            }
            for (int i = vanillaCount; i < grown.Length; i++)
                if (grown[i] == null)
                {
                    // Gap filler so code that walks the array never hits null.
                    grown[i] = new MoonPrototypeCatalog.Item("(unused)", MoonPrototypeCatalog.Work.Clean, "", 1, 1, 1, 1, 1, 0);
                    CoreLog.Debug($"Catalog[{i}] gap filled");
                }
            MoonPrototypeCatalog.Vehicles = grown;
            var check = MoonPrototypeCatalog.Vehicles;
            CoreLog.Info($"Catalog grown {vehicles.Length} -> {check?.Length} (setter {(check?.Length == grown.Length ? "worked" : "DID NOT stick")})");
        }

        static void EnsureOffers()
        {
            var offers = MoonWorld.GarageOffers;
            if (offers == null) { CoreLog.Once("offers-null", "GarageOffers null, will retry"); return; }
            var missing = MoonVehicles.All.Where(d => !offers.Any(o => o != null && o.vehicle == d.Index)).ToList();
            if (missing.Count == 0) return;

            var grown = new Il2CppReferenceArray<MoonWorld.GarageOffer>(offers.Length + missing.Count);
            for (int i = 0; i < offers.Length; i++) grown[i] = offers[i];
            int k = offers.Length;
            foreach (var d in missing)
            {
                var o = new MoonWorld.GarageOffer();
                o.vehicle = d.Index;
                o.price = new Il2CppSystem.Decimal((double)d.Price);
                o.summary = d.Summary;
                grown[k++] = o;
                CoreLog.Info($"Garage offer added: vehicle={d.Index} '{d.Name}' price={o.price.ToString()} summary='{d.Summary}'");
            }
            MoonWorld.GarageOffers = grown;
            CoreLog.Info($"GarageOffers {offers.Length} -> {MoonWorld.GarageOffers?.Length}");
        }

        static bool maskReported;

        // GarageVehicleMask is a compile-time constant in the game (writes don't stick), so it can't be extended.
        // Logged once so test runs show whether purchase still works without it.
        static void EnsureMask()
        {
            if (maskReported) return;
            maskReported = true;
            int mask = MoonWorld.GarageVehicleMask;
            var outside = MoonVehicles.All.Where(d => d.Index >= MoonVehicles.MaskLimit || (mask >> d.Index & 1) == 0).Select(d => d.ToString());
            CoreLog.Info($"GarageVehicleMask is {mask} ({MaskBits(mask)}), a constant; modded vehicles not in it: [{string.Join(", ", outside)}]");
        }

        static void EnsureNote()
        {
            string note = MoonWorld.GarageNote ?? "";
            if (MoonVehicles.All.All(d => note.Contains(d.Name))) return;
            vanillaNote ??= note;
            string extra = string.Join("\n", MoonVehicles.All.Select(d => $"{d.Name}: {MoonGarageMenuPrice(d.Price)}"));
            MoonWorld.GarageNote = vanillaNote.TrimEnd() + "\n" + extra;
            CoreLog.Debug($"GarageNote now:\n{MoonWorld.GarageNote}");
        }

        static string MoonGarageMenuPrice(decimal p) => "$" + p.ToString("#,0");
        static float Pick(float v, float fallback) => float.IsNaN(v) ? fallback : v;
        static string MaskBits(int m) => "bits " + string.Join(",", Enumerable.Range(0, 32).Where(i => (m >> i & 1) != 0));
    }
}
