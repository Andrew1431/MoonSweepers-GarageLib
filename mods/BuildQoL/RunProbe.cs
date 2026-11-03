#if DEVBUILD
using System;
using BepInEx.Configuration;
using BuildQoL.Pure;
using MoonCleaners;
using UnityEngine;

namespace BuildQoL
{
    // [Dev] RunProbe: drives FoundationRun through the builder's TestInput, 12 s after a world loads (never slot 1).
    //  1. wheel: a plain wheel tick rotates (control); five "Shift" (DevShift) + wheel ticks raise N to 6 and leave yaw alone.
    //  2. a run of 6 from a ghost 3 cells ahead: click, then the queued pieces (reach raised past 32 m, restored), wallet, notice.
    //  3. a run 2 cells to the right with a canister copy in cell 3: preview red, prompt "1 blocked", click refused.
    //  4. RunIgnoresReach=false, a run 2 cells to the left: stops at the first piece out of reach.
    //  5. control: an unraised TryBuildStructure about 38 m out is refused ("Move closer"), reach back at its value.
    //  Screenshots of the preview and the built runs go to BepInEx/config.
    internal static class RunProbe
    {
        static ConfigEntry<bool> Enabled;
        static string shotDir;

        enum Step { Wait, Select, Wheel, Run1, Watch1, Run2, Watch2, Run3, Watch3, Control, Shot, Done }
        static Step step = Step.Wait;
        static int frames;
        static MoonDepotBuilder b;
        static MoonWorld w;
        static MoonWalker walker;
        static MoonDepotBuildDefinition def;
        static MoonDepotBuilder.TestBuildInput input;
        static Vector3 feet, fwd, right, aimPoint;
        static float yaw0, reach0;
        static int depots0, cancelled0, refused0;
        static decimal money0;
        static GameObject obstacle;
        static Vector3 run1Far;
        const int Kind = 101;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Dev", "RunProbe", false, "Dev build: 12 s after a world loads, drive FoundationRun through TestInput (wheel, a run of 6, a blocked run, a reach-limited run, a reach control) and log each step.");
            shotDir = System.IO.Path.GetDirectoryName(cfg.ConfigFilePath);
        }

        static void Info(string s) => Plugin.L.LogInfo($"[run] f{Time.frameCount} {s}");
        static void Next(Step s) { step = s; frames = 0; }

        static decimal Money()
        {
            try { return (decimal)Il2CppSystem.Decimal.ToDouble(w.WalletDollars(MoonWorld.LocalPlayer)); }
            catch { return -1; }
        }

        static int Depots() => w != null && w.depots != null ? w.depots.Count : -1;

        internal static void Tick(float gameT)
        {
            if (Enabled == null || !Enabled.Value || step == Step.Done) return;
            try
            {
                if (step == Step.Wait)
                {
                    if (gameT < 12f) return;
                    if (DevTools.OnSlot1()) { Info("slot-1 loaded; refusing"); step = Step.Done; return; }
                    Setup();
                    return;
                }
                frames++;
                switch (step)
                {
                    case Step.Select: DoSelect(); break;
                    case Step.Wheel: DoWheel(); break;
                    case Step.Run1: DoArm(Step.Watch1, "run1", null); break;
                    case Step.Watch1: DoWatch(Step.Run2, "run1", 6); break;
                    case Step.Run2: DoArm(Step.Watch2, "run2-blocked", "buildqol-run-preview.png"); break;
                    case Step.Watch2: DoWatch(Step.Run3, "run2-blocked", 0); break;
                    case Step.Run3: DoArm(Step.Watch3, "run3-noreach", null); break;
                    case Step.Watch3: DoWatch(Step.Control, "run3-noreach", -1); break;
                    case Step.Control: DoControl(); break;
                    case Step.Shot: if (frames == 20) { Shot("buildqol-run-built.png"); Overhead("over-buildqol-run-built.png"); } if (frames > 30) Finish(); break;
                }
            }
            catch (Exception e) { Plugin.L.LogError("[run] threw: " + e); Finish(); }
        }

