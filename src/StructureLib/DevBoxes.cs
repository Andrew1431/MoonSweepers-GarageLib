#if DEVBUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    // Belt boxes (mods/BeltBoxes: ryanhartwig.box-splitter and ryanhartwig.box-merger, StructureLib routers). Chain run
    // config: AutoBuildLayout line, AutoBuildGap wide enough between the boxes for the side U-belts; AutoBuild 96,
    // ryanhartwig.example-grinder, ryanhartwig.box-splitter, ryanhartwig.box-merger, ryanhartwig.example-burner; AutoFeed
    // 96:Dust:12; AutoBelt 96 -> grinder, grinder:Grit out -> box-splitter:In, box-splitter:Forward/Left/Right ->
    // box-merger:Back/Left/Right, box-merger:Out -> burner:Grit in; AutoSave; DumpCards. Splitter outputs take turns in
    // the order Forward, Left, Right; merger inputs in the order Back, Left, Right.
    //
    // While the chain checks run, a feeder puts a 1 kg grit canister near the start of the splitter's input belt whenever
    // there is room (the grinder alone makes too little), and the burner's grit slot is emptied whenever it holds 5 kg
    // (its battery fills after two cycles, and the chain would then stall behind it). Both are counted by conserve.
    //  1 beltboxes-registered: both ids registered at their hashed kinds as a splitter (1 in, 3 out) and a merger (3 in,
    //    1 out), four ports on each clone, halfFootprint 0.70 x 0.70 (Footprint 1.4 x 1.4).
    //  2 beltboxes-model (both runs): each placed box's view has the StructureLibModel holder with its renderers enabled,
    //    the donor's mesh renderers hidden and exactly one BoxCollider outside the holder; the clone's ports are the
    //    model's Port_* nodes (ModelSwap.Ports read from the file), and a runtime port lies at each node's world point.
    //  3 beltboxes-split-even: of the first 12 or more hops after the self-test starts, each output had a third ± 1.
    //  4 beltboxes-merge-fair: a sand canister at the end of the merger's output belt (the burner refuses it) lets that
    //    belt fill, so the merger holds; once every input belt has two canisters stopped on it the sand is removed, and
    //    the next six hops come from the three inputs twice each, in turn order (Back, Left, Right, from wherever it is).
    //  5 beltboxes-conserve: grit the grinder emitted (CargoEmitted) plus grit the feeder put on == grit that entered the
    //    burner (CargoEntered) plus grit riding the five chain belts, within one canister (1 kg).
    //  6 beltboxes-heavy: a 3 kg grit canister put at the end of the splitter's input belt either hops (a CargoRouted
    //    of 3 kg) or waits there, still 3 kg, with the splitter's refusal logged (within 30 s).
    //  7 beltboxes-split-skips: the Forward belt removed (TryRemoveConveyor), the next six splitter hops go to Left and
    //    Right only (both used), and as many canisters left the input belt as hopped. The Forward belt is then rebuilt
    //    (AutoBelter.BuildOne, tried every 2 s up to five times) so the save has all five belts; the canisters that rode
    //    it lie on the ground in its path and block a new belt there ("Blocked by an object."), so they are removed first.
    //  8 beltboxes-save-clean: with the merger's output belt held by a sand canister again, the feeder fills the chain
    //    until canisters wait at the end of the splitter's input belt and of all three middle belts; then a save. The
    //    write after it must record both boxes and every grit canister riding the chain, with every belt still built,
    //    the canisters still waiting, and no refused save.
    //  9 beltboxes-remove-clean (last): the merger removed (RemoveStructureNow) with a canister waiting at the end of its
    //    Back belt: no exception or error, the depot gone with Removed raised, and 3 s later the canister still rides its
    //    belt. Nothing is rebuilt; the run ends.
    // Reload run (AutoBuild, AutoFeed and AutoBelt empty, the chain run's slot): beltboxes-reload: every box the sidecar
    // held is back with its view, the belts joined to them (one into the splitter and three out of it; with the merger
    // back too, those three end at it and one leaves it), and every grit canister the sidecar held is back with cargo
    // 250. To make a hop possible, a canister of other cargo on the merger's output belt (the sand holding it) is
    // removed, and so is the canister waiting at the end of each splitter output belt that ends at no machine; then a
    // box must hop within 20 s. beltboxes-model again. The game saves a few seconds after remove-clean, so the chain
    // run's last save holds the splitter and all five belts but no merger: that is what the reload run sees.
    internal static partial class SelfTest
    {
        const string SplitterId = "ryanhartwig.box-splitter", MergerId = "ryanhartwig.box-merger";
        static readonly string[] BoxChainNames =
        {
            "beltboxes-split-even", "beltboxes-merge-fair", "beltboxes-conserve", "beltboxes-heavy", "beltboxes-split-skips",
            "beltboxes-save-clean", "beltboxes-remove-clean",
        };
        static readonly string[] MergerTurn = { "Back", "Left", "Right" };
        static readonly List<(float t, CargoRouted c)> boxRouted = new();
        static readonly HashSet<string> boxAsserted = new(StringComparer.Ordinal);
        static bool boxRoutedHooked, boxChain, feeding, fillOk;
        static int boxPhase, spl = -1, mrg = -1, brn = -1, grd = -1, bIn = -1, bF = -1, bL = -1, bR = -1, bOut = -1;
        static int boxMark, blockerId = -1, heavyId = -1, boxWaitId = -1, boxWaitBelt = -1, boxSaveSeq = -1, inGone, unityMark, fed, sinkClears;
        static float boxUntil = -1f, boxGrace = -1f, feedNext;
        static double spawnedGrit, sinkKg;
        static string entryF, removeThrew, boxWaitPort;
        static HashSet<int> inPrev = new(), droppedF = new();
        static List<int> loadedBoxGrit = new(), backAtStart = new();
        static bool savedS, savedM;
        static List<int> mids = new(), splOuts = new();

        internal static bool BoxPending => boxPhase != 0;

        static void HookBoxEvents()
        {
            if (boxRoutedHooked) return;
            boxRoutedHooked = true;
            MoonStructures.CargoRouted += c => { if (c != null) boxRouted.Add((Time.unscaledTime, c)); };
        }

        static void BoxChecks()
        {
            HookBoxEvents();
            HookBeltEvents();
            var world = CoreState.World ?? CoreState.FindWorld();
            StructureDefinition s = MoonStructures.Definition(SplitterId), m = MoonStructures.Definition(MergerId);
            if (s == null && m == null)
            {
                CoreLog.Info($"[selftest] BeltBoxes is not registered ('{SplitterId}', '{MergerId}'): beltboxes-registered, beltboxes-model, beltboxes-reload, {string.Join(", ", BoxChainNames)} not applicable");
                return;
            }
            RegisteredCheck(world, s, m);
            if (s == null || m == null || world == null)
            {
                CoreLog.Info("[selftest] one belt box is missing (or no world): the other beltboxes-* checks not applicable");
                return;
            }
            spl = DepotOfKind(world, s.Kind);
            mrg = DepotOfKind(world, m.Kind);
            boxChain = DevTools.AutoBuildNames(SplitterId) || DevTools.AutoBuildNames(MergerId);
            if (!boxChain && spl < 0 && mrg < 0)
            {
                CoreLog.Info($"[selftest] no belt box was built (AutoBuild) or restored this run: beltboxes-model, beltboxes-reload, {string.Join(", ", BoxChainNames)} not applicable");
                return;
            }
            ModelCheck(world, s, m);
            if (!boxChain)
            {
                CoreLog.Info($"[selftest] AutoBuild names no belt box (reload run): {string.Join(", ", BoxChainNames)} not applicable");
                ReloadStart(world);
                return;
            }
            CoreLog.Info("[selftest] AutoBuild builds the belt boxes: beltboxes-reload not applicable (it runs in the next launch)");
            ChainStart(world);
        }

        // beltboxes-registered
        static void RegisteredCheck(MoonWorld world, StructureDefinition s, StructureDefinition m)
        {
            Check("beltboxes-registered", world == null ? "no MoonWorld found" : null, () =>
            {
                var problems = new List<string>();
                foreach (var (d, id, mode, ins, outs) in new[] { (s, SplitterId, RouterMode.Splitter, 1, 3), (m, MergerId, RouterMode.Merger, 3, 1) })
                {
                    if (d == null) { problems.Add($"'{id}' is not registered"); continue; }
                    var ports = MoonStructures.PortsOf(d);
                    int i = ports?.Count(p => p != null && !p.Output) ?? -1, o = ports?.Count(p => p != null && p.Output) ?? -1;
                    var def = world.BuildDefinition(d.Kind);
                    var clone = def?.conveyorPorts;
                    var half = def != null ? def.halfFootprint : Vector2.zero;
                    CoreLog.Info($"[selftest] '{id}': kind {d.Kind} (hashed {KindAllocator.KindOf(id)}), router {d.Router?.Mode}, Footprint {d.Footprint}, ports (rule '{d.PortRule}') {i} in / {o} out [{string.Join(", ", (ports ?? new List<PortDefinition>()).Select(p => $"'{p.Name}' {(p.Output ? "out" : "in")} {AutoBelter.V(p.Position)}"))}]; clone {Describe(clone)}, halfFootprint ({half.x:F2},{half.y:F2})");
                    if (d.Kind != KindAllocator.KindOf(id) || !MoonStructures.IsModded(d.Kind)) problems.Add($"'{id}' kind {d.Kind}, expected {KindAllocator.KindOf(id)}");
                    if (d.Router?.Mode != mode) problems.Add($"'{id}' router {d.Router?.Mode.ToString() ?? "none"}, expected {mode}");
                    if (i != ins || o != outs) problems.Add($"'{id}' has {i} in / {o} out, expected {ins} / {outs}");
                    if ((clone?.Length ?? 0) != 4) problems.Add($"'{id}' clone has {clone?.Length ?? -1} conveyorPorts, expected 4");
                    if (def == null || Mathf.Abs(half.x - 0.70f) > 0.005f || Mathf.Abs(half.y - 0.70f) > 0.005f) problems.Add($"'{id}' halfFootprint ({half.x:F3},{half.y:F3}), expected (0.70,0.70)");
                }
                return (problems.Count == 0, string.Join("; ", problems));
            });
        }

        // beltboxes-model
        static void ModelCheck(MoonWorld world, StructureDefinition s, StructureDefinition m)
        {
            var construction = UnityEngine.Object.FindFirstObjectByType<MoonDepotConstruction>();
            Check("beltboxes-model", construction == null ? "no MoonDepotConstruction found" : null, () =>
            {
                var problems = new List<string>();
                int seen = 0;
                foreach (var (d, depot, what) in new[] { (s, spl, "splitter"), (m, mrg, "merger") })
                {
                    if (depot < 0) { CoreLog.Info($"[selftest] beltboxes-model: no {what} depot in this world"); continue; }
                    seen++;
                    var view = construction.ViewOf(depot);
                    if (view == null) { problems.Add($"{what} depot {depot} has no view"); continue; }
                    var (swapped, why) = Swapped(world, view, d.BaseStructure, what);
                    if (!swapped) problems.Add($"{what}: {why}");
                    var model = ModelSwap.Ports(d, null) ?? new List<PortDefinition>();
                    var ports = MoonStructures.PortsOf(d) ?? new List<PortDefinition>();
                    var rec = world.depots.get_Item(depot);
                    var runtime = AutoBelter.RuntimePorts(world, rec);
                    var t = view.transform;
                    var lines = new List<string>();
                    if (d.PortRule != "model Port_* nodes") problems.Add($"{what} port rule '{d.PortRule}'");
                    if (model.Count != 4 || ports.Count != 4) problems.Add($"{what}: {model.Count} model Port_* node(s), {ports.Count} resolved port(s), expected 4 and 4");
                    foreach (var n in model)
                    {
                        var p = ports.FirstOrDefault(x => x != null && x.Name == n.Name);
                        var at = t.position + t.rotation * n.Position;
                        var r = runtime.Where(x => x.output == n.Output).OrderBy(x => Vector3.Distance(x.position, at)).FirstOrDefault();
                        float dist = r == null ? -1f : Vector3.Distance(r.position, at);
                        lines.Add($"'{n.Name}' node {AutoBelter.V(n.Position)} dir {AutoBelter.V(n.Direction)} -> port {(p == null ? "none" : AutoBelter.V(p.Position))}, runtime {(r == null ? "none" : $"'{r.label}' {dist:F3} m")}");
                        if (p == null || p.Output != n.Output || Vector3.Distance(p.Position, n.Position) > 0.01f || Vector3.Distance(p.Direction, n.Direction) > 0.01f)
                            problems.Add($"{what} port '{n.Name}' is not at its model node");
                        if (r == null || dist > MoonWorld.PortTolerance) problems.Add($"{what} has no runtime {(n.Output ? "output" : "input")} at node '{n.Name}' ({(r == null ? "none" : dist.ToString("F3") + " m")})");
                    }
                    CoreLog.Info($"[selftest] beltboxes-model: {what} depot {depot} view '{view.name}' at {AutoBelter.V(t.position)}: {string.Join("; ", lines)}");
                }
                if (seen == 0) problems.Add("no placed belt box");
                return (problems.Count == 0, string.Join("; ", problems));
            });
        }

        // ---- chain run ----

        static (string fromWho, string fromPort, string toWho, string toPort) Sides(string entry)
        {
            var sides = entry.Split(new[] { "->" }, StringSplitOptions.None);
            static (string, string) Split(string x)
            {
                int c = x.LastIndexOf(':');
                return c < 0 ? (x.Trim(), "") : (x.Substring(0, c).Trim(), x.Substring(c + 1).Trim());
            }
            var (a, b) = Split(sides[0]);
            var (c2, d) = sides.Length > 1 ? Split(sides[1]) : ("", "");
            return (a, b, c2, d);
        }

        static AutoBelter.Result BeltEntry(string fromWho, string fromPort, string toWho, string toPort) =>
            AutoBelter.Results.FirstOrDefault(r =>
            {
                var x = Sides(r.Entry);
                return (fromWho == null || x.fromWho == fromWho) && (fromPort == null || string.Equals(x.fromPort, fromPort, StringComparison.OrdinalIgnoreCase))
                       && (toWho == null || x.toWho == toWho) && (toPort == null || string.Equals(x.toPort, toPort, StringComparison.OrdinalIgnoreCase));
            });

        static void ChainStart(MoonWorld world)
        {
            var rIn = BeltEntry(null, null, SplitterId, "In");
            var rF = BeltEntry(SplitterId, "Forward", MergerId, "Back");
            var rL = BeltEntry(SplitterId, "Left", MergerId, "Left");
            var rR = BeltEntry(SplitterId, "Right", MergerId, "Right");
            var rOut = BeltEntry(MergerId, "Out", null, null);
            var problems = new List<string>();
            foreach (var (r, what) in new[] { (rIn, "into the splitter's In"), (rF, "splitter Forward -> merger Back"), (rL, "splitter Left -> merger Left"), (rR, "splitter Right -> merger Right"), (rOut, "out of the merger's Out") })
                if (r == null) problems.Add($"no AutoBelt entry {what}");
                else if (!r.Ok) problems.Add($"AutoBelt '{r.Entry}' did not build: {r.Reason}");
            if (spl < 0 || mrg < 0) problems.Add($"AutoBuild built splitter depot {spl}, merger depot {mrg}");
            if (problems.Count == 0 && (DepotOfKind(world, MoonStructures.Definition(BurnerId)?.Kind ?? -1) != rOut.To)) problems.Add($"the merger's belt ends at depot {rOut.To}, not the example burner");
            if (problems.Count > 0) { FailRest(string.Join("; ", problems)); return; }
            bIn = rIn.Belt; bF = rF.Belt; bL = rL.Belt; bR = rR.Belt; bOut = rOut.Belt;
            entryF = rF.Entry;
            grd = rIn.From; brn = rOut.To;
            CoreLog.Info($"[selftest] beltboxes chain: grinder {grd} -> belt {bIn} ({Len(world, bIn)}) -> splitter {spl}; Forward belt {bF} ({Len(world, bF)}), Left belt {bL} ({Len(world, bL)}), Right belt {bR} ({Len(world, bR)}) -> merger {mrg}; belt {bOut} ({Len(world, bOut)}) -> burner {brn}");
            boxPhase = 1;
            boxMark = boxRouted.Count;
            boxUntil = Time.unscaledTime + 150f;
            feeding = true;
        }

        static string Len(MoonWorld world, int belt) => world.BeltPath(belt) is MoonConveyorPath p ? $"{p.Length:F2} m" : "no path";

        internal static void TickBoxes()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            float now = Time.unscaledTime;
            try
            {
                if (world == null) return;
                if (boxChain)
                {
                    if (feeding) Feed(world, now);
                    Sink(world);
                }
                switch (boxPhase)
                {
                    case 1: TickSplitEven(world, now); break;
                    case 2: TickMergeBlock(world, now); break;
                    case 3: TickMergeFair(world, now); break;
                    case 4: TickConserve(world, now); break;
                    case 5: TickHeavy(world, now); break;
                    case 6: TickSplitSkips(world, now); break;
                    case 7: TickSaveFill(world, now); break;
                    case 8: TickBoxSave(world, now); break;
                    case 9: TickRemoved(world, now); break;
                    case 10: TickRebuild(world, now); break;
                    case 20: TickBoxReload(world, now); break;
                }
            }
            catch (Exception e)
            {
                feeding = false;
                if (boxPhase == 20) Assert("beltboxes-reload", false, "threw " + e.GetType().Name + ": " + e.Message);
                else FailRest("threw " + e.GetType().Name + ": " + e.Message);
                boxPhase = 0;
            }
        }

        static void BoxAssert(string name, bool ok, string detail)
        {
            boxAsserted.Add(name);
            Assert(name, ok, detail);
        }

        // Every chain check not yet answered fails with this reason, and the chain stops.
        static void FailRest(string why)
        {
            foreach (var n in BoxChainNames) if (!boxAsserted.Contains(n)) BoxAssert(n, false, why);
            boxPhase = 0;
            feeding = false;
        }

        static bool Clear(MoonWorld world, int belt, float d, float r)
        {
            foreach (var kv in world.riders)
                if (kv.Value.belt == belt && Math.Abs(kv.Value.distance - d) < r) return false;
            return true;
        }

        // A 1 kg grit canister near the start of the splitter's input belt when that stretch is free.
        static void Feed(MoonWorld world, float now)
        {
            if (now < feedNext) return;
            feedNext = now + 0.25f;
            var path = world.BeltPath(bIn);
            if (path == null || !Clear(world, bIn, 0.6f, 0.9f)) return;
            int id = Spawn(world, path, 0.6f, (MoonCargo)GritValue, out string why);
            if (id < 0) { CoreLog.Once("box-feed", "[selftest] beltboxes feeder: " + why, 3); return; }
            spawnedGrit += world.canisters.get_Item(id).kilograms;
            fed++;
        }

        // The burner's grit slot emptied at 5 kg, so the chain never stalls behind a full burner.
        static void Sink(MoonWorld world)
        {
            if (brn < 0) return;
            var s = world.ProductionOf(brn);
            double kg = Stored(s, 0);
            if (kg < 5) return;
            s.stores[0].Clear();
            sinkClears++;
            sinkKg += kg;
        }

        static int StoppedOn(MoonWorld world, int belt)
        {
            int n = 0;
            foreach (var kv in world.riders) if (kv.Value.belt == belt && !kv.Value.moving) n++;
            return n;
        }

        static string Hop(CargoRouted c) => $"{c.Kilograms} kg {c.Cargo.Id} {c.FromBeltId}->{c.ToBeltId}";

        static string OutName(int belt) => belt == bF ? "Forward" : belt == bL ? "Left" : belt == bR ? "Right" : $"belt {belt}";
        static string InName(int belt) => belt == bF ? "Back" : belt == bL ? "Left" : belt == bR ? "Right" : $"belt {belt}";

        static string ChainRiders(MoonWorld world) =>
            string.Join("; ", new[] { (bIn, "in"), (bF, "forward"), (bL, "left"), (bR, "right"), (bOut, "out") }
                .Where(x => x.Item1 >= 0).Select(x => $"{x.Item2} belt {x.Item1}: {RidersText(world, x.Item1)}"));

        static void TickSplitEven(MoonWorld world, float now)
        {
            var hops = boxRouted.Skip(boxMark).Where(x => x.c.DepotId == spl).Select(x => x.c).ToList();
            if (hops.Count < 12 && now < boxUntil) return;
            int f = hops.Count(h => h.ToBeltId == bF), l = hops.Count(h => h.ToBeltId == bL), r = hops.Count(h => h.ToBeltId == bR);
            int other = hops.Count - f - l - r;
            double third = hops.Count / 3.0;
            bool ok = hops.Count >= 12 && other == 0 && new[] { f, l, r }.All(c => Math.Abs(c - third) <= 1.0 + 1e-9);
            CoreLog.Info($"[selftest] beltboxes-split-even: {hops.Count} splitter hops in {now - (boxUntil - 150f):F1} s: [{string.Join(" ", hops.Select(h => OutName(h.ToBeltId)))}]; feeder put {fed} canister(s)");
            BoxAssert("beltboxes-split-even", ok, $"{hops.Count} hops (want 12 or more): Forward {f}, Left {l}, Right {r}, elsewhere {other}; a third is {third:F2}");
            StartBlock(world, 2, 90f);
        }

        // A sand canister at the end of the merger's output belt: the burner refuses it, so that belt fills and the
        // merger holds.
        static void StartBlock(MoonWorld world, int phase, float seconds)
        {
            var path = world.BeltPath(bOut);
            blockerId = -1;
            if (path != null)
                for (float d = path.Length - 0.3f; d > 0.1f && blockerId < 0; d -= 0.25f)
                    if (Clear(world, bOut, d, 0.6f)) blockerId = Spawn(world, path, d, MoonCargo.Sand, out _);
            CoreLog.Info($"[selftest] beltboxes: sand canister {blockerId} put on the merger's output belt {bOut} to hold it");
            boxPhase = phase;
            boxUntil = Time.unscaledTime + seconds;
            feeding = true;
        }

        static void RemoveBlocker(MoonWorld world)
        {
            if (blockerId >= 0 && world.canisters.ContainsKey(blockerId)) world.RemoveCanister(blockerId);
            CoreLog.Info($"[selftest] beltboxes: sand canister {blockerId} removed ({(world.canisters.ContainsKey(blockerId) ? "still there" : "gone")})");
            blockerId = -1;
        }

        static void TickMergeBlock(MoonWorld world, float now)
        {
            bool held = !world.EntranceClear(bOut);
            int f = StoppedOn(world, bF), l = StoppedOn(world, bL), r = StoppedOn(world, bR);
            bool ready = blockerId >= 0 && held && f >= 2 && l >= 2 && r >= 2;
            if (!ready && now < boxUntil) return;
            CoreLog.Info($"[selftest] beltboxes-merge-fair: merger output held {held}, stopped canisters Back {f} Left {l} Right {r}; {ChainRiders(world)}");
            RemoveBlocker(world);
            if (!ready)
            {
                BoxAssert("beltboxes-merge-fair", false, $"the merger's inputs did not fill within 90 s: output held {held}, stopped canisters Back {f}, Left {l}, Right {r} (want 2 each)");
                StartConserve();
                return;
            }
            boxMark = boxRouted.Count;
            boxUntil = now + 30f;
            boxPhase = 3;
        }

        static void TickMergeFair(MoonWorld world, float now)
        {
            var hops = boxRouted.Skip(boxMark).Where(x => x.c.DepotId == mrg).Select(x => x.c).Take(6).ToList();
            if (hops.Count < 6 && now < boxUntil) return;
            var names = hops.Select(h => InName(h.FromBeltId)).ToList();
            var at = names.Select(n => Array.IndexOf(MergerTurn, n)).ToList();
            bool rotates = at.All(i => i >= 0) && Enumerable.Range(0, Math.Max(0, at.Count - 1)).All(i => at[i + 1] == (at[i] + 1) % 3);
            bool twice = names.Count == 6 && MergerTurn.All(n => names.Count(x => x == n) == 2);
            CoreLog.Info($"[selftest] beltboxes-merge-fair: the next {hops.Count} merger hops [{string.Join(", ", hops.Select(Hop))}] = [{string.Join(" ", names)}]");
            BoxAssert("beltboxes-merge-fair", hops.Count == 6 && rotates && twice, $"{hops.Count} hops within 30 s from [{string.Join(" ", names)}]; want Back, Left, Right twice each in that turn order");
            StartConserve();
        }

        static void StartConserve()
        {
            feeding = false;
            boxPhase = 4;
            boxUntil = Time.unscaledTime + 5f;
        }

        // Grit kilograms riding the chain belts (waiting canisters ride too), and their count.
        static (double kg, int n) ChainGrit(MoonWorld world, List<int> ids = null)
        {
            var belts = new HashSet<int> { bIn, bF, bL, bR, bOut };
            double kg = 0; int n = 0;
            foreach (var kv in world.riders)
            {
                if (!belts.Contains(kv.Value.belt) || !world.canisters.ContainsKey(kv.Key)) continue;
                var c = world.canisters.get_Item(kv.Key);
                if ((byte)c.cargo != GritValue) continue;
                kg += c.kilograms; n++;
                ids?.Add(kv.Key);
            }
            return (kg, n);
        }

        static void TickConserve(MoonWorld world, float now)
        {
            if (now < boxUntil) return;
            var em = emitted.Where(x => x.e.DepotId == grd && x.e.Cargo.Value == GritValue).ToList();
            double emittedKg = em.Sum(x => x.e.Kilograms);
            var inB = entered.Where(e => e.DepotId == brn && e.Cargo.Id == GritId).ToList();
            double enteredKg = inB.Sum(e => e.Kilograms);
            var (ridingKg, riding) = ChainGrit(world);
            double put = emittedKg + spawnedGrit, found = enteredKg + ridingKg;
            bool ok = em.Count > 0 && Math.Abs(put - found) <= 1.0 + 1e-6;
            CoreLog.Info($"[selftest] beltboxes-conserve: grinder emitted {emittedKg} kg in {em.Count} canister(s), the feeder put {spawnedGrit} kg ({fed}); the burner took {enteredKg} kg in {inB.Count} (its slot emptied {sinkClears}x, {sinkKg} kg), {ridingKg} kg in {riding} canister(s) ride the chain belts; {ChainRiders(world)}");
            BoxAssert("beltboxes-conserve", ok, $"put on {put} kg (grinder {emittedKg} + feeder {spawnedGrit}), accounted {found} kg (burner {enteredKg} + riding {ridingKg}); difference {put - found:F3} kg, allowed 1");
            StartHeavy(world);
        }

        static void StartHeavy(MoonWorld world)
        {
            var path = world.BeltPath(bIn);
            heavyId = -1;
            for (float d = path.Length - 0.4f; d > 0.5f && heavyId < 0; d -= 0.25f)
                if (Clear(world, bIn, d, 0.6f)) heavyId = Spawn(world, path, d, (MoonCargo)GritValue, 3.0, out _);
            if (heavyId < 0) { BoxAssert("beltboxes-heavy", false, $"no room on belt {bIn} for a 3 kg canister: {RidersText(world, bIn)}"); StartSkips(world); return; }
            var c = world.canisters.get_Item(heavyId);
            spawnedGrit += c.kilograms;
            CoreLog.Info($"[selftest] beltboxes-heavy: canister {heavyId} ({c.kilograms} kg, capacity {c.capacity}, cargo {(byte)c.cargo}) put on belt {bIn} into the splitter");
            boxMark = boxRouted.Count;
            boxUntil = Time.unscaledTime + 30f;
            boxPhase = 5;
        }

        static void TickHeavy(MoonWorld world, float now)
        {
            var hop = boxRouted.Skip(boxMark).Select(x => x.c).FirstOrDefault(c => c.DepotId == spl && c.FromBeltId == bIn && Math.Abs(c.Kilograms - 3.0) < 0.01);
            bool timedOut = now >= boxUntil;
            if (hop == null && !timedOut) return;
            if (hop != null)
            {
                string fresh = "none found";
                foreach (var kv in world.riders)
                    if (kv.Value.belt == hop.ToBeltId && world.canisters.ContainsKey(kv.Key) && Math.Abs(world.canisters.get_Item(kv.Key).kilograms - 3.0) < 0.01)
                    { var c = world.canisters.get_Item(kv.Key); fresh = $"canister {kv.Key} {c.kilograms} kg capacity {c.capacity} cargo {(byte)c.cargo}"; break; }
                bool gone = !world.canisters.ContainsKey(heavyId);
                CoreLog.Info($"[selftest] beltboxes-heavy: hopped {Hop(hop)} ({OutName(hop.ToBeltId)}); on that belt: {fresh}; canister {heavyId} gone {gone}");
                BoxAssert("beltboxes-heavy", gone && hop.Cargo.Value == GritValue, $"CargoRouted {Hop(hop)}, arriving canister {heavyId} gone {gone}");
            }
            else
            {
                bool present = world.canisters.ContainsKey(heavyId);
                double kg = present ? world.canisters.get_Item(heavyId).kilograms : -1;
                bool riding = Rider(world, heavyId, out int b, out float d, out bool moving);
                string log = ReadWholeLog() ?? "";
                bool logged = (log.Contains($"Router '{SplitterId}' depot {spl}: belt ") && log.Contains($"refused canister {heavyId}")) || log.Contains($"canister {heavyId} waits");
                CoreLog.Info($"[selftest] beltboxes-heavy: no 3 kg hop in 30 s; canister {heavyId} present {present} {kg} kg, rider {riding} belt {b} d {d:F2} moving {moving}; wait logged {logged}");
                BoxAssert("beltboxes-heavy", present && Math.Abs(kg - 3.0) < 0.01 && riding && b == bIn && logged, $"no CargoRouted of 3 kg within 30 s, and canister {heavyId}: present {present}, {kg} kg, riding belt {b}, wait logged {logged}");
            }
            StartSkips(world);
        }

        static MoonWalker Walker()
        {
            var walkers = UnityEngine.Object.FindObjectsByType<MoonWalker>(FindObjectsSortMode.None);
            return walkers != null && walkers.Length > 0 ? walkers[0] : null;
        }

        static void StartSkips(MoonWorld world)
        {
            var walker = Walker();
            string reason = "no MoonWalker";
            string before = RidersText(world, bF);
            droppedF = RiderIds(world, bF);
            bool removed = walker != null && world.TryRemoveConveyor(walker, bF, out reason);
            CoreLog.Info($"[selftest] beltboxes-split-skips: TryRemoveConveyor({bF}) {removed} '{reason}'; riders on it before: {before}; belt in world.conveyors {world.conveyors.ContainsKey(bF)}");
            if (!removed || world.conveyors.ContainsKey(bF))
            {
                BoxAssert("beltboxes-split-skips", false, $"removing the Forward belt {bF} failed: '{reason}'");
                StartSaveFill(world);
                return;
            }
            bF = -1;
            inPrev = RiderIds(world, bIn);
            inGone = 0;
            boxMark = boxRouted.Count;
            boxUntil = Time.unscaledTime + 60f;
            boxPhase = 6;
            feeding = true;
        }

        static HashSet<int> RiderIds(MoonWorld world, int belt)
        {
            var s = new HashSet<int>();
            foreach (var kv in world.riders) if (kv.Value.belt == belt) s.Add(kv.Key);
            return s;
        }

        static void TickSplitSkips(MoonWorld world, float now)
        {
            var ids = RiderIds(world, bIn);
            foreach (int id in inPrev) if (!ids.Contains(id) && !world.canisters.ContainsKey(id)) inGone++;
            inPrev = ids;
            var hops = boxRouted.Skip(boxMark).Where(x => x.c.DepotId == spl).Select(x => x.c).ToList();
            if (hops.Count < 6 && now < boxUntil) return;
            int l = hops.Count(h => h.ToBeltId == bL), r = hops.Count(h => h.ToBeltId == bR);
            bool ok = hops.Count >= 6 && l + r == hops.Count && l > 0 && r > 0 && hops.All(h => h.FromBeltId == bIn) && inGone == hops.Count;
            CoreLog.Info($"[selftest] beltboxes-split-skips: {hops.Count} splitter hops with the Forward belt gone [{string.Join(" ", hops.Select(h => OutName(h.ToBeltId)))}]; canisters that left the input belt {inGone}");
            BoxAssert("beltboxes-split-skips", ok, $"{hops.Count} hops (want 6): Left {l}, Right {r}, elsewhere {hops.Count - l - r}; left the input belt {inGone}, hopped {hops.Count}");
            // Rebuilt outside every counting window, so the save and the reload have all five belts.
            // The canisters that rode the removed belt now lie in its path, and the game refuses a belt through them
            // ("Blocked by an object."): they go first (conserve has already run).
            var cleared = new List<string>();
            foreach (int id in droppedF)
                if (world.canisters.ContainsKey(id) && !world.riders.ContainsKey(id)) { cleared.Add($"{id} ({world.canisters.get_Item(id).kilograms} kg cargo {(byte)world.canisters.get_Item(id).cargo})"); world.RemoveCanister(id); }
            CoreLog.Info($"[selftest] beltboxes-split-skips: canisters left lying where the Forward belt was, removed before rebuilding it: [{string.Join(", ", cleared)}]");
            // The removed canisters leave the world a frame or more later: the rebuild is tried every 2 s, five times.
            rebuildTries = 0;
            boxUntil = now + 2f;
            boxPhase = 10;
            feeding = false;
        }

        static int rebuildTries;

        static void TickRebuild(MoonWorld world, float now)
        {
            if (now < boxUntil) return;
            rebuildTries++;
            var rebuilt = AutoBelter.BuildOne(entryF);
            if (rebuilt != null && rebuilt.Ok) bF = rebuilt.Belt;
            else if (rebuildTries < 5) { boxUntil = now + 2f; return; }
            else CoreLog.Warn($"[selftest] beltboxes: rebuilding the Forward belt failed {rebuildTries}x ('{rebuilt?.Reason}'); the save has four belts");
            StartSaveFill(world);
        }

        static List<int> Middles(MoonWorld world) => new[] { bF, bL, bR }.Where(b => b >= 0 && world.conveyors.ContainsKey(b)).ToList();

        static void StartSaveFill(MoonWorld world)
        {
            StartBlock(world, 7, 150f);
        }

        static void TickSaveFill(MoonWorld world, float now)
        {
            var m = Middles(world);
            bool held = !world.EntranceClear(bOut);
            // Full: every middle belt has a canister waiting at its end and no room at its entrance, so the canister at the
            // end of the input belt cannot hop and waits too (and the queue stands still through the save).
            bool midsWait = m.All(b => world.WaitingAtEnd(b)), midsFull = m.All(b => !world.EntranceClear(b));
            bool inWaits = world.WaitingAtEnd(bIn);
            fillOk = blockerId >= 0 && held && m.Count == 3 && midsWait && midsFull && inWaits;
            if (!fillOk && now < boxUntil) return;
            feeding = false;
            CoreLog.Info($"[selftest] beltboxes-save-clean: before the save: merger output held {held}, middle belts [{string.Join(",", m)}] waiting {midsWait} full {midsFull}, splitter input waiting {inWaits}; {ChainRiders(world)}");
            DevTools.RequestSave();
            boxSaveSeq = DevTools.SaveSeqAtRequest;
            boxGrace = -1f;
            boxUntil = now + 120f;
            boxPhase = 8;
        }

        static void TickBoxSave(MoonWorld world, float now)
        {
            if (boxGrace < 0f)
            {
                bool written = Sidecar.WriteSeq > boxSaveSeq;
                Il2CppSystem.Threading.Tasks.Task pending = null;
                try { pending = world.PendingSave; } catch (Exception) { }
                if (written && (pending == null || pending.IsCompleted)) boxGrace = now + 3f;
                else if (now < boxUntil) return;
            }
            if (boxGrace >= 0f && now < boxGrace) return;
            var w = Sidecar.LastWrite;
            var m = Middles(world);
            bool belts = world.conveyors.ContainsKey(bIn) && world.conveyors.ContainsKey(bOut) && m.Count == 3;
            bool still = world.WaitingAtEnd(bIn) && m.All(b => world.WaitingAtEnd(b));
            var live = new List<int>();
            ChainGrit(world, live);
            string path = SidecarPath();
            bool boxes = false;
            var missing = new List<int>(live);
            string recorded = "no sidecar";
            if (path != null && File.Exists(path))
            {
                var file = SidecarCodec.Deserialize(File.ReadAllText(path));
                int s = file.Structures.Count(x => x.Id == SplitterId), mm = file.Structures.Count(x => x.Id == MergerId);
                boxes = s >= 1 && mm >= 1;
                var grit = file.Canisters.Where(x => x.CargoId == GritId).Select(x => x.Id).ToHashSet();
                missing = live.Where(id => !grit.Contains(id)).ToList();
                recorded = $"structures splitter {s} merger {mm}; {file.Canisters.Count} canister record(s), {grit.Count} grit";
            }
            int refused = Sidecar.VanillaFailures - vanillaFailuresAtWatch;
            CoreLog.Info($"[selftest] beltboxes-save-clean: write #{w?.Seq} after the request (#{boxSaveSeq}); belts built {belts}, canisters still waiting {still}; grit canisters riding the chain {live.Count}; sidecar {recorded}; missing from it [{string.Join(", ", missing)}]; 'Moon save failed' {saveFailures}x, vanilla writes that threw {refused}");
            bool ok = fillOk && w != null && w.Seq > boxSaveSeq && belts && still && boxes && live.Count > 0 && missing.Count == 0 && saveFailures == 0 && refused == 0;
            BoxAssert("beltboxes-save-clean", ok, $"chain filled {fillOk}, write after the request {(w != null && w.Seq > boxSaveSeq)}, belts built {belts}, still waiting {still}, both boxes in the sidecar {boxes}, grit canisters riding {live.Count} missing from the sidecar [{string.Join(", ", missing)}], 'Moon save failed' {saveFailures}x, refused writes {refused}");
            StartRemove(world);
        }

        static void StartRemove(MoonWorld world)
        {
            boxWaitBelt = bF >= 0 && world.conveyors.ContainsKey(bF) ? bF : bL;
            boxWaitPort = boxWaitBelt == bF ? "Back" : "Left";
            var path = world.BeltPath(boxWaitBelt);
            boxWaitId = -1;
            float best = -1f;
            foreach (var kv in world.riders)
                if (kv.Value.belt == boxWaitBelt && !kv.Value.moving && kv.Value.distance > best) { best = kv.Value.distance; boxWaitId = kv.Key; }
            if (boxWaitId < 0 || path == null || best < path.Length - 0.3f)
            {
                BoxAssert("beltboxes-remove-clean", false, $"no canister waiting at the end of the merger's {boxWaitPort} belt {boxWaitBelt}: {RidersText(world, boxWaitBelt)}");
                boxPhase = 0;
                return;
            }
            unityMark = unityErrors;
            boxSaveSeq = Sidecar.WriteSeq;
            removeThrew = null;
            CoreLog.Info($"[selftest] beltboxes-remove-clean: removing merger depot {mrg} with canister {boxWaitId} waiting at d {best:F2}/{path.Length:F2} of its {boxWaitPort} belt {boxWaitBelt}");
            try { world.RemoveStructureNow(mrg); }
            catch (Exception e) { removeThrew = e.GetType().Name + ": " + e.Message; }
            boxUntil = Time.unscaledTime + 3f;
            boxPhase = 9;
        }

        static void TickRemoved(MoonWorld world, float now)
        {
            if (now < boxUntil) return;
            boxPhase = 0;
            bool gone = !world.depots.ContainsKey(mrg), raised = removedEvents.Contains(mrg);
            bool beltThere = world.conveyors.ContainsKey(boxWaitBelt);
            bool present = world.canisters.ContainsKey(boxWaitId);
            bool riding = Rider(world, boxWaitId, out int b, out float d, out bool moving) && b == boxWaitBelt;
            int errors = unityErrors - unityMark;
            string belts = string.Join(", ", new[] { (bIn, "in"), (bF, "forward"), (bL, "left"), (bR, "right"), (bOut, "out") }.Where(x => x.Item1 >= 0).Select(x => $"{x.Item2} {x.Item1} {(world.conveyors.ContainsKey(x.Item1) ? "kept" : "gone")}"));
            CoreLog.Info($"[selftest] beltboxes-remove-clean: merger depot {mrg} gone {gone}, Removed raised {raised}, threw {removeThrew ?? "no"}; belts {belts}; canister {boxWaitId} present {present}, riding belt {b} d {d:F2} moving {moving}; Unity errors since {errors}; sidecar writes since {Sidecar.WriteSeq - boxSaveSeq}");
            BoxAssert("beltboxes-remove-clean", removeThrew == null && gone && raised && errors == 0 && beltThere && present && riding,
                $"threw {removeThrew ?? "no"}, depot gone {gone}, Removed raised {raised}, Unity errors {errors}, the {boxWaitPort} belt {boxWaitBelt} kept {beltThere}, canister {boxWaitId} present {present} riding it {riding}");
        }

        // ---- reload run ----

        static void ReloadStart(MoonWorld world)
        {
            var saved = Sidecar.LoadedIds();
            savedS = saved.Contains(SplitterId);
            savedM = saved.Contains(MergerId);
            if (!savedS && !savedM) { CoreLog.Info("[selftest] the loaded sidecar holds no belt box: beltboxes-reload not applicable"); return; }
            bIn = bOut = -1;
            mids = new List<int>();
            splOuts = new List<int>();
            foreach (var kv in world.conveyors)
            {
                if (kv.Value == null) continue;
                int from = world.MachineWithPort(kv.Value.start, true), to = world.MachineWithPort(kv.Value.end, false);
                if (spl >= 0 && to == spl) bIn = kv.Key;
                if (spl >= 0 && from == spl) splOuts.Add(kv.Key);
                if (spl >= 0 && mrg >= 0 && from == spl && to == mrg) mids.Add(kv.Key);
                if (mrg >= 0 && from == mrg) bOut = kv.Key;
            }
            loadedBoxGrit = Sidecar.LoadedCanisters().Where(c => c.CargoId == GritId).Select(c => c.Id).ToList();
            backAtStart = loadedBoxGrit.Where(id => world.canisters.ContainsKey(id) && (byte)world.canisters.get_Item(id).cargo == GritValue).ToList();
            CoreLog.Info($"[selftest] beltboxes-reload: the sidecar held splitter {savedS}, merger {savedM}; restored splitter depot {spl}, merger depot {mrg}; belt into the splitter {bIn}, out of the splitter [{string.Join(",", splOuts)}], between the boxes [{string.Join(",", mids)}], out of the merger {bOut} ({world.conveyors.Count} belts); sidecar grit canisters {loadedBoxGrit.Count}, back with cargo {GritValue} {backAtStart.Count}");
            foreach (int b in new[] { bIn, bOut }.Concat(splOuts).Where(x => x >= 0))
                CoreLog.Info($"[selftest] beltboxes-reload: riders on belt {b}: {RidersText(world, b)}");
            // The chain run saved with sand holding the merger's output belt: take it off so a hop is possible.
            if (bOut >= 0)
                foreach (int id in RiderIds(world, bOut))
                    if (world.canisters.ContainsKey(id) && (byte)world.canisters.get_Item(id).cargo != GritValue)
                    {
                        world.RemoveCanister(id);
                        CoreLog.Info($"[selftest] beltboxes-reload: removed the holding canister {id} from belt {bOut}");
                    }
            // A splitter output belt whose merger is gone ends at nothing: its canisters wait at the end for good. Taking the
            // one at the end off lets the queue move up, which clears the entrance for a hop.
            foreach (int b in splOuts)
            {
                var c = world.conveyors.get_Item(b);
                int end = world.MachineWithPort(c.end, false);
                if (end >= 0 && world.depots.ContainsKey(end)) { CoreLog.Info($"[selftest] beltboxes-reload: belt {b} out of the splitter ends at depot {end}"); continue; }
                int last = -1; float far = -1f;
                foreach (var kv in world.riders)
                    if (kv.Value.belt == b && !kv.Value.moving && kv.Value.distance > far) { far = kv.Value.distance; last = kv.Key; }
                if (last < 0) continue;
                world.RemoveCanister(last);
                CoreLog.Info($"[selftest] beltboxes-reload: belt {b} out of the splitter ends at no machine; removed canister {last} waiting at its end (d {far:F2})");
            }
            boxMark = boxRouted.Count;
            boxUntil = Time.unscaledTime + 20f;
            boxPhase = 20;
        }

        static void TickBoxReload(MoonWorld world, float now)
        {
            var hop = boxRouted.Skip(boxMark).Select(x => x.c).FirstOrDefault(c => c.DepotId == spl || c.DepotId == mrg);
            bool boxes = (!savedS || (spl >= 0 && builtViews.Contains(spl))) && (!savedM || (mrg >= 0 && builtViews.Contains(mrg)));
            bool belts = (!savedS || (bIn >= 0 && splOuts.Count == 3)) && (!(savedS && savedM) || mids.Count == 3) && (!savedM || bOut >= 0);
            bool canisters = loadedBoxGrit.Count > 0 && backAtStart.Count == loadedBoxGrit.Count;
            bool ok = boxes && belts && canisters && hop != null;
            if (!ok && now < boxUntil) return;
            boxPhase = 0;
            CoreLog.Info($"[selftest] beltboxes-reload: first hop {(hop == null ? "none" : $"'{hop.Definition?.Id}' depot {hop.DepotId} {Hop(hop)}")} within {now - (boxUntil - 20f):F1} s");
            Assert("beltboxes-reload", ok, $"boxes back with views {boxes} (splitter {spl}, merger {mrg}; saved {savedS}/{savedM}), belts {belts} (in {bIn}, out of the splitter [{string.Join(",", splOuts)}], between [{string.Join(",", mids)}], out {bOut}), sidecar grit canisters back {backAtStart.Count} of {loadedBoxGrit.Count}, a hop within 20 s {hop != null}");
        }
    }
}
#endif
