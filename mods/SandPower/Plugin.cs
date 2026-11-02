using BepInEx;
using BepInEx.Unity.IL2CPP;
using MoonCleaners;
using StructureLib;

namespace SandPower
{
    // A sand compactor that presses moon sand into sand briquettes, and a generator that burns them for power.
    // Registrations only; the ids and the cargo value never change after release.
    [BepInPlugin(Guid, "SandPower", Version)]
    [BepInDependency(StructureLib.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.ryanhartwig.sandpower";
        public const string Version = "0.1.0";

        public override void Load()
        {
            // Value 3 is claimed in docs/cargo-registry.md.
            var briquette = MoonStructures.RegisterCargo(new CargoDefinition
            {
                Id = "ryanhartwig.sand-briquette",
                Value = 3,
                Name = "sand briquette",
                ShortName = "briquette",
                UnitKilograms = 2,
                Price = 7m,
            }, Guid);

            MoonStructures.Register(new StructureDefinition
            {
                Id = "ryanhartwig.sand-compactor",
                Name = "Sand compactor",
                Category = "Production",
                Note = "Presses 4 kg of moon sand into sand briquettes every 8 s. Needs 12 kW.",
                Price = 800m,
                BaseStructure = MoonWorld.StonePressKind,
                Processor = new ProcessorDefinition
                {
                    RunningKw = 12,
                    CycleSeconds = 8,
                    Slots =
                    {
                        Slot.Input("Sand", Cargo.Sand, 16),
                        Slot.Batch("Pressing", Cargo.Sand, 4),
                        Slot.Output("Briquettes", briquette, 10),
                    },
                },
            }, Guid);

            // The solar post's model, not the battery bank's (6): that prefab has no production view.
            MoonStructures.Register(new StructureDefinition
            {
                Id = "ryanhartwig.briquette-generator",
                Name = "Briquette generator",
                Category = "Power",
                Note = "Burns one sand briquette every 20 s for 10 kW. A 6 kWh battery for the gaps. Powers machines within 24 m, wirelessly.",
                Price = 1500m,
                BaseStructure = MoonWorld.SolarPostKind,
                Power = new PowerDefinition { PeakKw = 10, BatteryKwh = 6, Range = 24, SupplyMode = SupplyMode.Fuel },
                Processor = new ProcessorDefinition
                {
                    RunningKw = 0,
                    CycleSeconds = 20,
                    ProducesPower = true,
                    Slots =
                    {
                        Slot.Input("Briquettes", briquette, 10),
                        Slot.Batch("Burning", briquette, 1),
                    },
                },
            }, Guid);
        }
    }
}
