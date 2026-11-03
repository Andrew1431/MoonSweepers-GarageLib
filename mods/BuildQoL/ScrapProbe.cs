#if DEVBUILD
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using MoonCleaners;
using UnityEngine;

namespace BuildQoL
{
    // [Dev] ScrapProbe, driven from ScrapBeltContentsTweak.DevTick. 1: 12 s after a world loads, three cases on short belts
    // built from code next to the walker, each with canisters put on it (StructureLib's Spawn: PackDustCanister, then
    // MoveCanisterRecord onto the belt):
    //   A  scrap: the modifier forced on (ForceHeld), the belt removed through the builder's removal mode (BeginRemoval,
    //      TestInput aim + place), falling back to a direct TryRemoveConveyor call if the builder never aims at it.
    //   B  vanilla: the same without the modifier; the riders should drop.
    //   C  undo: scrap a belt that has an undo step, then UndoBelts.
    // then RequestSave. 2: only log the counts after load (the reload check). Every step logs canisters.Count,
    // RidingCanisterCount and DroppedCanisterCount.
    internal static class ScrapProbe
    {
        internal static bool ForceHeld;
        static ConfigEntry<int> Mode;
        static ConfigEntry<float> Hold;

        internal static void Bind(ConfigFile cfg)
        {
            Mode = cfg.Bind("Dev", "ScrapProbe", 0, "Dev build: 1 = 12 s after a world loads, build belts next to the walker, load them with canisters and remove them with and without the scrap modifier (forced), then save; 2 = only log the canister counts after load. 0 = off.");
            Hold = cfg.Bind("Dev", "ScrapProbeHold", 0f, "Dev build: seconds the probe keeps aiming at the first belt with the modifier forced on, before removing it (time for a screenshot).");
            Plugin.L.LogInfo($"[dev] ScrapProbe={Mode.Value}");
        }

        internal static void Patch(Harmony h)
        {
            if (Mode.Value <= 0) return;
            try { h.CreateClassProcessor(typeof(ScrapProbePatches)).Patch(); }
            catch (Exception e) { Plugin.L.LogError("[scrap] probe patches failed: " + e); }
        }

        enum Step { Wait, Case, Board, Equip, AimHint, AimHeld, Click, Watch, Undo, Save, Done }
        static Step step = Step.Wait;
        static float stepT;
        static int frames, caseIndex, heading;
        static MoonWorld w;
        static MoonWalker walker;
        static MoonDepotBuilder b;
        static int belt = -1;
        static List<int> spawned = new();
        static readonly List<int> belts = new();
        static readonly List<List<int>> spawnedBy = new();
        static bool viaBuilder;
        static int c0, r0, d0;
        static MoonDepotBuilder.TestBuildInput input;
        static readonly string[] CaseNames = { "A-scrap", "B-vanilla", "C-undo" };

        static void Info(string s) => Plugin.L.LogInfo($"[scrap] f{Time.frameCount} {s}");

        static string Counts() => $"canisters={w.canisters.Count} riding={w.RidingCanisterCount} dropped={w.DroppedCanisterCount} riders={w.riders.Count}";

        static void Next(Step s) { step = s; frames = 0; stepT = Time.unscaledTime; }

        internal static void Tick(float gameT)
        {
            if (Mode == null || Mode.Value <= 0 || step == Step.Done) return;
            if (step == Step.Wait)
            {
                if (gameT < 12f) return;
                if (DevTools.OnSlot1()) { Info("slot-1 is loaded; probe refuses"); step = Step.Done; return; }
                w = UnityEngine.Object.FindFirstObjectByType<MoonWorld>();
                var walkers = UnityEngine.Object.FindObjectsByType<MoonWalker>(FindObjectsSortMode.None);
                walker = walkers != null && walkers.Length > 0 ? walkers[0] : null;
                var cam = Camera.main;
                b = cam != null ? MoonDepotBuilder.ForCamera(cam) : null;
                if (w == null || walker == null) { Info($"no world ({w != null}) or walker ({walker != null}); stop"); step = Step.Done; return; }
                string local = null, lp = null;
                try { local = MoonCrew.LocalPlayerId; } catch { }
                try { lp = MoonWorld.LocalPlayer; } catch { }
                Info($"start mode {Mode.Value}: {Counts()} Joined={MoonCrew.Joined} LocalPlayerId='{local}' MoonWorld.LocalPlayer='{lp}' builder={(b != null)} modifier={ScrapBeltContentsTweak.Modifier.Value} othersCargo={ScrapBeltContentsTweak.OthersCargo.Value}");
                LogBindings();
                LogOwners();
                if (Mode.Value != 1) { step = Step.Done; return; }
                caseIndex = 0;
                Next(Step.Case);
                return;
            }
            try { Run(); }
            catch (Exception e) { Plugin.L.LogError("[scrap] probe threw: " + e); Finish(); }
        }

