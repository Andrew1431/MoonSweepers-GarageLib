using System;
using HarmonyLib;
using MoonCleaners;

namespace StructureLib.Patches
{
    // Per-kind lookups. A modded kind either gets an answer from its definition or is rewritten to its donor
    // (BaseStructure) before the vanilla code runs. Every prefix lets vanilla run untouched unless Remap.Live says the
    // kind is a modded structure in a working, frozen registry.
    internal static class Remap
    {
        // The one guard every prefix uses: null unless the registry is frozen, not disabled, and the kind is modded.
        internal static StructureDefinition Live(int kind) =>
            MoonStructures.Frozen && !MoonStructures.Disabled && MoonStructures.IsModded(kind) ? MoonStructures.Definition(kind) : null;

        internal static bool ToBase(ref int kind, string what)
        {
            var d = Live(kind);
            if (d == null) return false;
            CoreLog.Once("remap:" + what + kind, $"{what}({kind}) -> base {d.BaseStructure} for '{d.Id}'", 3);
            kind = d.BaseStructure;
            return true;
        }
    }

    [HarmonyPatch]
    internal static class DefinitionPatches
    {
        // Vanilla answers additionalStructures[kind - 1] for 1..192 and null above; a modded kind gets its clone.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.BuildDefinition)), HarmonyPrefix]
        static bool BuildDefinition(MoonWorld __instance, int kind, ref MoonDepotBuildDefinition __result)
        {
            var d = Remap.Live(kind);
            if (d == null) return true;
            try { __result = Injection.DefinitionFor(d, __instance); }
            catch (Exception e)
            {
                CoreLog.Once("builddef:" + kind, $"BuildDefinition({kind}) for '{d.Id}' threw: {e}", 2);
                __result = null;
            }
            return false;
        }

        // Shape, grid and robot queries: answer as the donor.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.HexPieceKind)), HarmonyPrefix]
        static void HexPieceKind(ref int kind) => Remap.ToBase(ref kind, "HexPieceKind");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsEdgeRail)), HarmonyPrefix]
        static void IsEdgeRail(ref int kind) => Remap.ToBase(ref kind, "IsEdgeRail");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsWorldHex)), HarmonyPrefix]
        static void IsWorldHex(ref int kind) => Remap.ToBase(ref kind, "IsWorldHex");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsWorldSquare)), HarmonyPrefix]
        static void IsWorldSquare(ref int kind) => Remap.ToBase(ref kind, "IsWorldSquare");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsWorldGrid)), HarmonyPrefix]
        static void IsWorldGrid(ref int kind) => Remap.ToBase(ref kind, "IsWorldGrid");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsRobotDock)), HarmonyPrefix]
        static void IsRobotDock(ref int kind) => Remap.ToBase(ref kind, "IsRobotDock");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.MapKindOf)), HarmonyPrefix]
        static void MapKindOf(ref int kind) => Remap.ToBase(ref kind, "MapKindOf");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.FarSpec)), HarmonyPrefix]
        static void FarSpec(ref int kind) => Remap.ToBase(ref kind, "FarSpec");

        // Build menu: vanilla CanBuild is true only for 1..192.
        [HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.CanBuild)), HarmonyPrefix]
        static bool CanBuild(int kind, ref bool __result)
        {
            if (Remap.Live(kind) == null) return true;
            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.ProductionNote)), HarmonyPrefix]
        static bool ProductionNote(int kind, ref string __result)
        {
            var d = Remap.Live(kind);
            if (d == null) return true;
            __result = d.Note ?? "";
            return false;
        }

        // What a machine accepts, shown in its details. Empty until processors land.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.AcceptsText)), HarmonyPrefix]
        static bool AcceptsText(int kind, ref string __result)
        {
            if (Remap.Live(kind) == null) return true;
            __result = "";
            return false;
        }
    }
}
