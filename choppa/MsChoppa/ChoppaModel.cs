using System;
using System.Collections.Generic;
using MoonVehicleCore;
using Rotorcraft;
using UnityEngine;

namespace MsChoppa
{
    // Puts the Blender choppa on the borrowed crew-van vehicle: hides the van's renderers and solid colliders, adds the
    // choppa mesh with the game's own materials, and simple box colliders for cabin, boom and skids.
    internal static class ChoppaModel
    {
        internal const string ModelNode = "ChoppaModel";

        internal static void Decorate(GameObject root, ModelContext ctx)
        {
            var t = root.transform;
            // Van bottom (wheel contact) in root space, so the skids end up where the wheels were.
            var vanRenderers = root.GetComponentsInChildren<Renderer>(true);
            float vanBottom = float.PositiveInfinity;
            foreach (var r in vanRenderers)
            {
                var b = r.bounds;
                vanBottom = Mathf.Min(vanBottom, t.InverseTransformPoint(b.min).y, t.InverseTransformPoint(b.max).y);
                r.enabled = false;
            }
            int layer = root.layer, hidden = vanRenderers.Length, disabled = 0;
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                layer = c.gameObject.layer;
                c.enabled = false;
                disabled++;
            }
            if (float.IsInfinity(vanBottom)) vanBottom = 0f;

            var model = MeshModel.Load("ms_choppa.bin", typeof(ChoppaModel).Assembly);
            var holder = new GameObject(ModelNode).transform;
            holder.SetParent(t, false);
            float scale = Plugin.Scale.Value;
            holder.localScale = Vector3.one * scale;
            var mats = new Materials(ctx.Models);
            var nodes = model.Instantiate(holder, _ => false, mats.For);

            // Lowest point of the choppa (skids) sits on the van's wheel line.
            float choppaBottom = float.PositiveInfinity;
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
                choppaBottom = Mathf.Min(choppaBottom, t.InverseTransformPoint(r.bounds.min).y);
            holder.localPosition = new Vector3(0f, vanBottom - choppaBottom, 0f);

            AddBox(t, nodes, layer, "Cabin", "Fuselage");
            AddBox(t, nodes, layer, "Boom", "TailBoom");
            AddBox(t, nodes, layer, "Skids", "Skid_L", "Skid_R", "CrossTube_F", "CrossTube_R");
            foreach (var tr in holder.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;

            Plugin.L.LogInfo($"Choppa model on {root.name}: hid {hidden} van renderers, disabled {disabled} van colliders, layer {layer} ({LayerMask.LayerToName(layer)}), " +
                             $"scale {scale}, van bottom {vanBottom:F2}, offset {holder.localPosition.y:F2}, rotors main={nodes.ContainsKey("MainRotor")} tail={nodes.ContainsKey("TailRotor")}");
        }

        // Box collider on the root that covers the named nodes' renderers (in root space).
        static void AddBox(Transform root, Dictionary<string, Transform> nodes, int layer, string name, params string[] parts)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var p in parts)
            {
                if (!nodes.TryGetValue(p, out var node)) { Plugin.L.LogWarning($"Collider {name}: node {p} missing"); continue; }
                foreach (var r in node.GetComponentsInChildren<Renderer>(true))
                {
                    var wb = r.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z);
                        var lp = root.InverseTransformPoint(corner);
                        if (!any) { b = new Bounds(lp, Vector3.zero); any = true; } else b.Encapsulate(lp);
                    }
                }
            }
            if (!any) return;
            var go = new GameObject("ChoppaCollider_" + name) { layer = layer };
            go.transform.SetParent(root, false);
            var box = go.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = Vector3.Max(b.size, Vector3.one * 0.1f);
            Plugin.L.LogInfo($"Collider {name}: center {b.center} size {b.size}");
        }

        // Blender material names -> the game's prototype materials, so the choppa matches vanilla vehicles.
        sealed class Materials
        {
            readonly MoonCleaners.MoonPrototypeModels m;
            readonly Dictionary<string, Material> made = new();
            public Materials(MoonCleaners.MoonPrototypeModels models) { m = models; }

            public Material For(string name, Color colour)
            {
                if (made.TryGetValue(name, out var mat)) return mat;
                mat = name switch
                {
                    "MS_BodyWhite" => m.shell,
                    "MS_BlackPlastic" => m.dark,
                    "MS_OrangeMetal" => m.orange,
                    "MS_LampOrange" => m.orange,
                    "MS_Glass" => m.glass,
                    "MS_Steel" => m.metal,
                    _ => null,
                };
                if (mat == null)
                {
                    mat = m.Mat("Choppa_" + name, colour);
                    Plugin.L.LogWarning($"No game material for '{name}', made one from its colour {colour}");
                }
                made[name] = mat;
                return mat;
            }
        }
    }
}
