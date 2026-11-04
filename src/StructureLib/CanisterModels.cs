using System;
using System.Collections.Generic;
using System.Linq;
using GarageLib;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    // CargoDefinition.CanisterModel: a .bin from shared/blender/export_mesh_model.py next to the registering mod's DLL, shown
    // on the near canister view (MoonDustCanisterView: belts, ground, hand) in place of the vanilla capsule. The model is built
    // once per (view, cargo) under a "StructureLibCargo_<value>" child of the view, at the view's origin and scale, and every
    // vanilla renderer that was enabled is disabled while the model shows. Views are pooled, so each ShowCargo first restores
    // what the last cargo hid: a view reused for dust, sand, stone, or a modded cargo without a model gets the capsule back
    // (the renderers' enabled flags only; vanilla's own SetActive choices, such as the stone block, are left to vanilla, which
    // always runs first). Colliders, the rigidbody and the view's scripts are never touched: the brick still rides belts, is
    // picked up and lands in machines as the capsule did. The far, instanced path stays the dust capsule.
    internal static class CanisterModels
    {
        internal const string HolderPrefix = "StructureLibCargo_";

        sealed class ViewState
        {
            public readonly Dictionary<byte, GameObject> Holders = new();
            public readonly List<Renderer> Hidden = new();
        }

        static readonly Dictionary<byte, MeshModel> models = new();
        static readonly HashSet<byte> failed = new();
        static readonly Dictionary<int, ViewState> views = new();
        static bool measured;

        /// <summary>The holder name a view carries for this cargo value (the self-test looks for it).</summary>
        internal static string HolderName(byte value) => HolderPrefix + value;

        // The cargo's model, or null (one Error per cargo) when it is missing or unreadable: the capsule (tinted) stays.
        static MeshModel Load(CargoDefinition d)
        {
            if (d?.CanisterModelPath == null || failed.Contains(d.Value)) return null;
            if (models.TryGetValue(d.Value, out var model)) return model;
            try
            {
                MeshModel.Log = CoreLog.Info;
                model = MeshModel.LoadFile(d.CanisterModelPath);
                models[d.Value] = model;
                int meshes = model.Nodes.Count(n => n.HasMesh);
                CoreLog.Info($"'{d.Id}': canister model '{d.CanisterModel}' read ({model.Nodes.Length} node(s), {meshes} mesh(es)); shown on near canister views in place of the capsule");
                return model;
            }
            catch (Exception e)
            {
                failed.Add(d.Value);
                CoreLog.Error($"'{d.Id}': canister model '{d.CanisterModel}' could not be read from {d.CanisterModelPath} ({e.GetType().Name}: {e.Message}); its canisters keep the capsule{(d.CanisterTint.HasValue ? " (tinted)" : "")}");
                return null;
            }
        }

        /// <summary>
        /// After vanilla's ShowCargo: shows the registered cargo's model on the view with the capsule hidden, or puts the
        /// capsule back when the cargo has no model (or none that reads). <paramref name="d"/> is null for vanilla cargo.
        /// </summary>
        internal static void Apply(MoonDustCanisterView v, MoonCargo cargo, CargoDefinition d)
        {
            int id = v.GetInstanceID();
            var model = Load(d);
            // A view this class never touched shows vanilla's own look; nothing to restore.
            if (!views.TryGetValue(id, out var state))
            {
                if (model == null) return;
                views[id] = state = new ViewState();
            }

            // Undo the last cargo's swap first: views are pooled and may now show anything.
            foreach (var r in state.Hidden) if (Alive(r)) r.enabled = true;
            state.Hidden.Clear();
            foreach (var kv in state.Holders) if (Alive(kv.Value)) kv.Value.SetActive(false);
            if (model == null) return;

            if (!measured) { measured = true; Measure(v); }

            if (!state.Holders.TryGetValue(d.Value, out var holder) || !Alive(holder))
            {
                holder = new GameObject(HolderName(d.Value));
                holder.transform.SetParent(v.transform, false);
                model.Instantiate(holder.transform, _ => false, StructureMaterials.For);
                int layer = v.gameObject.layer;
                foreach (var t in holder.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                state.Holders[d.Value] = holder;
                CoreLog.Once($"canister-model:{(byte)cargo}", $"'{d.Id}': canister model built on view '{v.name}' ({id}), {holder.GetComponentsInChildren<MeshRenderer>(true).Length} renderer(s), layer {layer}", 2);
            }
            holder.SetActive(true);

            // Hide the capsule: every renderer outside our holders that is on now. Only those come back later.
            foreach (var r in v.GetComponentsInChildren<Renderer>(true))
            {
                if (!Alive(r) || !r.enabled || UnderHolder(r.transform, state)) continue;
                r.enabled = false;
                state.Hidden.Add(r);
            }
        }

        static bool UnderHolder(Transform t, ViewState state)
        {
            foreach (var kv in state.Holders) if (Alive(kv.Value) && t.IsChildOf(kv.Value.transform)) return true;
            return false;
        }

        // The vanilla capsule's size, once, in view-local space: the box the capsule's meshes span, through each renderer's
        // transform (mesh bounds, not Renderer.bounds, so the view's place in the world does not matter) and the view's own
        // scale. What a model author sizes a canister model against (shared/blender/STRUCTURES.md, "Canister models").
        static void Measure(MoonDustCanisterView v)
        {
            try
            {
                var root = v.transform;
                var parts = new List<string>();
                var lo = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                var hi = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                foreach (var mf in v.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null) continue;
                    var b = mesh.bounds;
                    var plo = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                    var phi = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                    for (int i = 0; i < 8; i++)
                    {
                        var c = new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z);
                        var p = root.InverseTransformPoint(mf.transform.TransformPoint(c));
                        plo = Vector3.Min(plo, p); phi = Vector3.Max(phi, p);
                    }
                    lo = Vector3.Min(lo, plo); hi = Vector3.Max(hi, phi);
                    parts.Add($"'{mf.name}' active {mf.gameObject.activeInHierarchy} ({plo.x:F3},{plo.y:F3},{plo.z:F3})..({phi.x:F3},{phi.y:F3},{phi.z:F3})");
                }
                var s = root.lossyScale;
                CoreLog.Info($"Vanilla canister view '{v.name}': local scale ({root.localScale.x:F2},{root.localScale.y:F2},{root.localScale.z:F2}) world scale ({s.x:F2},{s.y:F2},{s.z:F2}); capsule meshes span ({lo.x:F3},{lo.y:F3},{lo.z:F3})..({hi.x:F3},{hi.y:F3},{hi.z:F3}) in view space: {string.Join("; ", parts)}");
            }
            catch (Exception e) { CoreLog.Warn($"Measuring the vanilla canister view failed: {e.Message}"); }
        }

        static bool Alive(UnityEngine.Object o) => o != null && o.Pointer != IntPtr.Zero && o;
    }
}
