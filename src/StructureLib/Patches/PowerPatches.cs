using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using MoonCleaners;
using UnityEngine;

namespace StructureLib.Patches
{
    // Power. The static role switches answer from a modded structure's Power definition. The supply itself happens in a
    // SolvePower postfix: SolvePower rebuilds its node list on every call from an inlined kind check (a node added before
    // the solve is dropped, IsPowerNode is not asked, and there is no per-node creation hook), so the library runs its own
    // grids after the vanilla solve. See research/STRUCTURES_RECON.md, "Power sources (Task 11)", and
    // docs/superpowers/specs/2026-11-08-power-grid-design.md.
    //
    // MoonWorld.TryPanel(int, out PanelSpec) is deliberately NOT patched: with a prefix on it the game died natively a few
    // seconds after world load (see "Interop hazards found"). A modded kind is none of vanilla's three panel kinds, so its
    // answer is presumably false (not traced); managed code never calls it.
    [HarmonyPatch]
    internal static class PowerPatches
    {
        // A 15-byte native leaf: never falls through to the original (falling through crashed every vanilla card click
        // for kinds 93..97; see LeafTables). Vanilla kinds answer from the table recorded before patching. A modded kind
        // is never a vanilla solar panel: a modded solar array is the library's (its sun is Sun.Factor), not vanilla's.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.IsSolarPanel)), HarmonyPrefix]
        static bool IsSolarPanel(int kind, ref bool __result)
        {
            var d = Remap.Live(kind);
            __result = d != null
                ? LeafTables.Modded(LeafTables.IsSolarPanel, kind, false, d.Id)
                : LeafTables.Vanilla(LeafTables.IsSolarPanel, kind);
            return false;
        }

        // A modded power structure reaches nothing by vanilla's rules: it reaches through poles only (SupplyRadius, in the
        // grid below). Any other modded structure answers as its donor.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.PowerRange)), HarmonyPrefix]
        static bool PowerRange(ref int kind, ref float __result)
        {
            var d = Remap.Live(kind);
            if (d?.Power == null) { Remap.ToBase(ref kind, "PowerRange"); return true; }
            __result = 0f;
            return false;
        }

        enum Role { Consumer, Fuel, Solar, Storage, Vanilla }

        // A grid member: a modded machine, fuel source, solar array or bank, or a vanilla machine node. Its site and
        // attachment are fixed until the depot set changes; the per-solve fields are rewritten every solve.
        sealed class Member
        {
            internal int Id, Kind;
            internal Role Role;
            internal StructureDefinition Def;
            internal Vector3 At;
            // The footprint for the game's FootprintDistance: rotation and half extents (local X, local Z).
            internal Quaternion Rotation;
            internal Vector2 Half;
            internal int Grid, Pole;
            internal float PoleDistance;
            // A modded machine: the vanilla node whose own PowerRange reaches it (today's sun-surplus path), or -1.
            internal int VanillaNode = -1;
            internal float VanillaDistance, VanillaRange;
            // Per solve.
            internal MoonProductionState State;
            internal float Demand, VanillaPower, Sun;
            internal int VanillaGrid;
        }

        sealed class Pole
        {
            internal int Id, Kind, Grid;
            internal StructureDefinition Def;
        }

        // The graph, rebuilt when the depot set changes (GridState.Version). Members are in depot-id order.
        static readonly List<Member> members = new();
        static readonly List<Pole> poles = new();
        static readonly List<int> gridOrder = new();                        // grid ids, lowest pole id first
        static readonly Dictionary<int, List<Member>> byGrid = new();
        static readonly Dictionary<int, List<Pole>> polesByGrid = new();
        static readonly Dictionary<int, Vector2> halfOfKind = new();
        static IntPtr builtFor, builtProduction;
        static int builtGeneration = -1;
        static int builtCount = -1;
        static long builtHash;
        static string noPower = "No power: build a power pole";

        // Per solve, reused.
        static readonly Dictionary<int, MoonWorld.PowerNode> vanillaNodes = new();
        static readonly List<MoonWorld.PowerNode> vanillaGridNodes = new();
        static readonly List<SourceIn> sources = new();
        static readonly List<Member> sourceOf = new();
        static readonly List<StorageIn> storage = new();
        static readonly List<Member> storageOf = new();
        static readonly List<ConsumerIn> consumers = new();
        static readonly List<Member> consumerOf = new();
        static readonly Dictionary<int, float> surplus = new(), charging = new();
        static readonly Dictionary<int, double> stored = new();
        static readonly Dictionary<int, string> shortText = new();