        static void Setup()
        {
            var cam = Camera.main;
            b = cam != null ? MoonDepotBuilder.ForCamera(cam) : null;
            if (b == null) b = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>();
            w = b != null ? b.World : null;
            walker = b != null ? b.walker : null;
            if (w == null || walker == null || cam == null) { Info("no world/walker/camera"); step = Step.Done; return; }
            def = w.BuildDefinition(Kind);
            reach0 = def.reach;
            feet = walker.transform.position;
            Vector3 up = feet.normalized;
            fwd = Vector3.ProjectOnPlane(cam.transform.forward, up).normalized;
            right = Vector3.Cross(up, fwd).normalized;
            FoundationRun.DevLog = true;
            PickHeading(up);
            Info($"start: FoundationRun active={Array.Find(Plugin.All, t => t.Name == "FoundationRun")?.Active} RunMax={FoundationRunTweak.RunMax.Value} RunIgnoresReach={FoundationRunTweak.RunIgnoresReach.Value} def 101 reach={def.reach} price={def.priceDollars} wallet {Money()} depots {Depots()} walker |{feet.magnitude:F2}|");
            aimPoint = feet + fwd * 20f;
            Next(Step.Select);
        }

        // The camera may face the base: take the heading nearest the camera whose three run rows (0 and ±16 m to the side,
        // 16 to 64 m out) are clear, and hand it to FoundationRun as the facing.
        static void PickHeading(Vector3 up)
        {
            Vector3 cam = fwd;
            int bestClear = -1; Vector3 best = fwd;
            foreach (float h in new[] { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f })
            {
                Vector3 d = Quaternion.AngleAxis(h, up) * cam, r = Vector3.Cross(up, d).normalized;
                int clear = 0;
                foreach (float l in new[] { 0f, -16f, -24f })
                    for (float m = 16f; m <= 64f; m += 8f)
                    {
                        Vector3 p = feet + d * m + r * l;
                        var plan = w.PlanStructure(Kind, p, 0f, -1, false, 0f, 0, p.normalized);
                        if (plan != null && w.StructureObstaclesClear(plan, out _)) clear++;
                    }
                Info($"heading {h}: {clear}/21 cells clear");
                if (clear > bestClear) { bestClear = clear; best = d; }
                if (clear == 21) break;
            }
            fwd = best; right = Vector3.Cross(up, fwd).normalized;
            FoundationRun.DevFacing = fwd;
            Info($"run heading {Vector3.SignedAngle(cam, fwd, up):F0} deg from the camera ({bestClear}/21 clear)");
        }

        // Straight down onto the target from 8 m above it: a ray from the camera can hit the base first, depending on how the
        // camera faces after the load.
        static Ray AimAt(Vector3 ground)
        {
            Vector3 up = ground.normalized;
            return new Ray(ground + up * 8f, -up);
        }

        static void SetInput(bool place, float wheel)
        {
            if (input == null) input = new MoonDepotBuilder.TestBuildInput();
            input.aim = AimAt(aimPoint);
            input.place = place; input.fine = false; input.free = false; input.ctrl = false;
            input.wheel = wheel;
            b.TestInput = input;
        }

        static void DoSelect()
        {
            if (frames == 1 && b.IsPlacing) { b.CancelPlacement(); return; }
            if (frames == 3) Info($"BeginPlacementFromShop(101) -> {b.BeginPlacementFromShop(Kind)}; {DevTools.State(b)}");
            if (frames > 3) SetInput(false, 0);
            if (frames > 40) Next(Step.Wheel);
        }

        // frame 10: control tick without Shift; 30..70: five Shift ticks; 90: report.
        static void DoWheel()
        {
            float wheel = 0;
            if (frames == 5) { yaw0 = b.yaw; Info($"wheel control: before yaw={b.yaw:F1} N={FoundationRun.Length} valid={b.PlacementValid}"); }
            if (frames == 10) wheel = 1;
            if (frames == 20)
            {
                Info($"wheel control (no Shift): yaw {yaw0:F1} -> {b.yaw:F1} N={FoundationRun.Length} (expect yaw +90, N 1)");
                b.yaw = yaw0;
                FoundationRun.DevShift = true;
                cancelled0 = FoundationRun.WheelCancelled;
            }
            if (frames >= 30 && frames <= 70 && frames % 10 == 0) wheel = 1;
            SetInput(false, wheel);
            if (frames == 90)
            {
                Info($"Shift+wheel x5: yaw {yaw0:F1} -> {b.yaw:F1} N={FoundationRun.Length} wheel ticks cancelled {FoundationRun.WheelCancelled - cancelled0} (expect yaw unchanged, N 6, 5)");
                Next(Step.Run1);
            }
        }

