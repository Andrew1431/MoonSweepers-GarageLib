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
                if (model != null)
                {
                    string path = model;
                    def.DecorateModel = (go, ctx) => Swap(go, ctx, def, path, scale);
                }
                MoonVehicles.Register(def, "data:" + Path.GetFileName(Path.GetDirectoryName(file)));
                n++;
            }
            if (n > 0) CoreLog.Info($"Data vehicles: {n} registered from stats files");
        }

        // Hide the base's meshes (its colliders, wheels, brush logic and particles keep working) and put the model on it,
        // standing where the base's wheels touch the ground.
        static void Swap(GameObject root, ModelContext ctx, VehicleDefinition def, string path, float scale)
        {
            try
            {
                var t = root.transform;
                float baseBottom = float.PositiveInfinity;
                int hidden = 0;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.TryCast<MeshRenderer>() == null && r.TryCast<SkinnedMeshRenderer>() == null) continue;
                    var bnd = r.bounds;
                    baseBottom = Mathf.Min(baseBottom, t.InverseTransformPoint(bnd.min).y, t.InverseTransformPoint(bnd.max).y);
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

                float modelBottom = float.PositiveInfinity;
                foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
                    modelBottom = Mathf.Min(modelBottom, t.InverseTransformPoint(r.bounds.min).y);
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
