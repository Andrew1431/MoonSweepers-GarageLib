#if DEVBUILD
using System;
using System.Collections.Generic;
using System.Linq;
using MoonCleaners;
using StructureLib.Patches;
using UnityEngine;

namespace StructureLib
{
    // [Dev] AutoBuild entry 'power-grid': the grid self-tests' own layout. TryBuildDepot needs the walker within the
    // build reach of each site and the layout spans 36 m, so the walker is teleported beside each site in turn (1 s
    // apart), then back. The line axis is the structures' +X at yaw 0 (AutoLine's: world +X projected on the tangent
    // plane); "across" runs away from the walker on its 270 deg side (slot-2 keeps vanilla structures ahead and at 90).
    // Sites (along, across) in metres, every structure at yaw 0:
    //   pole1 (-18, 14)  burner (-18, 9)  cell (-18, 19)      pole2 (0, 14)      pole3 (18, 14)  grinder (18, 8)  chamber 96 (18, 21)
    //   lone-burner (-18, 46)  lone-grinder (-12, 46): no pole within 10 m (pole1 is 32 m away).
    // Poles 18 m apart link (LinkRange 20); pole1 and pole3 (36 m) do not, so the three form one grid only as a chain.
    // Then it feeds the burners 10 kg of grit, the grinders and the chamber 10 kg of dust. The self-test starts once the
    // layout is done (DevTools.Tick waits on Busy). Config for a grid run: AutoBuild = power-grid (alone: the walker
    // moves), SelfTest = true, ForceArcs = true for arc-drawn; no AutoBelt or AutoFeed.
    internal static class GridLayout
    {
        internal const string Token = "power-grid";
        internal const string PoleId = "ryanhartwig.example-pole", CellId = "ryanhartwig.example-cell",
            BurnerId = "ryanhartwig.example-burner", GrinderId = "ryanhartwig.example-grinder", GritId = "ryanhartwig.example-grit";

        static readonly (string Key, string Who, float Along, float Across)[] Plan =
        {
            ("pole1", PoleId, -18f, 14f), ("burner", BurnerId, -18f, 9f), ("cell", CellId, -18f, 19f),
            ("pole2", PoleId, 0f, 14f), ("pole3", PoleId, 18f, 14f),
            ("grinder", GrinderId, 18f, 8f), ("chamber", MoonWorld.PressureChamberKind.ToString(), 18f, 21f),
            ("lone-burner", BurnerId, -18f, 46f), ("lone-grinder", GrinderId, -12f, 46f),
        };
        const float StandBack = 3.5f, StepSeconds = 1f, GiveUpSeconds = 90f;

        static readonly Dictionary<string, (int depot, string reason)> built = new();
        static bool started, done, teleported;
        static int index;
        static float startedAt, nextAt;
        static Vector3 home, axis, side;

        internal static bool Named => DevTools.AutoBuildNames(Token);
        internal static bool Done => done;
        /// <summary>The layout is being built (the self-test waits; at most GiveUpSeconds).</summary>
        internal static bool Busy => started && !done && Time.unscaledTime < startedAt + GiveUpSeconds;
        /// <summary>Unscaled time the layout's machines were fed; -1 before.</summary>
        internal static float FedAt { get; private set; } = -1f;

        /// <summary>The [Dev] AutoBuild list without the 'power-grid' entry (what AutoBuilder builds itself).</summary>
        internal static string Without(string spec) =>
            string.Join(",", (spec ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0 && s != Token));

        /// <summary>The depot built for a plan key, or -1.</summary>
        internal static int Depot(string key) => built.TryGetValue(key, out var b) ? b.depot : -1;

        /// <summary>Null when every key was built (and the layout fed), else why not.</summary>
        internal static string Refusal(params string[] keys)
        {
            if (!done) return "the grid layout is not built";
            var missing = keys.Where(k => Depot(k) < 0).Select(k => $"{k} ({(built.TryGetValue(k, out var b) ? b.reason : "not tried")})").ToList();
            return missing.Count == 0 ? null : "the grid layout did not build " + string.Join(", ", missing);
        }