        static void Arm(string name, Vector3 ground) { aimPoint = ground; Info($"{name}: aim {Vector3.Distance(feet, ground):F1} m out"); }

        // Aim, wait for the preview, optional obstacle/screenshot, then click once.
        static void DoArm(Step watch, string name, string shot)
        {
            if (frames == 1)
            {
                if (name == "run1") Arm(name, feet + fwd * 20f);
                else if (name == "run2-blocked") Arm(name, feet + fwd * 16f - right * 16f);
                else { Arm(name, feet + fwd * 12f - right * 24f); FoundationRunTweak.RunIgnoresReach.Value = false; }
            }
            SetInput(false, 0);
            if (frames == 35 && name == "run2-blocked") Info($"{name} before obstacle: blocked={FoundationRun.Blocked} cells1..='{FoundationRun.DevCells}' valid={b.PlacementValid}");
            if (frames == 40 && name == "run2-blocked") PlaceObstacle(3);
            if (frames == 70)
            {
                var c = FoundationRun.Centres;
                string d = "";
                for (int k = 1; k < c.Count; k++) d += $" {Vector3.Distance(c[k - 1], c[k]):F3}";
                string far = c.Count > 0 ? $"{Vector3.Distance(walker.transform.position, c[c.Count - 1]):F1}" : "-";
                Info($"{name} preview: N={FoundationRun.Length} cells={FoundationRun.CellCount} blocked={FoundationRun.Blocked} cells1..='{FoundationRun.DevCells}' refuse={FoundationRun.Refusing} valid={b.PlacementValid} reason='{b.PlacementReason}' steps[{d} ] last cell {far} m; prompt '{FoundationRun.DevPrompt}'");
                if (name == "run1" && c.Count > 0) run1Far = c[c.Count - 1];
            }
            if (frames == 75 && shot != null) { Shot(shot); Overhead("over-" + shot); }
            if (frames == 80)
            {
                depots0 = Depots(); money0 = Money(); refused0 = FoundationRun.ClicksRefused; FoundationRun.LastNotice = null;
                SetInput(true, 0);
                Info($"{name}: place pressed; depots {depots0} wallet {money0} {DevTools.State(b)}");
                Next(watch);
            }
        }

        static void DoWatch(Step next, string name, int expectBuilt)
        {
            SetInput(false, 0);
            if (frames < 10 || (FoundationRun.Building && frames < 600)) return;
            if (frames < 30) return;
            decimal m = Money();
            Info($"{name} result: depots {depots0} -> {Depots()} (+{Depots() - depots0}{(expectBuilt >= 0 ? ", expect +" + expectBuilt : "")}) wallet {money0} -> {m} (-{money0 - m}, {def.priceDollars} each) clicks refused {FoundationRun.ClicksRefused - refused0} notice='{FoundationRun.LastNotice}' reach now {def.reach} (was {reach0})");
            if (obstacle != null) { UnityEngine.Object.Destroy(obstacle); obstacle = null; }
            if (name == "run3-noreach") FoundationRunTweak.RunIgnoresReach.Value = true;
            Next(next);
        }

