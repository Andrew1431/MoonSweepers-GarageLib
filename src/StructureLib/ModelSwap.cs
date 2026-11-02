using System;
using System.Collections.Generic;
using System.Linq;
using GarageLib;
using UnityEngine;

namespace StructureLib
{
    // StructureDefinition.Model: a .bin from shared/blender/export_mesh_model.py next to the registering mod's DLL.
    // Build is the default BuildModel when Model is set (and BuildModel is not): the donor's prefab becomes the template
    // with every donor renderer hidden (its colliders, views and scripts stay) and the model under a "StructureLibModel" child
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

                // Hide every donor renderer (mesh, skinned, particle, line, trail...). The lowest point and the fallback
                // material come from the mesh and skinned ones only, in template-local space (mesh bounds taken through
                // each renderer's transform: the template is inactive, so Renderer.bounds is not usable).
                float donorBottom = float.PositiveInfinity;
                int hidden = 0, hiddenMeshes = 0;
                Material firstMat = null;
                foreach (var r in template.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = false;
                    hidden++;
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
                    hiddenMeshes++;
                    if (mesh != null) donorBottom = Mathf.Min(donorBottom, LowestCorner(mesh.bounds, r.transform, root));
                    if (firstMat == null) firstMat = r.sharedMaterial;
                }
                if (float.IsInfinity(donorBottom)) donorBottom = 0f;
                if (StructureMaterials.FallbackBase == null) StructureMaterials.FallbackBase = firstMat;

                // Floor: the model's lowest vertex goes on template-local y = 0, where the donors' own meshes start (the
                // pressure chamber and solar post: lowest bound 0.00) and where the kit floors every model. The definition's
                // floorOffset (0.02) is not added: the game applies it when it places the root, the donor meshes do not
                // carry it either. The donor's lowest bound is logged for information only (the stone press reaches
                // y -0.49 with its foundation below ground).
                var place = Placement(model, ctx.Donor.scale);
                var holder = new GameObject(HolderName).transform;
                holder.SetParent(root, false);
                holder.localScale = Vector3.one * place.Scale;
                holder.localPosition = new Vector3(0f, place.Offset, 0f);
                // Fact (e): the donors' views rotate nothing, and the holder hangs off the template root itself, so no
                // animated donor transform is its ancestor.
                if (holder.parent == null || holder.parent.Pointer != root.Pointer) throw new InvalidOperationException("model holder is not a child of the template root");
                model.Instantiate(holder, IsPort, StructureMaterials.For);

                // Check the placement against the built transforms: the lowest vertex must sit at y 0.
                float measured = float.PositiveInfinity;
                int meshes = 0;
                foreach (var mf in holder.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null) continue;
                    meshes++;
                    var t = mf.transform;
                    var verts = mesh.vertices;
                    for (int i = 0; i < verts.Length; i++) measured = Mathf.Min(measured, root.InverseTransformPoint(t.TransformPoint(verts[i])).y);
                }
                foreach (var t in holder.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = template.layer;

                CoreLog.Info($"'{d.Id}': model {d.Model} on base {d.BaseStructure} ('{prefab.name}'): hid {hidden} donor renderer(s) ({hiddenMeshes} mesh/skinned), {meshes} model mesh(es); donor lowest bound {donorBottom:F3} (floorOffset {ctx.Donor.floorOffset:F3}, not added), model lowest vertex {place.Lowest:F3} -> holder offset {place.Offset:F3}, scale {place.Scale:F2}; built lowest vertex {(float.IsInfinity(measured) ? "none" : measured.ToString("F3"))}");
                if (Mathf.Abs(place.Offset) > 1e-4f || Mathf.Abs(place.Scale - 1f) > 1e-4f)
                    CoreLog.Info($"'{d.Id}': model holder offset {place.Offset:F3} and scale {place.Scale:F3}; the model's ports move with it");
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

        static bool IsPort(MeshModel.Node n) => n.Name.StartsWith(ModelPorts.Prefix, StringComparison.Ordinal);

        // Every node in model-root space (MeshModel's transforms are parent-relative).
        static ((float x, float y, float z) Pos, (float x, float y, float z, float w) Rot, (float x, float y, float z) Scale)[] Composed(MeshModel model) =>
            ModelPorts.Compose(model.Nodes.Select(n => (n.Parent, (n.Pos.x, n.Pos.y, n.Pos.z), (n.Rot.x, n.Rot.y, n.Rot.z, n.Rot.w), (n.Scale.x, n.Scale.y, n.Scale.z))).ToList());

        /// <summary>
        /// The holder's transform for a model on a donor of this scale: uniform scale 1 / max(scale, 0.01), and the Y offset
        /// that puts the lowest vertex of every mesh Instantiate builds (not under a Port_* node) on structure y = 0. The
        /// swap and the ports both use it, so geometry and ports stay together.
        /// </summary>
        internal static (float Scale, float Offset, float Lowest) Placement(MeshModel model, float donorScale)
        {
            float scale = 1f / Math.Max(donorScale, 0.01f);
            var c = Composed(model);
            var skipped = new bool[model.Nodes.Length];
            float lowest = float.PositiveInfinity;
            for (int i = 1; i < model.Nodes.Length; i++)
            {
                var n = model.Nodes[i];
                skipped[i] = IsPort(n) || (n.Parent > 0 && n.Parent < i && skipped[n.Parent]);
                if (skipped[i] || !n.HasMesh) continue;
                foreach (var v in n.Vertices) lowest = Math.Min(lowest, ModelPorts.Point(c[i], (v.x, v.y, v.z)).y);
            }
            if (float.IsInfinity(lowest)) lowest = 0f;
            return (scale, ModelPorts.HolderOffset(lowest, scale), lowest);
        }

        /// <summary>
        /// MoonStructures.ModelPortsProvider: the model's Port_* nodes as ports in structure space (parent chain composed,
        /// then through the holder's scale and floor offset, as the swap builds the geometry); null without a readable model.
        /// </summary>
        internal static List<PortDefinition> Ports(StructureDefinition d, DonorShape donor)
        {
            var model = Load(d);
            if (model == null) return null;
            var place = Placement(model, donor?.Scale ?? 1f);
            var c = Composed(model);
            var nodes = model.Nodes.Select((n, i) => (n.Name, c[i].Pos, c[i].Rot, (IReadOnlyDictionary<string, object>)n.Props));
            var result = new List<PortDefinition>();
            foreach (var p in ModelPorts.FromNodes(nodes))
            {
                var at = ModelPorts.ApplyHolder(p.Pos, place.Scale, place.Offset);
                result.Add(new PortDefinition
                {
                    Name = p.Name,
                    Position = new Vector3(at.x, at.y, at.z),
                    Direction = new Vector3(p.Dir.x, p.Dir.y, p.Dir.z),
                    Output = p.Output,
                    Cargo = CargoOf(d, p.Name, p.CargoId),
                });
            }
            if (result.Count > 0 && (Math.Abs(place.Offset) > 1e-4f || Math.Abs(place.Scale - 1f) > 1e-4f))
                CoreLog.Info($"'{d.Id}': model ports moved by the holder (offset {place.Offset:F3}, scale {place.Scale:F3}) to stay on the geometry");
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