        /// <summary>Throws caught in the postfix this session, and solves run (the grid-pole-removed self-test reads them).</summary>
        internal static int SolveThrows, Solves;

#if DEVBUILD
        /// <summary>The vanilla node whose own range reaches a modded machine (its sun-surplus path), or -1 (self-tests).</summary>
        internal static int VanillaNodeOf(int depotId)
        {
            foreach (var m in members) if (m.Id == depotId) return m.VanillaNode;
            return -1;
        }
#endif

        // After the vanilla solve (every peer runs it: research/POWER_UX_RECON.md, F; every input below is replicated
        // state, the depots, the clock, Running and the stores, or integrated the same way on every peer, a bank's charge):
        // 1. When the depot set changed (count, a hash of the ids, or a new world): the poles (modded Relay depots) are
        //    linked into grids (PoleGraph: centre distance <= the larger LinkRange; grid id -(lowest pole id)), and every
        //    modded machine, fuel source, solar array and bank, and every vanilla machine node, attaches to the nearest
        //    pole whose SupplyRadius reaches its footprint (PoleGraph.AttachSite: the depot's x/z, yaw and its build
        //    definition's half footprint). A modded machine also finds the nearest vanilla node whose own PowerRange
        //    reaches it (FootprintDistance), as before the grid. Relay, Storage and Solar depots get a production state if
        //    they have none (a save does not keep theirs).
        // 2. A modded machine first takes from that vanilla node's grid what GridShare gives it: the sun surplus
        //    (supplyKw - usedKw, the share vanilla banked while charging taken back out of the charging panels), then the
        //    vanilla batteries; shared between modded machines in depot-id order. That satisfaction is its VanillaPower.
        //    A vanilla machine's VanillaPower is its Power as vanilla's solve left it.
        // 3. Per grid, in depot-id order: sources (a solar array: PeakKw x Sun.Factor; a fuel source: PeakKw while its
        //    batch burns, i.e. Running, else nothing, passed as ChargeKwh +inf / 0), banks (ChargeKwh, BatteryKwh, RateKw)
        //    and consumers (a modded machine's RunningKw while Running or ready to start a cycle, ProductionPatches.ReadyToStart;
        //    a vanilla machine's DemandKw) go to GridSolve.Solve:
        //    solar first, then banks, then fuel, and one satisfaction for every consumer for the share vanilla left unmet.
        // 4. Writes. A modded machine: Grid, Power (never below its VanillaPower), DemandKw, UsedKw, SuppliedKw 0
        //    (SetPower) and powerStatus "Powered by the grid" or "Grid short: <pct> %". A vanilla machine: Power and
        //    UsedKw only, when raised (vanilla resets them every solve and StepProduction reads them after this postfix,
        //    POWER_UX_RECON.md B), and "Powered by the grid" when fully fed. A fuel source: Grid, Power 1, SuppliedKw
        //    (SetSupply). A solar array: Grid, Power 1, SuppliedKw, SunFactor. A bank: Grid, ChargeKwh + delta (clamped
        //    to 0..BatteryKwh), BatteryKw (positive while charging, as vanilla's). A pole: Grid, Power = satisfaction,
        //    and the grid's totals: SuppliedKw (solar + fuel + discharge), DemandKw, UsedKw and ChargeKwh (stored).
        //    GridState gets the same totals, plus whether the grid still wanted fuel after solar and storage, which gates
        //    a fuel source's next burn (ProductionPatches.CanStartCycle, host side).
        // 5. Unreached: a modded machine keeps what the vanilla node gave it (on that vanilla grid) or reads Grid 0, Power 0
        //    and "No power: build a power pole within <r> m" (r: the largest SupplyRadius of the registered Relays); a
        //    source or bank reads Grid 0 and supplies nothing; a vanilla machine is left as vanilla set it.
        // 6. Every fuel source is appended to powerNodes as a panel node until the next solve rebuilds the list.
        // The vanilla grid's totals are a struct in a dictionary and cannot be written back from managed code, so they do
        // not include modded demand or supply.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.SolvePower)), HarmonyPostfix]
        static void SolvePower(MoonWorld __instance, float dt)
        {
            try { AttachModded(__instance, dt); }
            catch (Exception e)
            {
                SolveThrows++;
                if (SolveThrows <= 3) CoreLog.Error($"[solvepower] The power grid solve threw ({SolveThrows}{(SolveThrows == 3 ? ", further throws muted" : "")}): {e}");
                // A half-built graph is not kept: rebuild on the next solve.
                builtCount = -1;
            }
        }

