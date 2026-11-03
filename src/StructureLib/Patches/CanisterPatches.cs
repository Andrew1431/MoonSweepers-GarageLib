using System;
using HarmonyLib;
using MoonCleaners;
using UnityEngine;

namespace StructureLib.Patches
{
    // The canister look (research/POWER_UX_RECON.md, "D. The canister look"). MoonDustCanisterView is the near view of a
    // canister on a belt, on the ground and in the hand; ShowCargo (1152 B, normal prologue) sets its fill per cargo and
    // takes an unknown value down the dust branch. A postfix sets _BaseColor to the registered modded cargo's CanisterTint
    // on the view's own block (properties, which RefreshFill re-applies) and on the dust renderer's block. Views are pooled,
    // so every other cargo (vanilla, unregistered, or modded without a tint) gets white back. Vanilla always runs first.
    // The far, instanced path (MoonCanisterStreaming.LateUpdate) is not tinted: a far canister looks like dust.
    [HarmonyPatch]
    internal static class CanisterPatches
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        // Reused: GetPropertyBlock clears it and copies the renderer's block in, so nothing carries over between views.
        static MaterialPropertyBlock scratch;

        /// <summary>The colour a near canister view of this cargo shows: a registered modded cargo's tint, else white.</summary>
        internal static Color TintOf(MoonCargo cargo) => CargoPatches.Live(cargo)?.CanisterTint ?? Color.white;

        [HarmonyPatch(typeof(MoonDustCanisterView), nameof(MoonDustCanisterView.ShowCargo)), HarmonyPostfix]
        static void ShowCargo(MoonDustCanisterView __instance, MoonCargo cargo)
        {
            try
            {
                if (!(__instance != null)) return;
                var tint = TintOf(cargo);
                var own = __instance.properties;
                if (own != null) own.SetColor(BaseColor, tint);
                var r = __instance.dustRenderer;
                if (r != null)
                {
                    var b = scratch ??= new MaterialPropertyBlock();
                    r.GetPropertyBlock(b);
                    b.SetColor(BaseColor, tint);
                    r.SetPropertyBlock(b);
                }
            }
            catch (Exception e) { CoreLog.Once("tint", $"ShowCargo({(byte)cargo}) tint postfix threw; the canister keeps vanilla's look: {e.Message}", 2); }
        }
    }
}
