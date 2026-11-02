using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MoonCleaners;

namespace StructureLib.Patches
{
    // Modded processors. The game asks the static Processor(kind) for a machine's spec when it creates its production
    // state (NewProduction sizes the slot stores from it, on build and on restore) and while it runs it; a modded kind
    // gets the spec SpecBuilder synthesizes from its ProcessorDefinition. InputSlot and AcceptsText answer from the
    // definition's slots, IsPowerNode from its roles, and IsProcessorId for a built modded depot.
    //
    // MoonWorld.IsProcessor(int kind) is deliberately NOT patched. Harmony patches it without error, but the game then
    // dies natively during world load, before the prefix body ever runs (5 of 5 runs; see research/STRUCTURES_RECON.md,
    // "Interop hazards found"). Its native body is a 15-byte leaf. "Is this a processor" is answered through the instance
    // IsProcessorId(id) and the Processor(kind) spec instead; a modded kind still gets vanilla's IsProcessor answer (false).
    [HarmonyPatch]
    internal static class ProductionPatches
    {
        /// <summary>EmitCargo calls that put a registered modded cargo on a belt (diagnostics; read by the dev self-test).</summary>
        internal static int ModdedCargoEmits;

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.Processor)), HarmonyPrefix]
        static bool Processor(int kind, ref MoonWorld.ProcessorSpec __result)
        {
            var d = Remap.Live(kind);
            if (d == null) return true;
            try { __result = SpecBuilder.Build(d); }
            catch (Exception e)
            {
                CoreLog.Once("spec:" + kind, $"Processor({kind}) for '{d.Id}': building its spec threw; answering no processor: {e}", 2);
                __result = null;
            }
            return false;
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsProcessorId)), HarmonyPrefix]
        static bool IsProcessorId(MoonWorld __instance, int id, ref bool __result)
        {
            var d = DepotDefinition(__instance, id, "IsProcessorId");
            if (d == null) return true;
            __result = d.Processor != null;
            return false;
        }

        // Applying a load into a machine (the crew / AutoFeed path). It never touches the walker's cargo: the walker path,
        // LoadProcessor, deducts the carried dust itself and does not call it, and vanilla CrewProcessorLoad caps the load at
        // the slot's capacity and drops the excess (both checked in game on the chamber). Vanilla loads nothing into a
        // modded machine (no InputSlot call was seen), so for one the prefix does the same: the first Input slot that takes
        // the cargo gets as much as fits, under the player's share; a cargo no Input slot takes is refused.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.CrewProcessorLoad)), HarmonyPrefix]
        static bool CrewProcessorLoad(MoonWorld __instance, string player, int machine, MoonCargo cargo, double kilograms)
        {
            var d = DepotDefinition(__instance, machine, "CrewProcessorLoad");
            if (d == null) return true;
            try
            {
                var s = __instance.ProductionOf(machine);
                int slot = SpecBuilder.InputSlot(d, cargo);
                if (s?.stores == null || slot < 0 || slot >= s.stores.Length || kilograms <= 0) return false;
                double kg = Math.Min(kilograms, d.Processor.Slots[slot].Capacity - s.Stored(slot));
                if (kg > 0) MoonWorld.Put(s.stores[slot], player, kg);
            }
            catch (Exception e) { CoreLog.Once("crewload:" + d.Id, $"CrewProcessorLoad into '{d.Id}' depot {machine} threw: {e}", 2); }
            return false;
        }

        // The modded structure built as depot `id`, or null (not built, vanilla, or the registry is not working).
        static StructureDefinition DepotDefinition(MoonWorld world, int id, string what)
        {
            try
            {
                var depots = world.depots;
                return depots != null && depots.ContainsKey(id) ? Remap.Live(depots.get_Item(id)?.kind ?? 0) : null;
            }
            catch (Exception e)
            {
                CoreLog.Once("depotdef:" + what, $"{what}({id}): reading the depot threw; answering as vanilla: {e.Message}", 2);
                return null;
            }
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.InputSlot)), HarmonyPrefix]
        static bool InputSlot(int kind, MoonCargo cargo, ref int __result)
        {
            var d = Remap.Live(kind);
            if (d == null) return true;
            __result = SpecBuilder.InputSlot(d, cargo);
            return false;
        }

        // What a machine accepts, shown in its details ("dust or sand"); empty for a structure without a processor.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.AcceptsText)), HarmonyPrefix]
        static bool AcceptsText(int kind, ref string __result)
        {
            var d = Remap.Live(kind);
            if (d == null) return true;
            __result = SpecBuilder.AcceptsText(d);
            return false;
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsPowerNode)), HarmonyPrefix]
        static bool IsPowerNode(int kind, ref bool __result)
        {
            var d = Remap.Live(kind);
            if (d == null) return true;
            __result = d.Processor != null || d.Power != null;
            return false;
        }

        // The cycle. Vanilla's CanStartCycle answers false (batch 0) for any kind but the chamber and the press, even with
        // the input filled and the machine powered (checked in game), so a modded processor runs ProcessorCycle instead:
        // these three prefixes replace vanilla for modded kinds only. StepProduction still drives them (it calls
        // TryStartCycle, advances Progress while Running, and calls FinishCycle).
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.CanStartCycle)), HarmonyPrefix]
        static bool CanStartCycle(MoonProductionState s, ref double batch, ref bool __result)
        {
            var d = s == null ? null : Remap.Live(s.Kind);
            if (d == null) return true;
            var moves = d.Processor == null ? null : StartMoves(d, s);
            __result = moves != null;
            batch = moves?.Sum(m => m.Kg) ?? 0;
            return false;
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryStartCycle)), HarmonyPrefix]
        static bool TryStartCycle(MoonProductionState s)
        {
            var d = s == null ? null : Remap.Live(s.Kind);
            if (d == null) return true;
            try
            {
                if (d.Processor == null || s.Running) return false;
                var moves = StartMoves(d, s);
                if (moves == null) return false;
                foreach (var (from, to, kg) in moves) MoonWorld.Move(s.stores[from], s.stores[to], kg);
                s._Progress_k__BackingField = 0f;
                s._Running_k__BackingField = true;
            }
            catch (Exception e) { CoreLog.Once("startcycle:" + d.Id, $"Starting a cycle of '{d.Id}' depot {s.Id} threw: {e}", 2); }
            return false;
        }

        // Every Batch slot goes into the Output slot at the same mass (owners kept), or is burnt by a power producer, whose
        // battery then stores half its capacity more (ProcessorCycle.ChargeAfterBurn: two cycles fill it).
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.FinishCycle)), HarmonyPrefix]
        static bool FinishCyclePre(MoonProductionState s)
        {
            var d = s == null ? null : Remap.Live(s.Kind);
            if (d == null) return true;
            try
            {
                if (d.Processor == null || s.stores == null) return false;
                int output = ProcessorLayout.OutputIndex(d.Processor.Slots.Select(x => x.Role).ToList(), d.Processor.ProducesPower);
                foreach (int i in ProcessorCycle.BatchSlots(d.Processor.Slots.Select(x => x.Role).ToList()))
                {
                    if (i >= s.stores.Length || s.stores[i] == null) continue;
                    if (output >= 0 && output < s.stores.Length) MoonWorld.Move(s.stores[i], s.stores[output], MoonWorld.Total(s.stores[i]));
                    else s.stores[i].Clear();
                }
                s._Running_k__BackingField = false;
                s._Progress_k__BackingField = 0f;
                if (d.Processor.ProducesPower && d.Power != null) s._ChargeKwh_k__BackingField = ProcessorCycle.ChargeAfterBurn(s.ChargeKwh, d.Power.BatteryKwh);
            }
            catch (Exception e) { CoreLog.Once("finishcycle:" + d.Id, $"Finishing a cycle of '{d.Id}' depot {s.Id} threw: {e}", 2); }
            return false;
        }

        static List<(int From, int To, double Kg)> StartMoves(StructureDefinition d, MoonProductionState s)
        {
            int n = d.Processor.Slots.Count;
            if (s.stores == null || s.stores.Length != n) return null;
            // A fuel generator does not burn into a full battery.
            if (d.Processor.ProducesPower && d.Power != null && !ProcessorCycle.CanBurn(s.ChargeKwh, d.Power.BatteryKwh)) return null;
            var stored = new double[n];
            for (int i = 0; i < n; i++) stored[i] = s.Stored(i);
            return ProcessorCycle.StartMoves(SpecBuilder.CycleSlots(d), stored, d.Processor.ProducesPower);
        }

        // FinishCycle has moved the batch into the output slot (fact (c)); the state is the machine's live one.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.FinishCycle)), HarmonyPostfix]
        static void FinishCycle(MoonProductionState s)
        {
            if (s == null) return;
            var d = Remap.Live(s.Kind);
            if (d == null) return;
            MoonStructures.RaiseCycleFinished(new CycleFinished { Definition = d, DepotId = s.Id, State = s });
        }

        // A power producer has no output slot (spec.output -1). StepProduction calls EmitProduct on a timer even before any
        // cycle, and vanilla then throws IndexOutOfRangeException (seen in game, caught by MoonWorld.Fault), so it is skipped.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.EmitProduct)), HarmonyPrefix]
        static bool EmitProduct(MoonProductionState s)
        {
            var d = s == null ? null : Remap.Live(s.Kind);
            return d?.Processor == null || !d.Processor.ProducesPower;
        }

        // Only called with a connected outfeed belt (fact (d)). Vanilla EmitProduct emits a modded machine's output by itself
        // (one 1 kg canister per call, recon fact (f)); this postfix only reports it: for a canister put on the belt (__result)
        // by a modded machine (resolved from the belt's start, BeltPatches.EmitterOf) it raises MoonStructures.CargoEmitted.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.EmitCargo)), HarmonyPostfix]
        static void EmitCargo(MoonWorld __instance, MoonConveyorSnapshot outfeed, MoonCargo cargo, double kg, bool __result)
        {
            var c = CargoPatches.Live(cargo);
            if (c != null)
            {
                ModdedCargoEmits++;
                CoreLog.Once("emit:" + c.Id, $"EmitCargo put {kg} kg of '{c.Id}' on a belt: {__result}", 3);
            }
            if (!__result || !MoonStructures.Frozen || MoonStructures.Disabled) return;
            try
            {
                var e = BeltPatches.EmitterOf(__instance, outfeed);
                // A router's hop emits onto its output belt too (RouterHop); it raises CargoRouted, not CargoEmitted.
                if (e?.Definition == null || e.Definition.Router != null) return;
                MoonStructures.RaiseCargoEmitted(new CargoEmitted
                {
                    Definition = e.Definition, DepotId = e.Depot, Cargo = new Cargo((byte)cargo, BeltPatches.CargoId(cargo)), Kilograms = kg, BeltId = outfeed.id,
                });
            }
            catch (Exception ex) { CoreLog.Once("emit-resolve", $"EmitCargo on belt {outfeed?.id}: resolving the emitting machine threw: {ex}", 1); }
        }
    }
}