        /// <summary>The grid pole1 is in now (GridState), or 0.</summary>
        internal static int PoleGrid() => GridState.GridOfDepot(Depot("pole1"), out int g) ? g : 0;

        /// <summary>-(lowest of the three poles' ids): the chain's grid id while all three stand.</summary>
        internal static int ChainGrid()
        {
            int a = Depot("pole1"), b = Depot("pole2"), c = Depot("pole3");
            return a < 0 || b < 0 || c < 0 ? 0 : -Math.Min(a, Math.Min(b, c));
        }

        internal static void Start()
        {
            built.Clear();
            started = true;
            done = false;
            index = 0;
            teleported = false;
            FedAt = -1f;
            startedAt = Time.unscaledTime;
            nextAt = startedAt;
            var walker = Walker();
            if (walker == null) { CoreLog.Warn("[dev] grid layout: no MoonWalker; nothing built"); done = true; return; }
            var t = walker.transform;
            home = t.position;
            var up = home.normalized;
            axis = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
            side = Vector3.Cross(up, axis).normalized;
            if (Vector3.Dot(side, Quaternion.AngleAxis(AutoLine.SideHeading, up) * t.forward) < 0f) side = -side;
            CoreLog.Info($"[dev] grid layout: {Plan.Length} sites from the walker at {PowerUx.V(home)}, axis {PowerUx.V(axis)}, across {PowerUx.V(side)}");
        }

        internal static void Tick()
        {
            if (!started || done) return;
            float now = Time.unscaledTime;
            if (now < nextAt) return;
            var world = CoreState.World ?? CoreState.FindWorld();
            var walker = Walker();
            try
            {
                if (world == null || walker == null) { CoreLog.Warn("[dev] grid layout: the world or the walker went away"); done = true; return; }
                if (now >= startedAt + GiveUpSeconds) { CoreLog.Warn($"[dev] grid layout: gave up after {GiveUpSeconds:F0} s at site {index}"); Finish(world, walker); return; }
                if (index >= Plan.Length) { Finish(world, walker); return; }
                var (key, who, along, across) = Plan[index];
                var at = home + axis * along + side * across;
                if (!teleported)
                {
                    var ground = world.SurfaceAt((at - side * StandBack).normalized, out Vector3 gUp, out bool _);
                    walker.Teleport(ground + gUp * 0.2f, true);
                    teleported = true;
                    nextAt = now + StepSeconds;
                    return;
                }
                teleported = false;
                nextAt = now + 0.1f;
                index++;
                int kind = int.TryParse(who, out int k) ? k : MoonStructures.Definition(who)?.Kind ?? -1;
                if (kind < 0 || (!int.TryParse(who, out _) && !MoonStructures.IsModded(kind))) { Report(key, -1, $"'{who}' is not registered"); return; }
                int depot = AutoBuilder.BuildAt(world, walker, kind, at, 0f, out string reason);
                Report(key, depot, depot >= 0 ? "built" : reason);
                if (depot >= 0 && world.depots.ContainsKey(depot))
                {
                    var rec = world.depots.get_Item(depot);
                    CoreLog.Info($"[dev] grid layout: {key} depot {depot} at {PowerUx.V(new Vector3(rec.x, rec.y, rec.z))} ({along:F0} m along, {across:F0} m across) yaw {rec.yaw:F1} placedPose {rec.placedPose}");
                }
            }
            catch (Exception e) { CoreLog.Warn($"[dev] grid layout: site {index} threw {e.GetType().Name}: {e.Message}"); teleported = false; index++; nextAt = now + 0.1f; }
        }

        static void Report(string key, int depot, string reason)
        {
            built[key] = (depot, reason);
            DevTools.BuildResults["grid:" + key] = (depot >= 0, depot, reason);
            CoreLog.Info($"[dev] grid layout: {key} depot={depot} ok={depot >= 0} reason='{reason}'");
        }

