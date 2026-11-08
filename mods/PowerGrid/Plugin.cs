using BepInEx;
using BepInEx.Unity.IL2CPP;
using MoonCleaners;
using StructureLib;
using UnityEngine;

namespace PowerGrid
{
    // A power pole, a solar array and a battery bank: the pieces of a StructureLib power grid.
    // Registrations only; the ids never change after release.
    [BepInPlugin(Guid, "Power Grid", Version)]
    [BepInDependency(StructureLib.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.ryanhartwig.powergrid";
        public const string Version = "0.1.0";

        const float DefaultLinkRange = 24f;
        const float DefaultSupplyRadius = 12f;

        public override void Load()
        {
            var linkRange = Config.Bind("Grid", "LinkRange", DefaultLinkRange,
                "How far a power pole links to other power poles, in metres (pole centre to pole centre).").Value;
            var supplyRadius = Config.Bind("Grid", "SupplyRadius", DefaultSupplyRadius,
                "How far a power pole powers machines, in metres. Must not exceed LinkRange.").Value;

            // PowerRules refuses a relay with a non-positive range or a radius past its link range, which would drop the
            // pole from the game: fall back to the defaults instead.
            if (linkRange <= 0f || supplyRadius <= 0f || supplyRadius > linkRange)
            {
                Log.LogWarning($"[Grid] LinkRange {linkRange} and SupplyRadius {supplyRadius} are not usable (both must be above 0, " +
                    $"and SupplyRadius at most LinkRange); using {DefaultLinkRange} and {DefaultSupplyRadius}.");
                linkRange = DefaultLinkRange;
                supplyRadius = DefaultSupplyRadius;
            }

            // The solar post: a power node base that has a production view. Notes quote the configured ranges.
            MoonStructures.Register(new StructureDefinition
            {
                Id = "ryanhartwig.power-pole",
                Name = "Power pole",
                Category = "Power",
                Note = $"Links to poles within {linkRange:0.##} m and powers machines within {supplyRadius:0.##} m. Grids pool every source and share the load evenly.",
                Price = 50m,
                BaseStructure = MoonWorld.SolarPostKind,
                Footprint = new Vector2(0.6f, 0.6f),
                // blender/power_pole.py
                Model = "power_pole.bin",
                Power = new PowerDefinition { Role = PowerRole.Relay, LinkRange = linkRange, SupplyRadius = supplyRadius },
            }, Guid);

            MoonStructures.Register(new StructureDefinition
            {
                Id = "ryanhartwig.solar-array",
                Name = "Solar array",
                Category = "Power",
                Note = $"10 kW at full sun into the power grid. Needs a power pole within {supplyRadius:0.##} m.",
                Price = 180m,
                BaseStructure = MoonWorld.SolarTwinKind,
                Footprint = new Vector2(4f, 2f),
                // blender/solar_array.py
                Model = "solar_array.bin",
                Power = new PowerDefinition { Role = PowerRole.Source, SupplyMode = SupplyMode.Solar, PeakKw = 10 },
            }, Guid);

            MoonStructures.Register(new StructureDefinition
            {
                Id = "ryanhartwig.battery-bank",
                Name = "Battery bank",
                Category = "Power",
                Note = $"Stores 20 kWh for the grid, 10 kW in or out. Needs a power pole within {supplyRadius:0.##} m.",
                Price = 300m,
                BaseStructure = MoonWorld.SolarPostKind,
                Footprint = new Vector2(1.2f, 1.2f),
                // blender/battery_bank.py
                Model = "battery_bank.bin",
                Power = new PowerDefinition { Role = PowerRole.Storage, BatteryKwh = 20, RateKw = 10 },
            }, Guid);

            Log.LogInfo($"Power Grid {Version} loaded (LinkRange {linkRange}, SupplyRadius {supplyRadius})");
        }
    }
}
