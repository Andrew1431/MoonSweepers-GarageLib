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
            // The ports come from the models' Port_* nodes (box_splitter.bin, box_merger.bin, built by the scripts in
            // blender/). Flow runs along structure +X like every vanilla production donor. Left and right are as seen
            // looking along +X with Y up. Router ports carry no cargo (any cargo passes). The round-robin order is the
            // resolved port order: a model's nodes in file order, which the exporter writes alphabetically.
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
                // The model's ports (structure-local metres; the belt's flow), in round-robin order:
                //   Port_Forward  output  (0.70, 0.85, 0.00)   +X
                //   Port_In       input   (-0.70, 0.85, 0.00)  +X
                //   Port_Left     output  (0.00, 0.85, 0.70)   +Z
                //   Port_Right    output  (0.00, 0.85, -0.70)  -Z
                Model = "box_splitter.bin",
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
                // The model's ports (structure-local metres; the belt's flow), in round-robin order:
                //   Port_Back   input   (-0.70, 0.85, 0.00)  +X
                //   Port_Left   input   (0.00, 0.85, 0.70)   -Z
                //   Port_Out    output  (0.70, 0.85, 0.00)   +X
                //   Port_Right  input   (0.00, 0.85, -0.70)  +Z
                Model = "box_merger.bin",
            }, Guid);

            Log.LogInfo($"Belt Boxes {Version} loaded");
        }
    }
}