        static void Finish(MoonWorld world, MoonWalker walker)
        {
            done = true;
            try { walker.Teleport(home, true); }
            catch (Exception e) { CoreLog.Warn("[dev] grid layout: teleporting the walker home threw " + e.Message); }
            var grit = MoonStructures.CargoDefinition(GritId);
            foreach (var (key, cargo, kg) in new (string, MoonCargo?, double)[] { ("burner", grit != null ? (MoonCargo)grit.Value : (MoonCargo?)null, 10.0), ("lone-burner", grit != null ? (MoonCargo)grit.Value : null, 10.0),
                         ("grinder", MoonCargo.Dust, 10.0), ("lone-grinder", MoonCargo.Dust, 10.0), ("chamber", MoonCargo.Dust, 10.0) })
                Feed(world, key, cargo, kg);
            FedAt = Time.unscaledTime;
            CoreLog.Info($"[dev] grid layout: done ({built.Count(b => b.Value.depot >= 0)} of {Plan.Length} built) in {FedAt - startedAt:F1} s");
        }

        internal static void Feed(MoonWorld world, string key, MoonCargo? cargo, double kg)
        {
            int depot = Depot(key);
            if (depot < 0 || cargo == null) return;
            try
            {
                double before = SelfTest.Stored(world.ProductionOf(depot), 0);
                world.CrewProcessorLoad(MoonWorld.LocalPlayer, depot, cargo.Value, kg);
                CoreLog.Info($"[dev] grid layout: fed {key} (depot {depot}) {kg} kg of cargo {(byte)cargo.Value}: slot 0 {before} -> {SelfTest.Stored(world.ProductionOf(depot), 0)} kg");
            }
            catch (Exception e) { CoreLog.Warn($"[dev] grid layout: feeding {key} threw {e.Message}"); }
        }

        static MoonWalker Walker()
        {
            var walkers = UnityEngine.Object.FindObjectsByType<MoonWalker>(FindObjectsSortMode.None);
            return walkers != null && walkers.Length > 0 ? walkers[0] : null;
        }
    }

    // The grid self-tests (Patches/PowerPatches.cs, on the GridLayout above):
    //  grid-pole-has-state: pole1 has a production state after a solve.
    //  grid-chain-even-share: the burner (pole1), the grinder and the chamber (pole3) are in grid -(lowest pole id),
    //    which has 3 poles; while both consumers ask for power they read the same Power (within 0.01).
    //    (12 kW of supply against 11 kW of demand is satisfaction 1, so this proves same-grid membership and equal Power across
    //    three poles; even sharing when short is proven by the xunit EvenShareWhenShort.)
    //  grid-vanilla-topup: the grid raised the chamber (vanilla left it below 1): Power >= vanilla's, and its powerStatus
    //    names the grid once it is 1. Not applicable when vanilla's panels power it fully throughout.
    //  grid-no-pole-no-power: the lone grinder reads Grid 0 and a status naming a power pole; the lone burner supplies
    //    nothing and never burns. Not applicable when a vanilla node's own range reaches the lone grinder.
    //  grid-bank-carries: the example cell is given its full 1 kWh (the example mod has no solar array, and fuel never
    //    charges storage, so the charge is set by the test), the burner's grit and the chamber's dust are taken out and
    //    the grinder's output emptied and its dust refilled: once the burner and the chamber have stopped, the grinder runs at Power >= 0.99 while
    //    the cell's charge falls.
    //  power-no-burn-when-carried: then 2 kg of grit into the burner must not start a burn within 10 s (the cell
    //    carries the grid, so the grid wants no fuel).
    //  grid-pole-removed: the grinder is on the chain's grid -(lowest pole id) before; RemoveStructureNow(pole2): within
    //    two solves pole3 stands alone and the grinder reads its grid, exactly -(pole3 id), and no solve threw
    //    (PowerPatches.SolveThrows, which also counts the FootprintDistance fallback).
    internal static partial class SelfTest
    {
        static readonly Dictionary<string, float> gridOpen = new();
        static int gpSolves, gpThrows, gpOldGrid;
        static int bankStage, removeStage, carriedCycles;
        static float bankAt = -1f, bankUntil = -1f, carriedUntil = -1f;
        static double bankCharge;

