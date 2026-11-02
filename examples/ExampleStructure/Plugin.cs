using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using MoonCleaners;
using StructureLib;
using UnityEngine;

namespace ExampleStructure
{
    // Example consumer: a floodlight clone sold in the Services tab, tagged with a small cube so tests can find its model,
    // one modded cargo kind, example grit, a processor that grinds moon dust into it, and a generator that burns it.
    [BepInPlugin(Guid, "StructureLib Example Structure", "0.1.0")]
    [BepInDependency(StructureLib.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.ryanhartwig.structurelib.examplestructure";
        internal static readonly ManualLogSource L = BepInEx.Logging.Logger.CreateLogSource("ExampleStructure");

        public override void Load()
        {
            MoonStructures.Register(new StructureDefinition
            {
                Id = "ryanhartwig.example-lamp",
                Name = "Example lamp",
                Category = "Services",
                Note = "StructureLib example.",
                Price = 50m,
                BaseStructure = 4, // floodlight
                DecorateModel = (go, ctx) =>
                {
                    var tag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tag.name = "ExampleLampTag";
                    var collider = tag.GetComponent<Collider>();
                    if (collider != null) Object.Destroy(collider);
                    tag.transform.SetParent(go.transform, false);
                    tag.transform.localScale = Vector3.one * 0.2f;
                    L.LogInfo($"Decorated {go.name}: added ExampleLampTag");
                },
            }, Guid);

            // A modded cargo kind. Value 250 is what the world holds; structurelib.json records the id.
            var grit = MoonStructures.RegisterCargo(new CargoDefinition
            {
                Id = "ryanhartwig.example-grit",
                Value = 250,
                Name = "example grit",
                ShortName = "grit",
                UnitKilograms = 3,
                Price = 2m,
            }, Guid);

            // A processor on the pressure chamber's model: grinds moon dust into example grit, 2 kg per 4 s cycle.
            MoonStructures.Register(new StructureDefinition
            {
                Id = "ryanhartwig.example-grinder",
                Name = "Example grinder",
                Category = "Production",
                Note = "StructureLib example: grinds moon dust into example grit.",
                Price = 100m,
                BaseStructure = MoonWorld.PressureChamberKind,
                Processor = new ProcessorDefinition
                {
                    RunningKw = 1,
                    CycleSeconds = 4,
                    Slots =
                    {
                        Slot.Input("Dust", Cargo.Dust, 10),
                        Slot.Batch("Grinding", Cargo.Dust, 2),
                        Slot.Output("Grit", grit, 10),
                    },
                },
                // Belt ports at the pressure chamber's own IN and OUT (deck height 0.85; belts flow along local +X).
                Ports = new()
                {
                    PortDefinition.In("Dust in", Cargo.Dust, new Vector3(-2.95f, 0.85f, 0f), Vector3.right),
                    PortDefinition.Out("Grit out", grit, new Vector3(2.95f, 0.85f, 0f), Vector3.right),
                },
            }, Guid);

            // A fuel generator on the solar post's model (the battery bank prefab has no MoonProductionView; see the recon
            // note, "Power sources (Task 11)"): burns 1 kg of example grit per 5 s cycle into its 2 kWh
            // battery (two cycles fill it) and supplies up to 5 kW to modded machines within 20 m.
            MoonStructures.Register(new StructureDefinition
            {
                Id = "ryanhartwig.example-burner",
                Name = "Example burner",
                Category = "Power",
                Note = "StructureLib example: burns example grit for power.",
                Price = 100m,
                BaseStructure = MoonWorld.SolarPostKind,
                Power = new PowerDefinition { PeakKw = 5, BatteryKwh = 2, Range = 20, SupplyMode = SupplyMode.Fuel },
                Processor = new ProcessorDefinition
                {
                    RunningKw = 0,
                    CycleSeconds = 5,
                    ProducesPower = true,
                    Slots =
                    {
                        Slot.Input("Grit", grit, 10),
                        Slot.Batch("Burning", grit, 1),
                    },
                },
                // The solar post has no ports; this adds one at its -X footprint edge, flowing +X like every donor's.
                Ports = new() { PortDefinition.In("Grit in", grit, new Vector3(-0.90f, 0.85f, 0f), Vector3.right) },
            }, Guid);

            // Dev builds of StructureLib only ([Dev] RegisterBadPorts): a structure whose input port takes sand while no
            // slot does. The library must drop it when the registry freezes; the self-test checks that it did.
            if (StructureLibDevFlag("RegisterBadPorts"))
                MoonStructures.Register(new StructureDefinition
                {
                    Id = BadPortsId,
                    Name = "Example bad ports",
                    Category = "Production",
                    Price = 100m,
                    BaseStructure = MoonWorld.PressureChamberKind,
                    Processor = new ProcessorDefinition
                    {
                        RunningKw = 1,
                        CycleSeconds = 4,
                        Slots = { Slot.Input("Dust", Cargo.Dust, 10), Slot.Batch("Grinding", Cargo.Dust, 2), Slot.Output("Grit", grit, 10) },
                    },
                    Ports = new() { PortDefinition.In("Sand in", Cargo.Sand, new Vector3(-2.95f, 0.85f, 0f), Vector3.right) },
                }, Guid);
        }

        public const string BadPortsId = "ryanhartwig.example-bad-ports";

        // A [Dev] bool from StructureLib's own config (it loads first, as our dependency). False when absent (release builds).
        static bool StructureLibDevFlag(string key)
        {
            try
            {
                if (BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins.TryGetValue(StructureLib.Plugin.Guid, out var info) &&
                    info.Instance is BasePlugin lib &&
                    lib.Config.TryGetEntry<bool>(new BepInEx.Configuration.ConfigDefinition("Dev", key), out var entry))
                    return entry.Value;
            }
            catch (System.Exception e) { L.LogWarning($"Reading StructureLib's [Dev] {key} failed: {e.Message}"); }
            return false;
        }
    }
}
