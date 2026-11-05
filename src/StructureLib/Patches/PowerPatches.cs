using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MoonCleaners;
using UnityEngine;

namespace StructureLib.Patches
{
    // Power. The static role switches answer from a modded structure's Power definition. The supply itself happens in a
    // SolvePower postfix: SolvePower rebuilds its node list on every call from an inlined kind check (a node added before
    // the solve is dropped, IsPowerNode is not asked, and there is no per-node creation hook), so modded machines and
    // modded fuel generators are attached to the grids after the vanilla solve. See research/STRUCTURES_RECON.md, "Power
    // sources (Task 11)".
    //
    // MoonWorld.TryPanel(int, out PanelSpec) is deliberately NOT patched: with a prefix on it the game died natively a few
    // seconds after world load (see "Interop hazards found"). A modded kind is none of vanilla's three panel kinds, so its
    // answer is presumably false (not traced); managed code never calls it.
    [HarmonyPatch]
    internal static class PowerPatches
    {
        // A 15-byte native leaf: never falls through to the original (falling through crashed every vanilla card click
        // for kinds 93..97; see LeafTables). Vanilla kinds answer from the table recorded before patching.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsSolarPanel)), HarmonyPrefix]
        static bool IsSolarPanel(int kind, ref bool __result)
        {
            var d = Remap.Live(kind);
            __result = d != null
                ? LeafTables.Modded(LeafTables.IsSolarPanel, kind, d.Power != null && d.Power.SupplyMode == SupplyMode.Solar, d.Id)
                : LeafTables.Vanilla(LeafTables.IsSolarPanel, kind);
            return false;
        }

        // A power source's own range; any other modded structure answers as its donor.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.PowerRange)), HarmonyPrefix]
        static bool PowerRange(ref int kind, ref float __result)
        {
            var d = Remap.Live(kind);
            if (d?.Power == null) { Remap.ToBase(ref kind, "PowerRange"); return true; }
            __result = d.Power.Range;
            return false;
        }

        // A modded machine, a modded fuel generator, or a vanilla machine a fuel generator may top up (Def null, Vanilla its
        // own node), during one solve.
        sealed class Member
        {
            internal StructureDefinition Def;
            internal MoonProductionState State;
            internal MoonDepotSnapshot Record;
            internal Vector3 At;
            internal MoonWorld.PowerNode Vanilla;
            internal float VanillaDistance, VanillaRange;
            internal bool Source;
            internal float AvailableKw, SuppliedKw;
            internal bool IsVanilla => Def == null;
        }

        // What the last solve did, for the look-at panel's POWERING row and the arcs. Rewritten every solve.
        internal static class LastSolve
        {
            // Generator depot -> the members (modded or vanilla) it gave > 0 kW to.
            internal static readonly Dictionary<int, List<int>> Fed = new();
            // Every member's depot (sources included; a vanilla machine only when a fuel generator's network reaches it)
            // -> network id (the depot id of one member of the network).
            internal static readonly Dictionary<int, int> NetworkOf = new();
            // Vanilla member depot -> its Power as vanilla's solve left it, before the top-up.
            internal static readonly Dictionary<int, float> VanillaPowerBefore = new();

            internal static int FedCount(int generatorDepot) => Fed.TryGetValue(generatorDepot, out var fed) ? fed.Count : 0;

            internal static void Clear() { Fed.Clear(); NetworkOf.Clear(); VanillaPowerBefore.Clear(); }

            internal static void AddFed(int generatorDepot, int member)
            {
                if (!Fed.TryGetValue(generatorDepot, out var fed)) Fed[generatorDepot] = fed = new List<int>();
                if (!fed.Contains(member)) fed.Add(member);
            }
        }

