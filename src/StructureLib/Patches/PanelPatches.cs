using System;
using System.Collections.Generic;
using HarmonyLib;
using MoonCleaners;
using StructureLib.Pure;
using UnityEngine;

namespace StructureLib.Patches
{
    // The look-at panel for modded generators and machines (research/POWER_UX_RECON.md, "A. The look-at panel").
    // ShowPanel asks IsProcessor (false for a modded kind; unpatchable) and then TryPanel (not patched), so a modded kind
    // would get the solar tracker's power layout. The prefix draws our own panel through the three MoonLookPanel calls
    // instead (PanelText supplies the texts). Vanilla's Update never asks ShowPanel for a modded machine without the build
    // tool (IsProcessor again), so an Update postfix asks for it.
    //
    // Both targets are measured in the recon (ShowPanel 6912 B, Update 2688 B, normal prologues). Never patch
    // MoonWorld.TryPanel, IsProcessor or MoonProductionInteraction.SetRow (an 80 B leaf).
    [HarmonyPatch]
    internal static class PanelPatches
    {
        static readonly Color Dust = new Color(0.72f, 0.74f, 0.78f, 1f);
        static readonly Color Sand = new Color(0.86f, 0.66f, 0.38f, 1f);
        static readonly Color Stone = new Color(0.95f, 0.42f, 0.11f, 1f);

        [HarmonyPatch(typeof(MoonProductionInteraction), nameof(MoonProductionInteraction.ShowPanel)), HarmonyPrefix]
        static bool ShowPanel(MoonProductionInteraction __instance, MoonWorld world, MoonDepotSnapshot record, bool power)
        {
            var d = record == null ? null : Remap.Live(record.kind);
            if (d == null) return true;
            try
            {
                if (!Draw(__instance, world, record, power)) return true;
            }
            catch (Exception e)
            {
                CoreLog.Once("panel:" + record.id, $"Drawing the look-at panel of '{d.Id}' depot {record.id} threw: {e}", 2);
                return true;
            }
            return false;
        }

        // Without the build tool vanilla shows a vanilla machine's production panel, never a modded one's. Generators and
        // routers stay without a panel there, as vanilla power sources do.
        [HarmonyPatch(typeof(MoonProductionInteraction), nameof(MoonProductionInteraction.Update)), HarmonyPostfix]
        static void Update(MoonProductionInteraction __instance)
        {
            try { ShowWithoutTool(__instance); }
            catch (Exception e) { CoreLog.Once("panel-update", "The look-at panel's Update postfix threw: " + e, 2); }
        }

        static void ShowWithoutTool(MoonProductionInteraction inter)
        {
            if (!MoonSession.InGame || !MoonStructures.Frozen || MoonStructures.Disabled) return;
            var world = CoreState.World;
            if (world == null || inter == null) return;
            if (inter.builder?.HoldingBuildTool == true) return;
            // Carrying a canister turns the look-at panel off in vanilla.
            if (inter.capsules?.IsCarrying == true) return;
            int id = inter.Aimed(world);
            if (id <= 0) return;
            var depots = world.depots;
            var depot = depots != null && depots.ContainsKey(id) ? depots.get_Item(id) : null;
            if (depot == null) return;
            var d = Remap.Live(depot.kind);
            if (d?.Processor == null || d.Processor.ProducesPower) return;
            inter.ShowPanel(world, depot, false);
        }

        /// <summary>
        /// Draws the panel of a modded generator or machine through <c>inter.look</c>. False for anything else (a router),
        /// which the prefix then leaves to vanilla.
        /// </summary>
        internal static bool Draw(MoonProductionInteraction inter, MoonWorld world, MoonDepotSnapshot record, bool power)
        {
            var d = Remap.Live(record.kind);
            var look = inter?.look;
            if (d?.Processor == null || look == null || world == null) return false;
            var state = world.ProductionOf(record.id);
            var slots = d.Processor.Slots;

            PanelLayout l;
            var rowCargo = new List<byte?>();
            if (d.Power != null && d.Processor.ProducesPower)
            {
                int fuel = slots.FindIndex(s => s.Role == SlotRole.Input);
                byte cargo = fuel >= 0 ? slots[fuel].Cargo.Value : (byte)0;
                double capacity = fuel >= 0 ? slots[fuel].Capacity : 0;
                l = PanelText.Generator(d.Name, d.Power.Range, d.Power.PeakKw, state?.SuppliedKw ?? 0f, d.Power.BatteryKwh, state?.ChargeKwh ?? 0,
                    CargoName((MoonCargo)cargo), CargoShortName((MoonCargo)cargo), Kg(state, fuel), capacity, state?.Running ?? false,
                    PowerPatches.FedCountOf(record.id));
                foreach (var r in l.Rows) rowCargo.Add(r.Label == "FUEL" ? cargo : null);
            }
            else if (!d.Processor.ProducesPower)
            {
                var shown = new List<PanelSlot>();
                for (int i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    if (s.Role == SlotRole.Batch) continue;
                    shown.Add(new PanelSlot(CargoShortName(s.Cargo.ToMoon()), Kg(state, i), s.Capacity, s.Cargo.Value, s.Role == SlotRole.Input));
                }
                l = PanelText.Machine(d.Name, state?.Status, state?.Running ?? false, state?.Grid ?? 0, state?.Power ?? 0f, state?.UsedKw ?? 0f,
                    d.Processor.RunningKw, state?.Progress ?? 0f, state?.Cycles ?? 0, shown, power, MoonLookPanel.MaxRows);
                // PanelText emits [POWER], the first slot rows in slot order (the last dropped first), then CYCLE.
                int head = power ? 1 : 0;
                for (int i = 0; i < l.Rows.Count; i++)
                {
                    int k = i - head;
                    rowCargo.Add(k >= 0 && k < shown.Count && i < l.Rows.Count - 1 ? shown[k].Cargo : null);
                }
            }
            else return false;

            float opacity = 1f;
            try { if (inter.board != null) opacity = inter.board.HudOpacity; }
            catch (Exception) { opacity = 1f; }

            look.Show(l.Kicker, l.Title, l.State, Colour(l.StateColour, null), opacity);
            for (int i = 0; i < l.Rows.Count; i++)
            {
                var r = l.Rows[i];
                look.SetRow(i, r.Label, r.Fill, r.Value, Colour(r.Colour, rowCargo[i]));
            }
            look.Rows(l.Rows.Count);
            return true;
        }

        static double Kg(MoonProductionState s, int slot) =>
            s?.stores != null && slot >= 0 && slot < s.stores.Length ? s.Stored(slot) : 0;

        static string CargoName(MoonCargo cargo) => CargoPatches.Live(cargo)?.Name ?? MoonWorld.CargoName(cargo);
        static string CargoShortName(MoonCargo cargo) => CargoPatches.Live(cargo)?.ShortName ?? MoonWorld.CargoShortName(cargo);

        internal static Color Colour(PanelColour c, byte? cargo) => c switch
        {
            PanelColour.Cyan => MoonProductionInteraction.Cyan,
            PanelColour.Amber => MoonProductionInteraction.Amber,
            PanelColour.Coral => MoonProductionInteraction.Coral,
            PanelColour.Mint => MoonProductionInteraction.Mint,
            PanelColour.Dust => Dust,
            PanelColour.Sand => Sand,
            PanelColour.Stone => Stone,
            PanelColour.Tint => (cargo.HasValue ? MoonStructures.CargoDefinition((MoonCargo)cargo.Value)?.CanisterTint : null) ?? MoonProductionInteraction.Muted,
            _ => MoonProductionInteraction.Muted,
        };
    }
}
