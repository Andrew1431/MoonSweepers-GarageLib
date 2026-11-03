using System;
using HarmonyLib;
using MoonCleaners;
using UnityEngine;

namespace BuildQoL
{
    // KeepSelection: after a placement, start a new ghost of the same kind instead of the base game's reset.
    //
    // Measured on the 2026-11 build (README, "Hazards"): a successful placement calls NoteBuilt(selectedBuildId) and then
    // MachineBuilt(def), from UpdatePlacing (after TryBuildDepot) and from ShowPlan (after TryBuildStructure, for pieces
    // without a foundation module; module pieces never reach MachineBuilt and already stay in placement). MachineBuilt
    // either calls BeginConveyor (the definition has conveyor ports: the tool switches to belts, selectedBuildId = 1000) or
    // tail-jumps to CancelPlacement (no ghost). Both callers return right after MachineBuilt, so starting a new placement
    // in its postfix leaves no caller code running on the new state. Belts are built by UpdateConveyor and never reach
    // MachineBuilt.
    //
    // Both targets get postfixes only (no prefix):
    //   NoteBuilt(int)                          288 B, normal prologue (first branch at +0x1B)
    //   MachineBuilt(MoonDepotBuildDefinition)  576 B, normal prologue (first branch at +0x17)
    [HarmonyPatch]
    internal static class KeepSelectionPatches
    {
        // The kind NoteBuilt saw, for the MachineBuilt call that follows it in the same frame on the same builder.
        static int notedKind = -1, notedFrame = -1;
        static IntPtr notedBuilder;
        static bool busy;

        [HarmonyPostfix, HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.NoteBuilt))]
        static void NoteBuiltPostfix(MoonDepotBuilder __instance, int kind)
        {
            try
            {
                notedKind = kind;
                notedFrame = Time.frameCount;
                notedBuilder = __instance != null ? __instance.Pointer : IntPtr.Zero;
            }
            catch (Exception e) { Plugin.Once("note-built", "KeepSelection: NoteBuilt postfix threw: " + e); }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.MachineBuilt))]
        static void MachineBuiltPostfix(MoonDepotBuilder __instance)
        {
            // Re-entrancy: Reselect calls BeginPlacement, which never reaches MachineBuilt today; the guard keeps it that way.
            if (busy || __instance == null) return;
            busy = true;
            try { Reselect(__instance); }
            catch (Exception e) { Plugin.Once("machine-built", "KeepSelection: re-selecting after a placement threw (the tool may be left with no ghost; pick the structure again): " + e); }
            finally { busy = false; }
        }

        static void Reselect(MoonDepotBuilder b)
        {
            int kind = notedKind;
            bool fresh = notedFrame == Time.frameCount && notedBuilder == b.Pointer;
            notedKind = -1;
            if (!fresh || kind < 0) return;
            if (kind == MoonDepotBuilder.ConveyorKind) return;
            // Cannot afford (or otherwise build) another: keep the base game's reset rather than an unplaceable ghost.
            if (!b.CanBuild(kind)) return;
            // MachineBuilt left either no placement (CancelPlacement) or belt mode (BeginConveyor). Anything else is a state
            // this tweak does not know; leave it alone.
            if (b.IsPlacing && !b.ConveyorMode) return;

            bool wasBelt = b.ConveyorMode;
            float yaw = b.yaw;
            if (b.selectedStructure != kind) b.SelectStructure(kind);
            if (b.selectedStructure != kind) return; // not in the catalog list SelectStructure searches
            // BeginPlacement is what the menu's Place uses: it ends belt mode, copies selectedStructure into selectedBuildId
            // and builds the ghost. It resets yaw to 180; put the player's rotation back.
            b.BeginPlacement();
            if (!b.IsPlacing || b.ConveyorMode) return;
            b.yaw = yaw;
            // BeginConveyor posted "<name> built · draw its belts, or RMB to put the belt away"; with a ghost in hand instead
            // of a belt that hint is wrong, so drop it.
            if (wasBelt) b.noticeUntil = 0f;
        }
    }
}
