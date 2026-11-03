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

        // power-vanilla-fed (PowerPatches.TopUpVanilla): AutoBuild built a vanilla pressure chamber (96) and the example
        // burner. Within 60 s, at some tick: vanilla's solve left the chamber below Power 0.99 and the top-up raised it to
        // 0.99 or more, its Cycles rose since the check opened, and the summed ChargeKwh of the fuel generators that fed it
        // (LastSolve.Fed when it first read the raised Power) is below its value then. In a world where the burner shares a
        // network with another generator (slot-2: the briquette generator 28), that one may be the generator drawn from
        // first, so the energy check follows whichever generators fed the chamber. Not applicable when the chamber is not
        // in the burner's network after 20 s.
        static int vfChamber = -1, vfBurner = -1, vfCycles;
        static float vfOpened = -1f, vfUntil = -1f;
        static double vfCharge = -1;
        static List<int> vfFeeders = new();
        static bool vfNetwork;

        static bool VanillaFedPending => vfUntil >= 0f;

        static void VanillaFedChecks()
        {
            const string name = "power-vanilla-fed";
            string chamberEntry = MoonWorld.PressureChamberKind.ToString();
            if (!DevTools.AutoBuildNames(chamberEntry) || !DevTools.AutoBuildNames(BurnerId))
            {
                CoreLog.Info($"[selftest] {name} not applicable: AutoBuild does not name both '{chamberEntry}' and '{BurnerId}'");
                return;
            }
            DevTools.BuildResults.TryGetValue(chamberEntry, out var c);
            DevTools.BuildResults.TryGetValue(BurnerId, out var b);
            if (!c.ok || !b.ok)
            {
                CoreLog.Info($"[selftest] {name} not applicable: AutoBuild built chamber {c.ok} ({c.reason}), burner {b.ok} ({b.reason}); {Surroundings(b.ok ? b.depot : -1)}");
                return;
            }
            var world = CoreState.World ?? CoreState.FindWorld();
            var chamber = world?.ProductionOf(c.depot);
            if (chamber == null) { Assert(name, false, $"chamber depot {c.depot} has no production state"); return; }
            vfChamber = c.depot;
            vfBurner = b.depot;
            vfCycles = chamber.Cycles;
            vfCharge = -1;
            vfFeeders = new List<int>();
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
            var last = Patches.PowerPatches.LastSolve.NetworkOf;
            if (last.TryGetValue(vfChamber, out int net) && last.TryGetValue(vfBurner, out int bnet) && net == bnet) vfNetwork = true;
            float now = Time.unscaledTime;
            if (!vfNetwork)
            {
                if (now - vfOpened < 20f) return;
                vfUntil = -1f;
                // Not a candidate at all: its own grid already gives it full power.
                bool sunFed = c != null && c.Power >= 0.99f && !Patches.PowerPatches.LastSolve.VanillaPowerBefore.ContainsKey(vfChamber);
                string why = sunFed ? "vanilla already powers it fully" : "chamber not in the burner's network";
                CoreLog.Info($"[selftest] {name} not applicable: {why} (chamber {VanillaText(c)}; burner {PowerText(b)})");
                return;
            }
            if (c == null || b == null) { vfUntil = -1f; Assert(name, false, $"chamber {VanillaText(c)}; burner {PowerText(b)}"); return; }
            bool raised = Patches.PowerPatches.LastSolve.VanillaPowerBefore.TryGetValue(vfChamber, out float before) && before < 0.99f && c.Power >= 0.99f;
            if (raised && vfCharge < 0)
            {
                vfFeeders = Patches.PowerPatches.LastSolve.Fed.Where(kv => kv.Value.Contains(vfChamber)).Select(kv => kv.Key).OrderBy(d => d).ToList();
                if (vfFeeders.Count > 0)
                {
                    vfCharge = Charge(world, vfFeeders);
                    CoreLog.Info($"[selftest] {name}: chamber raised from {before:F2} by fuel generator(s) {string.Join(", ", vfFeeders)} (charge {vfCharge:F5} kWh); chamber {VanillaText(c)}; burner {PowerText(b)}");
                }
            }
            double charge = vfCharge >= 0 ? Charge(world, vfFeeders) : -1;
            bool ok = vfCharge >= 0 && c.Cycles > vfCycles && charge < vfCharge;
            if (!ok && now < vfUntil) return;
            vfUntil = -1f;
            Assert(name, ok, $"raised seen {vfCharge >= 0} (power before {before:F2}), cycles {vfCycles} -> {c.Cycles}, charge of feeders [{string.Join(", ", vfFeeders)}] at the raise {vfCharge:F5} -> {charge:F5} kWh; chamber {VanillaText(c)}; burner {PowerText(b)}");
            if (ok) CoreLog.Info($"[selftest] {name}: cycles {vfCycles} -> {c.Cycles}, feeders [{string.Join(", ", vfFeeders)}] charge {vfCharge:F5} -> {charge:F5} kWh; chamber {VanillaText(c)}; burner {PowerText(b)}");
        }

        static double Charge(MoonWorld world, List<int> depots) => depots.Sum(d => world?.ProductionOf(d)?.ChargeKwh ?? 0);

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
        // power-grinder-runs-on-burner opened (the burner was fed), the library's MoonPowerLinks drew at least one arc in a
        // frame. Not applicable without ForceArcs (the build-tool and aim gate is not driven here) or without either check.
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

        static void EmptyOutputs(MoonWorld world, int depot, int first)
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
                    CoreLog.Info($"[selftest] arc-drawn: emptied the full output slot {slot} ({kg:F1} kg) of depot {depot} (kind {s.Kind}) so it can run");
                }
            }
            catch (Exception e) { CoreLog.Warn($"[selftest] arc-drawn: emptying depot {depot}'s output threw {e.Message}"); }
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
            // A side-on screenshot of the burner and the nearest pressure chamber within 20 m of it, once the probe's tour is
            // over (it moves the walker for about 90 s from 24 s after load). Every chamber in that reach is loaded then: the
            // briquette generator 28 (slot-2) draws first and covers one chamber alone, so the burner feeds the next ones.
            var burner = MoonStructures.Definition(BurnerId);
            var world = CoreState.World ?? CoreState.FindWorld();
            shotGen = burner != null && world?.depots != null ? DepotOfKind(world, burner.Kind) : -1;
            shotLoad.Clear();
            if (shotGen >= 0)
                foreach (var kv in world.depots)
                    if (kv.Value != null && kv.Value.kind == MoonWorld.PressureChamberKind && Vector3.Distance(PowerUx.At(world, shotGen), PowerUx.At(world, kv.Key)) <= 20f) shotLoad.Add(kv.Key);
            shotMember = shotLoad.Count > 0 ? shotLoad.OrderBy(d => Vector3.Distance(PowerUx.At(world, shotGen), PowerUx.At(world, d))).First() : -1;
            if (shotMember >= 0 && DevTools.PowerUxProbe != null && DevTools.PowerUxProbe.Value) { shotAt = now + 100f; shotStage = 0; }
        }

        static readonly List<int> shotLoad = new();

        static int shotGen = -1, shotMember = -1, shotStage;
        static float shotAt = -1f, shotDeadline = -1f;
        static Vector3 shotLook;

        internal static bool ArcShotPending => shotAt >= 0f;

        // arc-drawn's screenshot (BepInEx/cache/structurelib/powerux-<n>.png, after the tour's): load the chambers in reach,
        // stand 14 m off the burner-chamber midpoint square to the pair (the freer side), wait for the burner to feed it, shoot.
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
                    // Outputs emptied and dust loaded, so the chambers demand power again; the shot waits for the solve to
                    // record the burner feeding the framed one (up to 20 s).
                    foreach (int d in shotLoad)
                    {
                        EmptyOutputs(world, d, 1);
                        var ms = world.ProductionOf(d);
                        double room = ms != null ? ms.Capacity(0) - ms.Stored(0) : 0;
                        if (room > 0.5) world.CrewProcessorLoad(MoonWorld.LocalPlayer, d, MoonCargo.Dust, room);
                        CoreLog.Info($"[selftest] arc-drawn shot: loaded {room:F1} kg dust into chamber {d}");
                    }
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
                    CoreLog.Info($"[selftest] arc-drawn shot: burner {shotGen}, chamber {shotMember}; walker to {PowerUx.V(best)} ({bestFree:F1} m from the nearest depot), looking at {PowerUx.V(shotLook)}");
                    shotStage = 1; shotAt = now + 4f; shotDeadline = now + 20f;
                    return;
                }
                var t = walker.transform;
                var wup = t.position.normalized;
                var to = shotLook - (t.position + wup * 1.6f);
                var flat = Vector3.ProjectOnPlane(to, wup);
                walker.Face(flat.normalized, Mathf.Atan2(Vector3.Dot(to, wup), flat.magnitude) * Mathf.Rad2Deg);
                bool fedNow = Patches.PowerPatches.LastSolve.Fed.TryGetValue(shotGen, out var fed) && fed.Contains(shotMember);
                if (shotStage == 1) { if (fedNow || now >= shotDeadline) { shotStage = 2; shotAt = now + 1.5f; } return; }
                shotAt = -1f;
                CoreLog.Info($"[selftest] arc-drawn shot: ArcsDrawn {PowerLinksView.ArcsDrawn}; fed {FedText()}");
                PowerUx.Tour.Shot("arc-drawn: burner and the machine it fed, side on");
            }
            catch (Exception e) { shotAt = -1f; CoreLog.Warn("[selftest] arc-drawn shot threw: " + e.Message); }
        }

        static string FedText()
        {
            return string.Join("; ", Patches.PowerPatches.LastSolve.Fed.Select(kv => $"{kv.Key} (network {(Patches.PowerPatches.LastSolve.NetworkOf.TryGetValue(kv.Key, out int n) ? n : -1)}) -> [{string.Join(", ", kv.Value)}]"));
        }

        static string VanillaText(MoonProductionState s) => s == null ? "no state" :
            $"depot {s.Id} grid {s.Grid} power {s.Power:F2} demand {s.DemandKw:F1} kW used {s.UsedKw:F2} kW running {s.Running} progress {s.Progress:F3} cycles {s.Cycles} status '{s.Status}'";
    }
}
#endif
