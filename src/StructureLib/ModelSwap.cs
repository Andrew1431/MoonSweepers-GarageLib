using System;
using System.Collections.Generic;
using System.Linq;
using GarageLib;
using UnityEngine;

namespace StructureLib
{
    // StructureDefinition.Model: a .bin from shared/blender/export_mesh_model.py next to the registering mod's DLL.
    // Build is the default BuildModel when Model is set (and BuildModel is not): the donor's prefab becomes the template
    // with every donor mesh hidden (its colliders, views and scripts stay) and the model under a "StructureLibModel" child
    // of the template root, standing where the donor's meshes stood. Ports reads the model's Port_* nodes.
    internal static class ModelSwap
    {
        internal const string HolderName = "StructureLibModel";

        static readonly HashSet<string> failed = new(StringComparer.Ordinal), swapLogged = new(StringComparer.Ordinal);

        // The model file, or null (one Error per structure) when it is missing or unreadable.
        internal static MeshModel Load(StructureDefinition d)
        {
            if (d?.ModelPath == null || failed.Contains(d.Id)) return null;
            try
            {
                MeshModel.Log = CoreLog.Info;
                return MeshModel.LoadFile(d.ModelPath);
            }
            catch (Exception e)
            {
                failed.Add(d.Id);
                CoreLog.Error($"'{d.Id}': model '{d.Model}' could not be read from {d.ModelPath} ({e.GetType().Name}: {e.Message}); showing the model of base kind {d.BaseStructure}, ports from the definition or the base");
                return null;
            }
        }

        /// <summary>The default BuildModel for a structure with a Model. Null (donor model kept) on any failure.</summary>
        internal static GameObject Build(StructureDefinition d, ModelContext ctx)
        {
            GameObject template = null;
            try
            {
                var model = Load(d);
                if (model == null) return null;
                var prefab = ctx.Donor?.prefab;
                if (prefab == null) throw new InvalidOperationException($"base kind {d.BaseStructure} has no prefab");

                template = UnityEngine.Object.Instantiate<GameObject>(prefab, ctx.Parent);
                template.name = prefab.name;
                var root = template.transform;

                // Hide every donor mesh; remember the lowest point of any of them in template-local space (mesh bounds
                // taken through each renderer's transform: the template is inactive, so Renderer.bounds is not usable).
                float donorBottom = float.PositiveInfinity;
                int hidden = 0;
                Material firstMat = null;
                foreach (var r in template.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh;
                    if (r.TryCast<MeshRenderer>() != null)
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        mesh = mf != null ? mf.sharedMesh : null;
                    }
                    else
                    {
                        var smr = r.TryCast<SkinnedMeshRenderer>();
                        if (smr == null) continue;
                        mesh = smr.sharedMesh;
                    }
                    if (mesh != null) donorBottom = Mathf.Min(donorBottom, LowestCorner(mesh.bounds, r.transform, root));
                    if (firstMat == null) firstMat = r.sharedMaterial;
                    r.enabled = false;
                    hidden++;
                }
                if (float.IsInfinity(donorBottom)) donorBottom = 0f;
                if (StructureMaterials.FallbackBase == null) StructureMaterials.FallbackBase = firstMat;

                float scale = ctx.Donor.scale;
                var holder = new GameObject(HolderName).transform;
                holder.SetParent(root, false);
                holder.localScale = Vector3.one / Mathf.Max(scale, 0.01f);
                // Fact (e): the donors' views rotate nothing, and the holder hangs off the template root itself, so no
                // animated donor transform is its ancestor.
                if (holder.parent == null || holder.parent.Pointer != root.Pointer) throw new InvalidOperationException("model holder is not a child of the template root");
                model.Instantiate(holder, n => n.Name.StartsWith(ModelPorts.Prefix, StringComparison.Ordinal), StructureMaterials.For);

                float modelBottom = float.PositiveInfinity;
                int meshes = 0;
                foreach (var mf in holder.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null) continue;
                    meshes++;
                    var t = mf.transform;
                    var verts = mesh.vertices;
                    for (int i = 0; i < verts.Length; i++) modelBottom = Mathf.Min(modelBottom, root.InverseTransformPoint(t.TransformPoint(verts[i])).y);
                }
                if (!float.IsInfinity(modelBottom)) holder.localPosition = new Vector3(0f, donorBottom - modelBottom, 0f);
                foreach (var t in holder.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = template.layer;

                CoreLog.Info($"'{d.Id}': model {d.Model} on base {d.BaseStructure} ('{prefab.name}'): hid {hidden} donor renderer(s), {meshes} model mesh(es); donor bottom {donorBottom:F3}, model bottom {(float.IsInfinity(modelBottom) ? "none" : modelBottom.ToString("F3"))}, holder y {holder.localPosition.y:F3}, scale {holder.localScale.x:F2}");
                return template;
            }
            catch (Exception e)
            {
                if (swapLogged.Add(d.Id)) CoreLog.Error($"'{d.Id}': model swap failed, showing the model of base kind {d.BaseStructure}: {e}");
                if (template != null) UnityEngine.Object.Destroy(template);
                return null;
            }
        }

        static float LowestCorner(Bounds b, Transform from, Transform root)
        {
            float low = float.PositiveInfinity;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z);
                low = Mathf.Min(low, root.InverseTransformPoint(from.TransformPoint(c)).y);
            }
            return low;
        }

        /// <summary>MoonStructures.ModelPortsProvider: the model's Port_* nodes as ports; null without a readable model.</summary>
        internal static List<PortDefinition> Ports(StructureDefinition d)
        {
            var model = Load(d);
            if (model == null) return null;
            var nodes = model.Nodes.Select(n => (n.Name, (n.Pos.x, n.Pos.y, n.Pos.z), (n.Rot.x, n.Rot.y, n.Rot.z, n.Rot.w), (IReadOnlyDictionary<string, object>)n.Props));
            var result = new List<PortDefinition>();
            foreach (var p in ModelPorts.FromNodes(nodes))
                result.Add(new PortDefinition
                {
                    Name = p.Name,
                    Position = new Vector3(p.Pos.x, p.Pos.y, p.Pos.z),
                    Direction = new Vector3(p.Dir.x, p.Dir.y, p.Dir.z),
                    Output = p.Output,
                    Cargo = CargoOf(d, p.Name, p.CargoId),
                });
            return result;
        }

        // "Dust", "Sand", "Stone" (any case) or a registered cargo id. Anything else keeps its id with no value, so the
        // freeze's port check names it and drops the structure.
        static Cargo CargoOf(StructureDefinition d, string port, string id)
        {
            switch (id?.Trim().ToLowerInvariant())
            {
                case "dust": return Cargo.Dust;
                case "sand": return Cargo.Sand;
                case "stone": return Cargo.Stone;
            }
            var c = MoonStructures.CargoDefinition(id);
            if (c != null) return new Cargo(c.Value, c.Id);
            CoreLog.Error($"'{d.Id}': model port '{port}' names cargo '{id ?? "(none)"}', which is neither Dust, Sand, Stone nor a registered cargo id");
            return new Cargo(0, id ?? "(none)");
        }
    }
}
