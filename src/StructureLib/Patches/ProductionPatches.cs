using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MoonCleaners;

namespace StructureLib.Patches
{
    // Power role switches still answered as the donor; Task 11 (power) replaces these remaps with answers from the
    // structure's own Power definition.
    [HarmonyPatch]
    internal static class KindRules
    {
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsSolarPanel)), HarmonyPrefix]
        static void IsSolarPanel(ref int kind) => Remap.ToBase(ref kind, "IsSolarPanel");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.PowerRange)), HarmonyPrefix]
        static void PowerRange(ref int kind) => Remap.ToBase(ref kind, "PowerRange");
    }

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

        // Grid membership. SolvePower rebuilds its node list on every call from an inlined kind check, so a modded
        // processor is never a node (a node added before the solve is dropped; IsPowerNode is not asked). After the
        // vanilla solve, each modded processor that is not a node attaches to the grid of the nearest vanilla node whose
        // own PowerRange reaches the machine's position (FootprintDistance to the node's footprint), and draws its demand
        // (RunningKw while Running) through GridShare: first from that grid's sun surplus (supplyKw - usedKw), then from
        // its batteries. Both come out of the grid's panels' ChargeKwh: the battery share in full, and of the sun share
        // what vanilla already stored while charging (GridShare.ChargeTakenKw against the panels' positive BatteryKw),
        // so no energy is counted twice. Surplus and charging are shared between modded machines in the same solve.
        // Grid, Power, DemandKw, UsedKw and SuppliedKw (0: a machine supplies nothing) are set as vanilla sets them on a
        // machine node. The grid's own totals are a struct in a dictionary and cannot be written back from managed code,
        // so they do not include the modded demand. With no node in range: grid 0, power 0.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.SolvePower)), HarmonyPostfix]
        static void SolvePower(MoonWorld __instance, float dt)
        {
            try { AttachModdedMachines(__instance, dt); }
            catch (Exception e) { CoreLog.Once("solvepower", $"Attaching modded machines to the power grid threw: {e}", 2); }
        }

        static void AttachModdedMachines(MoonWorld world, float dt)
        {
            var production = world.production;
            var depots = world.depots;
            if (production == null || depots == null || !MoonStructures.Frozen || MoonStructures.Disabled) return;
            var nodes = world.powerNodes;
            var vanilla = new List<MoonWorld.PowerNode>();
            var nodeIds = new HashSet<int>();
            for (int i = 0; nodes != null && i < nodes.Count; i++)
            {
                var n = nodes.get_Item(i);
                if (n?.record == null) continue;
                nodeIds.Add(n.record.id);
                if (n.state != null && n.state.Grid != 0) vanilla.Add(n);
            }
            var grids = world.powerGrids;
            var surplus = new Dictionary<int, float>();
            var charging = new Dictionary<int, float>();
            foreach (var kv in production)
            {
                var s = kv.Value;
                var d = s == null ? null : Remap.Live(s.Kind);
                if (d?.Processor == null || nodeIds.Contains(kv.Key) || !depots.ContainsKey(kv.Key)) continue;
                var rec = depots.get_Item(kv.Key);
                var at = new UnityEngine.Vector3(rec.x, rec.y, rec.z);
                MoonWorld.PowerNode best = null;
                float bestDistance = float.MaxValue, bestRange = 0f;
                foreach (var n in vanilla)
                {
                    float range = MoonWorld.PowerRange(n.record.kind);
                    float distance = MoonWorld.FootprintDistance(at, n.position, n.rotation, n.half);
                    if (distance <= range && distance < bestDistance) { best = n; bestDistance = distance; bestRange = range; }
                }
                float demand = s.Running ? d.Processor.RunningKw : 0f;
                if (best == null)
                {
                    SetPower(s, 0, demand, 0f);
                    CoreLog.Once("grid-none:" + kv.Key, $"Power: '{d.Id}' depot {kv.Key} has no vanilla power node in range; it gets no power", 1);
                    continue;
                }
                int g = best.state.Grid;
                var grid = grids != null && grids.ContainsKey(g) ? grids.get_Item(g) : default;
                if (!surplus.TryGetValue(g, out float left)) left = grid.supplyKw - grid.usedKw;
                if (!charging.TryGetValue(g, out float charge)) charge = ChargingKw(vanilla, g);
                var (satisfaction, fromSun, fromBattery) = GridShare.Draw(demand, left, grid.storedKwh);
                float takenBack = GridShare.ChargeTakenKw(fromSun, charge);
                surplus[g] = left - fromSun;
                charging[g] = charge - takenBack;
                double kwh = (fromBattery + takenBack) * (double)dt / 3600.0;
                double drained = kwh > 0 ? DrainBatteries(vanilla, g, kwh) : 0;
                SetPower(s, g, demand, satisfaction);
                CoreLog.Once("grid:" + kv.Key + ":" + g, $"Power: '{d.Id}' depot {kv.Key} attached to grid {g} through depot {best.record.id} ({bestDistance:F1} m, range {bestRange} m); grid supply {grid.supplyKw:F2} kW, used {grid.usedKw:F2} kW, stored {grid.storedKwh:F3} kWh", 1);
                if (demand > 0f)
                    CoreLog.Once("draw:" + kv.Key, $"Power: '{d.Id}' depot {kv.Key} draws {demand} kW on grid {g}: {fromSun:F3} kW from the sun surplus ({left:F2} kW left; {takenBack:F3} kW of it taken back from {charge:F2} kW charging), {fromBattery:F3} kW from batteries; {drained:F7} kWh ({(dt > 0f ? drained * 3600.0 / dt : 0):F3} kW) out of the panels' charge over {dt:F3} s", 3);
            }
        }

        static void SetPower(MoonProductionState s, int grid, float demandKw, float satisfaction)
        {
#if DEVBUILD
            if (DevTools.ForcePower?.Value == true) satisfaction = 1f;
#endif
            s._Grid_k__BackingField = grid;
            s._Power_k__BackingField = satisfaction;
            s._DemandKw_k__BackingField = demandKw;
            s._UsedKw_k__BackingField = demandKw * satisfaction;
            s._SuppliedKw_k__BackingField = 0f;
        }

        // kW the grid's panels are putting into their batteries this solve (their positive BatteryKw).
        static float ChargingKw(List<MoonWorld.PowerNode> nodes, int grid)
        {
            float kw = 0f;
            foreach (var n in nodes)
                if (n.panel && n.state.Grid == grid && n.state.BatteryKw > 0f) kw += n.state.BatteryKw;
            return kw;
        }

        // Takes kWh out of the charge of the grid's panels, in node order; returns how much it took.
        static double DrainBatteries(List<MoonWorld.PowerNode> nodes, int grid, double kwh)
        {
            double taken = 0;
            foreach (var n in nodes)
            {
                if (kwh - taken <= 0) break;
                if (!n.panel || n.state.Grid != grid || n.state.ChargeKwh <= 0) continue;
                double take = Math.Min(kwh - taken, n.state.ChargeKwh);
                n.state._ChargeKwh_k__BackingField = n.state.ChargeKwh - take;
                taken += take;
            }
            return taken;
        }

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

        // Every Batch slot goes into the Output slot at the same mass (owners kept), or is burnt by a power producer.
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
            }
            catch (Exception e) { CoreLog.Once("finishcycle:" + d.Id, $"Finishing a cycle of '{d.Id}' depot {s.Id} threw: {e}", 2); }
            return false;
        }

        static List<(int From, int To, double Kg)> StartMoves(StructureDefinition d, MoonProductionState s)
        {
            int n = d.Processor.Slots.Count;
            if (s.stores == null || s.stores.Length != n) return null;
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

        // Only called with a connected outfeed belt (fact (d)).
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.EmitCargo)), HarmonyPostfix]
        static void EmitCargo(MoonCargo cargo, double kg, bool __result)
        {
            var c = CargoPatches.Live(cargo);
            if (c == null) return;
            ModdedCargoEmits++;
            CoreLog.Once("emit:" + c.Id, $"EmitCargo put {kg} kg of '{c.Id}' on a belt: {__result}", 3);
        }
    }
}
