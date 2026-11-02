using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using StructureLib;
using UnityEngine;

namespace ExampleStructure
{
    // Example consumer: a floodlight clone sold in the Services tab, tagged with a small cube so tests can find its model,
    // and one modded cargo kind, example grit.
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
            MoonStructures.RegisterCargo(new CargoDefinition
            {
                Id = "ryanhartwig.example-grit",
                Value = 250,
                Name = "example grit",
                ShortName = "grit",
                UnitKilograms = 3,
                Price = 2m,
            }, Guid);
        }
    }
}