        static void Finish()
        {
            ForceHeld = false;
            try { if (b != null) { b.TestInput = null; if (b.IsRemoving) b.EndRemoval(); } } catch { }
            Info("done: " + (w != null ? Counts() : ""));
            step = Step.Done;
        }

        // Every MoonInput binding whose keys include a Ctrl key (reflection: the binding fields are static interop properties).
        static void LogBindings()
        {
            try
            {
                var all = new List<string>();
                var ctrl = new List<string>();
                foreach (var p in typeof(MoonInput).GetProperties(BindingFlags.Public | BindingFlags.Static))
                {
                    if (p.PropertyType != typeof(MoonInput.Binding)) continue;
                    var bind = p.GetValue(null) as MoonInput.Binding;
                    if (bind == null) continue;
                    var keys = new List<string>();
                    try { var ks = bind.keys; if (ks != null) foreach (var k in ks) keys.Add(k.ToString()); } catch (Exception e) { keys.Add("<" + e.GetType().Name + ">"); }
                    string s = $"{p.Name}=[{string.Join("+", keys)}]";
                    all.Add(s);
                    if (keys.Exists(k => k.Contains("Ctrl"))) ctrl.Add(s);
                }
                Info($"MoonInput bindings: {string.Join(" ", all)}");
                Info($"MoonInput bindings with a Ctrl key: {(ctrl.Count == 0 ? "none" : string.Join(" ", ctrl))}");
            }
            catch (Exception e) { Info("bindings: " + e.GetType().Name + " " + e.Message); }
        }

        static void LogOwners()
        {
            var by = new Dictionary<string, int>();
            foreach (var kv in w.canisters) { string o = kv.Value.owner ?? "<null>"; by[o] = by.TryGetValue(o, out int n) ? n + 1 : 1; }
            var parts = new List<string>();
            foreach (var kv in by) parts.Add($"'{kv.Key}' x{kv.Value}");
            Info("canister owners: " + string.Join(", ", parts));
        }

        static string RidersOn(int id)
        {
            var parts = new List<string>();
            foreach (var kv in w.riders)
                if (kv.Value.belt == id) parts.Add($"[{kv.Key} d {kv.Value.distance:F2} moving {kv.Value.moving} owner '{(w.canisters.ContainsKey(kv.Key) ? w.canisters.get_Item(kv.Key).owner : "?")}']");
            return parts.Count == 0 ? "none" : string.Join(" ", parts);
        }

        static string Present()
        {
            var parts = new List<string>();
            foreach (int id in spawned) parts.Add($"{id}:{(w.canisters.ContainsKey(id) ? "c" : "-")}{(w.riders.ContainsKey(id) ? "r" : "-")}");
            return string.Join(" ", parts);
        }