        // After the vanilla solve:
        // - Each modded processor that is not a node finds the nearest vanilla node whose own PowerRange reaches it
        //   (FootprintDistance to the node's footprint).
        // - FuelGrid groups modded members into networks around modded fuel generators (a Processor with ProducesPower and
        //   a Fuel PowerDefinition) and assigns grid ids: the vanilla grid where one reaches, else -(lowest depot id).
        // - A machine draws its demand (RunningKw while Running) through GridShare: first the vanilla grid's sun surplus
        //   (supplyKw - usedKw, the share vanilla banked while charging taken back out of the charging panels, see
        //   GridShare.ChargeTakenKw), then the fuel generators of its network (each up to PeakKw, at most what its
        //   ChargeKwh lasts over the solve), then the vanilla grid's batteries. Surplus, charging, stored charge and fuel
        //   are shared between modded machines in the same solve.
        // - Machines get Grid, Power (satisfaction), DemandKw, UsedKw and SuppliedKw 0, as vanilla sets them on a machine
        //   node. A fuel generator gets Grid, Power 1 (it runs on its own fuel), SuppliedKw and BatteryKw (minus what it
        //   gave), and the drawn kWh out of its ChargeKwh; it is then appended to powerNodes as a panel node until the next
        //   solve rebuilds the list.
        // - Then, when there is a fuel generator, vanilla machine nodes with demand and Power < 1 join as non-source
        //   members (centre to centre within a generator's Range, chained like modded members). In depot-id order each
        //   takes the unmet share of its demand (FuelTopUp) from what its network's generators have left after the modded
        //   machines; that kW goes into the generators' SuppliedKw, so it comes out of their ChargeKwh like a modded draw.
        //   Only Power and UsedKw are written (Grid and DemandKw stay as vanilla set them). Vanilla resets them every
        //   solve and StepProduction reads them after this postfix, so the raise is reapplied every solve
        //   (POWER_UX_RECON.md, B). Modded grid ids come from a FuelGrid.Assign of the modded members alone, so a vanilla
        //   machine never changes a modded member's grid or network; a second Assign over all members (the vanilla ones
        //   with no vanilla grid) gives the vanilla members' networks.
        // The vanilla grid's totals are a struct in a dictionary and cannot be written back from managed code, so they do
        // not include modded demand or fuel supply.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.SolvePower)), HarmonyPostfix]
        static void SolvePower(MoonWorld __instance, float dt)
        {
            try { AttachModded(__instance, dt); }
            catch (Exception e) { CoreLog.Once("solvepower", $"Attaching modded structures to the power grid threw: {e}", 2); }
        }

