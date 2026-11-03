using System;
using HarmonyLib;
using MoonCleaners;

namespace StructureLib.Patches
{
    // Per-kind lookups. A modded kind either gets an answer from its definition or is rewritten to its donor
    // (BaseStructure) before the vanilla code runs. Every prefix lets vanilla run untouched unless Remap.Live says the
    // kind is a modded structure in a working, frozen registry, except on a short native leaf, which never runs its
    // original at all (LeafTables).
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
        //
        // HexPieceKind (31 bytes), IsRobotDock (18) and MapKindOf (20) are short native leaves with branches and a ret in
        // the bytes the detour overwrites: they never fall through to the original (see LeafTables). A kind, or a modded
        // kind's donor, answers from the table recorded before patching. The instance queries below them are 80..944-byte
        // functions with no branch in their first 16 bytes; they still rewrite the kind and run the original.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.HexPieceKind)), HarmonyPrefix]
        static bool HexPieceKind(int kind, ref bool __result) { __result = LeafTables.AsBase(LeafTables.HexPieceKind, kind); return false; }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsRobotDock)), HarmonyPrefix]
        static bool IsRobotDock(int kind, ref bool __result) { __result = LeafTables.AsBase(LeafTables.IsRobotDock, kind); return false; }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.MapKindOf)), HarmonyPrefix]
        static bool MapKindOf(int kind, ref MoonWorld.MapRobotKind __result) { __result = LeafTables.AsBase(LeafTables.MapKindOf, kind); return false; }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsEdgeRail)), HarmonyPrefix]
        static void IsEdgeRail(ref int kind) => Remap.ToBase(ref kind, "IsEdgeRail");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsWorldHex)), HarmonyPrefix]
        static void IsWorldHex(ref int kind) => Remap.ToBase(ref kind, "IsWorldHex");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsWorldSquare)), HarmonyPrefix]
        static void IsWorldSquare(ref int kind) => Remap.ToBase(ref kind, "IsWorldSquare");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsWorldGrid)), HarmonyPrefix]
        static void IsWorldGrid(ref int kind) => Remap.ToBase(ref kind, "IsWorldGrid");

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
    }
}