        static bool GridPending => gridOpen.Count > 0 || bankStage > 0 || removeStage > 0 || previewStage > 0;
        static int previewStage;
        static float previewUntil;

        static readonly string[] GridNames = { "grid-pole-has-state", "grid-chain-even-share", "grid-vanilla-topup", "grid-no-pole-no-power", "grid-bank-carries", "power-no-burn-when-carried", "grid-pole-removed", "arc-grid-links", "arc-preview" };

        static void GridChecks()
        {
            if (!GridLayout.Named)
            {
                CoreLog.Info($"[selftest] [Dev] AutoBuild does not name '{GridLayout.Token}': {string.Join(", ", GridNames)} not applicable");
                return;
            }
            string all = GridLayout.Refusal("pole1", "pole2", "pole3", "burner", "cell", "grinder", "chamber");
            float now = Time.unscaledTime;
            gpSolves = PowerPatches.Solves;
            if (GridLayout.Refusal("pole1") is string p1) Assert("grid-pole-has-state", false, p1); else gridOpen["grid-pole-has-state"] = now + 5f;
            if (all != null) { foreach (var n in new[] { "grid-chain-even-share", "grid-vanilla-topup", "grid-bank-carries", "power-no-burn-when-carried", "grid-pole-removed", "arc-grid-links", "arc-preview" }) Assert(n, false, all); }
            else
            {
                gridOpen["grid-chain-even-share"] = now + 40f;
                gridOpen["grid-vanilla-topup"] = now + 40f;
                bankStage = 1;
                if (DevTools.ForceArcs != null && DevTools.ForceArcs.Value) gridOpen["arc-grid-links"] = now + 30f;
                else CoreLog.Info("[selftest] arc-grid-links not applicable: [Dev] ForceArcs is false");
                previewStage = 1;
                previewUntil = now + 40f;
                bankAt = now + 45f;
            }
            if (GridLayout.Refusal("lone-burner", "lone-grinder") is string lone) Assert("grid-no-pole-no-power", false, lone);
            else gridOpen["grid-no-pole-no-power"] = now + 15f;
            CoreLog.Info($"[selftest] grid checks opened: {GridText()}");
        }

        static void TickGrid()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            if (world == null) return;
            MoonProductionState P(string key) { int d = GridLayout.Depot(key); try { return d >= 0 ? world.ProductionOf(d) : null; } catch (Exception) { return null; } }
            var burner = P("burner");
            var grinder = P("grinder");
            var chamber = P("chamber");
            var cell = P("cell");
            int chain = GridLayout.ChainGrid();
            string text = $"burner {GridText(burner)}; grinder {GridText(grinder)}; chamber {GridText(chamber)}; cell {GridText(cell)}; {GridText()}";

            PollOpen(gridOpen, "grid-pole-has-state", text, () =>
                (P("pole1") != null && PowerPatches.Solves > gpSolves, $"pole1 depot {GridLayout.Depot("pole1")} state {(P("pole1") != null)}, solves {gpSolves} -> {PowerPatches.Solves}"));

            PollOpen(gridOpen, "grid-chain-even-share", text, () =>
            {
                bool chamberIn = GridState.GridOfDepot(GridLayout.Depot("chamber"), out int cg) && cg == chain;
                bool poles = GridState.Grids.TryGetValue(chain, out var grid) && grid.Poles == 3;
                bool asking = grinder != null && chamber != null && grinder.DemandKw > 0f && chamber.DemandKw > 0f;
                bool same = asking && Math.Abs(grinder.Power - chamber.Power) <= 0.01f;
                bool ok = chain != 0 && burner?.Grid == chain && grinder?.Grid == chain && chamberIn && poles && same;
                return (ok, $"expected grid {chain}: burner {burner?.Grid}, grinder {grinder?.Grid}, chamber in it {chamberIn} ({cg}), 3 poles {poles} ({grid?.Poles}); both asking {asking}, Power grinder {grinder?.Power:F3} chamber {chamber?.Power:F3}; {text}");
            });

