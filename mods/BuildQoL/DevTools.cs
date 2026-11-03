#if DEVBUILD
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using MoonCleaners;
using UnityEngine;

namespace BuildQoL
{
    // Dev-build helpers: load a test slot from the main menu, and a probe that logs the builder's state around a
    // placement and drives placements itself through MoonDepotBuilder.TestInput (the game's own test hook).
    internal static class DevTools
    {
        static ConfigEntry<int> AutoLoadSlot;
        static ConfigEntry<bool> Probe;
        internal static ConfigEntry<string> ProbeKinds;
        static float menuT, gameT;
        static bool loadRequested;

        internal static void Bind(ConfigFile cfg)
        {
            AutoLoadSlot = cfg.Bind("Dev", "AutoLoadSlot", -1, "Dev build: load the existing save in folder slot-<n> from the main menu. -1 = off. Slot 1 (the real save) is always refused.");
            Probe = cfg.Bind("Dev", "Probe", false, "Dev build: log the builder's state after MachineBuilt, NoteBuilt, CancelPlacement, BeginPlacement and BeginConveyor, and 12 s after a world loads place each of ProbeKinds through TestInput, logging the state after each.");
            ProbeKinds = cfg.Bind("Dev", "ProbeKinds", "93,93,96,101,1000", "Dev build: kinds the probe places, in order, each on its own heading 7 m from the walker. 1000 = a belt (two clicks). A kind repeated right after itself is placed from the kept ghost, without re-selecting it.");
            Plugin.L.LogInfo($"[dev] AutoLoadSlot={AutoLoadSlot.Value} Probe={Probe.Value} ProbeKinds='{ProbeKinds.Value}'");
        }

        internal static void Patch(Harmony h)
        {
            if (!Probe.Value) return;
            try { h.CreateClassProcessor(typeof(ProbePatches)).Patch(); Plugin.L.LogInfo("[dev] probe patches applied"); }
            catch (Exception e) { Plugin.L.LogError("[dev] probe patches failed: " + e); }
        }

        internal static string State(MoonDepotBuilder b)
        {
            if (b == null) return "builder=null";
            string s = $"selectedStructure={b.selectedStructure} selectedBuildId={b.selectedBuildId} ghostKind={b.ghostKind} IsPlacing={b.IsPlacing} ConveyorMode={b.ConveyorMode} valid={b.PlacementValid} IsOpen={b.IsOpen} ghost={(b.ghost != null)} yaw={b.yaw:F0}";
            try
            {
                var recent = b.Recent;
                if (recent != null) { var r = new List<int>(); foreach (var k in recent) r.Add(k); s += " Recent=[" + string.Join(",", r) + "]"; }
            }
            catch (Exception e) { s += " Recent=<" + e.GetType().Name + ">"; }
            try { if (b.noticeUntil > Time.unscaledTime && b.notice != null) s += $" notice='{b.notice}'"; } catch { }
            return s;
        }

        internal static void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            if (MoonSession.InMenu && !MoonSession.Loading)
            {
                menuT += dt;
                if (AutoLoadSlot.Value >= 0 && !loadRequested && menuT > 6f) { loadRequested = true; LoadSlot(AutoLoadSlot.Value); }
            }
            if (!MoonSession.InGame) { gameT = 0; return; }
            gameT += dt;
            if (Probe.Value) Prober.Tick(gameT);
        }

        static string ActiveFolder() => (MoonSaveSlots.SlotFolder(MoonSaveSlots.Active) ?? "").Replace('\\', '/').TrimEnd('/');

        internal static bool OnSlot1() => ActiveFolder().EndsWith("/slot-1");

