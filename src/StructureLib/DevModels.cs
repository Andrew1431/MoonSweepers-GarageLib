#if DEVBUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    // Dev tools for custom models: [Dev] BreakModelPath points every Model at a file that does not exist.
    internal static partial class DevTools
    {
        internal static ConfigEntry<bool> BreakModelPath;

        /// <summary>True once [Dev] DumpCards has written the card pictures this session.</summary>
        internal static bool CardsDumped => cardsDumped;

        static void BindModels(ConfigFile cfg)
        {
            BreakModelPath = cfg.Bind("Dev", "BreakModelPath", false, "Dev build: resolve every StructureDefinition.Model to a missing file ('missing-<name>' next to the mod's DLL), so the structure must keep its base model and fall back to its declared or base ports with one error (self-test model-missing-falls-back).");
            CoreLog.Info($"[dev] BreakModelPath={BreakModelPath.Value}");
        }
    }

    // Model self-tests on the example burner (Model = "example_burner.bin", one Port_Grit in node at its declared port).
    // They wait for [Dev] DumpCards (30 s in game) so model-card can compare the card pictures; the built burner's view
    // is inspected at the same time, well after the game spawned it.
    //  model-swapped: the view has a StructureLibModel child of its root with enabled mesh renderers, every donor
    //    mesh or skinned renderer outside it is disabled (the swap hides every donor renderer; only mesh and skinned ones
    //    are counted here), and it carries the solar post prefab's colliders.
    //  model-ports: the clone's ports are the model's (rule "model Port_* nodes"): one 'sl:250:Grit in' at
    //    (-0.90, 0.85, 0.00) dir (1, 0, 0).
    //  model-card: the burner's card PNG differs from the solar post's.
    //  model-missing-falls-back ([Dev] BreakModelPath): no StructureLibModel, donor renderers shown, declared ports, and
    //    exactly one error line for the missing file.
    internal static partial class SelfTest
    {
        static float modelUntil = -1f;
        static bool ModelPending => modelUntil >= 0f;

        static void ModelChecks()
        {
            var burner = MoonStructures.Definition(BurnerId);
            if (burner == null || string.IsNullOrEmpty(burner.Model))
            {
                CoreLog.Info($"[selftest] '{BurnerId}' {(burner == null ? "is not registered" : "has no Model")}: model checks not applicable");
                return;
            }
            if (!DevTools.AutoBuildNames(BurnerId))
            {
                CoreLog.Info($"[selftest] '{BurnerId}' was not built by AutoBuild this run: model checks not applicable");
                return;
            }
            if (!DevTools.BreakModelPath.Value && !DevTools.DumpCards.Value)
                CoreLog.Info("[selftest] [Dev] DumpCards is false: model-card will fail (it compares the dumped card pictures)");
            modelUntil = Time.unscaledTime + 60f;
        }

        static void TickModels()
        {
            bool timedOut = Time.unscaledTime >= modelUntil;
            if (!DevTools.BreakModelPath.Value && !DevTools.CardsDumped && !timedOut) return;
            modelUntil = -1f;
            var world = CoreState.World ?? CoreState.FindWorld();
            var burner = MoonStructures.Definition(BurnerId);
            int depot = world != null ? DepotOfKind(world, burner.Kind) : -1;
            var view = depot < 0 ? null : UnityEngine.Object.FindFirstObjectByType<MoonDepotConstruction>()?.ViewOf(depot);
            string noView = world == null ? "no MoonWorld found" : depot < 0 ? "no example burner depot" : view == null ? $"no view for burner depot {depot}" : null;

            if (DevTools.BreakModelPath.Value)
            {
                CoreLog.Info("[selftest] [Dev] BreakModelPath is true: model-swapped, model-ports and model-card not applicable");
                Check("model-missing-falls-back", noView, () => MissingFallsBack(world, burner, view));
                return;
            }
            CoreLog.Info("[selftest] [Dev] BreakModelPath is false: model-missing-falls-back not applicable");
            Check("model-swapped", noView, () => Swapped(world, view, MoonWorld.SolarPostKind, "burner"));
            Check("model-ports", world == null ? "no MoonWorld found" : null, () => ModelPortsOk(world, burner));
            Check("model-card", DevTools.CardsDumped ? null : "DumpCards did not run within 60 s (set [Dev] DumpCards = true)", () => CardDiffers(burner));
        }

        static (bool, string) Swapped(MoonWorld world, GameObject view, int donorKind, string what)
        {
            var root = view.transform;
            Transform holder = null;
            for (int i = 0; i < root.childCount; i++)
                if (root.GetChild(i).name == ModelSwap.HolderName) { holder = root.GetChild(i); break; }
            if (holder == null) return (false, $"view '{view.name}' has no direct child '{ModelSwap.HolderName}'");

            int modelOn = 0, modelAll = 0, donorOn = 0, donorAll = 0;
            var donorOnNames = new List<string>();
            var mats = new HashSet<string>();
            foreach (var r in view.GetComponentsInChildren<Renderer>(true))
            {
                if (r.TryCast<MeshRenderer>() == null && r.TryCast<SkinnedMeshRenderer>() == null) continue;
                bool inModel = r.transform.IsChildOf(holder);
                bool on = r.enabled && r.gameObject.activeInHierarchy;
                if (inModel) { modelAll++; if (on) modelOn++; foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m.name + "/" + m.shader?.name); }
                else { donorAll++; if (r.enabled) { donorOn++; if (donorOnNames.Count < 5) donorOnNames.Add(r.name); } }
            }
            int colliders = view.GetComponentsInChildren<Collider>(true).Count(c => !c.transform.IsChildOf(holder));
            int modelColliders = holder.GetComponentsInChildren<Collider>(true).Length;
            var donorPrefab = world.BuildDefinition(donorKind)?.prefab;
            int donorColliders = donorPrefab == null ? -1 : donorPrefab.GetComponentsInChildren<Collider>(true).Length;
            bool rootParent = holder.parent != null && holder.parent.Pointer == root.Pointer;
            CoreLog.Info($"[selftest] {what} view '{view.name}': {ModelSwap.HolderName} at local {AutoBelter.V(holder.localPosition)} scale {holder.localScale.x:F2}, parent is the view root {rootParent}; model renderers {modelOn}/{modelAll} enabled (materials {string.Join(", ", mats)}); donor mesh/skinned renderers {donorOn}/{donorAll} enabled{(donorOnNames.Count > 0 ? " [" + string.Join(", ", donorOnNames) + "]" : "")}; colliders {colliders} outside the model (+{modelColliders} in it), donor {donorKind} prefab {donorColliders}");
            // A Footprint with a Model swaps the donor's colliders for one BoxCollider (ModelSwap.SwapColliders).
            var def = view.name.StartsWith("ModStructure_", StringComparison.Ordinal) ? MoonStructures.Definition(view.name.Substring("ModStructure_".Length)) : null;
            bool swapped = def?.Footprint != null && def.ModelPath != null;
            int boxes = view.GetComponentsInChildren<BoxCollider>(true).Count(c => !c.transform.IsChildOf(holder));
            bool collidersOk = swapped ? colliders == 1 && boxes == 1 : colliders == donorColliders;
            bool ok = rootParent && modelOn >= 1 && donorAll >= 1 && donorOn == 0 && collidersOk && modelColliders == 0;
            return (ok, $"parent is root {rootParent}, model renderers enabled {modelOn}, donor mesh/skinned renderers enabled {donorOn}/{donorAll}, colliders {colliders} (+{modelColliders} in the model) vs {(swapped ? "one BoxCollider (Footprint swap)" : "prefab " + donorColliders)}");
        }

        static (bool, string) ModelPortsOk(MoonWorld world, StructureDefinition burner)
        {
            var ports = world.BuildDefinition(burner.Kind)?.conveyorPorts;
            CoreLog.Info($"[selftest] burner clone ports (rule '{burner.PortRule}'): {Describe(ports)}");
            string text = ReadWholeLog();
            bool logged = text != null && text.Contains($"'{BurnerId}': ports from model Port_* nodes");
            bool ok = burner.PortRule == "model Port_* nodes" && logged && ports != null && ports.Length == 1 && ports[0].name == "sl:250:Grit in" && !ports[0].output &&
                      Vector3.Distance(ports[0].position, new Vector3(-0.90f, 0.85f, 0f)) < 0.01f && Vector3.Distance(ports[0].direction, Vector3.right) < 0.01f;
            return (ok, $"rule '{burner.PortRule}', log line {(logged ? "found" : "missing")}, ports {Describe(ports)}; expected one 'sl:250:Grit in' in at (-0.90,0.85,0.00) dir (1,0,0)");
        }

        static (bool, string) CardDiffers(StructureDefinition burner)
        {
            string dir = Path.Combine(BepInEx.Paths.CachePath, "structurelib", "cards");
            string mine = Directory.GetFiles(dir, burner.Kind + "-*.png").FirstOrDefault();
            string donor = Directory.GetFiles(dir, MoonWorld.SolarPostKind + "-*.png").FirstOrDefault();
            if (mine == null || donor == null) return (false, $"card PNGs: burner {mine ?? "missing"}, solar post {donor ?? "missing"} in {dir}");
            var a = File.ReadAllBytes(mine);
            var b = File.ReadAllBytes(donor);
            bool differ = !a.SequenceEqual(b);
            CoreLog.Info($"[selftest] cards: {Path.GetFileName(mine)} {a.Length} bytes, {Path.GetFileName(donor)} {b.Length} bytes, differ {differ}");
            return (differ, $"{Path.GetFileName(mine)} and {Path.GetFileName(donor)} are byte-identical");
        }

        static (bool, string) MissingFallsBack(MoonWorld world, StructureDefinition burner, GameObject view)
        {
            bool holder = false;
            for (int i = 0; i < view.transform.childCount; i++)
                if (view.transform.GetChild(i).name == ModelSwap.HolderName) holder = true;
            int donorOn = 0;
            foreach (var r in view.GetComponentsInChildren<Renderer>(true))
                if ((r.TryCast<MeshRenderer>() != null || r.TryCast<SkinnedMeshRenderer>() != null) && r.enabled) donorOn++;
            var ports = world.BuildDefinition(burner.Kind)?.conveyorPorts;
            string text = ReadWholeLog() ?? "";
            var errors = text.Split('\n').Where(l => l.Contains("Error") && l.Contains($"'{BurnerId}': model '{burner.Model}' could not be read")).ToList();
            int allErrors = text.Split('\n').Count(l => l.StartsWith("[Error", StringComparison.Ordinal));
            CoreLog.Info($"[selftest] missing model: holder {holder}, donor renderers enabled {donorOn}, rule '{burner.PortRule}', ports {Describe(ports)}; model error lines {errors.Count}, all error lines {allErrors}");
            bool ok = !holder && donorOn >= 1 && burner.PortRule == "declared Ports" && ports != null && ports.Length == 1 && ports[0].name == "sl:250:Grit in" &&
                      Vector3.Distance(ports[0].position, new Vector3(-0.90f, 0.85f, 0f)) < 0.01f && errors.Count == 1 && allErrors == 1;
            return (ok, $"holder {holder}, donor renderers enabled {donorOn}, rule '{burner.PortRule}', ports {Describe(ports)}, model error lines {errors.Count}, all error lines {allErrors}");
        }

        // SandPower's models (run from the chain and reload checks in DevBelts.cs, once the depots exist):
        //  sandpower-model-swapped: both views carry the StructureLibModel holder with enabled renderers, every donor
        //    mesh/skinned renderer disabled (the ones Swapped counts) and the donor prefab's colliders (stone press 97
        //    for the oven, solar post 93 for the generator).
        //  sandpower-model-ports: the clones' ports are the models' Port_* nodes: the oven 'sl:<sand>:Sand in' in at
        //    (-4.10, 0.85, 0.00) and 'sl:3:Coalite out' out at (3.90, 0.85, 0.00); the generator 'sl:3:Coalite in' in
        //    at (-0.90, 0.85, 0.00); all flowing (1, 0, 0).
        static void SandModelChecks(MoonWorld world)
        {
            StructureDefinition c = MoonStructures.Definition(OvenId), g = MoonStructures.Definition(GeneratorId);
            var construction = UnityEngine.Object.FindFirstObjectByType<MoonDepotConstruction>();
            var viewC = ovenDepot < 0 ? null : construction?.ViewOf(ovenDepot);
            var viewG = generatorDepot < 0 ? null : construction?.ViewOf(generatorDepot);
            string skip = world == null ? "no MoonWorld found" : c == null || g == null ? "SandPower is not registered"
                : viewC == null ? $"no view for oven depot {ovenDepot}" : viewG == null ? $"no view for generator depot {generatorDepot}" : null;
            Check("sandpower-model-swapped", skip, () =>
            {
                var (okC, whyC) = Swapped(world, viewC, MoonWorld.StonePressKind, "oven");
                var (okG, whyG) = Swapped(world, viewG, MoonWorld.SolarPostKind, "generator");
                return (okC && okG, $"oven: {whyC}; generator: {whyG}");
            });
            Check("sandpower-model-ports", world == null || c == null || g == null ? "SandPower is not registered" : null, () =>
            {
                var pc = world.BuildDefinition(c.Kind)?.conveyorPorts;
                var pg = world.BuildDefinition(g.Kind)?.conveyorPorts;
                CoreLog.Info($"[selftest] oven clone ports (rule '{c.PortRule}'): {Describe(pc)}; generator clone ports (rule '{g.PortRule}'): {Describe(pg)}");
                int b = MoonStructures.CargoDefinition(CoaliteId)?.Value ?? -1;
                string sandIn = $"sl:{(byte)MoonCargo.Sand}:Sand in", briqOut = $"sl:{b}:Coalite out", briqIn = $"sl:{b}:Coalite in";
                bool okC = c.PortRule == "model Port_* nodes" && (pc?.Length ?? 0) == 2 &&
                           Has(pc, sandIn, false, new Vector3(-4.10f, 0.85f, 0f)) && Has(pc, briqOut, true, new Vector3(3.90f, 0.85f, 0f));
                bool okG = g.PortRule == "model Port_* nodes" && (pg?.Length ?? 0) == 1 && Has(pg, briqIn, false, new Vector3(-0.90f, 0.85f, 0f));
                return (okC && okG, $"oven rule '{c.PortRule}' ports {Describe(pc)}, expected '{sandIn}' in at (-4.10,0.85,0.00) and '{briqOut}' out at (3.90,0.85,0.00); generator rule '{g.PortRule}' ports {Describe(pg)}, expected '{briqIn}' in at (-0.90,0.85,0.00); all dir (1,0,0)");
            });

            static bool Has(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<MoonDepotBuildDefinition.ConveyorPort> ports, string name, bool output, Vector3 at)
            {
                for (int i = 0; i < (ports?.Length ?? 0); i++)
                {
                    var p = ports[i];
                    if (p != null && p.name == name && p.output == output && Vector3.Distance(p.position, at) < 0.01f && Vector3.Distance(p.direction, Vector3.right) < 0.01f) return true;
                }
                return false;
            }
        }
    }
}
#endif
