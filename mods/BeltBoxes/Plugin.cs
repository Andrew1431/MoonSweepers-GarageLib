using BepInEx;
using BepInEx.Unity.IL2CPP;
using StructureLib;
using UnityEngine;

namespace BeltBoxes
{
    // A compact box splitter and box merger: StructureLib routers that pass any cargo between belts.
    // Registrations only; the ids never change after release.
    [BepInPlugin(Guid, "Belt Boxes", Version)]
    [BepInDependency(StructureLib.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        // Never change Guid after release: it names players' config files.
        public const string Guid = "com.ryanhartwig.beltboxes";
        public const string Version = "0.1.0";

        // The floodlight: no game script and one collider, the tested router donor. Not the vanilla splitter or merger
        // (11, 12): their prefabs carry MoonMachineBelt and ten colliders.
        const int FloodlightKind = 4;

        public override void Load()
        {
            // Flow runs along structure +X like every vanilla production donor. Left and right are as seen looking along
            // +X with Y up. Router ports carry no cargo (any cargo passes): the Cargo here is ignored. The round-robin
            // order is the port order below.
            MoonStructures.Register(new StructureDefinition
            {
                Id = "ryanhartwig.box-splitter",
                Name = "Box splitter",
                Category = "Logistics",
                Note = "Splits one belt into up to three, taking turns. Skips an output that is blocked or has no belt.",
                Price = 25m,
                BaseStructure = FloodlightKind,
                Router = new RouterDefinition { Mode = RouterMode.Splitter },
                Footprint = new Vector2(1.4f, 1.4f),
                Ports = new()
                {
                    PortDefinition.In("In", Cargo.Dust, new Vector3(-0.70f, 0.85f, 0f), Vector3.right),
                    PortDefinition.Out("Forward", Cargo.Dust, new Vector3(0.70f, 0.85f, 0f), Vector3.right),
                    PortDefinition.Out("Left", Cargo.Dust, new Vector3(0f, 0.85f, 0.70f), Vector3.forward),
                    PortDefinition.Out("Right", Cargo.Dust, new Vector3(0f, 0.85f, -0.70f), Vector3.back),
                },
            }, Guid);

            MoonStructures.Register(new StructureDefinition
            {
                Id = "ryanhartwig.box-merger",
                Name = "Box merger",
                Category = "Logistics",
                Note = "Merges up to three belts into one, taking turns.",
                Price = 25m,
                BaseStructure = FloodlightKind,
                Router = new RouterDefinition { Mode = RouterMode.Merger },
                Footprint = new Vector2(1.4f, 1.4f),
                Ports = new()
                {
                    PortDefinition.In("Left", Cargo.Dust, new Vector3(0f, 0.85f, 0.70f), Vector3.back),
                    PortDefinition.In("Back", Cargo.Dust, new Vector3(-0.70f, 0.85f, 0f), Vector3.right),
                    PortDefinition.In("Right", Cargo.Dust, new Vector3(0f, 0.85f, -0.70f), Vector3.forward),
                    PortDefinition.Out("Out", Cargo.Dust, new Vector3(0.70f, 0.85f, 0f), Vector3.right),
                },
            }, Guid);

            Log.LogInfo($"Belt Boxes {Version} loaded");
        }
    }
}