        // Copied from StructureLib's DevTools.LoadSlot: setting MoonSaveSlots.Active then StartShift() loads; the slot
        // is matched by folder name, never by index.
        static void LoadSlot(int slot)
        {
            if (slot == 1) { Plugin.L.LogWarning("[dev] AutoLoadSlot: refusing slot-1 (the real save)"); return; }
            string suffix = "/slot-" + slot;
            int target = -1;
            for (int i = 0; i <= MoonSaveSlots.Count; i++)
            {
                string f = (MoonSaveSlots.SlotFolder(i) ?? "").Replace('\\', '/').TrimEnd('/');
                if (f.EndsWith(suffix)) { target = i; break; }
            }
            if (target < 0) { Plugin.L.LogWarning($"[dev] AutoLoadSlot: no slot index maps to folder slot-{slot}; staying in menu"); return; }
            MoonSaveSlots.Active = target;
            string chosen = ActiveFolder();
            if (!chosen.EndsWith(suffix)) { Plugin.L.LogWarning($"[dev] AutoLoadSlot: active slot resolved to '{chosen.Substring(chosen.LastIndexOf('/') + 1)}', refusing"); return; }
            Plugin.L.LogInfo($"[dev] AutoLoadSlot: loading slot index {target} (folder slot-{slot})");
            MoonSession.StartShift();
        }
    }

    // Logging postfixes, applied only with [Dev] Probe. Low priority: they log after KeepSelection's postfix ran.
    [HarmonyPatch]
    internal static class ProbePatches
    {
        static void Log(string what, MoonDepotBuilder b) =>
            Plugin.L.LogInfo($"[probe] f{Time.frameCount} after {what}: {DevTools.State(b)}");

        [HarmonyPostfix, HarmonyPriority(Priority.Low), HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.MachineBuilt))]
        static void MachineBuilt(MoonDepotBuilder __instance) { try { Log("MachineBuilt", __instance); } catch (Exception e) { Plugin.Once("probe-mb", "[probe] " + e); } }

        [HarmonyPostfix, HarmonyPriority(Priority.Low), HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.NoteBuilt))]
        static void NoteBuilt(MoonDepotBuilder __instance, int kind) { try { Log($"NoteBuilt({kind})", __instance); } catch (Exception e) { Plugin.Once("probe-nb", "[probe] " + e); } }

        [HarmonyPostfix, HarmonyPriority(Priority.Low), HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.CancelPlacement))]
        static void CancelPlacement(MoonDepotBuilder __instance) { try { Log("CancelPlacement", __instance); } catch (Exception e) { Plugin.Once("probe-cp", "[probe] " + e); } }

        [HarmonyPostfix, HarmonyPriority(Priority.Low), HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.BeginPlacement))]
        static void BeginPlacement(MoonDepotBuilder __instance) { try { Log("BeginPlacement", __instance); } catch (Exception e) { Plugin.Once("probe-bp", "[probe] " + e); } }

        [HarmonyPostfix, HarmonyPriority(Priority.Low), HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.BeginConveyor))]
        static void BeginConveyor(MoonDepotBuilder __instance) { try { Log("BeginConveyor", __instance); } catch (Exception e) { Plugin.Once("probe-bc", "[probe] " + e); } }
    }

    // Places each ProbeKinds entry through TestInput: select it (unless the previous entry was the same kind and the
    // ghost was kept), aim at a point 7 m out on its own heading until the placement check says valid, press place for one
    // frame, then log the state for a few frames.
    internal static class Prober
    {
        enum Step { Wait, Select, Aim, Press, Watch, BeltSecond, Done }
        static Step step = Step.Wait;
        static List<int> kinds;
        static int index, heading, frames, attempts, watchFrames;
        static float stepT;
        static MoonDepotBuilder b;
        static MoonDepotBuilder.TestBuildInput input;
        static int depotsBefore;

        static readonly float[] Headings = { 90f, 135f, 180f, 225f, 270f, 315f, 45f, 112f, 157f, 202f, 247f, 292f };
        static readonly float[] Metres = { 7f, 12f, 4f, 18f };
        static bool freshSelect;

        static void Info(string s) => Plugin.L.LogInfo($"[probe] f{Time.frameCount} {s}");

        static MoonWorld World() => b != null ? b.World : null;

        static int Depots() { var w = World(); return w != null && w.depots != null ? w.depots.Count : -1; }

        internal static void Tick(float gameT)
        {
            if (step == Step.Done) return;
            if (step == Step.Wait)
            {
                if (gameT < 12f) return;
                if (DevTools.OnSlot1()) { Info("slot-1 is loaded; probe refuses to place anything"); step = Step.Done; return; }
                kinds = ParseKinds();
                var cam = Camera.main;
                b = cam != null ? MoonDepotBuilder.ForCamera(cam) : null;
                if (b == null) b = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>();
                if (b == null) { Info("no MoonDepotBuilder; probe stops"); step = Step.Done; return; }
                Info($"start: kinds [{string.Join(",", kinds)}], ConveyorKind={MoonDepotBuilder.ConveyorKind}, KeepSelection={Plugin.KeepSelection.Value}, {DevTools.State(b)}");
                index = 0; heading = 0;
                Next(Step.Select);
                return;
            }
            try { Run(); }
            catch (Exception e) { Plugin.L.LogError("[probe] threw: " + e); Finish(); }
        }

        static List<int> ParseKinds()
        {
            var r = new List<int>();
            foreach (var s in DevTools.ProbeKinds.Value.Split(',')) if (int.TryParse(s.Trim(), out int k)) r.Add(k);
            return r;
        }

        static void Next(Step s) { step = s; frames = 0; stepT = Time.unscaledTime; }

        static void Finish()
        {
            try { if (b != null) { b.TestInput = null; if (b.IsPlacing) b.CancelPlacement(); } } catch { }
            Info("done: " + DevTools.State(b));
            step = Step.Done;
        }

        // A ray from the camera to a ground point 'metres' out from the walker at 'deg' from its facing.
        static Ray AimRay(float deg, float metres)
        {
            var cam = Camera.main;
            Vector3 pos = cam.transform.position;
            Transform wt = null;
            var walkers = UnityEngine.Object.FindObjectsByType<MoonWalker>(FindObjectsSortMode.None);
            if (walkers != null && walkers.Length > 0) wt = walkers[0].transform;
            Vector3 feet = wt != null ? wt.position : pos;
            Vector3 up = feet.normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, up).normalized;
            Vector3 dir = Quaternion.AngleAxis(deg, up) * fwd;
            Vector3 target = feet + dir * metres - up * 0.3f;
            return new Ray(pos, (target - pos).normalized);
        }

        static void SetInput(bool place)
        {
            if (input == null) input = new MoonDepotBuilder.TestBuildInput();
            float deg = Headings[heading % Headings.Length];
            input.aim = AimRay(deg, Metres[attempts % Metres.Length]);
            input.place = place;
            b.TestInput = input;
        }

        static void Run()
        {
            frames++;
            if (index >= kinds.Count) { Finish(); return; }
            int kind = kinds[index];
            switch (step)
            {
                case Step.Select:
                {
                    bool reuse = index > 0 && kinds[index - 1] == kind && kind != MoonDepotBuilder.ConveyorKind;
                    if (reuse)
                    {
                        Info($"#{index} kind {kind}: not re-selecting (kept ghost expected): {DevTools.State(b)}");
                        if (!b.IsPlacing || b.selectedBuildId != kind) Info($"#{index} kind {kind}: NO kept ghost of this kind; probe re-selects to continue");
                        else { attempts = 0; Next(Step.Aim); return; }
                    }
                    if (b.IsPlacing) { b.CancelPlacement(); return; } // next frame BeginPlacementFromShop sees Building cleared
                    if (frames < 3) return;
                    freshSelect = true;
                    bool ok = b.BeginPlacementFromShop(kind);
                    Info($"#{index} kind {kind}: BeginPlacementFromShop -> {ok}; CanBuild={b.CanBuild(kind)}; {DevTools.State(b)}");
                    if (!ok && frames < 15) return;
                    attempts = 0;
                    Next(Step.Aim);
                    return;
                }
                case Step.Aim:
                    SetInput(false);
                    // A fresh selection gets yaw 135 so the next, kept ghost shows whether KeepSelection carried the rotation over.
                    if (frames == 1 && freshSelect && kind != MoonDepotBuilder.ConveyorKind) { freshSelect = false; b.yaw = 135f; Info($"#{index} kind {kind}: yaw set to 135"); }
                    if (frames > 20 && (b.PlacementValid || (b.ConveyorMode && frames > 30))) { depotsBefore = Depots(); Next(Step.Press); return; }
                    if (Time.unscaledTime - stepT > 2.5f)
                    {
                        Info($"#{index} kind {kind}: heading {Headings[heading % Headings.Length]} at {Metres[attempts % Metres.Length]} m not valid ({b.PlacementReason}); next heading");
                        attempts++;
                        heading++;
                        if (attempts > 12) { Info($"#{index} kind {kind}: giving up"); index++; Next(Step.Select); return; }
                        Next(Step.Aim);
                    }
                    return;
                case Step.Press:
                    SetInput(frames == 1);
                    if (frames == 1) { Info($"#{index} kind {kind}: place pressed at heading {Headings[heading % Headings.Length]}: {DevTools.State(b)}"); return; }
                    watchFrames = 0;
                    Next(Step.Watch);
                    return;
                case Step.Watch:
                    SetInput(false);
                    watchFrames++;
                    if (watchFrames == 1 || watchFrames == 5 || watchFrames == 30)
                        Info($"#{index} kind {kind}: +{watchFrames}f depots {depotsBefore}->{Depots()}: {DevTools.State(b)}");
                    if (watchFrames < 30) return;
                    if (kind == MoonDepotBuilder.ConveyorKind && b.ConveyorMode)
                    {
                        // A belt needs a second click at its end: move along to the next heading 7 m out.
                        heading++;
                        Next(Step.BeltSecond);
                        return;
                    }
                    heading++;
                    index++;
                    Next(Step.Select);
                    return;
                case Step.BeltSecond:
                    SetInput(false);
                    if (frames < 40) return;
                    if (frames == 40) { SetInput(true); Info($"#{index} belt end pressed: {DevTools.State(b)}"); return; }
                    if (frames == 41 || frames == 45 || frames == 70) Info($"#{index} belt +{frames - 40}f: {DevTools.State(b)} ConveyorPreviewValid={b.ConveyorPreviewValid}");
                    if (frames < 70) return;
                    heading++;
                    index++;
                    Next(Step.Select);
                    return;
            }
        }
    }
}
#endif
