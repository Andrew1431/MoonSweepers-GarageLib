using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace GarageLib
{
    /// <summary>
    /// Vehicles declared entirely in <c>*.vehicles.json</c>: an entry with <c>"register": true</c> is registered by GarageLib
    /// itself, on a vanilla sweeper base (brush, hopper, wheels and driving all stay the base's), with the base's look
    /// swapped for the entry's <c>model</c> (a mesh .bin next to the json). No plugin DLL needed.
    /// </summary>
    internal static class DataVehicles
    {
        // Vanilla sweepers: Push sweeper, Ride-on, Lunar sweeper, Mega vacuum rover. Anything else has no brush or hopper.
        static readonly HashSet<int> SweeperBases = new() { 0, 1, 20, 21 };
        internal const string ModelNode = "GarageLibModel";

        internal static void RegisterAll()
        {
            int n = 0;
            foreach (var id in new List<string>(VehicleStats.Ids))
            {
                if (!VehicleStats.TryField(id, "register", out var reg, out var file) || reg.ValueKind != JsonValueKind.True) continue;
                int baseVehicle = VehicleStats.TryField(id, "baseVehicle", out var b, out _) && b.ValueKind == JsonValueKind.Number ? b.GetInt32() : -1;
                if (!SweeperBases.Contains(baseVehicle))
                {
                    CoreLog.Error($"Data vehicle '{id}' ({Path.GetFileName(file)}): baseVehicle {baseVehicle} isn't a vanilla sweeper (0, 1, 20 or 21). Not registered.");
                    continue;
                }
                string model = null;
                if (VehicleStats.TryField(id, "model", out var m, out var modelFile) && m.ValueKind == JsonValueKind.String)
                {
                    model = Path.Combine(Path.GetDirectoryName(modelFile), m.GetString());
                    if (!File.Exists(model)) { CoreLog.Error($"Data vehicle '{id}': model {model} not found; using the base vehicle's look."); model = null; }
                }
                float scale = VehicleStats.TryField(id, "modelScale", out var s, out _) && s.ValueKind == JsonValueKind.Number ? s.GetSingle() : 1f;
                var def = new VehicleDefinition { Id = id, BaseVehicle = baseVehicle };
                dataIds.Add(id);
                if (model != null)
                {
                    string path = model;
                    def.DecorateModel = (go, ctx) => Swap(go, ctx, def, path, scale);
                }
                MoonVehicles.Register(def, "data:" + Path.GetFileName(Path.GetDirectoryName(file)));
                n++;
            }
            if (n > 0)
            {
                MoonVehicles.Spawned += OnSpawned;
                CoreLog.Info($"Data vehicles: {n} registered from stats files");
            }
        }

        static readonly HashSet<string> dataIds = new();

        // After spawn: put the base's driver seat on the model's seat, and start the wheel / brush animator.
        static void OnSpawned(VehicleSpawn s)
        {
            if (s?.Vehicle == null || !dataIds.Contains(s.Definition.Id)) return;
            try
            {
                var vt = s.Vehicle.transform;
                Transform holder = null;
                foreach (var tr in vt.GetComponentsInChildren<Transform>(true)) if (tr.name == ModelNode) { holder = tr; break; }
                if (holder == null) return;

                // Authored bases (the Mega) clean their prefab's strip, not the catalog width: match it to the stat.
                var view = s.Vehicle.authored;
                var spec = s.Vehicle.Spec;
                if (view != null && spec != null && Mathf.Abs(view.cleaningWidth - spec.width) > 0.01f)
                {
                    CoreLog.Debug($"'{s.Definition.Id}': cleaning width {view.cleaningWidth:F2} -> {spec.width:F2} (offset {view.cleaningOffset:F2})");
                    view.cleaningWidth = spec.width;
                }

                // ...and move the strip to where the model's brushes are (the base's sits at its own intake).
                if (view != null && animated.TryGetValue(s.Definition.Id, out var bp) && bp.brushes.Count > 0)
                {
                    float sum = 0f, weight = 0f;
                    foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!bp.brushes.Contains(r.name)) continue;
                        float w = Mathf.Max(0.1f, r.bounds.size.x * r.bounds.size.x + r.bounds.size.z * r.bounds.size.z);
                        sum += vt.InverseTransformPoint(r.bounds.center).z * w;
                        weight += w;
                    }
                    if (weight > 0f)
                    {
                        float offset = sum / weight;
                        CoreLog.Debug($"'{s.Definition.Id}': cleaning offset {view.cleaningOffset:F2} -> {offset:F2}");
                        view.cleaningOffset = offset;
                    }
                }

                var seatNode = FindSeat(holder);
                var seat = s.Vehicle.seat;
                if (seatNode != null && seat != null)
                {
                    Vector3 target = seatNode.position;
                    var rend = seatNode.GetComponent<Renderer>();
                    if (seatNode.name != "DriverSeat" && rend != null) { var b = rend.bounds; target = new Vector3(b.center.x, b.max.y, b.center.z); }
                    var before = vt.InverseTransformPoint(seat.position);
                    seat.position = target;
                    CoreLog.Debug($"'{s.Definition.Id}': seat {before} -> {vt.InverseTransformPoint(seat.position)} (from {seatNode.name})");
                }
                else CoreLog.Once("seat-" + s.Definition.Id, $"'{s.Definition.Id}': no seat node in the model (name one DriverSeat); rider uses the base's seat", 1);

                animated.TryGetValue(s.Definition.Id, out var parts);
                cameraScale.TryGetValue(s.Definition.Id, out float camScale);
                if (camScale < 1.01f && (parts.wheels == null || parts.wheels.Count + parts.brushes.Count == 0)) return;
                var anim = s.Vehicle.gameObject.GetComponent<ModelAnimator>() ?? s.Vehicle.gameObject.AddComponent<ModelAnimator>();
                anim.Vehicle = s.Vehicle;
                anim.CameraScale = Mathf.Max(1f, camScale);
                CoreLog.Debug($"'{s.Definition.Id}': chase camera x{anim.CameraScale:F2}");
                if (parts.wheels == null) return;
                anim.Wheels.Clear(); anim.Brushes.Clear();
                foreach (var tr in holder.GetComponentsInChildren<Transform>(true))
                {
                    if (parts.wheels.TryGetValue(tr.name, out float r)) anim.Wheels.Add((tr, r));
                    else if (parts.brushes.Contains(tr.name)) anim.Brushes.Add(tr);
                }
            }
            catch (Exception e) { CoreLog.Once("spawned-" + s.Definition.Id, $"'{s.Definition.Id}': seat / animator setup failed: {e}", 2); }
        }

        // DriverSeat if the model names one; else the first mesh with "seat" in its name that isn't a backrest.
        static Transform FindSeat(Transform holder)
        {
            Transform best = null;
            foreach (var tr in holder.GetComponentsInChildren<Transform>(true))
            {
                if (tr.name == "DriverSeat") return tr;
                var n = tr.name.ToLowerInvariant();
                if (best == null && n.Contains("seat") && !n.Contains("back") && tr.GetComponent<Renderer>() != null) best = tr;
            }
            return best;
        }

        static readonly Dictionary<string, float> cameraScale = new();
        static float Size(Bounds b) => Mathf.Max(b.size.x, b.size.z) * 0.7f + b.size.y * 0.3f;

        static readonly Dictionary<string, (Dictionary<string, float> wheels, HashSet<string> brushes)> animated = new();

        // Hide the base's meshes (its colliders, wheels, brush logic and particles keep working) and put the model on it,
        // standing where the base's wheels touch the ground.
        static void Swap(GameObject root, ModelContext ctx, VehicleDefinition def, string path, float scale)
        {
            try
            {
                var t = root.transform;
                float baseBottom = float.PositiveInfinity;
                var baseBox = new Bounds();
                bool hasBase = false;
                int hidden = 0;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.TryCast<MeshRenderer>() == null && r.TryCast<SkinnedMeshRenderer>() == null) continue;
                    var bnd = r.bounds;
                    baseBottom = Mathf.Min(baseBottom, t.InverseTransformPoint(bnd.min).y, t.InverseTransformPoint(bnd.max).y);
                    if (hasBase) baseBox.Encapsulate(bnd); else { baseBox = bnd; hasBase = true; }
                    r.enabled = false;
                    hidden++;
                }
                if (float.IsInfinity(baseBottom)) baseBottom = 0f;

                var model = MeshModel.LoadFile(path);
                var holder = new GameObject(ModelNode).transform;
                holder.SetParent(t, false);
                holder.localScale = Vector3.one * scale;
                var mats = new GameMaterials(ctx.Models);
                model.Instantiate(holder, _ => false, mats.For);
                if (!animated.ContainsKey(def.Id))
                {
                    var wheels = new Dictionary<string, float>();
                    var brushes = new HashSet<string>();
                    foreach (var node in model.Nodes)
                    {
                        float r = node.Float("spin", 0f);
                        if (Mathf.Abs(r) > 0.01f) wheels[node.Name] = r;
                        else if (node.Props.ContainsKey("brush")) brushes.Add(node.Name);
                    }
                    animated[def.Id] = (wheels, brushes);
                }

                float modelBottom = float.PositiveInfinity;
                var modelBox = new Bounds();
                bool hasModel = false;
                foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
                {
                    modelBottom = Mathf.Min(modelBottom, t.InverseTransformPoint(r.bounds.min).y);
                    if (hasModel) modelBox.Encapsulate(r.bounds); else { modelBox = r.bounds; hasModel = true; }
                }
                // Chase camera pulls back by how much bigger the model is than the base (world AABB is fine: both are
                // measured in the same pose).
                if (hasBase && hasModel)
                    cameraScale[def.Id] = Mathf.Clamp(Size(modelBox) / Mathf.Max(0.5f, Size(baseBox)), 1f, 4f);
                if (!float.IsInfinity(modelBottom)) holder.localPosition = new Vector3(0f, baseBottom - modelBottom, 0f);
                foreach (var tr in holder.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = root.layer;
                CoreLog.Debug($"'{def.Id}': model {Path.GetFileName(path)} on base {def.BaseVehicle}, hid {hidden} base meshes, offset {holder.localPosition.y:F2}");
            }
            catch (Exception e) { CoreLog.Once("swap-" + def.Id, $"'{def.Id}': model swap failed, showing the base vehicle: {e}", 2); }
        }
    }

    /// <summary>Blender material names (see shared/blender/kit.py) mapped to the game's own vehicle materials.</summary>
    public sealed class GameMaterials
    {
        readonly MoonCleaners.MoonPrototypeModels m;
        readonly Dictionary<string, Material> made = new();
        public GameMaterials(MoonCleaners.MoonPrototypeModels models) { m = models; }

        public Material For(string name, Color colour)
        {
            if (made.TryGetValue(name, out var mat) && mat != null && !mat.WasCollected) return mat;
            mat = name switch
            {
                "MS_BodyWhite" => m.shell,
                "MS_BlackPlastic" => m.dark,
                "MS_OrangeMetal" => m.orange,
                "MS_LampOrange" => m.orange,
                "MS_Glass" => m.glass,
                "MS_Steel" => m.metal,
                "MS_Cyan" => m.cyan,
                _ => null,
            };
            if (mat == null)
            {
                mat = m.Mat("GarageLib_" + name, colour);
                CoreLog.Once("mat-" + name, $"No game material for Blender material '{name}'; made one from its colour", 1);
            }
            made[name] = mat;
            return mat;
        }
    }
}