            PollOpen(gridOpen, "grid-vanilla-topup", text, () =>
            {
                int id = GridLayout.Depot("chamber");
                bool raised = GridState.VanillaBefore.TryGetValue(id, out float before) && before < 0.999f;
                bool ok = raised && chamber != null && chamber.Power >= before - 1e-6f && (chamber.Power < 0.999f || (chamber.powerStatus ?? "").Contains("grid"));
                return (ok, $"raised {raised} (vanilla left {before:F3}), now {chamber?.Power:F3}, powerStatus '{chamber?.powerStatus}'; {text}");
            }, () => chamber != null && chamber.Power >= 0.999f && !GridState.VanillaBefore.ContainsKey(GridLayout.Depot("chamber"))
                ? "vanilla's own panels power the chamber fully" : null);

            {
                var lg = P("lone-grinder");
                var lb = P("lone-burner");
                int lgId = GridLayout.Depot("lone-grinder");
                PollOpen(gridOpen, "grid-no-pole-no-power", text, () =>
                {
                    bool ok = lg != null && lb != null && lg.Grid == 0 && (lg.powerStatus ?? "").Contains("power pole") && lb.SuppliedKw == 0f && !lb.Running && lb.Cycles == 0;
                    return (ok, $"lone grinder {GridText(lg)} powerStatus '{lg?.powerStatus}'; lone burner {GridText(lb)}");
                }, () => PowerPatches.VanillaNodeOf(lgId) >= 0 ? $"vanilla node {PowerPatches.VanillaNodeOf(lgId)} reaches the lone grinder" : null);
            }

            PollOpen(gridOpen, "arc-grid-links", text, () =>
            {
                int expected = GridState.PoleLinks.Count + GridState.Reach.Count;
                int arcs = PowerLinksView.ArcsDrawn;
                bool ok = chain != 0 && GridState.PoleLinks.Count == 2 && arcs == expected;
                return (ok, $"ArcsDrawn {arcs}, expected {GridState.PoleLinks.Count} pole links + {GridState.Reach.Count} reach = {expected}; {text}");
            });
            TickPreview(world);
            TickBank(world, burner, grinder, chamber, cell, chain, text);
            TickPoleRemoved(world, grinder, chain, text);
        }

        // An open grid assertion with a "not applicable" reason checked at the deadline (null: it applies).
        static void PollOpen(Dictionary<string, float> open, string name, string text, Func<(bool ok, string detail)> probe, Func<string> notApplicable)
        {
            if (!open.TryGetValue(name, out float until)) return;
            if (Time.unscaledTime >= until)
            {
                string why = null;
                try { why = notApplicable(); } catch (Exception) { }
                bool ok0 = false;
                try { ok0 = probe().ok; } catch (Exception) { }
                if (why != null && !ok0) { open.Remove(name); CoreLog.Info($"[selftest] {name} not applicable: {why}; {text}"); return; }
            }
            PollOpen(open, name, text, probe);
        }