        static void Run()
        {
            frames++;
            switch (step)
            {
                case Step.Case:
                {
                    if (caseIndex >= CaseNames.Length) { Next(Step.Save); return; }
                    ForceHeld = false;
                    if (caseIndex == 0)
                    {
                        // Every belt and canister is set up before the build tool is first taken out: PackDustCanister
                        // refuses while the tool is in hand.
                        for (int k = 0; k < CaseNames.Length; k++)
                        {
                            int bk = BuildBelt(out string why);
                            if (bk < 0) { Info($"{CaseNames[k]}: no belt ({why}); stop"); Finish(); return; }
                            var path = w.BeltPath(bk);
                            var ids = new List<int>();
                            int n = k == 2 ? 2 : 3;
                            for (int i = 0; i < n; i++)
                            {
                                int id = Spawn(path, 1.0f + i * Mathf.Max(MoonWorld.BeltSpacing, 0.6f), out string sw);
                                if (id >= 0) ids.Add(id); else Info($"spawn {i}: {sw}");
                            }
                            belts.Add(bk);
                            spawnedBy.Add(ids);
                            Info($"{CaseNames[k]}: belt {bk} length {path.Length:F2}, spawned [{string.Join(",", ids)}]; {Counts()}");
                        }
                    }
                    belt = belts[caseIndex];
                    spawned = spawnedBy[caseIndex];
                    Next(Step.Board);
                    return;
                }
                case Step.Board:
                    if (Time.unscaledTime - stepT < 3f) return;
                    Info($"{CaseNames[caseIndex]}: riders on belt {belt}: {RidersOn(belt)}; {Counts()}");
                    if (caseIndex == 2 && b != null)
                    {
                        var u = new MoonDepotBuilder.UndoStep();
                        u.belts = new Il2CppSystem.Collections.Generic.List<int>();
                        u.belts.Add(belt);
                        b.undo.Add(u);
                        Info($"C-undo: added an undo step for belt {belt}; undo steps {b.undo.Count}");
                    }
                    Next(Step.Equip);
                    return;
                case Step.Equip:
                    // Removal mode only runs with the build tool in hand: BeginPlacementFromShop selects the tool slot (as the
                    // KeepSelection probe does), then the placement is cancelled and removal begun.
                    viaBuilder = false;
                    if (b == null) { Next(Step.Click); return; }
                    if (frames == 1 && !b.HoldingBuildTool) { bool ok = b.BeginPlacementFromShop(93); Info($"{CaseNames[caseIndex]}: BeginPlacementFromShop(93) -> {ok}; HoldingBuildTool={b.HoldingBuildTool}"); return; }
                    if (frames == 4 && b.IsPlacing) { b.CancelPlacement(); return; }
                    if (frames < 8) return;
                    try { if (!b.IsRemoving) b.BeginRemoval(); } catch (Exception e) { Info("BeginRemoval threw: " + e.Message); }
                    viaBuilder = b.IsRemoving;
                    Info($"{CaseNames[caseIndex]}: builder removal mode {viaBuilder}; HoldingBuildTool={b.HoldingBuildTool} IsOpen={b.IsOpen}");
                    Next(Step.AimHint);
                    return;
                case Step.AimHint:
                    if (!viaBuilder) { Next(Step.Click); return; }
                    Aim(false);
                    if (frames == 20) Info($"{CaseNames[caseIndex]}: aimed (not held): aimBelt={ScrapBeltPatches.aimBelt} aimOk={ScrapBeltPatches.aimOk} aimFrame-now={ScrapBeltPatches.aimFrame - Time.frameCount} prompt='{PromptText()}' written='{Esc(ScrapPrompt.Shown)}'; {Diag()}");
                    if (frames < 20) return;
                    ForceHeld = caseIndex != 1;
                    Next(Step.AimHeld);
                    return;
                case Step.AimHeld:
                    Aim(false);
                    if (frames < 10) return;
                    if (caseIndex == 0 && Time.unscaledTime - stepT < Hold.Value) { if (frames % 120 == 10) Info(UiChain() + "; " + Layout() + "; shown='" + Esc(ScrapPrompt.Shown) + "'"); return; }
                    Info($"{CaseNames[caseIndex]}: aimed (held={ForceHeld}): aimBelt={ScrapBeltPatches.aimBelt} aimOk={ScrapBeltPatches.aimOk} prompt='{PromptText()}' written='{Esc(ScrapPrompt.Shown)}'; {Diag()}");
                    if (ScrapBeltPatches.aimBelt != belt) { Info($"{CaseNames[caseIndex]}: the builder aims at belt {ScrapBeltPatches.aimBelt}, not {belt}; removing directly instead"); viaBuilder = false; }
                    Next(Step.Click);
                    return;
                case Step.Click:
                    ForceHeld = caseIndex != 1;
                    if (frames == 1) { c0 = w.canisters.Count; r0 = w.RidingCanisterCount; d0 = w.DroppedCanisterCount; ScrapProbePatches.calls = 0; }
                    if (viaBuilder)
                    {
                        if (frames == 1) { Aim(true); Info($"{CaseNames[caseIndex]}: place pressed in removal mode (held={ForceHeld}); before {Counts()}"); return; }
                        Aim(false);
                        if (ScrapProbePatches.calls == 0 && frames < 3) return;
                    }
                    else
                    {
                        bool ok = w.TryRemoveConveyor(walker, belt, out string reason);
                        Info($"{CaseNames[caseIndex]}: direct TryRemoveConveyor({belt}) -> {ok} '{reason}' (held={ForceHeld})");
                    }
                    Next(Step.Watch);
                    return;
                case Step.Watch:
                    if (frames == 1) ForceHeld = false;
                    if (viaBuilder) Aim(false);
                    if (frames == 1 || frames == 5 || frames == 30)
                        Info($"{CaseNames[caseIndex]}: +{frames}f belt in conveyors {w.conveyors.ContainsKey(belt)}; spawned {Present()}; {Counts()} (riding {w.RidingCanisterCount - r0:+0;-0;0}, dropped {w.DroppedCanisterCount - d0:+0;-0;0}, canisters {w.canisters.Count - c0:+0;-0;0})");
                    if (frames < 30) return;
                    if (caseIndex == 2) { Next(Step.Undo); return; }
                    caseIndex++;
                    Next(Step.Case);
                    return;
                case Step.Undo:
                {
                    int before = b != null ? b.undo.Count : -1;
                    string ex = "none";
                    try { if (b != null) b.UndoBelts(); } catch (Exception e) { ex = e.ToString(); }
                    Info($"C-undo: UndoBelts after the scrap: exception {ex}; undo steps {before} -> {(b != null ? b.undo.Count : -1)}; belt in conveyors {w.conveyors.ContainsKey(belt)}; {Counts()}");
                    caseIndex++;
                    Next(Step.Case);
                    return;
                }
                case Step.Save:
                    if (frames == 1)
                    {
                        try { if (b != null) { b.TestInput = null; if (b.IsRemoving) b.EndRemoval(); } } catch { }
                        w.RequestSave();
                        Info($"RequestSave: {Counts()}");
                        return;
                    }
                    if (Time.unscaledTime - stepT < 4f) return;
                    Info($"after save 4 s: {Counts()}");
                    Finish();
                    return;
            }
        }