        static void AttachModded(MoonWorld world, float dt)
        {
            LastSolve.Clear();
            var production = world.production;
            var depots = world.depots;
            if (production == null || depots == null || !MoonStructures.Frozen || MoonStructures.Disabled) return;
            var nodes = world.powerNodes;
            var vanilla = new List<MoonWorld.PowerNode>();
            var nodeIds = new HashSet<int>();
            for (int i = 0; nodes != null && i < nodes.Count; i++)
            {
                var n = nodes.get_Item(i);
                if (n?.record == null || Remap.Live(n.record.kind) != null) continue;
                nodeIds.Add(n.record.id);
                if (n.state != null && n.state.Grid != 0) vanilla.Add(n);
            }

            var members = new List<Member>();
            foreach (var kv in production)
            {
                var s = kv.Value;
                var d = s == null ? null : Remap.Live(s.Kind);
                if (d?.Processor == null || nodeIds.Contains(kv.Key) || !depots.ContainsKey(kv.Key)) continue;
                var m = new Member { Def = d, State = s, Record = depots.get_Item(kv.Key) };
                m.At = new Vector3(m.Record.x, m.Record.y, m.Record.z);
                m.VanillaDistance = float.MaxValue;
                foreach (var n in vanilla)
                {
                    float range = MoonWorld.PowerRange(n.record.kind);
                    float distance = MoonWorld.FootprintDistance(m.At, n.position, n.rotation, n.half);
                    if (distance <= range && distance < m.VanillaDistance) { m.Vanilla = n; m.VanillaDistance = distance; m.VanillaRange = range; }
                }
                m.Source = IsFuelSource(d);
                if (m.Source) m.AvailableKw = FuelGrid.AvailableKw(d.Power.PeakKw, s.ChargeKwh, dt);
                members.Add(m);
            }
            if (members.Count == 0) return;
            int modded = members.Count;
            if (members.Any(m => m.Source))
            {
                var candidates = new List<MoonWorld.PowerNode>();
                for (int i = 0; nodes != null && i < nodes.Count; i++)
                {
                    var n = nodes.get_Item(i);
                    if (n?.record == null || n.panel || Remap.Live(n.record.kind) != null) continue;
                    if (n.state != null && n.state.DemandKw > 0f && n.state.Power < 0.999f) candidates.Add(n);
                }
                foreach (var n in candidates.OrderBy(n => n.record.id))
                    members.Add(new Member { State = n.state, Record = n.record, At = n.position, Vanilla = n });
            }

            var (gridOf, networkOf) = FuelGrid.Assign(members.Take(modded).Select(m =>
                new FuelGrid.Member(m.Record.id, m.At.x, m.At.y, m.At.z, m.Source ? m.Def.Power.Range : 0f, m.Vanilla?.state.Grid ?? 0)).ToList());
            // The vanilla members' networks. A vanilla machine within reach of generators of two modded networks bridges
            // them here, for its own (vanilla) draw only: modded members keep the networks of the modded Assign above,
            // which served them.
            var powerNet = members.Count == modded ? networkOf : FuelGrid.Assign(members.Select(m =>
                new FuelGrid.Member(m.Record.id, m.At.x, m.At.y, m.At.z, m.Source ? m.Def.Power.Range : 0f, m.IsVanilla ? 0 : m.Vanilla?.state.Grid ?? 0)).ToList()).Network;
            // Ledger network ids: a modded member's is the depot id of its modded network's root; a vanilla member's is
            // that of the first generator (member order, the order it draws in) in its network, so a vanilla machine
            // shares the id of the generator network it draws from first. A vanilla member with no generator is left out.
            for (int i = 0; i < modded; i++) LastSolve.NetworkOf[members[i].Record.id] = members[networkOf[i]].Record.id;
            for (int i = modded; i < members.Count; i++)
            {
                int first = Enumerable.Range(0, modded).FirstOrDefault(j => members[j].Source && powerNet[j] == powerNet[i], -1);
                if (first >= 0) LastSolve.NetworkOf[members[i].Record.id] = members[networkOf[first]].Record.id;
            }
            var grids = world.powerGrids;
            var surplus = new Dictionary<int, float>();
            var charging = new Dictionary<int, float>();
            var stored = new Dictionary<int, double>();
            for (int i = 0; i < modded; i++)
            {
                var m = members[i];
                if (m.Source) continue;
                var s = m.State;
                var d = m.Def;
                int g = gridOf[i];
                float demand = s.Running ? d.Processor.RunningKw : 0f;
                var grid = g > 0 && grids != null && grids.ContainsKey(g) ? grids.get_Item(g) : default;
                if (!surplus.TryGetValue(g, out float left)) left = grid.supplyKw - grid.usedKw;
                if (!charging.TryGetValue(g, out float charge)) charge = ChargingKw(vanilla, g);
                if (!stored.TryGetValue(g, out double storedKwh)) storedKwh = grid.storedKwh;
                var sources = Enumerable.Range(0, modded).Where(j => members[j].Source && networkOf[j] == networkOf[i]).ToList();
                float fuelKw = sources.Sum(j => members[j].AvailableKw);

                var (satisfaction, fromSun, fromFuel, fromBattery) = GridShare.Draw(demand, left, fuelKw, storedKwh);
                float takenBack = GridShare.ChargeTakenKw(fromSun, charge);
                surplus[g] = left - fromSun;
                charging[g] = charge - takenBack;
                var taken = FuelGrid.Split(fromFuel, sources.Select(j => members[j].AvailableKw).ToList());
                for (int k = 0; k < sources.Count; k++)
                {
                    members[sources[k]].AvailableKw -= taken[k];
                    members[sources[k]].SuppliedKw += taken[k];
                    if (taken[k] > 0f) LastSolve.AddFed(members[sources[k]].Record.id, m.Record.id);
                }
                double kwh = (fromBattery + takenBack) * (double)dt / 3600.0;
                double drained = kwh > 0 ? DrainBatteries(vanilla, g, kwh) : 0;
                stored[g] = storedKwh - drained;
                SetPower(s, g, demand, satisfaction);

                string id = $"'{d.Id}' depot {m.Record.id}";
                if (g == 0) { CoreLog.Once("grid-none:" + m.Record.id, $"Power: {id} has no vanilla power node or fuel generator in range; it gets no power", 1); continue; }
                string through = m.Vanilla != null
                    ? $"through depot {m.Vanilla.record.id} ({m.VanillaDistance:F1} m, range {m.VanillaRange} m)"
                    : $"through the fuel generator(s) {string.Join(", ", sources.Select(j => members[j].Record.id))}";
                CoreLog.Once("grid:" + m.Record.id + ":" + g, $"Power: {id} attached to {(g > 0 ? "grid" : "StructureLib grid")} {g} {through}; grid supply {grid.supplyKw:F2} kW, used {grid.usedKw:F2} kW, stored {storedKwh:F3} kWh; fuel {fuelKw:F2} kW", 1);
                if (demand > 0f)
                    CoreLog.Once("draw:" + m.Record.id, $"Power: {id} draws {demand} kW on grid {g}: {fromSun:F3} kW from the sun surplus ({left:F2} kW left; {takenBack:F3} kW of it taken back from {charge:F2} kW charging), {fromFuel:F3} kW from fuel generators ({fuelKw:F2} kW available), {fromBattery:F3} kW from batteries; {drained:F7} kWh out of the panels' charge over {dt:F3} s", 3);
            }

            for (int i = modded; i < members.Count; i++) TopUpVanilla(world, members, powerNet, i);

            for (int i = 0; i < modded; i++)
            {
                var m = members[i];
                if (!m.Source) continue;
                SetSupply(m.State, gridOf[i], m.SuppliedKw, dt);
                AddNode(world, m);
                CoreLog.Once("fuel:" + m.Record.id + ":" + gridOf[i], $"Power: fuel generator '{m.Def.Id}' depot {m.Record.id} on {(gridOf[i] > 0 ? "grid" : "StructureLib grid")} {gridOf[i]}; charge {m.State.ChargeKwh:F3} of {m.Def.Power.BatteryKwh} kWh", 1);
                if (m.SuppliedKw > 0f) CoreLog.Once("supply:" + m.Record.id, $"Power: fuel generator '{m.Def.Id}' depot {m.Record.id} supplies {m.SuppliedKw:F3} kW; charge {m.State.ChargeKwh:F5} kWh", 1);
            }
        }