        // arc-preview: once arc-grid-links has settled (its count is exact only without a ghost), select the example pole in
        // the builder, put the dev ghost 10 m from pole1 along the tangent and read PowerLinksView: at least one preview
        // arc and the ring centred on the ghost (0.5 m). The in-game picture of the real ghost is a deferred manual check.
        static void TickPreview(MoonWorld world)
        {
            if (previewStage == 0) return;
            float now = Time.unscaledTime;
            try
            {
                if (previewStage == 1)
                {
                    if (gridOpen.ContainsKey("arc-grid-links") && now < previewUntil) return;
                    var def = MoonStructures.Definition(GridLayout.PoleId);
                    int p1 = GridLayout.Depot("pole1");
                    var builder = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>();
                    if (def == null || p1 < 0 || !world.depots.ContainsKey(p1) || builder == null)
                    { previewStage = 0; Assert("arc-preview", false, $"pole def {def != null}, pole1 {p1}, builder {builder != null}"); return; }
                    try { builder.SelectStructure(def.Kind); }
                    catch (Exception e) { CoreLog.Warn("[selftest] arc-preview: SelectStructure threw " + e.Message); }
                    var rec = world.depots.get_Item(p1);
                    var at = new Vector3(rec.x, rec.y, rec.z);
                    var dir = Vector3.ProjectOnPlane(Vector3.right, at.normalized).normalized;
                    float gap = Math.Min(10f, def.Power.LinkRange * 0.8f);
                    previewGhost = at + dir * gap;
                    PowerLinksView.DevGhost = previewGhost;
                    previewUntil = now + 5f;
                    previewStage = 2;
                    CoreLog.Info($"[selftest] arc-preview: pole selected (builder kind {builder.selectedBuildId}, want {def.Kind}); ghost {PowerUx.V(previewGhost)}, {gap:F1} m from pole1 at {PowerUx.V(at)}");
                    return;
                }
                var ring = PowerLinksView.RingAt;
                bool ok = PowerLinksView.ArcsDrawn >= 1 && PowerLinksView.PreviewArcs >= 1 && ring.HasValue && Vector3.Distance(ring.Value, previewGhost) <= 0.5f;
                if (!ok && now < previewUntil) return;
                previewStage = 0;
                PowerLinksView.DevGhost = null;
                Assert("arc-preview", ok, $"ArcsDrawn {PowerLinksView.ArcsDrawn}, PreviewArcs {PowerLinksView.PreviewArcs}, ring {(ring.HasValue ? PowerUx.V(ring.Value) : "none")} vs ghost {PowerUx.V(previewGhost)}");
            }
            catch (Exception e) { previewStage = 0; PowerLinksView.DevGhost = null; Assert("arc-preview", false, "threw " + e.GetType().Name + ": " + e.Message); }
        }
        static Vector3 previewGhost;

        static void TickBank(MoonWorld world, MoonProductionState burner, MoonProductionState grinder, MoonProductionState chamber, MoonProductionState cell, int chain, string text)
        {
            float now = Time.unscaledTime;
            if (bankStage == 1)
            {
                // After the chain checks (or at the latest 45 s after the grid checks opened).
                bool others = gridOpen.ContainsKey("grid-chain-even-share") || gridOpen.ContainsKey("grid-vanilla-topup") || gridOpen.ContainsKey("arc-grid-links") || previewStage > 0 || VanillaFedPending || PowerPending || ArcPending;
                if (others && now < bankAt) return;
                if (cell == null || burner == null || grinder == null || chamber == null)
                {
                    bankStage = 0;
                    Assert("grid-bank-carries", false, $"a layout state is missing: {text}");
                    Assert("power-no-burn-when-carried", false, "grid-bank-carries could not run");
                    removeStage = 1;
                    return;
                }
                cell._ChargeKwh_k__BackingField = 1.0;
                bankCharge = 1.0;
                if (burner.stores != null && burner.stores.Length > 0) burner.stores[0].Clear();
                if (chamber.stores != null && chamber.stores.Length > 0) chamber.stores[0].Clear();
                if (grinder.stores != null && grinder.stores.Length > 2) grinder.stores[2].Clear();
                GridLayout.Feed(world, "grinder", MoonCargo.Dust, 10);
                bankUntil = now + 40f;
                bankStage = 2;
                CoreLog.Info($"[selftest] grid-bank-carries: cell set to 1 kWh, the burner's grit and the chamber's dust taken out, the grinder refilled; {text}");
                return;
            }
            if (bankStage == 2)
            {
                bool quiet = !burner.Running && chamber.DemandKw <= 0f;
                bool carried = quiet && grinder.Running && grinder.Power >= 0.99f && cell.ChargeKwh < bankCharge - 1e-7 && cell.Grid == chain
                    && GridState.Grids.TryGetValue(chain, out var g) && g.StorageKw > 0f && g.FuelKw <= 0f;
                if (!carried && now < bankUntil) return;
                Assert("grid-bank-carries", carried, $"burner and chamber stopped {quiet}; grinder running {grinder.Running} at Power {grinder.Power:F3}; cell charge {bankCharge:F4} -> {cell.ChargeKwh:F6} kWh on grid {cell.Grid} (expected {chain}); {text}");
                if (!carried) { bankStage = 0; Assert("power-no-burn-when-carried", false, "grid-bank-carries failed"); removeStage = 1; return; }
                CoreLog.Info($"[selftest] grid-bank-carries: {text}");
                var grit = MoonStructures.CargoDefinition(GridLayout.GritId);
                GridLayout.Feed(world, "burner", grit != null ? (MoonCargo)grit.Value : null, 2);
                carriedCycles = burner.Cycles;
                carriedUntil = now + 10f;
                bankStage = 3;
                return;
            }
            if (bankStage == 3)
            {
                bool burnt = burner.Running || burner.Cycles != carriedCycles;
                if (!burnt && now < carriedUntil) return;
                bankStage = 0;
                bool stillCarried = GridState.Grids.TryGetValue(chain, out var g) && g.StorageKw > 0f;
                Assert("power-no-burn-when-carried", !burnt && SelfTest.Stored(burner, 0) >= 2 - 1e-6, $"burner running {burner.Running}, cycles {carriedCycles} -> {burner.Cycles}, grit {SelfTest.Stored(burner, 0):F2} kg; cell still carrying {stillCarried}; {text}");
                removeStage = 1;
            }
        }