        static string Esc(string s) => (s ?? "").Replace("\n", "\\n");

        static string Rect(RectTransform rt)
        {
            if (rt == null) return "null";
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return $"'{rt.name}' {(rt.gameObject.activeInHierarchy ? "visible" : "hidden")} world ({c[0].x:F0},{c[0].y:F0})-({c[2].x:F0},{c[2].y:F0}) anchored {rt.anchoredPosition} size {rt.rect.size} pivot {rt.pivot}";
        }

        static string Layout()
        {
            var parts = new List<string>();
            try
            {
                var keys = b.removeKeys;
                parts.Add("removeKeys " + Rect(keys));
                var l = ScrapPrompt.Label;
                parts.Add("label " + (l != null ? Rect(l.rectTransform) + $" text '{l.text}' chars {l.textInfo.characterCount} font {l.fontSize:F0} color {l.color}" : "null"));
                if (keys != null && keys.parent != null)
                {
                    var comps = new List<string>();
                    foreach (var comp in keys.parent.GetComponents<Component>()) comps.Add(comp.GetIl2CppType().Name);
                    parts.Add($"parent '{keys.parent.name}' components [{string.Join(",", comps)}] children {keys.parent.childCount}");
                }
                parts.Add($"screen {Screen.width}x{Screen.height}");
            }
            catch (Exception e) { parts.Add("<" + e.GetType().Name + " " + e.Message + ">"); }
            return string.Join("; ", parts);
        }

