using System;
using System.Collections.Generic;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    /// <summary>
    /// Blender material names from shared/blender/kit.py mapped to the game's own prototype materials (a copy of GarageLib's
    /// GameMaterials: SandPower must not need GarageLib). An unknown name gets a material made from its colour, logged once.
    /// The prototype materials come from the player's equipment, which may not exist yet when a world's clones are built:
    /// then a plain copy of <see cref="FallbackBase"/> tinted with the colour stands in, and <see cref="Upgrade"/> turns
    /// each such stand-in into the game material in place once the equipment exists (every copy shares the instance).
    /// </summary>
    internal static class StructureMaterials
    {
        static readonly Dictionary<string, Material> made = new(StringComparer.Ordinal);
        static readonly Dictionary<string, (Material Mat, Color Colour)> tinted = new(StringComparer.Ordinal);
        static MoonPrototypeModels models;
        static float nextLook;

        /// <summary>A material to copy while the game's prototype materials are not found yet (the donor's first one).</summary>
        internal static Material FallbackBase;

        /// <summary>True while some model material is a tinted stand-in waiting for the game's own.</summary>
        internal static bool Waiting => tinted.Count > 0;

        internal static Material For(string name, Color colour)
        {
            if (made.TryGetValue(name, out var mat) && Alive(mat)) return mat;
            var m = Models();
            if (m == null) return Tinted(name, colour);
            mat = GameMaterial(m, name, colour);
            made[name] = mat;
            return mat;
        }

        static Material GameMaterial(MoonPrototypeModels m, string name, Color colour)
        {
            var mat = name switch
            {
                "MS_BodyWhite" => m.shell,
                "MS_BlackPlastic" => m.dark,
                "MS_OrangeMetal" => m.orange,
                "MS_LampOrange" => m.orange,
                "MS_Glass" => m.glass,
                "MS_Steel" => m.metal,
                "MS_Cyan" => m.cyan,
                // Pressed sand (cargo models): no game material has the colour. MoonPrototypeModels.Mat clones a textured
                // material (a sand brick came out speckled like moon stone), so this is the shell material in the model's own
                // colour with its textures cleared: the same plain matte the stand-ins use.
                "MS_Sand" => Plain(m.shell, "StructureLib_MS_Sand", colour),
                _ => null,
            };
            if (mat == null)
            {
                mat = m.Mat("StructureLib_" + name, colour);
                CoreLog.Once("mat-" + name, $"No game material for Blender material '{name}'; made one from its colour", 1);
            }
            return mat;
        }

        static Material Tinted(string name, Color colour)
        {
            if (tinted.TryGetValue(name, out var t) && Alive(t.Mat)) return t.Mat;
            if (!Alive(FallbackBase)) throw new InvalidOperationException($"no game material for '{name}': no MoonPrototypeModels yet and no fallback material");
            CoreLog.Once("mat-tinted", "No player equipment yet (MoonPrototypeModels); model materials are plain tinted stand-ins until it exists", 1);
            var mat = Plain(FallbackBase, "StructureLib_" + name, colour);
            tinted[name] = (mat, colour);
            return mat;
        }

        // A copy of `from` in one flat colour: textures and emission cleared.
        static Material Plain(Material from, string name, Color colour)
        {
            var mat = new Material(from) { name = name, color = colour };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
            mat.DisableKeyword("_EMISSION");
            mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return mat;
        }

        /// <summary>
        /// Once the player's equipment exists, makes every tinted stand-in the game material it stands for (shader,
        /// properties and keywords copied into the same instance, so templates and built copies change together).
        /// Cheap while nothing waits; looks for the equipment at most twice a second.
        /// </summary>
        internal static void Upgrade()
        {
            if (tinted.Count == 0 || Time.unscaledTime < nextLook) return;
            nextLook = Time.unscaledTime + 0.5f;
            var m = Models();
            if (m == null) return;
            int n = 0;
            foreach (var kv in tinted)
            {
                var g = GameMaterial(m, kv.Key, kv.Value.Colour);
                made[kv.Key] = g;
                var mat = kv.Value.Mat;
                if (!Alive(mat) || !Alive(g)) continue;
                mat.shader = g.shader;
                mat.CopyPropertiesFromMaterial(g);
                mat.shaderKeywords = g.shaderKeywords;
                mat.renderQueue = g.renderQueue;
                n++;
            }
            tinted.Clear();
            CoreLog.Info($"Model materials: {n} tinted stand-in(s) became the game's prototype materials (frame {Time.frameCount})");
        }

        // As GarageLib's IconRenderer.FindModels: the first player equipment whose models are set up.
        internal static MoonPrototypeModels Models()
        {
            if (models != null && models.Pointer != IntPtr.Zero && models.shell != null) return models;
            models = null;
            var eq = UnityEngine.Object.FindObjectsByType<MoonPrototypeEquipment>(FindObjectsSortMode.None);
            if (eq != null)
                foreach (var e in eq)
                    if (e != null && e.Models != null && e.Models.shell != null) { models = e.Models; break; }
            return models;
        }

        static bool Alive(UnityEngine.Object o) => o != null && o.Pointer != IntPtr.Zero && o;
    }
}