        // Machines a fuel generator fed in the last solve (the look-at panel's POWERING row).
        internal static int FedCountOf(int depotId) => LastSolve.FedCount(depotId);

        // A vanilla machine in a fuel generator's network: the unmet share of its demand from what the network's generators
        // have left after the modded machines (FuelTopUp), split over them in order into their SuppliedKw, which SetSupply
        // then drains from their ChargeKwh.
        static void TopUpVanilla(MoonWorld world, List<Member> members, int[] network, int i)
        {
            var m = members[i];
            var s = m.State;
            var sources = Enumerable.Range(0, members.Count).Where(j => members[j].Source && network[j] == network[i]).ToList();
            if (sources.Count == 0) return;
            float before = s.Power, demand = s.DemandKw;
            LastSolve.VanillaPowerBefore[m.Record.id] = before;
            float fuelKw = sources.Sum(j => members[j].AvailableKw);
            var (power, used, drawn) = FuelTopUp.Draw(before, demand, fuelKw);
            string id = $"vanilla '{VanillaName(world, m.Record.kind)}' depot {m.Record.id}";
            if (drawn <= 0f)
            {
                CoreLog.Once("vanilla-nofuel:" + m.Record.id, $"Power: {id} is in range of fuel generator(s) {string.Join(", ", sources.Select(j => members[j].Record.id))} but they have no fuel to spare (power {before:F2}, demand {demand} kW)", 1);
                return;
            }
            var taken = FuelGrid.Split(drawn, sources.Select(j => members[j].AvailableKw).ToList());
            Member top = null;
            for (int k = 0; k < sources.Count; k++)
            {
                var g = members[sources[k]];
                g.AvailableKw -= taken[k];
                g.SuppliedKw += taken[k];
                if (taken[k] > 0f) { LastSolve.AddFed(g.Record.id, m.Record.id); top ??= g; }
            }
            s._Power_k__BackingField = power;
            s._UsedKw_k__BackingField = used;
            CoreLog.Once("vanilla-draw:" + m.Record.id + ":" + s.Grid, $"Power: {id} draws {drawn:F3} kW from fuel generator(s) {string.Join(", ", sources.Select((j, k) => $"{members[j].Record.id} ({taken[k]:F3} kW)"))}; {fuelKw:F2} kW of fuel available; power {before:F2} -> {power:F2}, used {used:F2} of {demand} kW; own grid {s.Grid}", 1);
            if (power >= 0.999f && top != null) PoweredBy(s, top.Def.Name);
        }