        static void TickPoleRemoved(MoonWorld world, MoonProductionState grinder, int chain, string text)
        {
            if (removeStage == 1)
            {
                int pole2 = GridLayout.Depot("pole2");
                if (pole2 < 0 || grinder == null || chain == 0 || grinder.Grid != chain) { removeStage = 0; Assert("grid-pole-removed", false, $"before the removal: pole2 {pole2}, grinder state {(grinder != null)}, grinder grid {grinder?.Grid} (expected the chain's {chain})"); return; }
                gpOldGrid = grinder.Grid;
                gpThrows = PowerPatches.SolveThrows;
                try { world.RemoveStructureNow(pole2); }
                catch (Exception e) { removeStage = 0; Assert("grid-pole-removed", false, "RemoveStructureNow threw " + e.Message); return; }
                gpSolves = PowerPatches.Solves;
                removeStage = 2;
                CoreLog.Info($"[selftest] grid-pole-removed: removed pole2 (depot {pole2}); grinder on grid {gpOldGrid}; {text}");
                return;
            }
            if (removeStage == 2)
            {
                if (PowerPatches.Solves < gpSolves + 2) return;
                removeStage = 0;
                bool gone = !world.depots.ContainsKey(GridLayout.Depot("pole2"));
                int alone = -GridLayout.Depot("pole3");
                bool moved = grinder != null && grinder.Grid == alone;
                bool clean = PowerPatches.SolveThrows == gpThrows;
                Assert("grid-pole-removed", gone && moved && clean, $"pole2 gone {gone}; grinder grid {gpOldGrid} -> {grinder?.Grid} (expected pole3's {alone}) after {PowerPatches.Solves - gpSolves} solves; solve throws {gpThrows} -> {PowerPatches.SolveThrows}; {text}");
            }
        }

        static string GridText(MoonProductionState s) => s == null ? "no state" :
            $"depot {s.Id} grid {s.Grid} power {s.Power:F2} demand {s.DemandKw:F1} used {s.UsedKw:F2} supplied {s.SuppliedKw:F2} kW charge {s.ChargeKwh:F4} kWh battery {s.BatteryKw:F2} kW running {s.Running} cycles {s.Cycles}";

        static string GridText() => GridState.Grids.Count == 0 ? "no pole grid" : string.Join("; ", GridState.Grids.Values.Select(g =>
            $"grid {g.Id} ({g.Poles} poles, members [{string.Join(", ", g.MemberIds)}]): demand {g.DemandKw:F2} used {g.UsedKw:F2} solar {g.SolarKw:F2} storage {g.StorageKw:F2} fuel {g.FuelKw:F2} kW, stored {g.StoredKwh:F4} kWh, satisfaction {g.Satisfaction:F2}, wants fuel {g.WantsFuel}"));
    }
}
#endif
