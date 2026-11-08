#if DEVBUILD
using System;
using System.Collections.Generic;
using System.Linq;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    // Look-at panel self-tests (Patches/PanelPatches.cs). Each calls the game's ShowPanel from managed code, so the prefix
    // runs as it does when the walker aims the depot; the texts are read back from the panel's TMP_Text fields.
    //  panel-generator: the example burner's panel is ours (its Name, FUEL POWER, OUTPUT, BATTERY, FUEL, POWERING).
    //  panel-machine: the example grinder's production panel (PRODUCTION, GRIT, CYCLE, no POWER row) and with power a
    //    POWER row first.
    //  panel-vanilla-untouched: a vanilla pressure chamber still gets vanilla's panel ("Pressure chamber").
    internal static partial class SelfTest
    {
        static void PanelChecks()
        {
            string[] names = { "panel-generator", "panel-machine", "panel-vanilla-untouched" };
            var world = CoreState.World ?? CoreState.FindWorld();
            var inter = UnityEngine.Object.FindFirstObjectByType<MoonProductionInteraction>();
            if (inter == null || world == null)
            {
                foreach (var n in names) CoreLog.Info($"[selftest] {n} not applicable: no MoonProductionInteraction");
                return;
            }

            var burner = MoonStructures.Definition(BurnerId);
            int burnerAt = burner != null ? DepotOfKind(world, burner.Kind) : -1;
            if (burnerAt < 0) CoreLog.Info($"[selftest] panel-generator not applicable: no '{BurnerId}' depot in the world");
            else Check("panel-generator", null, () =>
            {
                string texts = PanelOf(inter, world, burnerAt, true, out var rows);
                var want = new[] { burner.Name, "FUEL POWER", "OUTPUT", "BATTERY", "FUEL", "POWERING" };
                var missing = want.Where(w => !texts.Contains(w, StringComparison.Ordinal)).ToList();
                bool rowsOk = rows.SequenceEqual(new[] { "OUTPUT", "BATTERY", "FUEL", "POWERING" });
                return (missing.Count == 0 && rowsOk, $"missing [{string.Join(", ", missing)}], rows [{string.Join(", ", rows)}]; {texts}");
            });

            var grinder = MoonStructures.Definition(GrinderId);
            int grinderAt = grinder != null ? DepotOfKind(world, grinder.Kind) : -1;
            if (grinderAt < 0) CoreLog.Info($"[selftest] panel-machine not applicable: no '{GrinderId}' depot in the world");
            else Check("panel-machine", null, () =>
            {
                string grit = (MoonStructures.CargoDefinition(GritId)?.ShortName ?? "grit").ToUpperInvariant();
                string off = PanelOf(inter, world, grinderAt, false, out var offRows);
                var want = new[] { "PRODUCTION", grit, "CYCLE" };
                var missing = want.Where(w => !off.Contains(w, StringComparison.Ordinal)).ToList();
                bool noPower = !offRows.Contains("POWER");
                string on = PanelOf(inter, world, grinderAt, true, out var onRows);
                bool powerFirst = onRows.Count > 0 && onRows[0] == "POWER";
                bool ok = missing.Count == 0 && noPower && powerFirst;
                return (ok, $"power false: missing [{string.Join(", ", missing)}], rows [{string.Join(", ", offRows)}]; power true: rows [{string.Join(", ", onRows)}]; texts (false) {off}; texts (true) {on}");
            });

            // The chamber AutoBuild '96' built, else any vanilla pressure chamber in the world (slot-2 keeps two).
            int chamber = DevTools.BuildResults.TryGetValue(MoonWorld.PressureChamberKind.ToString(), out var built) && built.ok ? built.depot : -1;
            if (chamber < 0) chamber = DepotOfKind(world, MoonWorld.PressureChamberKind);
            if (chamber < 0) CoreLog.Info("[selftest] panel-vanilla-untouched not applicable: no pressure chamber in the world");
            else Check("panel-vanilla-untouched", null, () =>
            {
                string texts = PanelOf(inter, world, chamber, false, out _);
                return (texts.Contains("Pressure chamber", StringComparison.Ordinal), $"depot {chamber}: {texts}");
            });
        }

        // ShowPanel for the depot, the panel's texts, its shown row labels in order; then hides the panel again.
        static string PanelOf(MoonProductionInteraction inter, MoonWorld world, int depot, bool power, out List<string> rows)
        {
            rows = new List<string>();
            var record = world.depots.get_Item(depot);
            inter.ShowPanel(world, record, power);
            var look = inter.look;
            try
            {
                string texts = PowerUx.Patches.PanelTexts(look);
                for (int i = 0; look?.rows != null && i < look.rows.Length; i++)
                {
                    var r = look.rows[i];
                    if (r?.root == null || !r.root.activeSelf) continue;
                    rows.Add(r.label?.text ?? "");
                }
                CoreLog.Info($"[selftest] panel of depot {depot} (power {power}): {texts}");
                return texts;
            }
            finally { look?.Hide(); }
        }

        // power-vanilla-fed (PowerPatches.SolveGrid, a vanilla consumer): the grid layout (DevPowerGrid.cs) built a vanilla
        // pressure chamber (96) within 10 m of pole 3 and the example burner within 10 m of pole 1, poles 1-2-3 linked, and
        // fed both. Within 60 s, at some tick: the chamber is in the poles' grid, vanilla's solve left it below Power 0.99
        // and the grid raised it to 0.99 or more (GridState.VanillaBefore), the burner supplied kW into the grid at that
        // tick (the energy came from fuel: nothing else feeds the grid), and the chamber's Cycles rose since the check
        // opened. Not applicable when vanilla's own panels power the chamber fully for 20 s (it never needs the grid).
        static int vfChamber = -1, vfBurner = -1, vfCycles;
        static float vfOpened = -1f, vfUntil = -1f, vfBefore = -1f, vfSupplied;
        static bool vfNetwork;

        static bool VanillaFedPending => vfUntil >= 0f;

        static void VanillaFedChecks()
        {
            const string name = "power-vanilla-fed";
            if (!GridLayout.Named) { CoreLog.Info($"[selftest] {name} not applicable: [Dev] AutoBuild does not name '{GridLayout.Token}'"); return; }
            string refusal = GridLayout.Refusal("chamber", "burner", "pole1", "pole2", "pole3");
            if (refusal != null) { CoreLog.Info($"[selftest] {name} not applicable: {refusal}; {Surroundings(GridLayout.Depot("burner"))}"); return; }
            var world = CoreState.World ?? CoreState.FindWorld();
            vfChamber = GridLayout.Depot("chamber");
            vfBurner = GridLayout.Depot("burner");
            var chamber = world?.ProductionOf(vfChamber);
            if (chamber == null) { Assert(name, false, $"chamber depot {vfChamber} has no production state"); return; }
            vfCycles = chamber.Cycles;
            vfBefore = -1f;
            vfSupplied = 0f;
            vfNetwork = false;
            vfOpened = Time.unscaledTime;
            vfUntil = vfOpened + 60f;
            CoreLog.Info($"[selftest] {name} opened: chamber {VanillaText(chamber)}; burner {PowerText(world.ProductionOf(vfBurner))}");
        }

        static void TickVanillaFed()
        {
            const string name = "power-vanilla-fed";
            var world = CoreState.World ?? CoreState.FindWorld();
            MoonProductionState c = null, b = null;
            try { c = world?.ProductionOf(vfChamber); b = world?.ProductionOf(vfBurner); } catch (Exception) { }
            int poleGrid = GridLayout.PoleGrid();
            if (GridState.GridOfDepot(vfChamber, out int g) && g == poleGrid && GridState.GridOfDepot(vfBurner, out int bg) && bg == poleGrid) vfNetwork = true;
            float now = Time.unscaledTime;
            bool raisedNow = GridState.VanillaBefore.TryGetValue(vfChamber, out float before) && before < 0.99f && c != null && c.Power >= 0.99f;
            if (raisedNow && vfBefore < 0f && b != null && b.SuppliedKw > 0f)
            {
                vfBefore = before;
                vfSupplied = b.SuppliedKw;
                CoreLog.Info($"[selftest] {name}: chamber raised from {before:F2} by grid {poleGrid} (burner supplies {vfSupplied:F2} kW); chamber {VanillaText(c)}; burner {PowerText(b)}");
            }
            if (vfBefore < 0f && now - vfOpened >= 20f && c != null && c.Power >= 0.99f && !GridState.VanillaBefore.ContainsKey(vfChamber))
            {
                vfUntil = -1f;
                CoreLog.Info($"[selftest] {name} not applicable: vanilla already powers the chamber fully (chamber {VanillaText(c)}; burner {PowerText(b)})");
                return;
            }
            if (c == null || b == null) { vfUntil = -1f; Assert(name, false, $"chamber {VanillaText(c)}; burner {PowerText(b)}"); return; }
            bool ok = vfNetwork && vfBefore >= 0f && c.Cycles > vfCycles;
            if (!ok && now < vfUntil) return;
            vfUntil = -1f;
            Assert(name, ok, $"in the poles' grid {poleGrid}: {vfNetwork}, raised seen {vfBefore >= 0f} (power before {vfBefore:F2}, burner supplying {vfSupplied:F2} kW), cycles {vfCycles} -> {c.Cycles}; chamber {VanillaText(c)}; burner {PowerText(b)}");
            if (ok) CoreLog.Info($"[selftest] {name}: cycles {vfCycles} -> {c.Cycles}; chamber {VanillaText(c)}; burner {PowerText(b)}");
        }

        // Where the walker and the depots within 40 m of the given one stand (to pick a free [Dev] PowerUxHome).
        static string Surroundings(int depot)
        {
            try
            {
                var world = CoreState.World ?? CoreState.FindWorld();
                var walker = UnityEngine.Object.FindFirstObjectByType<MoonWalker>();
                string at = walker == null ? "no walker" : $"walker at {PowerUx.V(walker.transform.position)}";
                if (world?.depots == null || depot < 0 || !world.depots.ContainsKey(depot)) return at;
                var centre = PowerUx.At(world, depot);
                var near = new List<string>();
                foreach (var kv in world.depots)
                {
                    var p = PowerUx.At(world, kv.Key);
                    float d = Vector3.Distance(centre, p);
                    if (d <= 40f) near.Add($"{kv.Key} kind {kv.Value.kind} at {PowerUx.V(p)} ({d:F1} m)");
                }
                return $"{at}; depots within 40 m of {depot}: {string.Join("; ", near)}";
            }
            catch (Exception e) { return "surroundings threw " + e.Message; }
        }

        // arc-drawn (PowerLinksView): with [Dev] ForceArcs, within 60 s after power-vanilla-fed or
        // power-grinder-runs-on-burner opened (the pole layout is built), the library's MoonPowerLinks drew at least one
        // arc in a frame (a pole-to-pole link or a pole-to-member reach arc). Not applicable without ForceArcs (the
        // build-tool and aim gate is not driven here) or without either check.
        static float arcOpened = -1f, arcUntil = -1f;

        static bool ArcPending => arcUntil >= 0f;

        static void ArcChecks()
        {
            const string name = "arc-drawn";
            if (DevTools.ForceArcs == null || !DevTools.ForceArcs.Value) { CoreLog.Info($"[selftest] {name} not applicable: [Dev] ForceArcs is false"); return; }
            if (!VanillaFedPending && !powerOpen.ContainsKey("power-grinder-runs-on-burner"))
            {
                CoreLog.Info($"[selftest] {name} not applicable: neither power-vanilla-fed nor power-grinder-runs-on-burner is open");
                return;
            }
            arcOpened = Time.unscaledTime;
            arcUntil = arcOpened + 60f;
            CoreLog.Info($"[selftest] {name} opened: ArcsDrawn {PowerLinksView.ArcsDrawn}; fed {FedText()}");
            UnblockArcMachines();
        }

        // A reused world (slot-2) keeps machines whose outputs filled in earlier runs: with nothing belting them away they
        // never start a cycle, demand no power, and no generator feeds anything, so there is no arc to draw. With ForceArcs
        // only, empty the full output slots of the example grinder and of the pressure chambers within 20 m of the burner,
        // so they draw power in the next solves (from whichever fuel generator of the network draws first).
        static void UnblockArcMachines()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            if (world?.depots == null) return;
            var grinder = MoonStructures.Definition(GrinderId);
            var burner = MoonStructures.Definition(BurnerId);
            int burnerAt = burner != null ? DepotOfKind(world, burner.Kind) : -1;
            var targets = new List<(int depot, int firstOutput)>();
            if (grinder != null && DepotOfKind(world, grinder.Kind) is int g && g >= 0) targets.Add((g, 2));
            if (burnerAt >= 0)
            {
                var at = PowerUx.At(world, burnerAt);
                foreach (var kv in world.depots)
                    if (kv.Value != null && kv.Value.kind == MoonWorld.PressureChamberKind && Vector3.Distance(at, PowerUx.At(world, kv.Key)) <= 20f) targets.Add((kv.Key, 1));
            }
            foreach (var (depot, first) in targets) EmptyOutputs(world, depot, first);
        }

        internal static void EmptyOutputs(MoonWorld world, int depot, int first, string who = "[selftest] arc-drawn")
        {
            try
            {
                var s = world.ProductionOf(depot);
                if (s?.stores == null) return;
                for (int slot = first; slot < s.stores.Length; slot++)
                {
                    if (s.Fill(slot) < 0.999f) continue;
                    double kg = s.Stored(slot);
                    s.stores[slot].Clear();
                    CoreLog.Info($"{who}: emptied the full output slot {slot} ({kg:F1} kg) of depot {depot} (kind {s.Kind}) so it can run");
                }
            }
            catch (Exception e) { CoreLog.Warn($"{who}: emptying depot {depot}'s output threw {e.Message}"); }
        }

        static void TickArc()
        {
            const string name = "arc-drawn";
            int arcs = PowerLinksView.ArcsDrawn;
            float now = Time.unscaledTime;
            if (arcs < 1 && now < arcUntil) return;
            arcUntil = -1f;
            Assert(name, arcs >= 1, $"ArcsDrawn {arcs} after {now - arcOpened:F1} s; fed {FedText()}");
            if (arcs < 1) return;
            CoreLog.Info($"[selftest] {name}: {arcs} arc(s) after {now - arcOpened:F1} s; fed {FedText()}");
            // A side-on screenshot of a pole and a member it reaches, once the probe's tour is over (it moves the walker
            // for about 90 s from 24 s after load). The pressure chambers within 20 m of the burner get their full outputs
            // emptied first; the pair framed is the layout's burner and the pole that reaches it, else any reached pair.
            var world = CoreState.World ?? CoreState.FindWorld();
            int burnerAt = world?.depots != null ? GridLayout.Depot("burner") : -1;
            shotLoad.Clear();
            if (burnerAt >= 0)
                foreach (var kv in world.depots)
                    if (kv.Value != null && kv.Value.kind == MoonWorld.PressureChamberKind && Vector3.Distance(PowerUx.At(world, burnerAt), PowerUx.At(world, kv.Key)) <= 20f) shotLoad.Add(kv.Key);
            shotBurner = burnerAt;
            if (burnerAt >= 0 && DevTools.PowerUxProbe != null && DevTools.PowerUxProbe.Value) { shotAt = now + 100f; shotStage = 0; }
        }

        static readonly List<int> shotLoad = new();

        static int shotBurner = -1, shotGen = -1, shotMember = -1, shotStage;
        static float shotAt = -1f, shotDeadline = -1f;
        static Vector3 shotLook;

        internal static bool ArcShotPending => shotAt >= 0f;

        // arc-drawn's screenshot (BepInEx/cache/structurelib/powerux-<n>.png, after the tour's): empty the chambers' full
        // outputs, wait (up to 15 s) for a solve that feeds something, stand 14 m off the generator-machine midpoint square to
        // the pair (the open side), face it, shoot.
        static void TickArcShot()
        {
            float now = Time.unscaledTime;
            if (now < shotAt) return;
            var world = CoreState.World ?? CoreState.FindWorld();
            var walker = UnityEngine.Object.FindFirstObjectByType<MoonWalker>();
            if (world == null || walker == null) { shotAt = -1f; return; }
            try
            {
                if (shotStage == 0)
                {
                    foreach (int d in shotLoad) EmptyOutputs(world, d, 1);
                    shotStage = 1; shotAt = now + 3f; shotDeadline = now + 15f;
                    return;
                }
                if (shotStage == 1)
                {
                    // The pole that reaches the burner, and the burner (else any reached pair).
                    var pairs = GridState.Reach.Where(r => r.Member == shotBurner).ToList();
                    if (pairs.Count == 0) pairs = GridState.Reach.ToList();
                    if (pairs.Count == 0)
                    {
                        if (now < shotDeadline) return;
                        shotAt = -1f;
                        CoreLog.Warn("[selftest] arc-drawn shot: no pole reached anything within 15 s; no screenshot");
                        return;
                    }
                    shotGen = pairs[0].Pole;
                    shotMember = pairs[0].Member;
                    var a = PowerUx.At(world, shotGen); var b = PowerUx.At(world, shotMember);
                    var mid = (a + b) / 2f; var up = mid.normalized;
                    var side = Vector3.Cross(up, (b - a).normalized).normalized;
                    // The side whose view of the pair is open (no hit nearer than 60% of the way, not on a structure), then the
                    // one furthest from every depot.
                    Vector3 best = Vector3.zero, bestUp = up; float bestFree = -1f;
                    foreach (float sgn in new[] { 1f, -1f })
                    {
                        var g = world.SurfaceAt((mid + side * sgn * 14f).normalized, out Vector3 gu, out bool onStructure);
                        var eye = g + gu * 1.6f; var look = mid + up * 1.5f;
                        bool blocked = Physics.Raycast(eye, (look - eye).normalized, out RaycastHit hit, Vector3.Distance(eye, look)) && hit.distance < 0.6f * Vector3.Distance(eye, look);
                        float free = onStructure || blocked ? -0.5f : float.MaxValue;
                        if (!onStructure) foreach (var kv in world.depots) free = Mathf.Min(free, Vector3.Distance(g, PowerUx.At(world, kv.Key)));
                        CoreLog.Info($"[selftest] arc-drawn shot: side {sgn}: ground {PowerUx.V(g)} onStructure {onStructure} view blocked {blocked}, {free:F1} m from the nearest depot");
                        if (free > bestFree) { bestFree = free; best = g; bestUp = gu; }
                    }
                    walker.Teleport(best + bestUp * 0.2f, true);
                    shotLook = mid + up * 1.5f;
                    CoreLog.Info($"[selftest] arc-drawn shot: pole {shotGen}, member {shotMember}; walker to {PowerUx.V(best)} ({bestFree:F1} m from the nearest depot), looking at {PowerUx.V(shotLook)}");
                    shotStage = 2; shotAt = now + 4f;
                    return;
                }
                var t = walker.transform;
                var wup = t.position.normalized;
                var to = shotLook - (t.position + wup * 1.6f);
                var flat = Vector3.ProjectOnPlane(to, wup);
                walker.Face(flat.normalized, Mathf.Atan2(Vector3.Dot(to, wup), flat.magnitude) * Mathf.Rad2Deg);
                if (shotStage == 2) { shotStage = 3; shotAt = now + 1.5f; return; }
                shotAt = -1f;
                CoreLog.Info($"[selftest] arc-drawn shot: ArcsDrawn {PowerLinksView.ArcsDrawn}; fed {FedText()}");
                PowerUx.Tour.Shot($"arc-drawn: pole {shotGen} and member {shotMember}, side on");
            }
            catch (Exception e) { shotAt = -1f; CoreLog.Warn("[selftest] arc-drawn shot threw: " + e.Message); }
        }

        static string FedText() => GridState.Grids.Count == 0 ? "no pole grid" : string.Join("; ", GridState.Grids.Values.Select(g =>
            $"grid {g.Id}: poles [{string.Join(", ", g.PoleIds)}], members [{string.Join(", ", g.MemberIds)}], fed {g.Fed}, demand {g.DemandKw:F2} kW, satisfaction {g.Satisfaction:F2}")) +
            $"; {GridState.PoleLinks.Count} link(s), {GridState.Reach.Count} reach arc(s)";

        // cargo-tint (Patches/CanisterPatches.cs). Views are pooled: a view that showed tinted grit and is reused for a
        // dust (or other) canister must come back white. The check drives the walker, so it starts once every other
        // pending check is done (and, with the probe on, once its tour is over) and no save is in flight; after 240 s of
        // waiting it is not applicable and changes nothing. Every exit puts the borrowed canister back to dust and the walker back where it started:
        //  1. The ground dust canister nearest the walker (within 40 m) that has a near view and another dust canister
        //     within 2 m is borrowed. The walker goes 250 m away (the spot's near views go back to the pool), the canister
        //     is made grit in place (MoveCanisterRecord; slot-2's PackDustCanister refuses new canisters, and a view
        //     does not re-show a cargo changed under it), the walker comes back to 2.8 m off it, so the game places the
        //     views again; a close-up screenshot is taken.
        //  2. The walker goes 250 m away again, the canister is made dust again, the walker comes back: the views are
        //     placed again from the pool, so a view that showed grit is reused for another canister.
        // Every 0.5 s from the first return on, each near view (MoonCanisterStreaming.views, canister id -> view; cargo
        // from world.canisters) must have _BaseColor on its own block (properties) and its dust renderer's block equal to
        // grit's CanisterTint (within 0.01) for grit, a modded cargo's own tint for one that has it, white for every other
        // cargo; a reading wrong for 1.5 s in a row is a failure.
        // Pass: the grit canister's view matched, dust views read white, a view that showed grit was later seen with the
        // right colour on another cargo (white, or that modded cargo's own tint),
        // nothing read wrong. Not applicable when no dust canister could be borrowed, when making it grit fails, when the
        // pool never reused a grit view, or when the world, streaming or walker goes away; the whole check stops at 300 s.
        static readonly int TintColor = Shader.PropertyToID("_BaseColor");
        static float ctOpened = -1f, ctUntil = -1f, ctNext, ctStageAt;
        static int ctStage, ctGrit = -1, ctSamples;
        static bool ctGritOk, ctDustOk, ctReused, ctPutBack;
        // CanisterModel (CanisterModels.cs): grit's view shows its model holder with the capsule hidden, and a view that
        // showed it gets the capsule back for another cargo. Only judged when grit is registered with a CanisterModel.
        static bool ctGritModel, ctModelOk, ctModelBack;
        static byte ctGritValue;
        static Color ctTint;
        static Vector3 ctSpot, ctStand, ctFar, ctLook, ctHome;
        static bool ctHomeSet;
        // The wait for the other checks, the probe tour and any save in flight; then about 17 s of stages. ctUntil (opened
        // + WaitLimit + 60 s) is the hard stop for the whole check.
        const float WaitLimit = 240f;
        static readonly List<string> ctBad = new();
        static readonly List<string> ctSeen = new();
        static readonly Dictionary<int, int> ctTinted = new();          // view instance id -> the grit canister it showed
        static readonly Dictionary<string, float> ctWrongSince = new();  // "view:canister:cargo" -> first time it read wrong

        static bool TintPending => ctUntil >= 0f;

        static void CargoTintChecks()
        {
            const string name = "cargo-tint";
            var grit = MoonStructures.CargoDefinition(GritId);
            if (grit?.CanisterTint == null) { CoreLog.Info($"[selftest] {name} not applicable: '{GritId}' is not registered with a CanisterTint"); return; }
            var world = CoreState.World ?? CoreState.FindWorld();
            var streaming = UnityEngine.Object.FindFirstObjectByType<MoonCanisterStreaming>();
            if (world == null || streaming == null) { CoreLog.Info($"[selftest] {name} not applicable: no {(world == null ? "MoonWorld" : "MoonCanisterStreaming")}"); return; }
            ctTint = grit.CanisterTint.Value;
            ctGritValue = grit.Value;
            ctGritModel = grit.CanisterModelPath != null;
            ctModelOk = ctModelBack = false;
            ctOpened = Time.unscaledTime;
            ctUntil = ctOpened + WaitLimit + 60f;
            ctStage = 0;
            CoreLog.Info($"[selftest] {name} opened: grit value {ctGritValue} tint {PowerUx.C(ctTint)}{(ctGritModel ? $" canister model '{grit.CanisterModel}'" : "")}; ActiveViews {streaming.ActiveViews}; waits for the other checks{(DevTools.PowerUxProbe?.Value == true ? " and the probe's tour" : "")}");
        }

        static bool OthersPending => cycleUntil >= 0f || PowerPending || GridPending || VanillaFedPending || ArcPending || ArcShotPending || sandOpen.Count > 0 ||
            BeltPending || OutPending || ModelPending || SandChainPending || BoxPending || artUntil >= 0f || renderUntil >= 0f;

        // The canister nearest `near` (within the radius) that is a ground dust canister (not riding, carried or flying),
        // has a near view now (so the game places one at its spot), and has another ground dust canister within 2 m.
        static int BorrowableDust(MoonWorld world, MoonCanisterStreaming streaming, Vector3 near, float within)
        {
            var all = new List<(int id, Vector3 p)>();
            foreach (var kv in world.canisters)
            {
                var c = kv.Value;
                if (c == null || (byte)c.cargo != 0) continue;
                if (world.IsCanisterRiding(kv.Key) || world.IsCanisterCarried(kv.Key) || world.IsCanisterFlying(kv.Key)) continue;
                all.Add((kv.Key, c.position));
            }
            int best = -1; float bd = within;
            foreach (var (id, p) in all)
            {
                if (!streaming.views.ContainsKey(id)) continue;
                float d = Vector3.Distance(near, p);
                if (d >= bd || !all.Any(o => o.id != id && Vector3.Distance(o.p, p) <= 2f)) continue;
                bd = d; best = id;
            }
            return best;
        }

        // The canister's record again with another cargo, where it lies (MoveCanisterRecord, as SelfTest.Spawn does).
        static bool Recargo(MoonWorld world, int id, MoonCargo cargo)
        {
            try
            {
                if (id < 0 || !world.canisters.ContainsKey(id)) return false;
                var c = world.canisters.get_Item(id);
                var rotation = new Il2CppSystem.Nullable<Quaternion>(c.rotation);
                var moved = new MoonDroppedCanister(c.id, c.owner, c.kilograms, c.capacity, c.position, c.up, c.createdAt, rotation, cargo);
                moved.physicalPose = c.physicalPose;
                world.MoveCanisterRecord(moved, c.position, c.up, rotation);
                return world.canisters.ContainsKey(id) && world.canisters.get_Item(id).cargo == cargo;
            }
            catch (Exception e) { CoreLog.Warn($"[selftest] cargo-tint: re-cargoing canister {id} to {(byte)cargo} threw {e.Message}"); return false; }
        }

        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) <= 0.01f && Mathf.Abs(a.g - b.g) <= 0.01f && Mathf.Abs(a.b - b.b) <= 0.01f;

        // The two _BaseColor readings of a view: its own block and its dust renderer's block (null when either is missing).
        static (Color own, Color dust)? ViewColours(MoonDustCanisterView v)
        {
            var own = v.properties;
            var r = v.dustRenderer;
            if (own == null || r == null) return null;
            var b = new MaterialPropertyBlock();
            r.GetPropertyBlock(b);
            return (own.GetColor(TintColor), b.GetColor(TintColor));
        }

        static void Stand(MoonWalker walker, Vector3 at)
        {
            var ground = (CoreState.World ?? CoreState.FindWorld()).SurfaceAt(at.normalized, out Vector3 gUp, out bool _);
            walker.Teleport(ground + gUp * 0.2f, true);
        }

        static void LookAt(MoonWalker walker, Vector3 target, float sign)
        {
            var t = walker.transform;
            var up = t.position.normalized;
            var to = target - (t.position + up * 1.6f);
            var flat = Vector3.ProjectOnPlane(to, up);
            walker.Face(flat.normalized, sign * Mathf.Atan2(Vector3.Dot(to, up), flat.magnitude) * Mathf.Rad2Deg);
        }

        // A save the world has in flight, or an AutoSave save requested and not yet written: the check changes a canister's
        // cargo and moves the walker, neither of which may be saved.
        static bool SaveInFlight(MoonWorld world)
        {
            try { var p = world?.PendingSave; if (p != null && !p.IsCompleted) return true; } catch (Exception) { }
            return DevTools.AutoSave.Value && DevTools.SaveRequested && Sidecar.WriteSeq <= DevTools.SaveSeqAtRequest;
        }

        static void TickCargoTint()
        {
            const string name = "cargo-tint";
            float now = Time.unscaledTime;
            var world = CoreState.World ?? CoreState.FindWorld();
            var streaming = UnityEngine.Object.FindFirstObjectByType<MoonCanisterStreaming>();
            var walker = UnityEngine.Object.FindFirstObjectByType<MoonWalker>();
            if (world == null || streaming == null || walker == null) { TintFinish(name, world, walker, $"the {(world == null ? "MoonWorld" : streaming == null ? "MoonCanisterStreaming" : "MoonWalker")} went away (stage {ctStage})"); return; }
            if (now > ctUntil) { TintFinish(name, world, walker, $"timed out at stage {ctStage} after {now - ctOpened:F0} s"); return; }
            try
            {
                if (ctStage == 0)
                {
                    // Nothing in the world is changed until this wait is over; on timeout nothing is changed at all.
                    bool tour = DevTools.PowerUxProbe?.Value == true && !PowerUx.Tour.Finished;
                    bool others = OthersPending, saving = SaveInFlight(world);
                    if (others || tour || saving)
                    {
                        if (now < ctOpened + WaitLimit) return;
                        TintFinish(name, world, walker, $"still waiting after {WaitLimit:F0} s (other checks pending {others}, probe tour running {tour}, save in flight {saving})");
                        return;
                    }
                    ctGrit = BorrowableDust(world, streaming, walker.transform.position, 40f);
                    if (ctGrit < 0) { TintFinish(name, world, walker, "no ground dust canister with a near view (and another beside it) within 40 m of the walker to borrow"); return; }
                    ctHome = walker.transform.position;
                    ctHomeSet = true;
                    ctSpot = world.canisters.get_Item(ctGrit).position;
                    var up = ctSpot.normalized;
                    var away = Vector3.ProjectOnPlane(ctHome - ctSpot, up).normalized;
                    if (away.sqrMagnitude < 0.5f) away = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
                    ctStand = ctSpot + away * 2.8f;
                    ctFar = ctSpot + away * 250f;
                    ctLook = ctSpot + up * 0.15f;
                    Stand(walker, ctFar);
                    CoreLog.Info($"[selftest] {name}: started after {now - ctOpened:F1} s; walker home {PowerUx.V(ctHome)}; borrowing dust canister {ctGrit} at {PowerUx.V(ctSpot)} (view {streaming.views.get_Item(ctGrit).GetInstanceID()}); walker 250 m off");
                    ctStage = 1; ctStageAt = now + 3f;
                    return;
                }
                if (ctStage >= 2) Sample(world, streaming, now);
                if (now < ctStageAt) return;
                switch (ctStage)
                {
                    case 1:
                    {
                        bool viewed = streaming.views.ContainsKey(ctGrit);
                        if (!Recargo(world, ctGrit, (MoonCargo)ctGritValue)) { TintFinish(name, world, walker, $"could not make canister {ctGrit} grit (MoveCanisterRecord did not take; near view while away {viewed})"); return; }
                        Stand(walker, ctStand);
                        CoreLog.Info($"[selftest] {name}: canister {ctGrit} near view while away {viewed}; made grit; walker back to {PowerUx.V(ctStand)}");
                        ctStage = 2; ctStageAt = now + 3f; return;
                    }
                    case 2: LookAt(walker, ctLook, -1f); ctStage = 3; ctStageAt = now + 1.5f; return;
                    case 3: ShotLine(streaming, "close-up"); PowerUx.Tour.Shot($"cargo-tint: grit canister {ctGrit} among dust canisters"); ctStage = 4; ctStageAt = now + 1f; return;
                    case 4:
                        Stand(walker, ctFar);
                        CoreLog.Info($"[selftest] {name}: walker 250 m off again; views that showed grit so far: [{string.Join(", ", ctTinted.Select(kv => $"{kv.Key} (canister {kv.Value})"))}]");
                        ctStage = 5; ctStageAt = now + 3f; return;
                    case 5:
                    {
                        bool viewed = streaming.views.ContainsKey(ctGrit);
                        ctPutBack = Recargo(world, ctGrit, MoonCargo.Dust);
                        Stand(walker, ctStand);
                        CoreLog.Info($"[selftest] {name}: canister {ctGrit} near view while away {viewed}; made dust again {ctPutBack}; walker back; while away: {ViewsText(streaming)}");
                        ctStage = 6; ctStageAt = now + 4f; return;
                    }
                    case 6: LookAt(walker, ctLook, -1f); CoreLog.Info($"[selftest] {name}: back: {ViewsText(streaming)}"); ctStage = 7; ctStageAt = now + 1.5f; return;
                }
                TintFinish(name, world, walker, null);
            }
            catch (Exception e)
            {
                ctBad.Add($"the check threw {e.GetType().Name} at stage {ctStage}: {e.Message}");
                TintFinish(name, world, walker, null);
            }
        }

        static void ShotLine(MoonCanisterStreaming streaming, string how)
        {
            var v = streaming.views.ContainsKey(ctGrit) ? streaming.views.get_Item(ctGrit) : null;
            var got = v != null ? ViewColours(v) : null;
            CoreLog.Info($"[selftest] cargo-tint shot ({how}): canister {ctGrit}: {(v == null ? "no near view" : got == null ? $"view {v.GetInstanceID()} no block" : $"view {v.GetInstanceID()} own {PowerUx.C(got.Value.own)} dust renderer {PowerUx.C(got.Value.dust)}")}; ActiveViews {streaming.ActiveViews}");
        }

        static void Sample(MoonWorld world, MoonCanisterStreaming streaming, float now)
        {
            if (now < ctNext || streaming.views == null) return;
            ctNext = now + 0.5f;
            ctSamples++;
            var live = new HashSet<string>();
            foreach (var kv in streaming.views)
            {
                int id = kv.Key;
                var v = kv.Value;
                if (v == null || !world.canisters.ContainsKey(id)) continue;
                byte cargo = (byte)world.canisters.get_Item(id).cargo;
                // Grit wants its tint; every other cargo its own CanisterTint when it is a modded one with a tint, else white.
                var want = cargo == ctGritValue ? ctTint : (MoonStructures.IsModded((MoonCargo)cargo) ? MoonStructures.CargoDefinition((MoonCargo)cargo)?.CanisterTint : null) ?? Color.white;
                var got = ViewColours(v);
                int vid = v.GetInstanceID();
                // A cargo with a CanisterModel wants its holder on and no capsule renderer enabled; every other cargo wants
                // no holder on and the capsule enabled (what CanisterModels restores on a pooled view).
                bool wantModel = MoonStructures.IsModded((MoonCargo)cargo) && MoonStructures.CargoDefinition((MoonCargo)cargo)?.CanisterModelPath != null;
                var look = ModelLook(v, cargo);
                bool modelWrong = wantModel ? !look.model || look.capsule : look.model || !look.capsule;
                string text = (got == null ? "no block" : $"own {PowerUx.C(got.Value.own)} dust renderer {PowerUx.C(got.Value.dust)}") + $" model {look.model} capsule {look.capsule}";
                if (got == null || !Near(got.Value.own, want) || !Near(got.Value.dust, want) || modelWrong)
                {
                    string key = $"{vid}:{id}:{cargo}";
                    live.Add(key);
                    if (!ctWrongSince.TryGetValue(key, out float since)) ctWrongSince[key] = since = now;
                    if (now - since >= 1.5f && ctBad.Count < 6 && !ctBad.Any(b => b.StartsWith($"view {vid} canister {id} ", StringComparison.Ordinal)))
                        ctBad.Add($"view {vid} canister {id} cargo {cargo}: {text} for {now - since:F1} s, want {PowerUx.C(want)}{(wantModel ? " model on, capsule off" : " capsule on, no model")}");
                    continue;
                }
                if (cargo == ctGritValue)
                {
                    if (id == ctGrit && !ctGritOk) { ctGritOk = true; CoreLog.Info($"[selftest] cargo-tint: grit canister {id} view {vid} reads {text}"); }
                    if (id == ctGrit && wantModel) ctModelOk = true;
                    ctTinted[vid] = id;
                }
                else
                {
                    if (!ctDustOk && cargo == 0) CoreLog.Info($"[selftest] cargo-tint: dust canister {id} view {vid} reads {text}");
                    if (cargo == 0) ctDustOk = true;
                    if (ctTinted.TryGetValue(vid, out int was) && !ctReused)
                    {
                        ctReused = true;
                        CoreLog.Info($"[selftest] cargo-tint: view {vid} showed grit canister {was} and now shows canister {id} of cargo {cargo}: {text}");
                    }
                    if (ctTinted.ContainsKey(vid) && ctGritModel && !wantModel) ctModelBack = true;
                }
                if (ctSeen.Count < 6 && !ctSeen.Any(s => s.StartsWith($"view {vid} canister {id} ", StringComparison.Ordinal))) ctSeen.Add($"view {vid} canister {id} cargo {cargo}: {text}");
            }
            foreach (var k in ctWrongSince.Keys.Where(k => !live.Contains(k)).ToList()) ctWrongSince.Remove(k);
        }

        // Whether the view's holder for this cargo (CanisterModels.HolderName) is on, and whether any renderer outside the
        // library's holders is enabled (the capsule, whichever of its parts vanilla has active).
        static (bool model, bool capsule) ModelLook(MoonDustCanisterView v, byte cargo)
        {
            bool model = false, capsule = false;
            var holders = new List<Transform>();
            var root = v.transform;
            foreach (var t in v.GetComponentsInChildren<Transform>(true))
            {
                if (t.parent == null || t.parent.Pointer != root.Pointer || !t.name.StartsWith(CanisterModels.HolderPrefix, StringComparison.Ordinal)) continue;
                holders.Add(t);
                if (t.name == CanisterModels.HolderName(cargo) && t.gameObject.activeSelf) model = true;
            }
            foreach (var r in v.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || holders.Any(h => r.transform.IsChildOf(h))) continue;
                capsule = true;
                break;
            }
            return (model, capsule);
        }

        // The near views (instance:canister) and the pool size; a view that showed grit is marked *.
        static string ViewsText(MoonCanisterStreaming s)
        {
            var parts = new List<string>();
            foreach (var kv in s.views) if (kv.Value != null) { int vid = kv.Value.GetInstanceID(); parts.Add($"{vid}:{kv.Key}{(ctTinted.ContainsKey(vid) ? "*" : "")}"); }
            return $"ActiveViews {s.ActiveViews}, pool {s.pool?.Count ?? -1}, views [{string.Join(" ", parts)}]";
        }

        // Every exit of the check comes here, once: the borrowed canister back to dust, the walker back where it stood when
        // the check started, then the verdict. `skip` set: not applicable with that reason. A run whose grit and dust views
        // read right but whose pool never reused a grit view for another canister is not applicable too (nothing wrong
        // was seen, the reuse just did not happen).
        static void TintFinish(string name, MoonWorld world, MoonWalker walker, string skip)
        {
            ctUntil = -1f;
            try
            {
                if (ctGrit >= 0 && world != null && world.canisters.ContainsKey(ctGrit))
                    ctPutBack = world.canisters.get_Item(ctGrit).cargo == MoonCargo.Dust || Recargo(world, ctGrit, MoonCargo.Dust);
            }
            catch (Exception e) { CoreLog.Warn($"[selftest] {name}: putting canister {ctGrit} back to dust threw {e.Message}"); }
            // The world went away after the borrowed canister was made grit: it could not be put back, so the check fails
            // rather than read not-applicable.
            if (world == null && ctGrit >= 0 && ctStage >= 2 && !ctPutBack && ctBad.Count == 0) ctBad.Add($"canister {ctGrit} left grit");
            if (ctHomeSet)
            {
                try
                {
                    if (walker != null) { walker.Teleport(ctHome, true); CoreLog.Info($"[selftest] {name}: walker back home {PowerUx.V(ctHome)} (now at {PowerUx.V(walker.transform.position)}, {Vector3.Distance(ctHome, walker.transform.position):F2} m off)"); }
                    else CoreLog.Warn($"[selftest] {name}: no walker to put back home {PowerUx.V(ctHome)}");
                }
                catch (Exception e) { CoreLog.Warn($"[selftest] {name}: putting the walker back home threw {e.Message}"); }
            }
            string modelText = ctGritModel ? $", grit view showed its canister model with the capsule hidden {ctModelOk}, a view that showed it got the capsule back {ctModelBack}" : ", no canister model on grit (not judged)";
            string detail = $"grit canister {ctGrit} matched {ctGritOk}, dust white {ctDustOk}, a grit view reused for another cargo with its colour {ctReused} ({ctTinted.Count} view(s) showed grit){modelText}, back to dust {(ctGrit >= 0 ? ctPutBack.ToString() : "n/a")}, {ctSamples} samples; wrong: [{string.Join("; ", ctBad)}]; seen: [{string.Join("; ", ctSeen)}]";
            if (skip == null && ctBad.Count == 0 && ctGritOk && ctDustOk && !ctReused) skip = "the pool never reused a view that showed grit for another canister";
            if (skip != null && ctBad.Count == 0) { CoreLog.Info($"[selftest] {name} not applicable: {skip} ({detail})"); return; }
            bool ok = ctGritOk && ctDustOk && ctReused && ctBad.Count == 0 && (ctGrit < 0 || ctPutBack) && (!ctGritModel || (ctModelOk && ctModelBack));
            Assert(name, ok, skip != null ? $"{skip}; {detail}" : detail);
            if (ok) CoreLog.Info($"[selftest] {name}: {detail}");
        }

        static string VanillaText(MoonProductionState s) => s == null ? "no state" :
            $"depot {s.Id} grid {s.Grid} power {s.Power:F2} demand {s.DemandKw:F1} kW used {s.UsedKw:F2} kW running {s.Running} progress {s.Progress:F3} cycles {s.Cycles} status '{s.Status}'";
    }
}
#endif
