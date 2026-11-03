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
                CoreLog.Info($"[selftest] {name} not applicable: chamber not in the burner's network (chamber {VanillaText(c)}; burner {PowerText(b)})");
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

        static string VanillaText(MoonProductionState s) => s == null ? "no state" :
            $"depot {s.Id} grid {s.Grid} power {s.Power:F2} demand {s.DemandKw:F1} kW used {s.UsedKw:F2} kW running {s.Running} progress {s.Progress:F3} cycles {s.Cycles} status '{s.Status}'";
    }
}
#endif
