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
    }
}
#endif