        static void AttachModded(MoonWorld world, float dt)
        {
            var production = world.production;
            var depots = world.depots;
            if (production == null || depots == null || !MoonStructures.Frozen || MoonStructures.Disabled) { GridState.Clear(); return; }
            Solves++;

            var nodes = world.powerNodes;
            vanillaNodes.Clear();
            vanillaGridNodes.Clear();
            for (int i = 0; nodes != null && i < nodes.Count; i++)
            {
                var n = nodes.get_Item(i);
                if (n?.record == null || Remap.Live(n.record.kind) != null) continue;
                vanillaNodes[n.record.id] = n;
                if (n.state != null && n.state.Grid != 0) vanillaGridNodes.Add(n);
            }

            if (DepotSetChanged(world, depots, production)) Rebuild(world, depots, production);
            if (members.Count == 0 && poles.Count == 0) return;

            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                m.State = m.Role == Role.Vanilla
                    ? (vanillaNodes.TryGetValue(m.Id, out var vn) ? vn.state : null)
                    : production.ContainsKey(m.Id) ? production.get_Item(m.Id) : null;
            }

            ShareVanilla(world, dt);
            GridState.VanillaBefore.Clear();
            foreach (int g in gridOrder) SolveGrid(world, g, dt);

            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.Grid == 0 && m.State != null) WriteUnreached(m);
            }

            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.State == null) continue;
                // A bank's charge, for the next save (Sidecar keeps it: the vanilla save cannot).
                if (m.Role == Role.Storage) { Sidecar.NoteBankCharge(m.Id, m.State.ChargeKwh); continue; }
                if (m.Role != Role.Fuel) continue;
                AddNode(world, m);
                if (m.State.SuppliedKw > 0f) CoreLog.Once("supply:" + m.Id, $"Power: fuel source '{m.Def.Id}' depot {m.Id} supplies {m.State.SuppliedKw:F3} kW to grid {m.Grid}", 1);
            }
        }

        // ---- 1. the graph ----

        // The key also carries the load: the production dictionary's pointer and Sidecar's load generation, so a save loaded
        // into the same MoonWorld with the same depot ids still rebuilds (EnsureState and RestoreCharge run only here).
        static bool DepotSetChanged(MoonWorld world, Il2CppSystem.Collections.Generic.Dictionary<int, MoonDepotSnapshot> depots,
            Il2CppSystem.Collections.Generic.Dictionary<int, MoonProductionState> production)
        {
            int count = 0;
            long hash = 0;
            foreach (var kv in depots)
            {
                count++;
                // Order-independent: a dictionary's enumeration order may change after a removal.
                unchecked { hash += (long)((ulong)kv.Key * 0x9E3779B97F4A7C15UL >> 16); }
            }
            int generation = Sidecar.LoadGeneration;
            if (world.Pointer == builtFor && production.Pointer == builtProduction && generation == builtGeneration && count == builtCount && hash == builtHash) return false;
            builtProduction = production.Pointer;
            builtGeneration = generation;
            builtFor = world.Pointer;
            builtCount = count;
            builtHash = hash;
            return true;
        }

        static void Rebuild(MoonWorld world, Il2CppSystem.Collections.Generic.Dictionary<int, MoonDepotSnapshot> depots,
            Il2CppSystem.Collections.Generic.Dictionary<int, MoonProductionState> production)
        {
            members.Clear();
            sites.Clear();
            poles.Clear();
            gridOrder.Clear();
            byGrid.Clear();
            polesByGrid.Clear();
            GridState.BeginGraph();
            noPower = NoPowerText();

            var ids = new List<int>();
            foreach (var kv in depots) ids.Add(kv.Key);
            ids.Sort();
            var polesIn = new List<StructureLib.Pole>();
            foreach (int id in ids)
            {
                var rec = depots.get_Item(id);
                var d = rec == null ? null : Remap.Live(rec.kind);
                if (d == null) continue;
                var at = new Vector3(rec.x, rec.y, rec.z);
                if (d.Power != null && d.Power.Role == PowerRole.Relay)
                {
                    poles.Add(new Pole { Id = id, Kind = rec.kind, Def = d });
                    polesIn.Add(new StructureLib.Pole(id, at.x, at.y, at.z, d.Power.LinkRange, d.Power.SupplyRadius));
                    EnsureState(production, id, rec.kind, d);
                    continue;
                }
                Role role;
                if (d.Power == null) { if (d.Processor == null || d.Processor.ProducesPower) continue; role = Role.Consumer; }
                else if (d.Power.Role == PowerRole.Storage) role = Role.Storage;
                else if (d.Power.SupplyMode == SupplyMode.Solar) role = Role.Solar;
                else if (d.Processor != null && d.Processor.ProducesPower) role = Role.Fuel;
                else continue;
                if (role == Role.Storage || role == Role.Solar) EnsureState(production, id, rec.kind, d);
                if (role == Role.Storage) RestoreCharge(production, id, d);
                var half = HalfOf(world, rec.kind);
                var m = new Member { Id = id, Kind = rec.kind, Role = role, Def = d, At = at, Rotation = RotationOf(rec, at), Half = half };
                members.Add(m);
                m.Grid = 0;
                m.Pole = 0;
                sites.Add(new Site(id, at.x, at.y, at.z, half.x, half.y, YawOf(rec)));
            }
            // Vanilla machine nodes (not panels): consumers of whatever grid reaches them.
            foreach (var kv in vanillaNodes)
            {
                var n = kv.Value;
                if (n.panel) continue;
                var m = new Member { Id = kv.Key, Kind = n.record.kind, Role = Role.Vanilla, At = n.position, Rotation = n.rotation, Half = n.half };
                members.Add(m);
                sites.Add(new Site(kv.Key, n.position.x, n.position.y, n.position.z, n.half.x, n.half.y, YawOf(n.rotation)));
            }
            // Depot-id order for everything that follows.
            var order = new List<int>(members.Count);
            for (int i = 0; i < members.Count; i++) order.Add(i);
            order.Sort((a, b) => members[a].Id.CompareTo(members[b].Id));
            var sorted = new List<Member>(members.Count);
            var sortedSites = new List<Site>(members.Count);
            foreach (int i in order) { sorted.Add(members[i]); sortedSites.Add(sites[i]); }
            members.Clear();
            members.AddRange(sorted);
            sites.Clear();

            var graph = PoleGraph.Build(polesIn);
            foreach (var p in poles)
            {
                p.Grid = graph.GridOf(p.Id);
                if (!polesByGrid.TryGetValue(p.Grid, out var list)) { polesByGrid[p.Grid] = list = new List<Pole>(); gridOrder.Add(p.Grid); byGrid[p.Grid] = new List<Member>(); }
                list.Add(p);
                GridState.AddPole(p.Grid, p.Id);
            }
            foreach (var (a, b) in graph.Links) GridState.AddLink(a, b);

            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                m.Grid = Attach(graph, m, sortedSites[i]);
                if (m.Grid != 0)
                {
                    byGrid[m.Grid].Add(m);
                    GridState.AddMember(m.Grid, m.Pole, m.Id);
                }
                if (m.Role == Role.Consumer) FindVanillaNode(m);
                string what = m.Role == Role.Vanilla ? $"vanilla machine depot {m.Id} (kind {m.Kind})" : $"'{m.Def.Id}' depot {m.Id} ({m.Role})";
                CoreLog.Once("grid-attach:" + m.Id + ":" + m.Grid, m.Grid != 0
                    ? $"Power: {what} attached to grid {m.Grid} through pole {m.Pole} ({m.PoleDistance:F1} m to its footprint)"
                    : $"Power: {what} is in no pole's reach" + (m.VanillaNode >= 0 ? $"; vanilla node {m.VanillaNode} reaches it ({m.VanillaDistance:F1} m, range {m.VanillaRange} m)" : ""), 1);
            }
            CoreLog.Debug($"Power grid rebuilt (version {GridState.Version}): {poles.Count} pole(s) in {graph.GridCount} grid(s), {graph.Links.Count} link(s), {members.Count} member(s), {GridState.Reach.Count} reached");
        }

        static readonly List<Site> sites = new();

        // A modded machine's nearest vanilla node whose own PowerRange reaches it (FootprintDistance to the node's
        // footprint), as before the grid; its grid is read each solve.
        static void FindVanillaNode(Member m)
        {
            m.VanillaNode = -1;
            m.VanillaDistance = float.MaxValue;
            foreach (var kv in vanillaNodes)
            {
                var n = kv.Value;
                float range = MoonWorld.PowerRange(n.record.kind);
                if (range <= 0f) continue;
                float distance = MoonWorld.FootprintDistance(m.At, n.position, n.rotation, n.half);
                if (distance <= range && distance < m.VanillaDistance) { m.VanillaNode = kv.Key; m.VanillaDistance = distance; m.VanillaRange = range; }
            }
        }

        // A save keeps no production state for a structure without a Processor (Sidecar strips modded states and records
        // only processors), and the game creates one only right after a build (IsProcessorId), so a pole, array or bank
        // restored from a save gets its state here. NewProduction asks Processor(kind) (null for these: stores stay null).
        static void EnsureState(Il2CppSystem.Collections.Generic.Dictionary<int, MoonProductionState> production, int id, int kind, StructureDefinition d)
        {
            if (production.ContainsKey(id)) return;
            try
            {
                var s = MoonWorld.NewProduction(id, kind);
                if (s == null) { CoreLog.Once("newprod-null:" + d.Id, $"Power: NewProduction({id}, {kind}) for '{d.Id}' returned null; it has no state", 1); return; }
                s._Power_k__BackingField = 0f;
                production.set_Item(id, s);
                CoreLog.Once("newprod:" + id, $"Power: gave '{d.Id}' depot {id} a production state (none was restored)", 1);
            }
            catch (Exception e) { CoreLog.Once("newprod:" + d.Id, $"Power: creating the production state of '{d.Id}' depot {id} threw: {e}", 2); }
        }

        // A bank restored from a save: the charge the sidecar kept (Sidecar.TakeLoadedCharge), clamped to its capacity.
        static void RestoreCharge(Il2CppSystem.Collections.Generic.Dictionary<int, MoonProductionState> production, int id, StructureDefinition d)
        {
            if (!production.ContainsKey(id) || !Sidecar.TakeLoadedCharge(id, out double kwh)) return;
            var s = production.get_Item(id);
            if (s == null) return;
            s._ChargeKwh_k__BackingField = Math.Min(Math.Max(0, kwh), d.Power.BatteryKwh);
            CoreLog.Once("bank-charge:" + id, $"Power: bank '{d.Id}' depot {id} gets back its saved charge, {s.ChargeKwh:F4} kWh", 1);
        }

        // The build definition's half footprint (local X, local Z), per kind for the session (registry frozen).
        static Vector2 HalfOf(MoonWorld world, int kind)
        {
            if (halfOfKind.TryGetValue(kind, out var half)) return half;
            try
            {
                var def = world.BuildDefinition(kind);
                half = def != null ? def.halfFootprint : Vector2.zero;
            }
            catch (Exception e) { CoreLog.Once("half:" + kind, $"Power: reading the footprint of kind {kind} threw; using a point: {e.Message}", 2); half = Vector2.zero; }
            halfOfKind[kind] = half;
            return half;
        }

        // A member attaches by the game's own MoonWorld.FootprintDistance (pole centre to the member's footprint on the
        // sphere's tangent plane), measured only for poles whose centre is within SupplyRadius plus the footprint's half
        // diagonal (the others cannot reach it). If the game call throws, PoleGraph's pure world-X/Z rectangle distance
        // is the fallback (logged once): right near the moon's +Y pole only.
        static readonly List<float> distances = new();
        static bool footprintFailed;

        static int Attach(PoleGraph graph, Member m, Site site)
        {
            if (!footprintFailed)
            {
                try
                {
                    var all = graph.Poles;
                    distances.Clear();
                    float diagonal = m.Half.magnitude;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var p = all[i];
                        var at = new Vector3(p.X, p.Y, p.Z);
                        distances.Add(Vector3.Distance(at, m.At) <= p.SupplyRadius + diagonal + 0.01f
                            ? MoonWorld.FootprintDistance(at, m.At, m.Rotation, m.Half)
                            : float.PositiveInfinity);
                    }
                    return graph.AttachSite(distances, out m.Pole, out m.PoleDistance);
                }
                catch (Exception e)
                {
                    footprintFailed = true;
                    SolveThrows++;
                    CoreLog.Error($"[solvepower] MoonWorld.FootprintDistance threw; poles attach by the pure X/Z footprint distance from now on (wrong away from the moon's +Y pole): {e}");
                }
            }
            return graph.AttachSite(site, out m.Pole, out m.PoleDistance);
        }

        // A modded depot's world rotation. A free pose (placedPose) carries it. Otherwise it is built from the surface up
        // and the record's yaw in degrees: the tangent frame whose +X is world +X projected on the tangent plane (the
        // structures' +X at yaw 0 near slot-2's walker, DevBelts AutoLine), turned by yaw about the up. (Not checked
        // against the game's own StructurePose away from slot-2's base; StructurePose has struct outs, which interop
        // must not call.)
        static Quaternion RotationOf(MoonDepotSnapshot rec, Vector3 at)
        {
            if (rec.placedPose) return new Quaternion(rec.qx, rec.qy, rec.qz, rec.qw);
            var up = at.sqrMagnitude > 1e-6f ? at.normalized : Vector3.up;
            var x = Vector3.ProjectOnPlane(Vector3.right, up);
            if (x.sqrMagnitude < 1e-6f) x = Vector3.ProjectOnPlane(Vector3.forward, up);
            var forward = Vector3.Cross(x.normalized, up); // LookRotation's right = cross(up, forward) = x
            return Quaternion.LookRotation(forward, up) * Quaternion.Euler(0f, rec.yaw, 0f);
        }

        // Site.Yaw is Unity's rotation about +Y in radians (local X = (cos, -sin) in (X, Z)). A free pose (placedPose)
        // carries the rotation itself; otherwise the record's yaw is TryBuildDepot's, in degrees ([Dev] AutoBuild '@90'
        // built a box splitter turned a quarter, SPLITTERS_RECON.md run3).
        static float YawOf(MoonDepotSnapshot rec) =>
            rec.placedPose ? YawOf(new Quaternion(rec.qx, rec.qy, rec.qz, rec.qw)) : rec.yaw * Mathf.Deg2Rad;

        static float YawOf(Quaternion q)
        {
            var x = q * Vector3.right;
            return x.x * x.x + x.z * x.z < 1e-8f ? 0f : Mathf.Atan2(-x.z, x.x);
        }

        static string NoPowerText()
        {
            float radius = 0f;
            foreach (var d in MoonStructures.All)
                if (d.Power != null && d.Power.Role == PowerRole.Relay && d.Power.SupplyRadius > radius) radius = d.Power.SupplyRadius;
            return radius > 0f
                ? string.Format(CultureInfo.InvariantCulture, "No power: build a power pole within {0:0.#} m", radius)
                : "No power: build a power pole";
        }

        // ---- 2. a modded machine's share of a vanilla grid ----

        static void ShareVanilla(MoonWorld world, float dt)
        {
            surplus.Clear();
            charging.Clear();
            stored.Clear();
            var grids = world.powerGrids;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.Role != Role.Consumer) continue;
                m.Demand = 0f;
                m.VanillaPower = 0f;
                m.VanillaGrid = 0;
                var s = m.State;
                if (s == null) continue;
                // While Running, and while idle with its next cycle ready to start (vanilla's chamber asks for power then
                // too); a machine whose start waits on power would otherwise never ask a fuel-only grid for any.
                m.Demand = s.Running || ProductionPatches.ReadyToStart(m.Def, s) ? m.Def.Processor.RunningKw : 0f;
                if (m.VanillaNode < 0 || !vanillaNodes.TryGetValue(m.VanillaNode, out var n) || n.state == null) continue;
                int g = n.state.Grid;
                if (g <= 0 || grids == null || !grids.ContainsKey(g)) continue;
                var grid = grids.get_Item(g);
                if (!surplus.TryGetValue(g, out float left)) left = grid.supplyKw - grid.usedKw;
                if (!charging.TryGetValue(g, out float charge)) charge = ChargingKw(g);
                if (!stored.TryGetValue(g, out double storedKwh)) storedKwh = grid.storedKwh;
                var (satisfaction, fromSun, _, fromBattery) = GridShare.Draw(m.Demand, left, 0f, storedKwh);
                float takenBack = GridShare.ChargeTakenKw(fromSun, charge);
                surplus[g] = left - fromSun;
                charging[g] = charge - takenBack;
                double kwh = (fromBattery + takenBack) * (double)dt / 3600.0;
                double drained = kwh > 0 ? DrainBatteries(g, kwh) : 0;
                stored[g] = storedKwh - drained;
                m.VanillaPower = satisfaction;
                m.VanillaGrid = g;
                if (m.Demand > 0f)
                    CoreLog.Once("draw:" + m.Id, $"Power: '{m.Def.Id}' depot {m.Id} draws {m.Demand} kW: {fromSun:F3} kW from vanilla grid {g}'s sun surplus ({left:F2} kW left; {takenBack:F3} kW of it taken back from {charge:F2} kW charging), {fromBattery:F3} kW from its batteries ({drained:F7} kWh over {dt:F3} s)", 3);
            }
        }

        // ---- 3 and 4. one pole grid ----

        static void SolveGrid(MoonWorld world, int gridId, float dt)
        {
            sources.Clear(); sourceOf.Clear();
            storage.Clear(); storageOf.Clear();
            consumers.Clear(); consumerOf.Clear();
            float peak = 0f, capacity = 0f;
            foreach (var m in byGrid[gridId])
            {
                var s = m.State;
                if (s == null) continue;
                switch (m.Role)
                {
                    case Role.Fuel:
                        sources.Add(new SourceIn(m.Id, false, m.Def.Power.PeakKw, 0f, s.Running ? double.PositiveInfinity : 0));
                        sourceOf.Add(m);
                        peak += m.Def.Power.PeakKw;
                        break;
                    case Role.Solar:
                        m.Sun = Sun.Factor(world, m.At, m.At.normalized);
                        sources.Add(new SourceIn(m.Id, true, m.Def.Power.PeakKw, m.Sun, 0));
                        sourceOf.Add(m);
                        peak += m.Def.Power.PeakKw;
                        break;
                    case Role.Storage:
                        storage.Add(new StorageIn(m.Id, s.ChargeKwh, m.Def.Power.BatteryKwh, m.Def.Power.RateKw));
                        storageOf.Add(m);
                        capacity += m.Def.Power.BatteryKwh;
                        break;
                    case Role.Consumer:
                        consumers.Add(new ConsumerIn(m.Id, m.Demand, m.VanillaPower));
                        consumerOf.Add(m);
                        break;
                    case Role.Vanilla:
                        if (s.DemandKw <= 0f) break;
                        m.VanillaPower = s.Power;
                        consumers.Add(new ConsumerIn(m.Id, s.DemandKw, s.Power));
                        consumerOf.Add(m);
                        break;
                }
            }

            var r = GridSolve.Solve(sources, storage, consumers, dt, MoonWorld.StallPower);

            int fed = 0;
            float used = 0f;
            for (int i = 0; i < consumerOf.Count; i++)
            {
                var m = consumerOf[i];
                var s = m.State;
                float power = Math.Max(r.ConsumerPower[i], m.VanillaPower);
                if (consumers[i].DemandKw > 0f && power > m.VanillaPower + 1e-4f) fed++;
                if (m.Role == Role.Consumer)
                {
                    SetPower(s, gridId, m.Demand, power);
                    used += s.UsedKw;
                    s.powerStatus = power >= 0.999f ? PoweredByText("the grid") : ShortText(power);
                    continue;
                }
                // A vanilla machine: only ever raised.
                float before = m.VanillaPower, demand = consumers[i].DemandKw;
                if (power > before + 1e-6f)
                {
                    GridState.VanillaBefore[m.Id] = before;
                    s._Power_k__BackingField = power;
                    s._UsedKw_k__BackingField = demand * power;
                    if (power >= 0.999f) s.powerStatus = PoweredByText("the grid");
                    CoreLog.Once("vanilla-draw:" + m.Id + ":" + gridId, $"Power: vanilla machine depot {m.Id} (kind {m.Kind}) on grid {gridId}: power {before:F2} -> {power:F2} of {demand} kW; own grid {s.Grid}", 1);
                }
                used += s.UsedKw;
            }

            for (int i = 0; i < sourceOf.Count; i++)
            {
                var m = sourceOf[i];
                if (m.Role == Role.Fuel) SetSupply(m.State, gridId, r.SourceKw[i]);
                else
                {
                    var s = m.State;
                    s._Grid_k__BackingField = gridId;
                    s._Power_k__BackingField = 1f;
                    s._DemandKw_k__BackingField = 0f;
                    s._UsedKw_k__BackingField = 0f;
                    s._SuppliedKw_k__BackingField = r.SourceKw[i];
                    s._SunFactor_k__BackingField = m.Sun;
                }
            }

            double storedKwh = 0;
            for (int i = 0; i < storageOf.Count; i++)
            {
                var m = storageOf[i];
                var s = m.State;
                double delta = r.StorageDeltaKwh[i];
                double charge = Math.Min(Math.Max(0, s.ChargeKwh + delta), m.Def.Power.BatteryKwh);
                s._Grid_k__BackingField = gridId;
                s._ChargeKwh_k__BackingField = charge;
                s._BatteryKw_k__BackingField = dt > 0f ? (float)(delta * 3600.0 / dt) : 0f;
                s._Power_k__BackingField = 1f;
                storedKwh += charge;
            }

            float supplied = r.SolarKw + r.FuelKw + Math.Max(0f, r.StorageKw);
            if (GridState.Grids.TryGetValue(gridId, out var grid))
            {
                grid.SolarKw = r.SolarKw;
                grid.FuelKw = r.FuelKw;
                grid.StorageKw = r.StorageKw;
                grid.PeakKw = peak;
                grid.DemandKw = r.DemandKw;
                grid.UsedKw = used;
                grid.Satisfaction = r.Satisfaction;
                grid.StoredKwh = storedKwh;
                grid.CapacityKwh = capacity;
                grid.WantsFuel = r.UnmetDemandKw + r.FuelKw > 1e-4f;
                grid.Fed = fed;
            }
            if (polesByGrid.TryGetValue(gridId, out var gridPoles))
                foreach (var p in gridPoles)
                {
                    var s = StateOf(world, p.Id);
                    if (s == null) continue;
                    s._Grid_k__BackingField = gridId;
                    s._Power_k__BackingField = r.Satisfaction;
                    s._SuppliedKw_k__BackingField = supplied;
                    s._DemandKw_k__BackingField = r.DemandKw;
                    s._UsedKw_k__BackingField = used;
                    s._ChargeKwh_k__BackingField = storedKwh;
                    s._BatteryKw_k__BackingField = 0f;
                }
            CoreLog.Once("grid-solve:" + gridId, $"Power: grid {gridId} ({(gridPoles?.Count ?? 0)} pole(s)): {sources.Count} source(s), {storage.Count} bank(s), {consumers.Count} consumer(s); demand {r.DemandKw:F2} kW, solar {r.SolarKw:F2}, storage {r.StorageKw:F2}, fuel {r.FuelKw:F2} kW, satisfaction {r.Satisfaction:F2}", 3);
        }

        static MoonProductionState StateOf(MoonWorld world, int id)
        {
            var production = world.production;
            return production != null && production.ContainsKey(id) ? production.get_Item(id) : null;
        }

        // ---- 5. unreached ----

        static void WriteUnreached(Member m)
        {
            var s = m.State;
            switch (m.Role)
            {
                case Role.Consumer:
                    if (m.VanillaGrid > 0) SetPower(s, m.VanillaGrid, m.Demand, m.VanillaPower);
                    else SetPower(s, 0, m.Demand, 0f);
                    if (m.VanillaPower <= 0f) s.powerStatus = noPower;
                    if (m.VanillaGrid <= 0) CoreLog.Once("grid-none:" + m.Id, $"Power: '{m.Def.Id}' depot {m.Id} has no pole or vanilla power node in reach; it gets no power", 1);
                    break;
                case Role.Fuel:
                    SetSupply(s, 0, 0f);
                    break;
                case Role.Solar:
                    s._Grid_k__BackingField = 0;
                    s._SuppliedKw_k__BackingField = 0f;
                    s._SunFactor_k__BackingField = 0f;
                    break;
                case Role.Storage:
                    s._Grid_k__BackingField = 0;
                    s._BatteryKw_k__BackingField = 0f;
                    break;
            }
        }

        // ---- status texts ----

        // A fully fed machine's status text: Status reads powerStatus back ("Powered by Coalite furnace" in the Task 3 run,
        // where vanilla's own text was "No power: build solar panels within reach"). Vanilla's solve rewrites powerStatus
        // every solve, so the write is reapplied every solve like Power; each text is built once.
        static readonly Dictionary<string, string> poweredBy = new(StringComparer.Ordinal);
        static string PoweredByText(string what)
        {
            if (!poweredBy.TryGetValue(what, out var text)) poweredBy[what] = text = $"Powered by {what}";
            return text;
        }

        static string ShortText(float satisfaction)
        {
            int pct = (int)Math.Floor(Math.Clamp(satisfaction, 0f, 1f) * 100f);
            if (!shortText.TryGetValue(pct, out var text)) shortText[pct] = text = string.Format(CultureInfo.InvariantCulture, "Grid short: {0} %", pct);
            return text;
        }

        // ---- state writes ----

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

        // A fuel source's ChargeKwh is not the library's any more (no kWh buffer in the solve), so it is left alone.
        static void SetSupply(MoonProductionState s, int grid, float suppliedKw)
        {
            s._Grid_k__BackingField = grid;
            s._Power_k__BackingField = 1f;
            s._DemandKw_k__BackingField = 0f;
            s._UsedKw_k__BackingField = 0f;
            s._SuppliedKw_k__BackingField = suppliedKw;
            s._BatteryKw_k__BackingField = 0f;
        }

        // The fuel source as a panel node, its footprint a point at its depot position, its own root (parent = its
        // index). Gone at the next solve, which rebuilds the list; a new object each solve, since the game may pool the old.
        static void AddNode(MoonWorld world, Member m)
        {
            var list = world.powerNodes;
            if (list == null) return;
            var depots = world.depots;
            var rec = depots != null && depots.ContainsKey(m.Id) ? depots.get_Item(m.Id) : null;
            if (rec == null) return;
            var node = new MoonWorld.PowerNode();
            node.record = rec;
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

        // kW the vanilla grid's panels are putting into their batteries this solve (their positive BatteryKw).
        static float ChargingKw(int grid)
        {
            float kw = 0f;
            foreach (var n in vanillaGridNodes)
                if (n.panel && n.state.Grid == grid && n.state.BatteryKw > 0f) kw += n.state.BatteryKw;
            return kw;
        }

        // Takes kWh out of the charge of the vanilla grid's panels, in node order; returns how much it took.
        static double DrainBatteries(int grid, double kwh)
        {
            double taken = 0;
            foreach (var n in vanillaGridNodes)
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
