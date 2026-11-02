using HarmonyLib;
using MoonCleaners;

namespace StructureLib.Patches
{
    // Production and power role switches. For now a modded kind answers as its donor; Tasks 10 (processors) and 11
    // (power) replace these remaps with answers from the structure's own Processor / Power definition.
    //
    // MoonWorld.IsProcessor is deliberately NOT patched. Harmony patches it without error, but the game then dies
    // natively during world load, before the prefix body ever runs (every run with it enabled crashed, 5 of 5; every
    // run without it was clean; IsSolarPanel, IsPowerNode and PowerRange patch and run fine). Its native body is a
    // 15-byte leaf (cmp ecx,0x60 / jne / mov al,1 / ret / cmp ecx,0x61 / sete al / ret) not shared with any other
    // Assembly-CSharp method. Until a safe hook is found, a modded kind gets vanilla's answer for kinds above 192: false.
    [HarmonyPatch]
    internal static class KindRules
    {
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsSolarPanel)), HarmonyPrefix]
        static void IsSolarPanel(ref int kind) => Remap.ToBase(ref kind, "IsSolarPanel");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsPowerNode)), HarmonyPrefix]
        static void IsPowerNode(ref int kind) => Remap.ToBase(ref kind, "IsPowerNode");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.PowerRange)), HarmonyPrefix]
        static void PowerRange(ref int kind) => Remap.ToBase(ref kind, "PowerRange");
    }
}
