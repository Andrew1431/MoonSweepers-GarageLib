using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BuildQoL.Pure;
using HarmonyLib;
using MoonCleaners;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BuildQoL
{
    // FoundationRun: Shift + mouse wheel while holding a snapped world-square foundation ghost (platform foundation 101 and
    // the other IsWorldSquare kinds) sets a run length N; the vanilla click builds the ghost's cell and the mod builds
    // the next N-1 cells in the direction the camera faces, one per frame, with the game's own planner and build call.
    //
    // Measured on the 2026-11 build (README, "Hazards"); both targets get postfixes only:
    //   MoonDepotBuilder.ReadInput()   0x442AB0, 2208 B, normal prologue (first branch at +0x17). Zeroes wheel on Shift
    //                                   ticks (vanilla would rotate the ghost) and clears place for a refused run.
    //   MoonDepotBuilder.NoteBuilt(int) 0x43FCE0, 288 B, normal prologue (first branch at +0x1B). Queues the run after
    //                                   vanilla built cell 0 (KeepSelection has its own postfix there; both run).
    // Everything else is called, never patched: PlanStructure, BeginStructureCheck / Advance, TryBuildStructure,
    // StructureObstaclesClear, GridCenter / GridFrame / GridCorners (short, take the plan-encoded cell -(id+1), return
    // unit-sphere points), MoonSquareGrid.Find, SetPrompt, Notice (64 B leaf).
    internal sealed class FoundationRunTweak : Tweak
    {
        public override string Name => "FoundationRun";
        protected override string Toggle =>
            "Shift + mouse wheel while holding a platform-foundation ghost sets a run length (1 to RunMax); a click then builds that many " +
            "foundations in a straight line in the direction you face, each charged as usual. A run with a blocked cell, or one you cannot " +
            "afford, is refused. Plain wheel still rotates. False: base-game behaviour, nothing is patched.";

        internal static ConfigEntry<int> RunMax;
        internal static ConfigEntry<string> RunModifier;
        internal static ConfigEntry<bool> RunIgnoresReach;

        protected override void BindMore(ConfigFile cfg)
        {
            RunMax = cfg.Bind("Tweaks", "RunMax", 10, $"FoundationRun: longest run, in pieces (1 to {RunPlan.MaxCap}).");
            RunModifier = cfg.Bind("Tweaks", "RunModifier", "LeftShift",
                "FoundationRun: key held with the mouse wheel to change the run length (a Unity Input System key name; LeftShift or RightShift accept either Shift). " +
                "Shift is the only modifier the base game does not use with the wheel on a foundation ghost.");
            RunIgnoresReach = cfg.Bind("Tweaks", "RunIgnoresReach", true,
                "FoundationRun: build the whole run even where it goes past your build reach (the first piece is always within reach). " +
                "False: the run stops at the first piece out of reach.");
            FoundationRun.ParseModifier(RunModifier.Value);
        }

        public override string[] Needed => new[]
        {
            "MoonCleaners.MoonDepotBuilder:ReadInput", "MoonCleaners.MoonDepotBuilder:NoteBuilt", "MoonCleaners.MoonDepotBuilder:structurePlan",
            "MoonCleaners.MoonDepotBuilder:yaw", "MoonCleaners.MoonDepotBuilder:IsPlacing", "MoonCleaners.MoonDepotBuilder:ConveyorMode",
            "MoonCleaners.MoonDepotBuilder:freeMode", "MoonCleaners.MoonDepotBuilder:selectedBuildId", "MoonCleaners.MoonDepotBuilder:SetPrompt",
            "MoonCleaners.MoonDepotBuilder:Notice", "MoonCleaners.MoonDepotBuilder:CanBuild", "MoonCleaners.MoonDepotBuilder:World",
            "MoonCleaners.MoonDepotBuilder:walker",
            "MoonCleaners.MoonDepotBuilder+BuildInput:wheel", "MoonCleaners.MoonDepotBuilder+BuildInput:place",
            "MoonCleaners.MoonWorld:PlanStructure", "MoonCleaners.MoonWorld:BeginStructureCheck", "MoonCleaners.MoonWorld:TryBuildStructure",
            "MoonCleaners.MoonWorld:StructureObstaclesClear", "MoonCleaners.MoonWorld:BuildDefinition", "MoonCleaners.MoonWorld:IsWorldSquare",
            "MoonCleaners.MoonWorld:GridCenter", "MoonCleaners.MoonWorld:GridFrame", "MoonCleaners.MoonWorld:GridCorners",
            "MoonCleaners.MoonWorld:Squares", "MoonCleaners.MoonWorld:WalletDollars", "MoonCleaners.MoonWorld:LocalPlayer",
            "MoonCleaners.MoonSquareGrid:Find",
            "MoonCleaners.MoonDepotBuildDefinition:reach", "MoonCleaners.MoonDepotBuildDefinition:priceDollars", "MoonCleaners.MoonDepotBuildDefinition:module",
            "MoonCleaners.MoonWorld+DepotPlacementCheck:Advance", "MoonCleaners.MoonWorld+DepotPlacementCheck:Done",
            "MoonCleaners.MoonWorld+DepotPlacementCheck:Valid", "MoonCleaners.MoonWorld+DepotPlacementCheck:Reason",
            "MoonCleaners.MoonWorld+StructurePlan:cell", "MoonCleaners.MoonWorld+StructurePlan:position",
            "MoonCleaners.MoonWorld+StructurePlan:free", "MoonCleaners.MoonWorld+StructurePlan:snapped",
        };

        public override Type[] Patches => new[] { typeof(FoundationRunPatches) };

        public override void Update() => FoundationRun.Update();
        public override void LateUpdate() => FoundationRun.LateUpdate();

#if DEVBUILD
        public override void DevBind(ConfigFile cfg) => RunProbe.Bind(cfg);
        public override void DevTick(float gameT) => RunProbe.Tick(gameT);
#endif
    }

    [HarmonyPatch]
    internal static class FoundationRunPatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.ReadInput))]
        static void ReadInputPostfix(MoonDepotBuilder __instance, ref MoonDepotBuilder.BuildInput __result)
        {
            try { FoundationRun.OnInput(__instance, ref __result); }
            catch (Exception e) { Plugin.Once("run-input", "FoundationRun: ReadInput postfix threw: " + e); }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.NoteBuilt))]
        static void NoteBuiltPostfix(MoonDepotBuilder __instance, int kind)
        {
            try { FoundationRun.OnBuilt(__instance, kind); }
            catch (Exception e) { Plugin.Once("run-built", "FoundationRun: NoteBuilt postfix threw (no run was queued): " + e); }
        }
    }

    internal static class FoundationRun
    {
        const float StepMetres = 8f;     // MoonSquareGrid cell size; neighbour centres measured 7.96 m apart
        const float OutlineLift = 0.25f;
        const int MaxSlices = 2000;      // site check slices of 5 ms; a 101 check takes 1

        static Key modifierKey = Key.LeftShift;
        static bool modifierIsShift = true;

        // The builder whose ReadInput ran last, and when.
        static MoonDepotBuilder builder;
        static int inputFrame = -100;

        // Run state.
        static int length = 1, lengthKind = -1;
        static float pendingWheel;
        static readonly Dictionary<int, bool> squareKind = new();

        // The current preview: cells 0..N-1 (0 = the vanilla ghost's cell), their centres on the surface, outlines for 1..N-1.
        static readonly List<int> cells = new();
        static readonly List<Vector3> centres = new();
        static readonly List<GameObject> outlines = new();
        static Vector3 lastAxis;
        static bool haveAxis;
        static int previewKind = -1, previewFrame = -100, blocked, outOfReach;
        static bool affordable = true, refuse;
        static Material lineMat;

        // The queued run: cells 1..N-1 of the preview at click time.
        static readonly List<int> qCells = new();
        static readonly List<Vector3> qCentres = new();
        static int qIndex, qKind;
        static float qYaw;
        static RunTally tally;
        static MoonDepotBuilder qBuilder;

#if DEVBUILD
        internal static bool DevShift;          // the probe's stand-in for holding Shift
        internal static bool DevLog;            // per-piece log lines
        internal static string LastNotice;
        internal static int Length => length;
        internal static bool Refusing => refuse;
        internal static int Blocked => blocked;
        internal static int CellCount => cells.Count;
        internal static bool Building => qBuilder != null;
        internal static IReadOnlyList<Vector3> Centres => centres;
        internal static int WheelCancelled, ClicksRefused;
#endif

        internal static void ParseModifier(string name)
        {
            if (!Enum.TryParse(name != null ? name.Trim() : "", true, out Key k) || k == Key.None)
            {
                Plugin.L.LogWarning($"FoundationRun: RunModifier '{name}' is not an Input System key name; using LeftShift.");
                k = Key.LeftShift;
            }
            modifierKey = k;
            modifierIsShift = k == Key.LeftShift || k == Key.RightShift;
        }

        static bool ModifierHeld()
        {
#if DEVBUILD
            if (DevShift) return true;
#endif
            var kb = Keyboard.current;
            if (kb == null) return false;
            return modifierIsShift ? kb.shiftKey.isPressed : kb.get_Item(modifierKey).isPressed;
        }

        static bool IsSquareKind(MoonWorld w, int kind)
        {
            if (squareKind.TryGetValue(kind, out bool v)) return v;
            v = w.IsWorldSquare(kind);
            squareKind[kind] = v;
            return v;
        }

        /// <summary>Placing a world-square kind with the build tool, not in free mode.</summary>
        static bool Capable(MoonDepotBuilder b)
        {
            if (b == null || !b.IsPlacing || b.ConveyorMode || b.freeMode) return false;
            var w = b.World;
            return w != null && IsSquareKind(w, b.selectedBuildId);
        }

        // ReadInput postfix: remember the builder; on a modifier + wheel tick take the tick for the run length and hide it
        // from vanilla (which would rotate the ghost); clear the click for a run the preview refuses.
        internal static void OnInput(MoonDepotBuilder b, ref MoonDepotBuilder.BuildInput input)
        {
            builder = b;
            inputFrame = Time.frameCount;
            if (!Capable(b)) return;
            if (input.wheel != 0f && ModifierHeld())
            {
                pendingWheel += input.wheel;
                input.wheel = 0f;
#if DEVBUILD
                WheelCancelled++;
#endif
            }
            // While a run is being built, or when the preview refuses the run (blocked cell, not enough money), vanilla must not
            // build cell 0 on its own.
            if (input.place && length >= 2 && (qBuilder != null || (refuse && previewFrame >= Time.frameCount - 2)))
            {
                input.place = false;
#if DEVBUILD
                ClicksRefused++;
#endif
            }
        }

        // NoteBuilt postfix: vanilla just built the ghost's cell. Queue cells 1..N-1 of the preview.
        internal static void OnBuilt(MoonDepotBuilder b, int kind)
        {
            if (b == null || length < 2 || qBuilder != null || refuse) return;
            if (kind != previewKind || previewFrame < Time.frameCount - 2 || cells.Count < 2) return;
            var w = b.World;
            var walker = b.walker;
            if (w == null || walker == null) return;
            var def = w.BuildDefinition(kind);
            if (def == null) return;
            // The run starts at the cell vanilla accepted within reach; re-check that and every step before queuing.
            float start = Vector3.Distance(walker.transform.position, centres[0]);
            if (start > def.reach + 3f) { Plugin.Once("run-start-far", $"FoundationRun: run start {start:F1} m from the walker, past reach {def.reach}; run not built"); return; }
            for (int k = 1; k < centres.Count; k++)
                if (!RunPlan.StepOk(Vector3.Distance(centres[k - 1], centres[k]))) { Plugin.Once("run-step-queue", "FoundationRun: run cells are not one cell apart; run not built"); return; }
            qCells.Clear(); qCentres.Clear();
            for (int k = 1; k < cells.Count; k++) { qCells.Add(cells[k]); qCentres.Add(centres[k]); }
            qIndex = 0; qKind = kind; qYaw = b.yaw; qBuilder = b;
            tally = new RunTally(cells.Count);
            tally.AddBuilt(); // cell 0, by vanilla
            HideOutlines();
            if (DevLogOn) Plugin.L.LogInfo($"[run] f{Time.frameCount} queued {qCells.Count} pieces of kind {kind} after vanilla built cell {cells[0]}");
        }

#if DEVBUILD
        static bool DevLogOn => DevLog;
#else
        static bool DevLogOn => false;
#endif

        internal static void Update()
        {
            if (qBuilder != null) BuildNext();
        }

        // One queued piece per frame: TryBuildStructure spawns and saves (13 to 37 ms per piece).
        static void BuildNext()
        {
            var b = qBuilder;
            if (b == null || qIndex >= qCells.Count) { FinishRun(); return; }
            var w = b.World;
            var walker = b.walker;
            if (w == null || walker == null) { tally.Stop(RunPlan.Refused); FinishRun(); return; }
            int cell = qCells[qIndex];
            Vector3 aim = qCentres[qIndex];
            qIndex++;
            if (!b.CanBuild(qKind)) { tally.Stop(RunPlan.NoMoney); FinishRun(); return; }
            var plan = w.PlanStructure(qKind, aim, qYaw, -1, false, 0f, 0, aim.normalized);
            if (plan == null || RunPlan.Decode(plan.cell) != cell) { tally.AddRefused(RunPlan.Blocked); DevPiece(cell, "plan off-cell"); return; }
            var check = w.BeginStructureCheck(plan);
            int slices = 0;
            while (check != null && !check.Done && slices < MaxSlices) { check.Advance(5.0); slices++; }
            if (check == null || !check.Done || !check.Valid)
            {
                string why = check != null ? check.Reason : null;
                tally.AddRefused(RunPlan.Category(why ?? RunPlan.Blocked));
                DevPiece(cell, $"site check refused '{why}'");
                return;
            }
            var def = w.BuildDefinition(qKind);
            if (def == null) { tally.AddRefused(RunPlan.Refused); return; }
            float dist = Vector3.Distance(walker.transform.position, plan.position);
            float orig = def.reach;
            float need = RunPlan.ReachFor(orig, dist);
            if (need != orig && !FoundationRunTweak.RunIgnoresReach.Value)
            {
                tally.Stop(RunPlan.OutOfReach);
                DevPiece(cell, $"out of reach at {dist:F1} m (RunIgnoresReach=false): run stops");
                FinishRun();
                return;
            }
            bool ok;
            string reason;
            float during = orig;
            // The reach is raised only around this one call and always restored; TryBuildStructure is the only reader
            // during it (AimAt, AimPastEdge and the HUD read it from the builder's own Update).
            try
            {
                if (need != orig) def.reach = need;
                during = def.reach;
                ok = w.TryBuildStructure(walker, plan, check, out reason);
            }
            finally { def.reach = orig; }
            if (ok) tally.AddBuilt(); else tally.AddRefused(RunPlan.Category(reason));
            DevPiece(cell, $"dist {dist:F2} m reach {orig} -> {during} -> {def.reach}: TryBuildStructure ok={ok} reason='{reason}'");
        }

        static void DevPiece(int cell, string what)
        {
            if (DevLogOn) Plugin.L.LogInfo($"[run] f{Time.frameCount} piece {qIndex}/{qCells.Count} cell {cell}: {what}");
        }

        static void FinishRun()
        {
            var b = qBuilder;
            qBuilder = null;
            qCells.Clear(); qCentres.Clear();
            if (tally == null) return;
            string text = tally.Notice();
            tally = null;
            Plugin.L.LogInfo("FoundationRun: " + text);
#if DEVBUILD
            LastNotice = text;
#endif
            try { if (b != null) b.Notice(text); }
            catch (Exception e) { Plugin.Once("run-notice", "FoundationRun: Notice threw: " + e); }
        }

        // Preview and prompt. In LateUpdate: ShowPlan (the builder's Update) writes its own prompt every frame.
        internal static void LateUpdate()
        {
            var b = builder;
            bool live = b != null && inputFrame >= Time.frameCount - 1 && Capable(b);
            if (!live) { Disarm(); return; }
            int kind = b.selectedBuildId;
            if (kind != lengthKind) { length = 1; lengthKind = kind; haveAxis = false; }
            if (pendingWheel != 0f) { length = RunPlan.NextLength(length, pendingWheel, FoundationRunTweak.RunMax.Value); pendingWheel = 0f; }
            if (qBuilder != null) { HideOutlines(); return; }
            if (length < 2) { ClearPreview(); return; }
            var plan = b.structurePlan;
            var w = b.World;
            if (plan == null || w == null || plan.free || !plan.snapped || RunPlan.Decode(plan.cell) < 0) { ClearPreview(); return; }
            var def = w.BuildDefinition(kind);
            if (def == null || def.module == null) { ClearPreview(); return; }
            if (!PlanCells(w, def.module, RunPlan.Decode(plan.cell), plan.position.magnitude, Mathf.Min(length, RunPlan.ClampMax(FoundationRunTweak.RunMax.Value))))
            {
                length = 1;
                ClearPreview();
                return;
            }
            Colour(b, w, def, kind);
            previewKind = kind;
            previewFrame = Time.frameCount;
            string text = RunPlan.Prompt(cells.Count, def.priceDollars, blocked, outOfReach, affordable, out bool ok);
            refuse = RunPlan.Refuse(blocked, affordable);
            b.SetPrompt(text, ok, false);
        }

        // Cells of the run from the start cell along the facing axis, stepping in world space so each cell's own frame is
        // used (straight across a cube-face seam). False (logged once) when a step is not one cell long.
        static bool PlanCells(MoonWorld w, MoonFoundationModule mod, int start, float radius, int n)
        {
            cells.Clear(); centres.Clear();
            var cam = Camera.main;
            if (cam == null || radius < 100f) return false;
            var grid = w.Squares;
            if (grid == null) return false;
            Vector3 c = w.GridCenter(mod, RunPlan.Encode(start)).normalized * radius;
            Vector3 up = c.normalized;
            Vector3 facing = Vector3.ProjectOnPlane(cam.transform.forward, up);
            if (facing.sqrMagnitude < 1e-6f) facing = haveAxis ? lastAxis : Vector3.ProjectOnPlane(cam.transform.up, up);
            facing.Normalize();
            var ax = Axes(w, mod, start);
            var dots = new float[4];
            for (int k = 0; k < 4; k++) dots[k] = Vector3.Dot(ax[k], facing);
            int prev = -1;
            if (haveAxis) { float bd = -2f; for (int k = 0; k < 4; k++) { float d = Vector3.Dot(ax[k], lastAxis); if (d > bd) { bd = d; prev = k; } } }
            Vector3 axis = ax[RunPlan.PickAxis(dots, prev)];
            lastAxis = axis; haveAxis = true;
            int cur = start;
            for (int k = 0; k < n; k++)
            {
                cells.Add(cur); centres.Add(c);
                if (k == n - 1) break;
                int next = grid.Find((c + axis * StepMetres).normalized);
                Vector3 nc = w.GridCenter(mod, RunPlan.Encode(next)).normalized * radius;
                float step = Vector3.Distance(c, nc);
                if (!RunPlan.StepOk(step) || cells.Contains(next))
                {
                    Plugin.Once("run-step", $"FoundationRun: step {k} from cell {cur} to {next} is {step:F2} m (expected {RunPlan.StepMin} to {RunPlan.StepMax}); run cancelled");
                    cells.Clear(); centres.Clear();
                    return false;
                }
                // Carry the axis over: the new cell's axis nearest the old one.
                var nax = Axes(w, mod, next);
                Vector3 carried = Vector3.ProjectOnPlane(axis, nc.normalized).normalized;
                int best = 0; float bd = -2f;
                for (int j = 0; j < 4; j++) { float d = Vector3.Dot(nax[j], carried); if (d > bd) { bd = d; best = j; } }
                axis = nax[best];
                cur = next; c = nc;
            }
            return true;
        }

        static Vector3[] Axes(MoonWorld w, MoonFoundationModule mod, int id)
        {
            Quaternion f = w.GridFrame(mod, RunPlan.Encode(id));
            Vector3 fw = f * Vector3.forward, r = f * Vector3.right;
            return new[] { fw, -fw, r, -r };
        }

        static readonly Color Green = new(0.2f, 1f, 0.3f, 1f), Red = new(1f, 0.25f, 0.2f, 1f), Grey = new(0.6f, 0.6f, 0.6f, 1f);

        // Cell 0 is the vanilla ghost (its own colour and validity); cells 1..N-1 get an outline each, coloured from the
        // game's planner and obstacle check (about 0.05 ms a cell).
        static void Colour(MoonDepotBuilder b, MoonWorld w, MoonDepotBuildDefinition def, int kind)
        {
            blocked = 0; outOfReach = 0;
            var mod = def.module;
            var walker = b.walker;
            Vector3 feet = walker != null ? walker.transform.position : centres[0];
            bool ignoreReach = FoundationRunTweak.RunIgnoresReach.Value;
            float radius = centres[0].magnitude;
            while (outlines.Count < cells.Count - 1) outlines.Add(NewOutline());
            for (int k = 0; k < outlines.Count; k++) if (outlines[k] != null) outlines[k].SetActive(k < cells.Count - 1);
            for (int k = 1; k < cells.Count; k++)
            {
                var go = outlines[k - 1];
                if (go == null) { go = NewOutline(); outlines[k - 1] = go; }
                var lr = go.GetComponent<LineRenderer>();
                var corners = w.GridCorners(mod, RunPlan.Encode(cells[k]));
                if (corners != null)
                {
                    lr.positionCount = corners.Length;
                    for (int j = 0; j < corners.Length; j++) lr.SetPosition(j, corners[j].normalized * (radius + OutlineLift));
                }
                Vector3 aim = centres[k];
                var p = w.PlanStructure(kind, aim, b.yaw, -1, false, 0f, 0, aim.normalized);
                bool good = p != null && RunPlan.Decode(p.cell) == cells[k] && w.StructureObstaclesClear(p, out _);
                Color col;
                if (!good) { blocked++; col = Red; }
                else if (!ignoreReach && Vector3.Distance(feet, aim) > def.reach + 2f) { outOfReach++; col = Grey; }
                else col = Green;
                lr.startColor = col; lr.endColor = col;
            }
            affordable = Affordable(b, w, kind, def.priceDollars, cells.Count);
        }

        static bool Affordable(MoonDepotBuilder b, MoonWorld w, int kind, int price, int n)
        {
            if (!b.CanBuild(kind)) return false;
            if (price <= 0) return true;
            try
            {
                var wallet = w.WalletDollars(MoonWorld.LocalPlayer);
                if (RunPlan.TryParseMoney(wallet.ToString(), out decimal dollars)) return dollars >= (decimal)price * n;
            }
            catch (Exception e) { Plugin.Once("run-wallet", "FoundationRun: reading the wallet threw; the run checks money per piece only: " + e.Message); }
            return true; // wallet unreadable: CanBuild per piece still stops the run when money runs out
        }

        static GameObject NewOutline()
        {
            var go = new GameObject("BuildQoL.RunOutline");
            var lr = go.AddComponent<LineRenderer>();
            lr.loop = true;
            lr.useWorldSpace = true;
            lr.startWidth = lr.endWidth = 0.18f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (lineMat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                if (sh != null) lineMat = new Material(sh);
            }
            if (lineMat != null) lr.sharedMaterial = lineMat; // vertex colours: no per-outline material copies
            return go;
        }

        static void HideOutlines()
        {
            foreach (var go in outlines) if (go != null) go.SetActive(false);
        }

        static void ClearPreview()
        {
            cells.Clear(); centres.Clear();
            blocked = 0; outOfReach = 0; refuse = false;
            previewKind = -1;
            HideOutlines();
        }

        // Placement ended or the tool is gone: N back to 1 and the outlines destroyed. A queued run carries on.
        static void Disarm()
        {
            length = 1; lengthKind = -1; pendingWheel = 0f; haveAxis = false;
            ClearPreview();
            if (outlines.Count == 0) return;
            foreach (var go in outlines) if (go != null) UnityEngine.Object.Destroy(go);
            outlines.Clear();
        }
    }
}
