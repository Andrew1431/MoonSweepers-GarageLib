using System;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MoonCleaners;
using UnityEngine;

namespace StructureLib.Patches
{
    // Build-menu cards. A modded catalog entry's image is "structurelib:<id>"; CardArt answers it with the rendered
    // (or PNG) picture, or the donor's vanilla art until that exists. Anything else goes to vanilla untouched.
    [HarmonyPatch]
    internal static class CatalogPatches
    {
        internal const string ImagePrefix = "structurelib:";

        [HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.CardArt)), HarmonyPrefix]
        static bool CardArt(MoonDepotBuilder __instance, string image, ref Sprite __result)
        {
            if (!MoonStructures.Frozen || MoonStructures.Disabled || image == null || !image.StartsWith(ImagePrefix, StringComparison.Ordinal)) return true;
            var d = MoonStructures.Definition(image.Substring(ImagePrefix.Length));
            if (d == null || !MoonStructures.IsModded(d.Kind)) return true;
            try
            {
                // A mod-supplied key is vanilla art by another name: ask vanilla for it (the nested call is not ours).
                if (!string.IsNullOrEmpty(d.CardImageKey)) __result = __instance.CardArt(d.CardImageKey);
                else __result = IconRenderer.Get(d) ?? DonorArt(__instance, d);
            }
            catch (Exception e)
            {
                CoreLog.Once("cardart-threw:" + d.Id, $"CardArt for '{d.Id}' threw: {e}", 2);
                __result = null;
            }
            if (__result == null) CoreLog.Once("cardart-null:" + d.Id, $"CardArt for '{d.Id}': no picture (not rendered yet, and no donor art)", 1);
            return false;
        }

        // The donor's own card picture: the catalog entry of BaseStructure if it has one, else the floodlight's (kind 4,
        // the usual donor, has no entry), else null.
        static Sprite DonorArt(MoonDepotBuilder builder, StructureDefinition d)
        {
            string key = ImageOf(MoonDepotBuilder.baseStructures, d.BaseStructure) ?? ImageOf(builder.structureEntries, d.BaseStructure);
            var art = key != null ? builder.CardArt(key) : null;
            return art != null ? art : builder.CardArt("floodlight");
        }

        static string ImageOf(Il2CppReferenceArray<MoonDepotBuilder.CatalogEntry> entries, int kind)
        {
            if (entries == null) return null;
            foreach (var e in entries)
                if (e != null && e.kind == kind && !string.IsNullOrEmpty(e.image) && !e.image.StartsWith(ImagePrefix, StringComparison.Ordinal)) return e.image;
            return null;
        }

        // The menu copies the static catalog into structureEntries here. Read-only: logs whether the modded kinds made it.
        [HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.BuildCatalog)), HarmonyPostfix]
        static void BuildCatalog(MoonDepotBuilder __instance)
        {
            if (!MoonStructures.Frozen || MoonStructures.Disabled) return;
            try
            {
                var entries = __instance.structureEntries;
                var live = MoonStructures.All.Where(d => d.Kind > 0 && MoonStructures.IsModded(d.Kind)).ToList();
                var absent = live.Where(d => entries == null || !entries.Any(e => e != null && e.kind == d.Kind)).Select(d => d.Id).ToList();
                string state = absent.Count == 0 ? $"all {live.Count} modded kind(s) present" : "missing " + string.Join(", ", absent);
                CoreLog.Once("buildcatalog:" + (absent.Count == 0), $"BuildCatalog: {entries?.Length ?? -1} structureEntries, {state}", 1);
            }
            catch (Exception e) { CoreLog.Once("buildcatalog-threw", "BuildCatalog postfix threw: " + e, 1); }
        }

        // A card built before its picture was rendered shows the donor's art; swap the rendered one in on every open menu.
        internal static void ShowArt(StructureDefinition d, Sprite sprite)
        {
            var builders = UnityEngine.Object.FindObjectsByType<MoonDepotBuilder>(FindObjectsSortMode.None);
            if (builders == null) return;
            int swapped = 0;
            foreach (var b in builders)
            {
                var cards = b?.cards;
                if (cards == null) continue;
                for (int i = 0; i < cards.Count; i++)
                {
                    var c = cards.get_Item(i);
                    if (c?.entry == null || c.entry.kind != d.Kind || c.art == null) continue;
                    c.art.sprite = sprite;
                    swapped++;
                }
            }
            CoreLog.Debug($"Card art for '{d.Id}' swapped into {swapped} existing card(s)");
        }
    }
}