        // A copy of a loose dust canister (no script, no rigidbody) standing in run cell k.
        static void PlaceObstacle(int k)
        {
            var c = FoundationRun.Centres;
            if (c.Count <= k) { Info($"obstacle: preview has only {c.Count} cells"); return; }
            Vector3 at = c[k], up = at.normalized;
            var views = UnityEngine.Object.FindObjectsByType<MoonDustCanisterView>(FindObjectsSortMode.None);
            if (views != null && views.Length > 0)
            {
                var holder = new GameObject("BuildQoL.ProbeHolder");
                holder.SetActive(false);
                obstacle = UnityEngine.Object.Instantiate(views[0].gameObject, holder.transform);
                foreach (var v in obstacle.GetComponentsInChildren<MoonDustCanisterView>(true)) UnityEngine.Object.DestroyImmediate(v);
                foreach (var r in obstacle.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(r);
                obstacle.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(holder);
                Info($"obstacle: copy of '{views[0].gameObject.name}' (of {views.Length} canisters)");
            }
            else
            {
                obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Info("obstacle: no canister in the scene; a 1 m cube");
            }
            obstacle.transform.position = at + up * 0.6f;
            obstacle.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
            obstacle.SetActive(true);
            Info($"obstacle placed in run cell {k} at {Vector3.Distance(feet, at):F1} m");
        }

        // A neighbour of the run-1 cell nearest 38 m, built without raising the reach: the game must still refuse it.
        static void DoControl()
        {
            SetInput(false, 0);
            if (frames != 5) { if (frames > 10) Next(Step.Shot); return; }
            Vector3 aim = feet + fwd * 38f + right * 8f;
            var plan = w.PlanStructure(Kind, aim, b.yaw, -1, false, 0f, 0, aim.normalized);
            if (plan == null) { Info("control: no plan"); return; }
            var check = w.BeginStructureCheck(plan);
            int slices = 0; while (check != null && !check.Done && slices < 2000) { check.Advance(5.0); slices++; }
            int before = Depots();
            bool ok = w.TryBuildStructure(walker, plan, check, out string reason);
            Info($"control (reach not raised, reach={def.reach}): dist {Vector3.Distance(walker.transform.position, plan.position):F2} m check valid={check?.Valid}: TryBuildStructure ok={ok} reason='{reason}' depots {before}->{Depots()}");
        }

        static void Finish()
        {
            try { if (obstacle != null) UnityEngine.Object.Destroy(obstacle); } catch { }
            try { FoundationRunTweak.RunIgnoresReach.Value = true; } catch { }
            FoundationRun.DevShift = false;
            FoundationRun.DevFacing = Vector3.zero;
            try { if (b != null) { b.TestInput = null; if (b.IsPlacing) b.CancelPlacement(); } } catch { }
            try { Info($"done: reach {(def != null ? def.reach : -1)} (orig {reach0}); wallet {Money()} depots {Depots()}; {DevTools.State(b)}"); } catch { }
            step = Step.Done;
        }

        // A temporary camera 40 m above the run area looking down, rendered to a texture (the player camera may face the base).
        static void Overhead(string name)
        {
            GameObject go = null; RenderTexture rt = null; Texture2D tex = null;
            try
            {
                Vector3 mid = feet + fwd * 36f - right * 10f, up = mid.normalized;
                go = new GameObject("BuildQoL.ProbeCam");
                var cam = go.AddComponent<Camera>();
                cam.CopyFrom(Camera.main);
                go.transform.position = mid + up * 45f - fwd * 15f;
                go.transform.rotation = Quaternion.LookRotation((mid - go.transform.position).normalized, fwd);
                cam.fieldOfView = 70f;
                rt = new RenderTexture(1920, 1080, 24);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                string path = System.IO.Path.Combine(shotDir, name);
                System.IO.File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
                Info("overhead screenshot " + path);
            }
            catch (Exception e) { Info("overhead screenshot failed: " + e.Message); }
            finally
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                if (rt != null) UnityEngine.Object.Destroy(rt);
                if (go != null) UnityEngine.Object.Destroy(go);
            }
        }

        static void Shot(string name)
        {
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                var png = ImageConversion.EncodeToPNG(tex);
                string path = System.IO.Path.Combine(shotDir, name);
                System.IO.File.WriteAllBytes(path, png);
                UnityEngine.Object.Destroy(tex);
                Info("screenshot " + path);
            }
            catch (Exception e) { Info("screenshot failed: " + e.Message); }
        }
    }
}
#endif