        // Which of the prompt's ancestors are switched off, and the other removal UI (removeKeys, controls).
        static string UiChain()
        {
            var parts = new List<string>();
            try
            {
                for (var t = b.prompt != null ? b.prompt.transform : null; t != null; t = t.parent)
                    parts.Add($"{t.name}:{(t.gameObject.activeSelf ? "on" : "OFF")}");
            }
            catch (Exception e) { parts.Add("<" + e.GetType().Name + ">"); }
            string s = "prompt chain " + string.Join(" < ", parts);
            try { s += $"; removeKeys {(b.removeKeys != null ? (b.removeKeys.gameObject.activeInHierarchy ? "visible" : "hidden") : "null")}"; } catch { }
            try { s += $"; controls '{Esc(b.controls != null ? b.controls.text : null)}' {(b.controls != null && b.controls.gameObject.activeInHierarchy ? "visible" : "hidden")}"; } catch { }
            try { s += $"; placementHud {(b.placementHud != null && b.placementHud.activeInHierarchy ? "visible" : "hidden")}; ui {(b.ui != null && b.ui.activeInHierarchy ? "visible" : "hidden")}"; } catch { }
            return s;
        }

        static string Diag()
        {
            string s = "";
            try { s += $"HoldingBuildTool={b.HoldingBuildTool} IsRemoving={b.IsRemoving} PromptText='{b.PromptText}' promptActive={(b.prompt != null && b.prompt.gameObject.activeInHierarchy)}"; } catch (Exception e) { s += " <" + e.GetType().Name + ">"; }
            try { s += $" FirstPerson={walker.IsFirstPerson} UIBlocked={walker.UIBlocked} Mounted={walker.Mounted}"; } catch (Exception e) { s += " walker <" + e.GetType().Name + ">"; }
            try { s += $" RemoveReach={MoonDepotBuilder.RemoveReach}"; } catch { }
            try
            {
                if (input != null && Physics.Raycast(input.aim, out RaycastHit h, 200f, ~0, QueryTriggerInteraction.Ignore))
                    s += $" aimHit='{h.collider.name}' at {h.distance:F1} m (walker {Vector3.Distance(walker.transform.position, h.point):F1} m)";
                else s += " aimHit=none";
            }
            catch (Exception e) { s += " ray <" + e.GetType().Name + ">"; }
            return s;
        }

        static string PromptText()
        {
            try { return b != null && b.prompt != null ? (b.prompt.text ?? "").Replace("\n", "\\n") : "<none>"; } catch (Exception e) { return "<" + e.GetType().Name + ">"; }
        }

        // A ray from the camera to the belt 0.4 m from its start (the canisters wait at the far end).
        static void Aim(bool place)
        {
            if (b == null) return;
            if (input == null) input = new MoonDepotBuilder.TestBuildInput();
            var path = w.BeltPath(belt);
            var cam = Camera.main;
            if (path != null && cam != null)
            {
                var p = path.Point(0.4f);
                input.aim = new Ray(cam.transform.position, (p - cam.transform.position).normalized);
            }
            input.place = place;
            b.TestInput = input;
        }

        static readonly float[] Headings = { 90f, 270f, 180f, 0f, 45f, 135f, 225f, 315f, 112f, 247f };

