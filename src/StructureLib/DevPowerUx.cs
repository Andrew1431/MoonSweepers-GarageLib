#if DEVBUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    // Power-UX recon probe (research/POWER_UX_RECON.md): the look-at panel for modded structures, fuel power to vanilla
    // machines, the power arcs and the modded canister look. Dev only; [Dev] PowerUxProbe turns it on, [Dev] PowerUxTest
    // picks the route experiments. Every patch target here was measured (native size, first 16 bytes) before patching:
    // none is a short leaf, and every probe patch is a log-only prefix or postfix that lets the original run.
    internal static partial class DevTools
    {
        internal static ConfigEntry<bool> PowerUxProbe;
        internal static ConfigEntry<string> PowerUxTest;
        internal static ConfigEntry<float> PowerUxTeleport;

        static void BindPowerUx(ConfigFile cfg)
        {
            PowerUxProbe = cfg.Bind("Dev", "PowerUxProbe", false, "Dev build: the power-UX recon probe ([powerux] log lines): traces the look-at panel, the power solve, the power arcs and canister views, tours the walker's view over the AutoBuild depots and takes screenshots to BepInEx/cache/structurelib/powerux-<n>.png.");
            PowerUxTest = cfg.Bind("Dev", "PowerUxTest", "", "Dev build: comma-separated PowerUxProbe experiments: fuel (raise the AutoBuilt pressure chamber's Power from the briquette generator after the solve), link (add the generator-chamber pair to powerLinks), anchor (add the generator to powerAnchors), arc (draw an arc from the generator to the chamber with our own MoonPowerLinks), tint (tint modded-cargo canister views), far (walk 70 m away from the canisters and look back).");
            PowerUxTeleport = cfg.Bind("Dev", "PowerUxTeleport", 0f, "Dev build: with PowerUxProbe, 6 s after load move the walker this many metres to the free side of the base (away from every depot), before AutoBuild. 0 = stay.");
            CoreLog.Info($"[dev] PowerUxProbe={PowerUxProbe.Value} PowerUxTest='{PowerUxTest.Value}' PowerUxTeleport={PowerUxTeleport.Value}");
            if (!PowerUxProbe.Value) return;
            try
            {
                new Harmony(Plugin.Guid + ".powerux").CreateClassProcessor(typeof(PowerUx.Patches)).Patch();
                CoreLog.Info("[powerux] probe patches applied");
            }
            catch (Exception e) { CoreLog.Error("[powerux] patching failed: " + e); }
        }
    }

    internal static class PowerUx
    {
        internal const string GenId = "ryanhartwig.briquette-generator", CompactorId = "ryanhartwig.sand-compactor", SplitterId = "ryanhartwig.box-splitter", BriqId = "ryanhartwig.sand-briquette";
        static bool On => DevTools.PowerUxProbe?.Value == true;
        internal static bool Test(string t) => (DevTools.PowerUxTest?.Value ?? "").Split(',').Any(x => x.Trim().Equals(t, StringComparison.OrdinalIgnoreCase));

        internal static void L(string m) => CoreLog.Info("[powerux] " + m);
        static readonly HashSet<string> seen = new(StringComparer.Ordinal);
        internal static void Once(string key, string m) { if (seen.Add(key)) L(m); }
        internal static string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
        internal static string C(Color c) => $"rgba({c.r:F2},{c.g:F2},{c.b:F2},{c.a:F2})";
        internal static string T(Func<string> f) { try { return f(); } catch (Exception e) { return "threw " + e.GetType().Name + ": " + e.Message; } }

        static bool teleported, layoutDone, tourStarted, canistersDone, routeLogged;
        internal static int Gen = -1, Chamber = -1, Compactor = -1, Splitter = -1;
        internal static float TraceUntil = -1f;          // unscaled time; power order trace is live while now < TraceUntil
        internal static int TraceLines;
        internal static float LinksUntil = -1f;          // MoonPowerLinks trace window
        static float nextState = -1f;

        internal static void Tick(float gameT)
        {
            if (!On) return;
            try
            {
                var world = CoreState.World ?? CoreState.FindWorld();
                if (world == null) return;
                float now = Time.unscaledTime;
                if (!teleported && gameT > 6f) { teleported = true; if (DevTools.PowerUxTeleport.Value > 0f) Teleport(world, DevTools.PowerUxTeleport.Value); }
                if (!layoutDone && gameT > 12f) { layoutDone = true; Resolve(world); Layout(world); LinksUntil = now + 40f; }
                if (layoutDone && now >= nextState && gameT < 140f) { nextState = now + 2f; StateLine(world, gameT); }
                if (!routeLogged && gameT > 18f) { routeLogged = true; Arm(now, "18 s (after AutoFeed)"); }
                if (!canistersDone && gameT > 16f) { canistersDone = true; Canisters.Start(world); }
                Canisters.Tick(world, now);
                if (!tourStarted && gameT > 24f) { tourStarted = true; Tour.Start(world); }
                Tour.Tick(world, now);
                OwnArc.Tick(world);
            }
            catch (Exception e) { CoreLog.Once("powerux-tick", "[powerux] tick threw: " + e, 3); }
        }

        internal static void Arm(float now, string why) { TraceUntil = now + 1.5f; TraceLines = 0; L($"order trace armed at {why} (frame {Time.frameCount})"); }

        static int BuiltDepot(string entry) => DevTools.BuildResults.TryGetValue(entry, out var r) && r.ok ? r.depot : -1;
        // AutoBuild reuses a modded kind's existing depot for the first entry; a second entry ('#2') is a new depot.
        static int Fresh(string id) { int d = BuiltDepot(id + "#2"); return d >= 0 ? d : BuiltDepot(id); }

        static void Resolve(MoonWorld world)
        {
            Gen = Fresh(GenId); Compactor = Fresh(CompactorId); Splitter = Fresh(SplitterId);
            Chamber = BuiltDepot(MoonWorld.PressureChamberKind.ToString());
            // Not built this run: the nearest depot of that kind to the walker (slot-2 keeps the last run's layout).
            if (Gen < 0) Gen = Nearest(world, MoonStructures.Definition(GenId)?.Kind ?? -1);
            if (Compactor < 0) Compactor = Nearest(world, MoonStructures.Definition(CompactorId)?.Kind ?? -1);
            if (Splitter < 0) Splitter = Nearest(world, MoonStructures.Definition(SplitterId)?.Kind ?? -1);
            if (Chamber < 0) Chamber = Nearest(world, MoonWorld.PressureChamberKind);
            L($"depots: generator {Gen}, chamber {Chamber} (PressureChamberKind {MoonWorld.PressureChamberKind}), compactor {Compactor}, splitter {Splitter}; StallPower {MoonWorld.StallPower}");
            byte briq = (byte)(MoonStructures.CargoDefinition(BriqId)?.Value ?? 3);
            foreach (var (id, cargo, kg) in new[] { (Gen, (MoonCargo)briq, 10.0), (Chamber, MoonCargo.Dust, 12.0), (Compactor, MoonCargo.Sand, 8.0) })
            {
                if (id < 0) continue;
                world.CrewProcessorLoad(MoonWorld.LocalPlayer, id, cargo, kg);
                L($"fed depot {id} {kg} kg cargo {(byte)cargo}: {State(world.ProductionOf(id))}");
            }
        }

        internal static int Nearest(MoonWorld world, int kind, float within = 80f)
        {
            var walker = Walker();
            if (walker == null || kind < 0) return -1;
            int best = -1; float bd = within;
            foreach (var kv in world.depots)
            {
                if (kv.Value.kind != kind) continue;
                float d = Vector3.Distance(walker.transform.position, At(world, kv.Key));
                if (d < bd) { bd = d; best = kv.Key; }
            }
            return best;
        }

        // The belt whose end is at the generator's input (AutoBelt's, else any built belt ending within 1.5 m of it).
        internal static int BeltIntoGen(MoonWorld world)
        {
            var r = AutoBelter.Results.FirstOrDefault(x => x.Ok && x.To == Gen);
            if (r != null) return r.Belt;
            var g = At(world, Gen);
            foreach (var kv in world.conveyors)
            {
                var path = world.BeltPath(kv.Key);
                if (path != null && Vector3.Distance(path.Point(path.Length), g) < 1.5f) return kv.Key;
            }
            return -1;
        }

        internal static Vector3 At(MoonWorld world, int id)
        {
            if (id < 0 || world.depots == null || !world.depots.ContainsKey(id)) return Vector3.zero;
            var r = world.depots.get_Item(id);
            return new Vector3(r.x, r.y, r.z);
        }

        static MoonWalker Walker()
        {
            var w = UnityEngine.Object.FindObjectsByType<MoonWalker>(FindObjectsSortMode.None);
            return w != null && w.Length > 0 ? w[0] : null;
        }

        // Moves the walker 'metres' along the ground towards the heading (of 8) whose spot is furthest from every depot.
        static void Teleport(MoonWorld world, float metres)
        {
            var walker = Walker();
            if (walker == null) { L("teleport: no walker"); return; }
            var t = walker.transform;
            var up = t.position.normalized;
            float radius = t.position.magnitude;
            Vector3 best = Vector3.zero; float bestFree = -1f; float bestH = 0f;
            for (int h = 0; h < 360; h += 45)
            {
                var dir = Quaternion.AngleAxis(h, up) * t.forward;
                var axis = Vector3.Cross(up, dir).normalized;
                var p = Quaternion.AngleAxis(metres / radius * Mathf.Rad2Deg, axis) * t.position;
                float free = float.MaxValue;
                foreach (var kv in world.depots) free = Mathf.Min(free, Vector3.Distance(p, new Vector3(kv.Value.x, kv.Value.y, kv.Value.z)));
                if (free > bestFree) { bestFree = free; best = p; bestH = h; }
            }
            var ground = world.SurfaceAt(best.normalized, out Vector3 gUp, out bool onStructure);
            var to = ground + gUp * 1.2f;
            L($"teleport: walker at {V(t.position)} -> heading {bestH} deg, {metres} m: ground {V(ground)} (onStructure {onStructure}), nearest depot {bestFree:F1} m; Teleport({V(to)})");
            walker.Teleport(to, true);
            L($"teleport: walker now {V(t.position)}");
        }

        static void Layout(MoonWorld world)
        {
            var walker = Walker();
            var wp = walker != null ? walker.transform.position : Vector3.zero;
            L($"walker at {V(wp)} forward {V(walker != null ? walker.transform.forward : Vector3.zero)}");
            var nodes = world.powerNodes;
            for (int i = 0; nodes != null && i < nodes.Count; i++)
            {
                var n = nodes.get_Item(i);
                if (n?.record == null) continue;
                string spec = n.panel ? T(() => $"spec '{n.spec.name}' {n.spec.peakKw} kW / {n.spec.batteryKwh} kWh / {n.spec.range} m") : "machine";
                L($"node {i}: depot {n.record.id} kind {n.record.kind} panel {n.panel} parent {n.parent} grid {n.state?.Grid} at {V(n.position)} ({Vector3.Distance(n.position, wp):F1} m from walker) PowerRange {MoonWorld.PowerRange(n.record.kind)}; {spec}");
            }
            if (world.powerGrids != null)
                foreach (var kv in world.powerGrids)
                {
                    var g = kv.Value;
                    L($"grid {kv.Key}: panels {g.panels} machines {g.machines} supply {g.supplyKw:F2} demand {g.demandKw:F2} used {g.usedKw:F2} peak {g.peakKw:F2} satisfaction {g.satisfaction:F2} stored {g.storedKwh:F3}/{g.capacityKwh:F3}");
                }
            L($"powerLinks {world.powerLinks?.Count}: {T(() => string.Join(" ", Enumerable.Range(0, world.powerLinks.Count).Select(i => { var p = world.powerLinks.get_Item(i); return $"({p.Item1},{p.Item2})"; })))}");
            L($"powerAnchors {world.powerAnchors?.Count}: {T(() => { var sb = new StringBuilder(); foreach (var kv in world.powerAnchors) sb.Append($"{kv.Key}={V(kv.Value)} "); return sb.ToString(); })}");
            foreach (var kv in world.depots)
            {
                var r = kv.Value;
                var p = new Vector3(r.x, r.y, r.z);
                float d = Vector3.Distance(p, wp);
                if (d > 45f) continue;
                var def = MoonStructures.IsModded(r.kind) ? null : world.BuildDefinition(r.kind);
                string name = MoonStructures.IsModded(r.kind) ? MoonStructures.Definition(r.kind)?.Id : def?.displayName;
                L($"depot {r.id} kind {r.kind} '{name}' {d:F1} m from walker at {V(p)}");
            }
            foreach (int id in new[] { Gen, Chamber, Compactor, Splitter })
            {
                if (id < 0) continue;
                L($"PowerAnchor({id}) {T(() => world.PowerAnchor(id, out Vector3 a) ? V(a) : "false")}; IsSolarPanelId {T(() => world.IsSolarPanelId(id).ToString())}; IsProcessorId {T(() => world.IsProcessorId(id).ToString())}");
            }
            if (Gen >= 0 && Chamber >= 0) L($"generator-chamber distance {Vector3.Distance(At(world, Gen), At(world, Chamber)):F2} m");
        }

        internal static string State(MoonProductionState s)
        {
            if (s == null) return "no state";
            var stores = T(() => string.Join(" ", Enumerable.Range(0, s.stores?.Length ?? 0).Select(i => s.Stored(i).ToString("F2"))));
            return $"Grid {s.Grid} Power {s.Power:F3} DemandKw {s.DemandKw:F2} UsedKw {s.UsedKw:F2} SuppliedKw {s.SuppliedKw:F2} BatteryKw {s.BatteryKw:F2} ChargeKwh {s.ChargeKwh:F4} Running {s.Running} Progress {s.Progress:F3} Cycles {s.Cycles} Status '{T(() => s.Status)}' stores [{stores}]";
        }

        static void StateLine(MoonWorld world, float gameT)
        {
            foreach (var (what, id) in new[] { ("chamber", Chamber), ("generator", Gen) })
                if (id >= 0) L($"t {gameT:F0} {what} {id}: {State(world.ProductionOf(id))}");
        }

        // fuel / link / anchor routes, run from the SolvePower postfix after StructureLib's own.
        internal static void Routes(MoonWorld world, float dt)
        {
            if (Gen < 0 || Chamber < 0) return;
            var g = world.ProductionOf(Gen);
            var c = world.ProductionOf(Chamber);
            if (g == null || c == null) return;
            bool trace = Time.unscaledTime < TraceUntil && TraceLines < 60;
            if (Test("link"))
            {
                var links = world.powerLinks;
                bool has = false;
                for (int i = 0; i < links.Count; i++) { var p = links.get_Item(i); if ((p.Item1 == Gen && p.Item2 == Chamber) || (p.Item1 == Chamber && p.Item2 == Gen)) has = true; }
                if (!has) { links.Add(new Il2CppSystem.ValueTuple<int, int>(Gen, Chamber)); if (trace) L($"f{Time.frameCount} link: added ({Gen},{Chamber}) to powerLinks, now {links.Count}"); }
                else if (trace) L($"f{Time.frameCount} link: pair already present after the solve ({links.Count})");
            }
            if (Test("anchor"))
            {
                var anchors = world.powerAnchors;
                if (anchors != null && !anchors.ContainsKey(Gen))
                {
                    var a = At(world, Gen) + At(world, Gen).normalized * 2f;
                    anchors.set_Item(Gen, a);
                    Once("anchor-set", $"anchor: set powerAnchors[{Gen}] = {V(a)}; read back {T(() => V(anchors.get_Item(Gen)))}; PowerAnchor({Gen}) {T(() => world.PowerAnchor(Gen, out Vector3 v) ? V(v) : "false")}");
                }
            }
            if (Test("fuel"))
            {
                string before = State(c);
                if (g.ChargeKwh > 0 && c.Power < MoonWorld.StallPower)
                {
                    c._Power_k__BackingField = 1f;
                    c._UsedKw_k__BackingField = c.DemandKw;
                    c._Grid_k__BackingField = g.Grid;
                    if (trace) L($"f{Time.frameCount} fuel: chamber before {before} -> after {State(c)}");
                    else Once("fuel-first", $"f{Time.frameCount} fuel: first raise; chamber before {before} -> after {State(c)}");
                }
            }
        }

        [HarmonyPatch]
        internal static class Patches
        {
            // ---- A: the look-at panel ----
            static bool inPanel;
            static readonly List<string> calls = new();
            static readonly Dictionary<int, string> lastSig = new();

            // 6912 B, normal prologue.
            [HarmonyPatch(typeof(MoonProductionInteraction), nameof(MoonProductionInteraction.ShowPanel)), HarmonyPrefix]
            static void ShowPanelPre(MoonDepotSnapshot record, bool power) { inPanel = true; calls.Clear(); calls.Add($"ShowPanel(id {record?.id}, kind {record?.kind}, power {power})"); }

            [HarmonyPatch(typeof(MoonProductionInteraction), nameof(MoonProductionInteraction.ShowPanel)), HarmonyPostfix]
            static void ShowPanelPost(MoonProductionInteraction __instance, MoonWorld world, MoonDepotSnapshot record, bool power)
            {
                inPanel = false;
                try
                {
                    if (record == null) return;
                    string sig = string.Join(" | ", calls);
                    if (lastSig.TryGetValue(record.id, out var old) && old == sig) return;
                    lastSig[record.id] = sig;
                    var s = world.ProductionOf(record.id);
                    string grid = T(() => s != null && world.powerGrids.ContainsKey(s.Grid) ? "grid in powerGrids" : "grid NOT in powerGrids");
                    L($"panel for depot {record.id} kind {record.kind} ({(MoonStructures.IsModded(record.kind) ? MoonStructures.Definition(record.kind)?.Id : "vanilla")}): {sig}");
                    L($"panel state depot {record.id}: {State(s)}; {grid}");
                    L($"panel texts depot {record.id}: {PanelTexts(__instance.look)}");
                }
                catch (Exception e) { CoreLog.Once("powerux-panel", "[powerux] panel postfix threw: " + e, 3); }
            }

            internal static string PanelTexts(MoonLookPanel look)
            {
                if (look == null) return "no look panel";
                var sb = new StringBuilder();
                sb.Append($"kicker '{T(() => look.kicker?.text)}' title '{T(() => look.title?.text)}' status '{T(() => look.status?.text)}' (status colour {T(() => C(look.status.color))}); rows:");
                for (int i = 0; look.rows != null && i < look.rows.Length; i++)
                {
                    var r = look.rows[i];
                    if (r?.root == null || !r.root.activeSelf) continue;
                    sb.Append($" [{i}] '{T(() => r.label?.text)}' = '{T(() => r.value?.text)}' fill {T(() => r.fill.fillAmount.ToString("F2"))} colour {T(() => C(r.fill.color))};");
                }
                var texts = look.canvas != null ? look.canvas.GetComponentsInChildren<TMPro.TMP_Text>(false) : null;
                sb.Append($" | TMP_Text under canvas ({texts?.Length ?? 0}): ");
                if (texts != null) foreach (var t in texts) sb.Append($"{t.gameObject.name}='{t.text}' ");
                return sb.ToString();
            }

            // 400 B, first branch at +0x1C.
            [HarmonyPatch(typeof(MoonLookPanel), nameof(MoonLookPanel.Show), new[] { typeof(string), typeof(string), typeof(string), typeof(Color), typeof(float) }), HarmonyPrefix]
            static void LookShow(string what, string name, string state, Color stateColor, float opacity)
            { if (inPanel) calls.Add($"Show(what '{what}', name '{name}', state '{state}', colour {C(stateColor)})"); }

            // 464 B, normal prologue.
            [HarmonyPatch(typeof(MoonLookPanel), nameof(MoonLookPanel.SetRow)), HarmonyPrefix]
            static void LookSetRow(int i, string label, float fill, string value, Color color)
            { if (inPanel) calls.Add($"SetRow({i}, '{label}', fill {fill:F2}, '{value}', {C(color)})"); }

            // 416 B, no branch in the first 16 B.
            [HarmonyPatch(typeof(MoonLookPanel), nameof(MoonLookPanel.Rows)), HarmonyPrefix]
            static void LookRows(int shown) { if (inPanel) calls.Add($"Rows({shown})"); }

            // ---- B: solve order and the fuel route ----
            // StepProduction 912 B, SolvePower 9440 B, FinishCycle (table: 416..3168 B); normal prologues.
            static bool Tracing => Time.unscaledTime < TraceUntil && TraceLines < 60;
            static void TL(string m) { TraceLines++; L(m); }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.StepProduction)), HarmonyPrefix]
            static void StepPre(MoonWorld __instance, float dt) { if (Tracing && Chamber >= 0) TL($"f{Time.frameCount} StepProduction pre dt {dt:F3}: chamber {State(__instance.ProductionOf(Chamber))}"); }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.StepProduction)), HarmonyPostfix]
            static void StepPost(MoonWorld __instance) { if (Tracing && Chamber >= 0) TL($"f{Time.frameCount} StepProduction post: chamber {State(__instance.ProductionOf(Chamber))}"); }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.SolvePower)), HarmonyPrefix]
            static void SolvePre(MoonWorld __instance, float dt) { if (Tracing && Chamber >= 0) TL($"f{Time.frameCount} SolvePower pre dt {dt:F3}: chamber {State(__instance.ProductionOf(Chamber))}; powerLinks {__instance.powerLinks?.Count}"); }

            // After StructureLib's own postfix.
            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.SolvePower)), HarmonyPostfix, HarmonyPriority(Priority.Last)]
            static void SolvePost(MoonWorld __instance, float dt)
            {
                try
                {
                    if (Tracing && Chamber >= 0) TL($"f{Time.frameCount} SolvePower post (after StructureLib): chamber {State(__instance.ProductionOf(Chamber))}; generator {State(Gen >= 0 ? __instance.ProductionOf(Gen) : null)}; powerLinks {__instance.powerLinks?.Count}");
                    Routes(__instance, dt);
                }
                catch (Exception e) { CoreLog.Once("powerux-solve", "[powerux] SolvePower postfix threw: " + e, 3); }
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.FinishCycle)), HarmonyPrefix]
            static void FinishPre(MoonProductionState s) { if (s != null && s.Id == Chamber) L($"f{Time.frameCount} FinishCycle(chamber): {State(s)}"); }

            // ---- C: arcs ----
            static bool inGrid;
            static int gridId;
            internal static float ArcWidth = -1f;
            internal static Color ArcColor;
            static bool Links => Time.unscaledTime < LinksUntil;

            // 2464 B.
            [HarmonyPatch(typeof(MoonPowerLinks), nameof(MoonPowerLinks.ShowGrid)), HarmonyPrefix]
            static void ShowGridPre(int id) { inGrid = true; gridId = id; if (Links) Once("grid:" + id, $"ShowGrid({id})"); }
            [HarmonyPatch(typeof(MoonPowerLinks), nameof(MoonPowerLinks.ShowGrid)), HarmonyPostfix]
            static void ShowGridPost(MoonPowerLinks __instance, int id) { inGrid = false; if (Links) Once("gridpost:" + id, $"ShowGrid({id}) done: used {__instance.used} lines {__instance.lines?.Count} gridPanels [{T(() => string.Join(",", Il2Set(__instance.gridPanels)))}] linkedPanels [{T(() => string.Join(",", Il2Set(__instance.linkedPanels)))}] gridMachines [{T(() => string.Join(",", Enumerable.Range(0, __instance.gridMachines.Count).Select(i => __instance.gridMachines.get_Item(i))))}] ring {V(__instance.ringCentre)} r {__instance.ringRadius:F1}"); }

            static IEnumerable<int> Il2Set(Il2CppSystem.Collections.Generic.HashSet<int> s) { var l = new List<int>(); if (s == null) return l; foreach (var x in s) l.Add(x); return l; }

            // 464 B.
            [HarmonyPatch(typeof(MoonPowerLinks), nameof(MoonPowerLinks.Link)), HarmonyPrefix]
            static void LinkPre(int a, int b, float strength) { if (Links) Once($"link:{a}:{b}:{(inGrid ? gridId : -1)}", $"Link({a}, {b}, strength {strength:F2}) {(inGrid ? $"in ShowGrid({gridId})" : "outside ShowGrid")}"); }

            // 1792 B.
            [HarmonyPatch(typeof(MoonPowerLinks), nameof(MoonPowerLinks.Arc)), HarmonyPrefix]
            static void ArcPre(Vector3 from, Vector3 to, float width, Color color)
            {
                if (ArcWidth < 0f) { ArcWidth = width; ArcColor = color; }
                if (Links) Once($"arc:{Mathf.RoundToInt(from.x)}:{Mathf.RoundToInt(from.z)}:{Mathf.RoundToInt(to.x)}:{Mathf.RoundToInt(to.z)}", $"Arc({V(from)} -> {V(to)}, width {width:F3}, {C(color)}) {(inGrid ? $"in ShowGrid({gridId})" : "")}");
            }

            static float nextAmbient;
            // 1088 B.
            [HarmonyPatch(typeof(MoonPowerLinks), nameof(MoonPowerLinks.ShowAmbient)), HarmonyPrefix]
            static void AmbientPre() { if (Links && Time.unscaledTime > nextAmbient) { nextAmbient = Time.unscaledTime + 5f; L("ShowAmbient()"); } }

            // 1088 B.
            [HarmonyPatch(typeof(MoonPowerLinks), nameof(MoonPowerLinks.ShowPreview)), HarmonyPrefix]
            static void PreviewPre(int kind, Vector3 position) { if (Links) Once("preview:" + kind, $"ShowPreview(kind {kind}, {V(position)})"); }

            // ---- D: canister views ----
            // Place 624 B, Show 1104 B, ShowCargo 1152 B, Bind 560 B; normal prologues.
            [HarmonyPatch(typeof(MoonCanisterStreaming), nameof(MoonCanisterStreaming.Place)), HarmonyPrefix]
            static void PlacePre(MoonDustCanisterView view, MoonDroppedCanister c) { if (Canisters.Watched(c.id)) Once($"place:{c.id}", $"Place(view {view?.GetInstanceID()}, canister {c.id} cargo {(byte)c.cargo})"); }

            [HarmonyPatch(typeof(MoonDustCanisterView), nameof(MoonDustCanisterView.Show)), HarmonyPrefix]
            static void ViewShowPre(MoonDustCanisterView __instance, MoonDroppedCanister c) { if (Canisters.Watched(c.id)) Once($"show:{__instance.GetInstanceID()}:{c.id}", $"View.Show(view {__instance.GetInstanceID()} '{__instance.name}' Id {__instance.Id}, canister {c.id} cargo {(byte)c.cargo} kg {c.kilograms:F2})"); }

            [HarmonyPatch(typeof(MoonDustCanisterView), nameof(MoonDustCanisterView.Bind)), HarmonyPrefix]
            static void ViewBindPre(MoonDustCanisterView __instance, int id) { if (Canisters.Watched(id)) L($"View.Bind(view {__instance.GetInstanceID()} '{__instance.name}', id {id}); view Cargo was {(byte)__instance.Cargo} cargoShown {__instance.cargoShown}"); }

            [HarmonyPatch(typeof(MoonDustCanisterView), nameof(MoonDustCanisterView.ShowCargo)), HarmonyPrefix]
            static void ShowCargoPre(MoonDustCanisterView __instance, MoonCargo cargo)
            {
                if (Canisters.Watched(__instance.Id) || __instance.Id <= 0 || (byte)cargo > 2)
                    Once($"cargo:{__instance.GetInstanceID()}:{(byte)cargo}", $"ShowCargo(view {__instance.GetInstanceID()} '{__instance.name}' Id {__instance.Id}, cargo {(byte)cargo}); before: Cargo {(byte)__instance.Cargo} cargoShown {__instance.cargoShown}");
            }

            [HarmonyPatch(typeof(MoonDustCanisterView), nameof(MoonDustCanisterView.ShowCargo)), HarmonyPostfix]
            static void ShowCargoPost(MoonDustCanisterView __instance, MoonCargo cargo)
            {
                try
                {
                    if (Canisters.Watched(__instance.Id) || __instance.Id <= 0 || (byte)cargo > 2)
                        Once($"cargopost:{__instance.GetInstanceID()}:{(byte)cargo}", $"ShowCargo done (view {__instance.GetInstanceID()} Id {__instance.Id}, cargo {(byte)cargo}): {Canisters.Describe(__instance)}");
                    if (Test("tint")) Canisters.Tint(__instance, cargo);
                }
                catch (Exception e) { CoreLog.Once("powerux-cargo", "[powerux] ShowCargo postfix threw: " + e, 3); }
            }
        }

        // D: canisters to look at: briquette and dust on the compactor->generator belt, on the ground, and in the hand.
        internal static class Canisters
        {
            static readonly HashSet<int> ids = new();
            static readonly Dictionary<int, string> what = new();
            static float next = -1f, until = -1f;
            internal static bool Watched(int id) => ids.Contains(id);
            static int hand = -1;

            // After the tour (carrying hides the look-at panel): pick up the hand briquette.
            internal static void Carry(MoonWorld world)
            {
                var inter = UnityEngine.Object.FindFirstObjectByType<MoonDustCanisterInteraction>();
                if (inter == null || hand < 0) { L("canisters: nothing to carry"); return; }
                L($"canisters: TryCarry({hand}) = {T(() => { bool ok = inter.TryCarry(hand, out string msg); return ok + " '" + msg + "'"; })}; IsCarrying {inter.IsCarrying} CarriedCargo {(byte)inter.CarriedCargo}");
                next = Time.unscaledTime + 1f; until = Time.unscaledTime + 8f;
            }

            internal static void Start(MoonWorld world)
            {
                byte briq = (byte)(MoonStructures.CargoDefinition(BriqId)?.Value ?? 3);
                var walker = Walker();
                int beltId = BeltIntoGen(world);
                if (beltId >= 0)
                {
                    var path = world.BeltPath(beltId);
                    L($"canisters: belt {beltId} into the generator, length {path.Length:F2}");
                    Add(world, SelfTest.Spawn(world, path, Mathf.Max(0.2f, path.Length - 2.4f), (MoonCargo)briq, 2.0, out string w1), "briquette on belt", w1);
                    Add(world, SelfTest.Spawn(world, path, Mathf.Max(0.2f, path.Length - 3.4f), MoonCargo.Dust, out string w2), "dust on belt", w2);
                    Add(world, SelfTest.Spawn(world, path, Mathf.Max(0.2f, path.Length - 4.4f), MoonCargo.Sand, out string w3), "sand on belt", w3);
                    Add(world, SelfTest.Spawn(world, path, Mathf.Max(0.2f, path.Length - 5.4f), MoonCargo.Stone, 2.0, out string w4), "stone on belt", w4);
                }
                else L("canisters: no AutoBelt belt into the generator");
                if (walker != null)
                {
                    var t = walker.transform;
                    var up = t.position.normalized;
                    var fwd = Vector3.ProjectOnPlane(t.forward, up).normalized;
                    var side = Vector3.Cross(up, fwd).normalized;
                    Add(world, Ground(world, t.position + fwd * 2.5f + side * 0.5f, up, (MoonCargo)briq), "briquette on ground", null);
                    Add(world, Ground(world, t.position + fwd * 2.5f - side * 0.5f, up, MoonCargo.Dust), "dust on ground", null);
                    hand = Ground(world, t.position + fwd * 1.0f, up, (MoonCargo)briq);
                    Add(world, hand, "briquette for the hand", null);
                }
                next = Time.unscaledTime + 1f; until = Time.unscaledTime + 30f;
            }

            static void Add(MoonWorld world, int id, string label, string why)
            {
                L($"canisters: {label}: canister {id}{(id < 0 ? " not made: " + why : "")}");
                if (id < 0) return;
                ids.Add(id); what[id] = label;
            }

            // PackDustCanister at the point, then the record moved there with the cargo (as SelfTest.Spawn does off-belt).
            static int Ground(MoonWorld world, Vector3 p, Vector3 up, MoonCargo cargo)
            {
                string player = MoonWorld.LocalPlayer;
                world.DevSetCarriedDust(player, 1.0);
                if (!world.PackDustCanister(player, p + up * 0.6f, up, out int id)) return -1;
                if (!world.canisters.ContainsKey(id)) return -1;
                var c = world.canisters.get_Item(id);
                if ((byte)cargo == 0) return id;
                var moved = new MoonDroppedCanister(c.id, c.owner, c.kilograms, c.capacity, c.position, c.up, c.createdAt, new Il2CppSystem.Nullable<Quaternion>(c.rotation), cargo);
                moved.physicalPose = c.physicalPose;
                world.MoveCanisterRecord(moved, c.position, c.up, new Il2CppSystem.Nullable<Quaternion>(c.rotation));
                return id;
            }

            internal static void Tick(MoonWorld world, float now)
            {
                if (next < 0f || now < next || now > until) return;
                next = now + 3f;
                var streaming = UnityEngine.Object.FindFirstObjectByType<MoonCanisterStreaming>();
                var inter = UnityEngine.Object.FindFirstObjectByType<MoonDustCanisterInteraction>();
                foreach (int id in ids)
                {
                    string rec = T(() => world.canisters.ContainsKey(id) ? $"cargo {(byte)world.canisters.get_Item(id).cargo} at {V(world.canisters.get_Item(id).position)}" : "no record");
                    MoonDustCanisterView view = null;
                    if (streaming?.views != null && streaming.views.ContainsKey(id)) view = streaming.views.get_Item(id);
                    string far = T(() => streaming?.farShown != null && streaming.farShown.ContainsKey(id) ? $"farShown cargo {(byte)streaming.farShown.get_Item(id).cargo} fill {streaming.farShown.get_Item(id).fill:F2} valid {streaming.farShown.get_Item(id).valid}" : "not in farShown");
                    Once($"canister:{id}:{(view != null ? view.GetInstanceID() : 0)}:{rec}", $"canister {id} ({what[id]}): {rec}; streaming view {(view != null ? $"{view.GetInstanceID()} {Describe(view)}" : "none")}; {far}");
                }
                if (inter != null && inter.carriedView != null) Once("carried:" + inter.carriedView.GetInstanceID() + (byte)inter.carriedView.Cargo, $"carried view {inter.carriedView.GetInstanceID()} '{inter.carriedView.name}' carriedId {inter.carriedId}: {Describe(inter.carriedView)}");
                if (streaming != null) Once("streaming:" + (int)(now / 10), $"streaming: ActiveViews {streaming.ActiveViews} FarDrawn {streaming.FarDrawn} FarParts {streaming.FarParts} FarDistance {streaming.FarDistance:F1}; parts: {T(() => streaming.parts == null ? "null" : string.Join("; ", streaming.parts.Select(p => $"{p.mesh?.name}/{p.submesh} mat '{p.material?.name}' dust {p.dust} sand {p.sand} stone {p.stone} fill {p.fill}")))}");
            }

            internal static string Describe(MoonDustCanisterView v)
            {
                var sb = new StringBuilder();
                sb.Append($"Cargo {(byte)v.Cargo} cargoShown {v.cargoShown} shownLevel {v.shownLevel}; dustRenderer '{v.dustRenderer?.name}' enabled {v.dustRenderer?.enabled} mat '{v.dustRenderer?.sharedMaterial?.name}' shader '{v.dustRenderer?.sharedMaterial?.shader?.name}'; stoneBlock active {v.stoneBlock?.activeSelf}; stoneRenderer '{v.stoneRenderer?.name}' mat '{v.stoneRenderer?.sharedMaterial?.name}'; sandTexture '{v.sandTexture?.name}' dustTexture '{v.dustTexture?.name}' stoneGlow {C(v.stoneGlow)}; ");
                sb.Append($"dust block: {Block(v.dustRenderer, v.properties)}; stone block: {Block(v.stoneRenderer, v.stoneProperties)}; renderers:");
                foreach (var r in v.GetComponentsInChildren<Renderer>(true))
                {
                    var m = r.sharedMaterial;
                    sb.Append($" ['{r.name}' enabled {r.enabled} active {r.gameObject.activeInHierarchy} mat '{m?.name}' shader '{m?.shader?.name}' {MatProps(m)}]");
                }
                return sb.ToString();
            }

            static string MatProps(Material m)
            {
                if (m == null) return "";
                var parts = new List<string>();
                foreach (var p in new[] { "_BaseColor", "_Color", "_EmissionColor", "_Tint" }) if (m.HasProperty(p)) parts.Add($"{p} {C(m.GetColor(p))}");
                foreach (var p in new[] { "_BaseMap", "_MainTex", "_EmissionMap" }) if (m.HasProperty(p)) parts.Add($"{p} '{m.GetTexture(p)?.name}'");
                return string.Join(" ", parts);
            }

            static string Block(Renderer r, MaterialPropertyBlock own) => T(() =>
            {
                if (r == null) return "no renderer";
                var b = new MaterialPropertyBlock();
                r.GetPropertyBlock(b);
                var parts = new List<string> { $"HasPropertyBlock {r.HasPropertyBlock()} empty {b.isEmpty}" };
                foreach (var p in new[] { "_BaseMap", "_MainTex", "_DustTex", "_Texture" }) { var t = b.GetTexture(p); if (t != null) parts.Add($"{p} '{t.name}'"); }
                foreach (var p in new[] { "_BaseColor", "_Color", "_EmissionColor", "_Tint" }) if (b.HasColor(p)) parts.Add($"{p} {C(b.GetColor(p))}");
                foreach (var p in new[] { "_Fill", "_Heat", "_Glow", "_Level" }) if (b.HasFloat(p)) parts.Add($"{p} {b.GetFloat(p):F2}");
                return string.Join(" ", parts);
            });

            static readonly Color TestTint = new(0.25f, 0.2f, 0.18f, 1f);

            // tint: the modded cargo's CanisterTint (or a test charcoal) as _BaseColor on the dust renderer's property block;
            // a vanilla cargo on a reused view gets _BaseColor back to white. Logged once per (view, cargo).
            internal static void Tint(MoonDustCanisterView v, MoonCargo cargo)
            {
                var r = v.dustRenderer;
                if (r == null) return;
                var b = new MaterialPropertyBlock();
                r.GetPropertyBlock(b);
                bool modded = (byte)cargo > 2;
                var tint = modded ? (MoonStructures.CargoDefinition(cargo)?.CanisterTint ?? TestTint) : Color.white;
                b.SetColor("_BaseColor", tint);
                r.SetPropertyBlock(b);
                Once($"tint:{v.GetInstanceID()}:{(byte)cargo}", $"tint: view {v.GetInstanceID()} Id {v.Id} cargo {(byte)cargo} -> _BaseColor {C(tint)}; {Block(r, null)}");
            }
        }

        // A: walk to each target in turn (teleport 5 m off it, MoonWalker.Face), so the game's own Aimed/ShowPanel/ShowGrid
        // run; screenshot each. Then carry the hand briquette, then (far) stand 70 m off and look back at the belt.
        internal static class Tour
        {
            static readonly List<(int id, string label)> targets = new();
            static int index = -1, shots;
            static float stepAt, checkAt, shotAt;
            static bool checkedAim;
            static Vector3 home;
            const float StepSeconds = 6f;

            internal static void Start(MoonWorld world)
            {
                targets.Clear();
                var walker = Walker();
                home = walker != null ? walker.transform.position : Vector3.zero;
                foreach (int k in new[] { MoonWorld.SolarPostKind, MoonWorld.SolarTwinKind, MoonWorld.SolarTrackerKind })
                {
                    int best = Nearest(world, k);
                    if (best >= 0) targets.Add((best, $"vanilla kind {k}"));
                }
                int vanillaChamber = -1; float bd = 80f;
                foreach (var kv in world.depots) { if (kv.Value.kind != MoonWorld.PressureChamberKind || kv.Key == Chamber) continue; float d = Vector3.Distance(home, At(world, kv.Key)); if (d < bd) { bd = d; vanillaChamber = kv.Key; } }
                if (vanillaChamber >= 0) targets.Add((vanillaChamber, "vanilla chamber on a solar grid"));
                if (Gen >= 0) targets.Add((Gen, "generator"));
                if (Chamber >= 0) targets.Add((Chamber, "chamber"));
                if (Compactor >= 0) targets.Add((Compactor, "compactor"));
                if (Splitter >= 0) targets.Add((Splitter, "splitter"));
                targets.Add((-3, "carried briquette"));
                if (Test("far")) targets.Add((-2, "far canisters"));
                L($"tour: {string.Join(", ", targets.Select(t => $"{t.label} {t.id}"))}");
                index = -1; stepAt = Time.unscaledTime;
                LinksUntil = Time.unscaledTime + targets.Count * StepSeconds + 5f;
            }

            internal static void Tick(MoonWorld world, float now)
            {
                if (index >= targets.Count || targets.Count == 0) return;
                var walker = Walker();
                if (walker == null) return;
                if (now >= stepAt)
                {
                    index++;
                    if (index >= targets.Count) { L("tour: done"); return; }
                    var (id, label) = targets[index];
                    stepAt = now + StepSeconds; checkAt = now + 1.5f; shotAt = now + 3.5f; checkedAim = false;
                    if (id >= 0) { Near(world, walker, id); Aim(walker, At(world, id) + At(world, id).normalized, 1f); }
                    else if (id == -3) { Near(world, walker, Gen); Aim(walker, At(world, Gen), 1f); Canisters.Carry(world); }
                    else if (id == -2) FarWalk(world, walker);
                    L($"tour: looking at {label} {id}");
                }
                var inter = UnityEngine.Object.FindFirstObjectByType<MoonProductionInteraction>();
                int aimed = inter != null ? inter.Aimed(world) : -9;
                int want = targets[index].id;
                if (want >= 0 && !checkedAim && now >= checkAt)
                {
                    checkedAim = true;
                    if (aimed != want) { Aim(walker, At(world, want) + At(world, want).normalized, -1f); L($"tour: Aimed {aimed} != {want}; aimed again with the pitch negated"); }
                    else L($"tour: Aimed {aimed} == target; LookPitch {walker.LookPitch:F1}");
                }
                if (shotAt > 0f && now >= shotAt)
                {
                    shotAt = -1f;
                    L($"tour: at the shot Aimed {aimed}, want {want}; look panel: {Patches.PanelTexts(inter?.look)}");
                    Shot(targets[index].label);
                }
            }

            // Stand 5 m off the depot, on the side facing where the walker started.
            static void Near(MoonWorld world, MoonWalker walker, int id)
            {
                var c = At(world, id);
                var up = c.normalized;
                var dir = Vector3.ProjectOnPlane(home - c, up);
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.ProjectOnPlane(walker.transform.forward, up);
                float off = 5f + (world.depots.ContainsKey(id) && !MoonStructures.IsModded(world.depots.get_Item(id).kind) ? 1f : 0f);
                var p = c + dir.normalized * off;
                var ground = world.SurfaceAt(p.normalized, out Vector3 gUp, out bool on);
                walker.Teleport(ground + gUp * 0.2f, true);
                L($"tour: teleported to {V(ground)} (onStructure {on}), {Vector3.Distance(ground, c):F1} m from depot {id}");
            }

            static Vector3 farTarget;
            static void FarWalk(MoonWorld world, MoonWalker walker)
            {
                var c = At(world, Gen);
                var up = c.normalized;
                var away = Vector3.ProjectOnPlane(home - c, up).normalized;
                var axis = Vector3.Cross(up, away).normalized;
                var p = Quaternion.AngleAxis(70f / c.magnitude * Mathf.Rad2Deg, axis) * c;
                var ground = world.SurfaceAt(p.normalized, out Vector3 gUp, out bool _);
                walker.Teleport(ground + gUp * 0.2f, true);
                farTarget = c;
                Aim(walker, c, 1f);
                L($"tour: far walk to {V(ground)} ({Vector3.Distance(ground, c):F1} m from the generator)");
            }

            static void Aim(MoonWalker walker, Vector3 target, float sign)
            {
                var t = walker.transform;
                var up = t.position.normalized;
                var eye = t.position + up * 1.6f;
                var to = target - eye;
                var flat = Vector3.ProjectOnPlane(to, up);
                float pitch = Mathf.Atan2(Vector3.Dot(to, up), flat.magnitude) * Mathf.Rad2Deg;
                walker.Face(flat.normalized, sign * pitch);
                L($"tour: Face({V(flat.normalized)}, {sign * pitch:F1}) towards {V(target)} {flat.magnitude:F1} m away");
            }

            internal static void Shot(string label)
            {
                string dir = Path.Combine(BepInEx.Paths.CachePath, "structurelib");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"powerux-{++shots}.png");
                CoreDriver.Instance.StartCoroutine(BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(ShotRun(path, label)));
            }

            static System.Collections.IEnumerator ShotRun(string path, string label)
            {
                yield return new WaitForEndOfFrame();
                try
                {
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
                    UnityEngine.Object.Destroy(tex);
                    L($"screenshot {path} ({label})");
                }
                catch (Exception e) { L($"screenshot {path} threw: {e.Message}"); }
            }
        }

        // arc: our own MoonPowerLinks drawing generator -> chamber each frame.
        internal static class OwnArc
        {
            static MoonPowerLinks links;
            static bool logged;
            internal static void Tick(MoonWorld world)
            {
                if (!Test("arc") || Gen < 0 || Chamber < 0) return;
                if (links == null)
                {
                    var inter = UnityEngine.Object.FindFirstObjectByType<MoonProductionInteraction>();
                    if (inter == null) return;
                    links = new MoonPowerLinks(world, inter.transform);
                    L($"arc: created MoonPowerLinks(world, '{inter.name}'); material {T(() => links.Material?.name)}");
                }
                bool a = world.PowerAnchor(Gen, out Vector3 from);
                bool b = world.PowerAnchor(Chamber, out Vector3 to);
                if (!a) from = At(world, Gen) + At(world, Gen).normalized * 2f;
                if (!b) to = At(world, Chamber) + At(world, Chamber).normalized * 2f;
                float width = Patches.ArcWidth > 0f ? Patches.ArcWidth : 0.06f;
                var color = Patches.ArcWidth > 0f ? Patches.ArcColor : MoonProductionInteraction.Cyan;
                links.Begin();
                links.Arc(from, to, width, color);
                links.End();
                if (!logged) { logged = true; L($"arc: drawing {V(from)} (anchor {a}) -> {V(to)} (anchor {b}) width {width:F3} {C(color)}; used {links.used} lines {links.lines?.Count}"); }
            }
        }
    }
}
#endif