        static string VanillaName(MoonWorld world, int kind)
        {
            try
            {
                var def = world.BuildDefinition(kind);
                var name = def != null ? def.displayName : null;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch (Exception) { }
            return "kind " + kind;
        }

        // A fully fed vanilla machine's status text: Status reads powerStatus back ("Powered by Coalite furnace" in the
        // Task 3 run, where vanilla's own text was "No power: build solar panels within reach"). Vanilla's solve rewrites
        // powerStatus every solve (that run read vanilla's text again before each of our writes), so the write is
        // reapplied every solve like Power; the text per generator name is built once.
        static readonly Dictionary<string, string> poweredBy = new(StringComparer.Ordinal);
        static void PoweredBy(MoonProductionState s, string generator)
        {
            if (!poweredBy.TryGetValue(generator, out var text)) poweredBy[generator] = text = $"Powered by {generator}";
            s.powerStatus = text;
        }

        static bool IsFuelSource(StructureDefinition d) => d.Power != null && d.Power.SupplyMode == SupplyMode.Fuel && d.Processor != null && d.Processor.ProducesPower;

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

        static void SetSupply(MoonProductionState s, int grid, float suppliedKw, float dt)
        {
            s._Grid_k__BackingField = grid;
            s._Power_k__BackingField = 1f;
            s._DemandKw_k__BackingField = 0f;
            s._UsedKw_k__BackingField = 0f;
            s._SuppliedKw_k__BackingField = suppliedKw;
            s._BatteryKw_k__BackingField = -suppliedKw;
            s._ChargeKwh_k__BackingField = Math.Max(0, s.ChargeKwh - suppliedKw * (double)dt / 3600.0);
        }

        // The fuel generator as a panel node, its footprint a point at its depot position, its own root (parent = its
        // index). Gone at the next solve, which rebuilds the list; a new object each solve, since the game may pool the old.
        static void AddNode(MoonWorld world, Member m)
        {
            var list = world.powerNodes;
            if (list == null) return;
            var node = new MoonWorld.PowerNode();
            node.record = m.Record;
            node.state = m.State;
            node.position = m.At;
            node.up = m.At.normalized;
            node.rotation = Quaternion.identity;
            node.half = Vector2.zero;
            node.panel = true;
            node.spec = SpecBuilder.PanelSpecFor(m.Def);
            node.parent = list.Count;
            list.Add(node);
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
    }
}