        // A free-standing 4 m belt, lifted off the ground, starting 3 m from the walker, on the first heading the route check accepts.
        static int BuildBelt(out string why)
        {
            why = "no heading valid";
            var kit = MoonConveyorKit.Load();
            Vector3 feet = walker.transform.position;
            Vector3 up = feet.normalized;
            var cam = Camera.main;
            Vector3 fwd = Vector3.ProjectOnPlane(cam != null ? cam.transform.forward : walker.transform.forward, up).normalized;
            for (int tries = 0; tries < Headings.Length; tries++)
            {
                float deg = Headings[(heading + tries) % Headings.Length];
                Vector3 dir = Quaternion.AngleAxis(deg, up) * fwd;
                if (!Ground(feet + dir * 3f, up, out Vector3 gs) || !Ground(feet + dir * 7f, up, out Vector3 ge)) continue;
                foreach (float lift in new[] { 0.4f, 0.8f, 1.4f })
                foreach (int none in new[] { -1, 0 })
                {
                    Vector3 s = gs + gs.normalized * lift, e = ge + ge.normalized * lift;
                    var plan = new MoonWorld.ConveyorPlan();
                    plan.start = s; plan.startDirection = dir; plan.startUp = s.normalized;
                    plan.end = e; plan.endDirection = dir; plan.endUp = e.normalized;
                    plan.startStructure = none; plan.endStructure = none;
                    plan.startBelt = none; plan.endBelt = none; plan.replaces = none;
                    bool valid = w.ConveyorRouteValid(plan, kit, feet, out MoonConveyorPath path, out string reason, out bool routable);
                    if (!valid) { why = $"heading {deg} ids {none}: {reason}"; Info($"route heading {deg} lift {lift} ids {none}: {reason}"); continue; }
                    if (w.TryBuildConveyor(walker, plan, out reason, out int id))
                    {
                        heading = (heading + tries + 1) % Headings.Length;
                        Info($"built belt {id} at heading {deg} (ids {none}): '{reason}'");
                        return id;
                    }
                    why = $"heading {deg}: TryBuildConveyor '{reason}'";
                }
            }
            heading = (heading + 1) % Headings.Length;
            return -1;
        }

        static bool Ground(Vector3 p, Vector3 up, out Vector3 hit)
        {
            hit = p;
            if (Physics.Raycast(p + up * 10f, -up, out RaycastHit h, 30f, ~0, QueryTriggerInteraction.Ignore)) { hit = h.point; return true; }
            return false;
        }

        // StructureLib's DevBelts.Spawn: a 1 kg dust canister packed from the walker's dust, moved onto the belt at ride height.
        static int Spawn(MoonConveyorPath path, float d, out string why)
        {
            why = null;
            var p = path.Point(d);
            var up = path.Up(p);
            if (up.sqrMagnitude < 0.5f) up = p.normalized;
            string player = MoonWorld.LocalPlayer;
            w.DevSetCarriedDust(player, 1.0);
            if (!w.PackDustCanister(player, p + up * 0.5f, up, out int id)) { why = "PackDustCanister refused"; return -1; }
            if (!w.canisters.ContainsKey(id)) { why = $"canister {id} is not in world.canisters"; return -1; }
            var c = w.canisters.get_Item(id);
            var at = p + up * MoonWorld.BeltRideHeight;
            var rotation = new Il2CppSystem.Nullable<Quaternion>(c.rotation);
            var moved = new MoonDroppedCanister(c.id, c.owner, c.kilograms, c.capacity, at, up, c.createdAt, rotation, c.cargo);
            moved.physicalPose = c.physicalPose;
            w.MoveCanisterRecord(moved, at, up, rotation);
            return id;
        }
    }

    // Logs every TryRemoveConveyor result (after BuildQoL's prefix and the original) while the probe runs.
    [HarmonyPatch]
    internal static class ScrapProbePatches
    {
        internal static int calls;

        [HarmonyPostfix, HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryRemoveConveyor))]
        static void After(MoonWorld __instance, int id, bool __result, ref string reason)
        {
            try
            {
                calls++;
                Plugin.L.LogInfo($"[scrap] f{Time.frameCount} TryRemoveConveyor({id}) -> {__result} '{reason}'; belt in conveyors {__instance.conveyors.ContainsKey(id)}; riders on it now {CountOn(__instance, id)}; last scrap {ScrapBeltPatches.lastCount} ({ScrapBeltPatches.lastKg:F1} kg) belt {ScrapBeltPatches.lastBelt}");
            }
            catch (Exception e) { Plugin.Once("scrap-probe-after", "[scrap] " + e); }
        }

        static int CountOn(MoonWorld w, int id) { int n = 0; foreach (var kv in w.riders) if (kv.Value.belt == id) n++; return n; }
    }
}
#endif
