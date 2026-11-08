using BepInEx;
using BepInEx.Unity.IL2CPP;
using MoonCleaners;
using StructureLib;
using UnityEngine;

namespace SandPower
{
    // A coking oven that bakes moon sand into coalite, and a furnace that burns it to feed the power grid through a
    // power pole (PowerGrid): it burns only while the grid is still short after solar and battery banks.
    // Registrations only; the ids and the cargo value never change after release.
    [BepInPlugin(Guid, "SandPower", Version)]
    [BepInDependency(StructureLib.Plugin.Guid)]
    [BepInDependency("com.ryanhartwig.powergrid")]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.ryanhartwig.sandpower";
        public const string Version = "0.1.0";

        public override void Load()
        {
            // Value 3 is claimed in docs/cargo-registry.md.
            var coalite = MoonStructures.RegisterCargo(new CargoDefinition
            {
                Id = "ryanhartwig.coalite",
                Value = 3,
                Name = "coalite",
                ShortName = "coalite",
                UnitKilograms = 2,
                Price = 7m,
                // Pressed-sand brown: the look-at panel's coalite rows, and the capsule when the brick model below is
                // missing (far canisters stay dust either way).
                CanisterTint = new Color(0.42f, 0.33f, 0.24f),
                // blender/coalite.py: a chamfered sand brick shown in place of the dust capsule on belts, on the
                // ground and in the hand (the capsule's colliders stay, so riding, pickup and the hoppers are unchanged).
                CanisterModel = "coalite.bin",
            }, Guid);

            MoonStructures.Register(new StructureDefinition
            {
                Id = "ryanhartwig.coking-oven",
                Name = "Coking oven",
                Category = "Production",
                Note = "Bakes 4 kg of moon sand into coalite every 8 s. Needs 12 kW.",
                Price = 800m,
                BaseStructure = MoonWorld.StonePressKind,
                // blender/coking_oven.py. Its Port_Sand in and Port_Coalite out nodes are the belt ports (Ports stays
                // null: model ports win): sand in at the -X end, coalite out at the +X end, both flowing +X.
                Model = "coking_oven.bin",
                Processor = new ProcessorDefinition
                {
                    RunningKw = 12,
                    CycleSeconds = 8,
                    Slots =
                    {
                        Slot.Input("Sand", Cargo.Sand, 16),
                        Slot.Batch("Baking", Cargo.Sand, 4),
                        Slot.Output("Coalite", coalite, 10),
                    },
                },
            }, Guid);

            // The solar post's model, not the battery bank's (6): that prefab has no production view.
            MoonStructures.Register(new StructureDefinition
            {
                Id = "ryanhartwig.coalite-furnace",
                Name = "Coalite furnace",
                Category = "Power",
                Note = "Burns one coalite every 20 s for 10 kW into the power grid. Needs a power pole within 12 m.",
                Price = 1500m,
                BaseStructure = MoonWorld.SolarPostKind,
                // blender/coalite_furnace.py. Its Port_Coalite in node, at the -X footprint edge flowing +X, is the
                // belt port (the solar post has none).
                Model = "coalite_furnace.bin",
                Power = new PowerDefinition { Role = PowerRole.Source, SupplyMode = SupplyMode.Fuel, PeakKw = 10, BatteryKwh = 6 },
                Processor = new ProcessorDefinition
                {
                    RunningKw = 0,
                    CycleSeconds = 20,
                    ProducesPower = true,
                    Slots =
                    {
                        Slot.Input("Coalite", coalite, 10),
                        Slot.Batch("Burning", coalite, 2),
                    },
                },
            }, Guid);
        }
    }
}
