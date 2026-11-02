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
    // config: AutoBuildLayout line, AutoBuildGap 1.3,1.3,5,1.3 (5 m between the boxes for the side U-belts); AutoBuild
    // 96, ryanhartwig.example-grinder, ryanhartwig.box-splitter, ryanhartwig.box-merger, ryanhartwig.example-burner;
    // AutoFeed 96:Dust:12; AutoBelt 96 -> grinder, grinder:Grit out -> box-splitter:In, box-splitter:Forward/Left/Right
    // -> box-merger:Back/Left/Right, box-merger:Out -> burner:Grit in; AutoSave; DumpCards. Splitter outputs take turns
    // in the order Forward, Left, Right; merger inputs in the order Back, Left, Right.
    //
    // While the chain checks run, a feeder puts a 1 kg grit canister near the start of the splitter's input belt whenever
    // there is room (the grinder alone makes too little), and the burner's grit slot is emptied whenever it holds 5 kg
    // (its battery fills after two cycles, and the chain would then stall behind it). Both are counted by the balances.
    //  1 beltboxes-registered: both ids registered at their hashed kinds as a splitter (1 in, 3 out) and a merger (3 in,
    //    1 out), four ports on each clone, halfFootprint 0.70 x 0.70 (Footprint 1.4 x 1.4).
    //  2 beltboxes-model (both runs, both boxes): each placed box's view has the StructureLibModel holder with its
    //    renderers enabled, the donor's mesh renderers hidden and exactly one BoxCollider outside the holder; the clone's
    //    ports are the model's Port_* nodes (ModelSwap.Ports read from the file), and a runtime port lies at each node's
    //    world point. A box missing from the world fails it.
    //  3 beltboxes-split-even: of the first 12 or more hops after the self-test starts, each output had a third ± 1.
    //  4 beltboxes-merge-fair: a sand canister at the end of the merger's output belt (the burner refuses it) lets that
    //    belt fill, so the merger holds; once every input belt has two canisters queued at its end the sand is removed,
    //    and the next six hops come from the three inputs twice each, in turn order (Back, Left, Right, from wherever).
    //  5 beltboxes-conserve: grit the grinder emitted (CargoEmitted) plus grit the feeder put on == grit that entered the
    //    burner (CargoEntered) plus grit riding the five chain belts, to 0.01 kg (a partial intake still balances: the
    //    kilograms entered plus the rider left over make the original canister).
    //  6 beltboxes-heavy: a 3 kg grit canister put at the end of the splitter's input belt hops (a CargoRouted of 3 kg)
    //    and a 3 kg canister of capacity 3 or more then rides the belt it went to; or it waits there, still 3 kg, with
    //    the splitter's refusal logged (within 30 s).
    //  7 beltboxes-split-skips: the Forward belt removed (TryRemoveConveyor), the next six splitter hops go to Left and
    //    Right only (both used), and as many canisters left the input belt as hopped. The canisters that rode the
    //    removed belt now lie in its path and block a new belt there ("Blocked by an object."): they are removed (and
    //    counted), and the Forward belt is rebuilt (AutoBelter.BuildOne, every 2 s up to five times).
    //  8 beltboxes-save-clean: with the merger's output belt held by a sand canister again, the feeder fills the chain
    //    until a canister is stopped at the end of the splitter's input belt and of all three middle belts, and the
    //    middle belts' entrances are full; then a save. The write after it must record both boxes and every grit
    //    canister riding the chain, with every belt still built, the canisters still stopped there, no refused save, and
    //    the grit balance over the whole run (as conserve, plus the heavy canister and the canisters split-skips
    //    removed) to 0.01 kg.
    // Reload run (AutoBuild, AutoFeed and AutoBelt empty, the chain run's slot):
    //  9 beltboxes-reload: the sidecar held both boxes, and both are back with their views; the belts are back at their
    //    ports (one into the splitter's In, Forward/Left/Right each to the merger's Back/Left/Right, one out of the
    //    merger's Out); every grit canister the sidecar held was restored (the sidecar's "N of N restored canisters hold
    //    their modded cargo" line) and is still in the world with cargo 250, or has since hopped through a box or gone
    //    into a machine (the chain moves in the 20 s between the load and the self-test; RouterHop's "canister N ->"
    //    and the intake's "Belt canister N:" lines say so); an unexplained loss fails. The sand canister holding the
    //    merger's output belt is removed (nothing else), and within 20 s the merger hops or the burner takes grit.
    //  beltboxes-model again (both boxes).
    // 10 beltboxes-remove-clean (after those): the merger removed (RemoveStructureNow) with a canister stopped at the end
    //    of its Back belt: no exception or error, the depot gone with Removed raised, and 3 s later the canister still
    //    rides its belt. Nothing is rebuilt; the run ends. (The game saves a few seconds after a removal, which is why
    //    this runs last and not in the chain run, whose save the reload needs with both boxes.)
    internal static partial class SelfTest
    {
        const string SplitterId = "ryanhartwig.box-splitter", MergerId = "ryanhartwig.box-merger";
        static readonly string[] BoxChainNames =
        {
            "beltboxes-split-even", "beltboxes-merge-fair", "beltboxes-conserve", "beltboxes-heavy", "beltboxes-split-skips",
            "beltboxes-save-clean",
        };
        static readonly string[] MergerTurn = { "Back", "Left", "Right" };
        static readonly List<(float t, CargoRouted c)> boxRouted = new();
        static readonly HashSet<string> boxAsserted = new(StringComparer.Ordinal);
        static bool boxRoutedHooked, boxChain, feeding, fillOk;
        static int boxPhase, spl = -1, mrg = -1, brn = -1, grd = -1, bIn = -1, bF = -1, bL = -1, bR = -1, bOut = -1;
        static int boxMark, enteredBoxMark, blockerId = -1, heavyId = -1, boxWaitId = -1, boxWaitBelt = -1, boxSaveSeq = -1, inGone, unityMark, fed, sinkClears;
        static float boxUntil = -1f, boxGrace = -1f, feedNext;
        static double spawnedGrit, removedGrit, sinkKg;
        static string entryF, removeThrew;
        static HashSet<int> inPrev = new(), droppedF = new();
        static List<int> loadedBoxGrit = new(), backAtStart = new();
        static string restoreLine = "none", goneText = "";
        static bool restoredAll, goneExplained;
        static bool savedS, savedM;

        internal static bool BoxPending => boxPhase != 0;

        static void HookBoxEvents()
        {
            if (boxRoutedHooked) return;
            boxRoutedHooked = true;
            MoonStructures.CargoRouted += c => { if (c != null) boxRouted.Add((Time.unscaledTime, c)); };
            // A canister of other than whole kilograms is unusual (the game emits 1 kg); say where one comes from.
            MoonStructures.CargoEmitted += e =>
            {
                if (e != null && Math.Abs(e.Kilograms - Math.Round(e.Kilograms)) > 1e-6)
                    CoreLog.Info($"[selftest] beltboxes: CargoEmitted of {e.Kilograms} kg {e.Cargo.Id} from '{e.Definition?.Id}' depot {e.DepotId} onto belt {e.BeltId}");
            };
        }

        static void BoxChecks()
        {
            HookBoxEvents();
            HookBeltEvents();
            var world = CoreState.World ?? CoreState.FindWorld();
            StructureDefinition s = MoonStructures.Definition(SplitterId), m = MoonStructures.Definition(MergerId);
            if (s == null && m == null)
            {
                CoreLog.Info($"[selftest] BeltBoxes is not registered ('{SplitterId}', '{MergerId}'): beltboxes-registered, beltboxes-model, {string.Join(", ", BoxChainNames)}, beltboxes-reload, beltboxes-remove-clean not applicable");
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
            if (!boxChain && spl < 0 && mrg < 0 && !Sidecar.LoadedIds().Contains(SplitterId) && !Sidecar.LoadedIds().Contains(MergerId))
            {
                CoreLog.Info($"[selftest] no belt box was built (AutoBuild) or saved in the loaded sidecar this run: beltboxes-model, {string.Join(", ", BoxChainNames)}, beltboxes-reload, beltboxes-remove-clean not applicable");
                return;
            }
            ModelCheck(world, s, m);
            if (!boxChain)
            {
                CoreLog.Info($"[selftest] AutoBuild names no belt box (reload run): {string.Join(", ", BoxChainNames)} not applicable");
                ReloadStart(world, s, m);
                return;
            }
            CoreLog.Info("[selftest] AutoBuild builds the belt boxes: beltboxes-reload and beltboxes-remove-clean not applicable (they run in the next launch, on this run's save)");
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

        // beltboxes-model: both boxes, in both runs.
        static void ModelCheck(MoonWorld world, StructureDefinition s, StructureDefinition m)
        {
            var construction = UnityEngine.Object.FindFirstObjectByType<MoonDepotConstruction>();
            Check("beltboxes-model", construction == null ? "no MoonDepotConstruction found" : null, () =>
            {
                var problems = new List<string>();
                foreach (var (d, depot, what) in new[] { (s, spl, "splitter"), (m, mrg, "merger") })
                {
                    if (depot < 0) { problems.Add($"no {what} depot in this world"); continue; }
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
                    case 10: TickRebuild(world, now); break;
                    case 20: TickBoxReload(world, now); break;
                    case 21: TickRemoveWait(world, now); break;
                    case 22: TickRemoved(world, now); break;
                }
            }
            catch (Exception e)
            {
                string why = "threw " + e.GetType().Name + ": " + e.Message;
                feeding = false;
                if (boxPhase == 20) { Assert("beltboxes-reload", false, why); Assert("beltboxes-remove-clean", false, "beltboxes-reload threw"); }
                else if (boxPhase >= 21) Assert("beltboxes-remove-clean", false, why);
                else FailRest(why);
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
            var c = world.canisters.get_Item(id);
            spawnedGrit += c.kilograms;
            fed++;
            CoreLog.Info($"[selftest] beltboxes feeder: canister {id} {c.kilograms} kg (capacity {c.capacity}) on belt {bIn}; fed {fed}, {spawnedGrit} kg so far");
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

        // True when n canisters stand queued at the belt's end: the k-th stopped rider from the end (k = 0 .. n-1) is at
        // d >= length - 0.1 - k * BeltSpacing. A rider stopped elsewhere (just put on, or behind a gap) does not count.
        static bool QueuedAtEnd(MoonWorld world, int belt, int n)
        {
            var path = belt >= 0 ? world.BeltPath(belt) : null;
            if (path == null) return false;
            var stopped = new List<float>();
            foreach (var kv in world.riders) if (kv.Value.belt == belt && !kv.Value.moving) stopped.Add(kv.Value.distance);
            stopped.Sort((a, b) => b.CompareTo(a));
            for (int k = 0; k < n; k++)
                if (k >= stopped.Count || stopped[k] < path.Length - 0.1f - k * MoonWorld.BeltSpacing) return false;
            return true;
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
            bool f = QueuedAtEnd(world, bF, 2), l = QueuedAtEnd(world, bL, 2), r = QueuedAtEnd(world, bR, 2);
            bool ready = blockerId >= 0 && held && f && l && r;
            if (!ready && now < boxUntil) return;
            CoreLog.Info($"[selftest] beltboxes-merge-fair: merger output held {held}, two queued at the end of Back {f} Left {l} Right {r}; {ChainRiders(world)}");
            RemoveBlocker(world);
            if (!ready)
            {
                BoxAssert("beltboxes-merge-fair", false, $"the merger's inputs did not fill within 90 s: output held {held}, two queued at the end of Back {f}, Left {l}, Right {r}");
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

        // The grit balance over the run so far (CargoEmitted and CargoEntered are recorded from the first in-game frame):
        // grinder emits + test canisters == burner intake + riding the chain + removed by the test.
        static (bool ok, string text) Balance(MoonWorld world)
        {
            var em = emitted.Where(x => x.e.DepotId == grd && x.e.Cargo.Value == GritValue).ToList();
            double emittedKg = em.Sum(x => x.e.Kilograms);
            var inB = entered.Where(e => e.DepotId == brn && e.Cargo.Id == GritId).ToList();
            double enteredKg = inB.Sum(e => e.Kilograms);
            var (ridingKg, riding) = ChainGrit(world);
            double put = emittedKg + spawnedGrit, found = enteredKg + ridingKg + removedGrit;
            bool ok = em.Count > 0 && Math.Abs(put - found) <= 0.01;
            return (ok, $"put on {put:0.###} kg (grinder {emittedKg:0.###} in {em.Count} canister(s) + test {spawnedGrit:0.###}: feeder {fed} canister(s){(heavyId >= 0 ? " and the 3 kg one" : "")}), accounted {found:0.###} kg (burner {enteredKg:0.###} in {inB.Count}, its slot emptied {sinkClears}x / {sinkKg:0.###} kg; riding the chain {ridingKg:0.###} in {riding}; removed by the test {removedGrit:0.###}); difference {put - found:F4} kg, allowed 0.01");
        }

        static void TickConserve(MoonWorld world, float now)
        {
            if (now < boxUntil) return;
            var (ok, text) = Balance(world);
            CoreLog.Info($"[selftest] beltboxes-conserve: {text}; {ChainRiders(world)}");
            BoxAssert("beltboxes-conserve", ok, text);
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
                string fresh = "none";
                bool riding = false;
                foreach (var kv in world.riders)
                    if (kv.Value.belt == hop.ToBeltId && world.canisters.ContainsKey(kv.Key) && Math.Abs(world.canisters.get_Item(kv.Key).kilograms - 3.0) < 0.01)
                    {
                        var c = world.canisters.get_Item(kv.Key);
                        fresh = $"canister {kv.Key} {c.kilograms} kg capacity {c.capacity} cargo {(byte)c.cargo} d {kv.Value.distance:F2}";
                        riding = c.capacity >= 3.0 - 1e-9 && (byte)c.cargo == GritValue;
                        break;
                    }
                bool gone = !world.canisters.ContainsKey(heavyId);
                CoreLog.Info($"[selftest] beltboxes-heavy: hopped {Hop(hop)} ({OutName(hop.ToBeltId)}); on that belt: {fresh}; canister {heavyId} gone {gone}");
                BoxAssert("beltboxes-heavy", gone && hop.Cargo.Value == GritValue && riding,
                    $"CargoRouted {Hop(hop)}, arriving canister {heavyId} gone {gone}; a 3 kg grit canister of capacity 3 or more riding belt {hop.ToBeltId}: {fresh}");
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
            // The canisters that rode the removed belt now lie in its path, and the game refuses a belt through them
            // ("Blocked by an object."): they go first, counted as removed by the test.
            var cleared = new List<string>();
            foreach (int id in droppedF)
                if (world.canisters.ContainsKey(id) && !world.riders.ContainsKey(id))
                {
                    var c = world.canisters.get_Item(id);
                    cleared.Add($"{id} ({c.kilograms} kg cargo {(byte)c.cargo})");
                    if ((byte)c.cargo == GritValue) removedGrit += c.kilograms;
                    world.RemoveCanister(id);
                }
            CoreLog.Info($"[selftest] beltboxes-split-skips: canisters left lying where the Forward belt was, removed before rebuilding it: [{string.Join(", ", cleared)}] (of {droppedF.Count} that rode it)");
            // The removed canisters leave the world a frame or more later: the rebuild is tried every 2 s, five times;
            // it is outside every counting window, so the save and the reload have all five belts.
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

        static void StartSaveFill(MoonWorld world) => StartBlock(world, 7, 150f);

        // Full: a canister stopped at the end of each middle belt and of the input belt, the middle belts' entrances
        // taken, so nothing can hop and the queue stands still through the save.
        static (bool inWaits, bool midsWait, bool midsFull) Filled(MoonWorld world, List<int> m) =>
            (QueuedAtEnd(world, bIn, 1), m.All(b => QueuedAtEnd(world, b, 1)), m.All(b => !world.EntranceClear(b)));

        static void TickSaveFill(MoonWorld world, float now)
        {
            var m = Middles(world);
            bool held = !world.EntranceClear(bOut);
            var (inWaits, midsWait, midsFull) = Filled(world, m);
            fillOk = blockerId >= 0 && held && m.Count == 3 && midsWait && midsFull && inWaits;
            if (!fillOk && now < boxUntil) return;
            feeding = false;
            CoreLog.Info($"[selftest] beltboxes-save-clean: before the save: merger output held {held}, middle belts [{string.Join(",", m)}] stopped at the end {midsWait} full {midsFull}, splitter input stopped at the end {inWaits}; {ChainRiders(world)}");
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
            boxPhase = 0;
            var w = Sidecar.LastWrite;
            var m = Middles(world);
            bool belts = world.conveyors.ContainsKey(bIn) && world.conveyors.ContainsKey(bOut) && m.Count == 3;
            var (inWaits, midsWait, _) = Filled(world, m);
            bool still = inWaits && midsWait;
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
            var (balanced, balance) = Balance(world);
            CoreLog.Info($"[selftest] beltboxes-save-clean: write #{w?.Seq} after the request (#{boxSaveSeq}); belts built {belts}, canisters still stopped at the ends {still}; grit canisters riding the chain {live.Count}; sidecar {recorded}; missing from it [{string.Join(", ", missing)}]; 'Moon save failed' {saveFailures}x, vanilla writes that threw {refused}");
            CoreLog.Info($"[selftest] beltboxes-save-clean: balance over the run: {balance}");
            bool ok = fillOk && w != null && w.Seq > boxSaveSeq && belts && still && boxes && live.Count > 0 && missing.Count == 0 && saveFailures == 0 && refused == 0 && balanced;
            BoxAssert("beltboxes-save-clean", ok, $"chain filled {fillOk}, write after the request {(w != null && w.Seq > boxSaveSeq)}, belts built {belts}, still stopped {still}, both boxes in the sidecar {boxes}, grit canisters riding {live.Count} missing from the sidecar [{string.Join(", ", missing)}], 'Moon save failed' {saveFailures}x, refused writes {refused}; balance {(balanced ? "holds" : "broken")}: {balance}");
        }

        // ---- reload run ----

        // The world point of a box's port (its view's pose), or null.
        static Vector3? PortPoint(StructureDefinition d, int depot, string name)
        {
            var view = depot < 0 ? null : UnityEngine.Object.FindFirstObjectByType<MoonDepotConstruction>()?.ViewOf(depot);
            var p = MoonStructures.PortsOf(d)?.FirstOrDefault(x => x != null && x.Name == name);
            if (view == null || p == null) return null;
            return view.transform.position + view.transform.rotation * p.Position;
        }

        static int StartingAt(MoonWorld world, Vector3? at) => at.HasValue ? world.BeltStartingAt(at.Value)?.id ?? -1 : -1;
        static int EndingAt(MoonWorld world, Vector3? at) => at.HasValue ? world.BeltEndingAt(at.Value)?.id ?? -1 : -1;

        static int[] mergerIns = { -1, -1, -1 };

        static void ReloadStart(MoonWorld world, StructureDefinition s, StructureDefinition m)
        {
            var saved = Sidecar.LoadedIds();
            savedS = saved.Contains(SplitterId);
            savedM = saved.Contains(MergerId);
            brn = DepotOfKind(world, MoonStructures.Definition(BurnerId)?.Kind ?? -1);
            bIn = EndingAt(world, PortPoint(s, spl, "In"));
            bF = StartingAt(world, PortPoint(s, spl, "Forward"));
            bL = StartingAt(world, PortPoint(s, spl, "Left"));
            bR = StartingAt(world, PortPoint(s, spl, "Right"));
            mergerIns = new[] { EndingAt(world, PortPoint(m, mrg, "Back")), EndingAt(world, PortPoint(m, mrg, "Left")), EndingAt(world, PortPoint(m, mrg, "Right")) };
            bOut = StartingAt(world, PortPoint(m, mrg, "Out"));
            loadedBoxGrit = Sidecar.LoadedCanisters().Where(c => c.CargoId == GritId).Select(c => c.Id).ToList();
            backAtStart = loadedBoxGrit.Where(id => world.canisters.ContainsKey(id) && (byte)world.canisters.get_Item(id).cargo == GritValue).ToList();
            // The load restored every saved grit canister (the sidecar's own count), and the chain moves in the ~20 s before
            // the self-test starts: a saved canister gone by now must have hopped through a box (RouterHop's "canister N ->"
            // line) or gone into the burner ("Belt canister N: ... into"); any other loss fails.
            string log = ReadWholeLog() ?? "";
            var restore = System.Text.RegularExpressions.Regex.Matches(log, @"Sidecar: ([0-9]+) of ([0-9]+) restored canisters hold their modded cargo");
            var last = restore.Count > 0 ? restore[restore.Count - 1] : null;
            restoreLine = last?.Value ?? "none";
            restoredAll = last != null && last.Groups[1].Value == last.Groups[2].Value && int.Parse(last.Groups[1].Value) >= loadedBoxGrit.Count;
            var gone = loadedBoxGrit.Except(backAtStart).ToList();
            var why = gone.Select(id => (id, how: log.Contains($"canister {id} -> ") ? "hopped" : log.Contains($"Belt canister {id}: ") ? "into a machine" : null)).ToList();
            goneExplained = why.All(x => x.how != null);
            goneText = string.Join(", ", why.Select(x => $"{x.id} {x.how ?? "unexplained"}"));
            CoreLog.Info($"[selftest] beltboxes-reload: the sidecar held splitter {savedS}, merger {savedM}; restored splitter depot {spl}, merger depot {mrg}, burner depot {brn}; belt into the splitter's In {bIn}; out of its Forward/Left/Right {bF}/{bL}/{bR}; into the merger's Back/Left/Right {mergerIns[0]}/{mergerIns[1]}/{mergerIns[2]}; out of its Out {bOut} ({world.conveyors.Count} belts); sidecar grit canisters {loadedBoxGrit.Count}, back with cargo {GritValue} now {backAtStart.Count}; load: '{restoreLine}'; gone since the load: [{goneText}]");
            foreach (int b in new[] { bIn, bF, bL, bR, bOut }.Where(x => x >= 0))
                CoreLog.Info($"[selftest] beltboxes-reload: riders on belt {b}: {RidersText(world, b)}");
            // The chain run saved with sand holding the merger's output belt: only that canister is taken off.
            if (bOut >= 0)
                foreach (int id in RiderIds(world, bOut))
                    if (world.canisters.ContainsKey(id) && world.canisters.get_Item(id).cargo == MoonCargo.Sand)
                    {
                        world.RemoveCanister(id);
                        CoreLog.Info($"[selftest] beltboxes-reload: removed the sand canister {id} holding the merger's output belt {bOut}");
                    }
            boxMark = boxRouted.Count;
            enteredBoxMark = entered.Count;
            boxUntil = Time.unscaledTime + 20f;
            boxPhase = 20;
        }

        static void TickBoxReload(MoonWorld world, float now)
        {
            var hop = boxRouted.Skip(boxMark).Select(x => x.c).FirstOrDefault(c => c.DepotId == mrg);
            var intake = entered.Skip(enteredBoxMark).FirstOrDefault(e => e.DepotId == brn && e.Cargo.Id == GritId);
            bool both = savedS && savedM;
            bool boxes = spl >= 0 && mrg >= 0 && builtViews.Contains(spl) && builtViews.Contains(mrg);
            bool belts = bIn >= 0 && bF >= 0 && bL >= 0 && bR >= 0 && bOut >= 0 && mergerIns[0] == bF && mergerIns[1] == bL && mergerIns[2] == bR && bOut != bIn;
            bool canisters = loadedBoxGrit.Count > 0 && restoredAll && goneExplained;
            bool moved = hop != null || intake != null;
            bool ok = both && boxes && belts && canisters && moved;
            if (!ok && now < boxUntil) return;
            CoreLog.Info($"[selftest] beltboxes-reload: first merger hop {(hop == null ? "none" : Hop(hop))}, first burner intake {(intake == null ? "none" : $"{intake.Kilograms} kg")} within {now - (boxUntil - 20f):F1} s");
            Assert("beltboxes-reload", ok, $"the sidecar held both boxes {both} (splitter {savedS}, merger {savedM}); both back with views {boxes} (splitter {spl}, merger {mrg}); belts at their ports {belts} (In {bIn}; Forward/Left/Right {bF}/{bL}/{bR} into Back/Left/Right {string.Join("/", mergerIns)}; Out {bOut}); sidecar grit canisters {loadedBoxGrit.Count}: load '{restoreLine}', in the world at the self-test {backAtStart.Count}, gone since [{goneText}] all explained {goneExplained}; a merger hop or burner intake within 20 s {moved}");
            StartRemoveWait(world);
        }

        // beltboxes-remove-clean, after beltboxes-reload and beltboxes-model: wait (up to 15 s) for a canister stopped at
        // the end of the merger's Back belt, then remove the merger.
        static void StartRemoveWait(MoonWorld world)
        {
            boxWaitBelt = mrg >= 0 ? mergerIns[0] : -1;
            if (boxWaitBelt < 0) { Assert("beltboxes-remove-clean", false, $"no merger (depot {mrg}) or no belt into its Back port"); boxPhase = 0; return; }
            boxUntil = Time.unscaledTime + 15f;
            boxPhase = 21;
        }

        static void TickRemoveWait(MoonWorld world, float now)
        {
            var path = world.BeltPath(boxWaitBelt);
            boxWaitId = -1;
            float best = -1f;
            foreach (var kv in world.riders)
                if (kv.Value.belt == boxWaitBelt && !kv.Value.moving && kv.Value.distance > best) { best = kv.Value.distance; boxWaitId = kv.Key; }
            bool waiting = boxWaitId >= 0 && path != null && best >= path.Length - 0.1f;
            if (!waiting && now < boxUntil) return;
            if (!waiting)
            {
                Assert("beltboxes-remove-clean", false, $"no canister stopped at the end of the merger's Back belt {boxWaitBelt} within 15 s: {RidersText(world, boxWaitBelt)}");
                boxPhase = 0;
                return;
            }
            unityMark = unityErrors;
            boxSaveSeq = Sidecar.WriteSeq;
            removeThrew = null;
            CoreLog.Info($"[selftest] beltboxes-remove-clean: removing merger depot {mrg} with canister {boxWaitId} stopped at d {best:F2}/{path.Length:F2} of its Back belt {boxWaitBelt}");
            try { world.RemoveStructureNow(mrg); }
            catch (Exception e) { removeThrew = e.GetType().Name + ": " + e.Message; }
            boxUntil = now + 3f;
            boxPhase = 22;
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
            Assert("beltboxes-remove-clean", removeThrew == null && gone && raised && errors == 0 && beltThere && present && riding,
                $"threw {removeThrew ?? "no"}, depot gone {gone}, Removed raised {raised}, Unity errors {errors}, the Back belt {boxWaitBelt} kept {beltThere}, canister {boxWaitId} present {present} riding it {riding}");
        }
    }
}
#endif
