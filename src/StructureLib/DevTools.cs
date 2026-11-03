#if DEVBUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using BepInEx.Logging;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    // Dev-build automation for testing against a cloned save. Loads slot-1 (the real save) only with [Dev] AllowSlot1.
    internal static partial class DevTools
    {
        internal static ConfigEntry<int> AutoLoadSlot;
        internal static ConfigEntry<bool> AllowSlot1, RunSelfTest, ScreenshotMenu, AutoRemove, AutoSave, ForcePower, AutoBuildViaPlan;
        internal static ConfigEntry<string> AutoBuild, AutoFeed;
        static float menuT, gameT, saveAt = -1f, feedAt = -1f, beltAt = -1f;
        static bool loadRequested, selfTestRan, menuShot, autoBuilt;

        /// <summary>What AutoBuild did per entry (id or kind number as written in the config): ok, depot id, reason.</summary>
        internal static readonly Dictionary<string, (bool ok, int depot, string reason)> BuildResults = new(StringComparer.Ordinal);

        /// <summary>True once AutoSave asked the world to save; SaveSeqAtRequest is the sidecar's write count then.</summary>
        internal static bool SaveRequested;
        internal static int SaveSeqAtRequest;

        /// <summary>True when [Dev] AutoBuild names the given structure id.</summary>
        internal static bool AutoBuildNames(string id) =>
            (AutoBuild?.Value ?? "").Split(',').Any(s => s.Trim() == id);

        internal static void Bind(ConfigFile cfg)
        {
            AutoLoadSlot = cfg.Bind("Dev", "AutoLoadSlot", -1, "Dev build: load the existing save in folder slot-<n> from the main menu. -1 = off. Slot 1 is the real save and is refused unless AllowSlot1 is true.");
            AllowSlot1 = cfg.Bind("Dev", "AllowSlot1", false, "Dev build: let AutoLoadSlot and SelfTest touch slot-1 (the real save). Leave false.");
            RunSelfTest = cfg.Bind("Dev", "SelfTest", false, "Dev build: run the in-game self-test 20 s after a world loads and log [selftest] lines.");
            AutoBuild = cfg.Bind("Dev", "AutoBuild", "", "Dev build: comma-separated structure ids or vanilla kind numbers to build once, 10 s after a world loads, on a ring 10 m from the walker (headings 0, 90, 180, 270, 45, 135, 225, 315 deg from its facing, in entry order; a refused site tries the next free heading). An entry whose kind already has a depot reuses it (a repeated entry gets its own). '<entry>@<yaw>' builds at that yaw in degrees (TryBuildDepot's yaw; default 0).");
            AutoFeed = cfg.Bind("Dev", "AutoFeed", "", "Dev build: ';'-separated '<structureId|vanillaKind>:<cargoId|Dust|Sand|Stone>:<kg>' entries, each loaded through the game's CrewProcessorLoad, 5 s after the AutoBuild step, into the first built structure of that id, or for a vanilla kind number into the depot AutoBuild built for that entry.");
            AutoRemove = cfg.Bind("Dev", "AutoRemove", true, "Dev build: the self-test removes the example lamp it checked. false leaves it in place (the sidecar carries it into the next launch).");
            AutoSave = cfg.Bind("Dev", "AutoSave", false, "Dev build: request a save 10 s after the AutoBuild step (whether or not AutoBuild names anything). The self-test waits for that save and checks the structurelib.json sidecar.");
            ForcePower = cfg.Bind("Dev", "ForcePower", false, "Dev build: run every modded processor at full power (Power = 1) whatever its grid supplies. For isolating power problems; no self-test relies on it.");
            AutoBuildViaPlan = cfg.Bind("Dev", "AutoBuildViaPlan", false, "Dev build: AutoBuild places modded structures through the build menu's own path (PlanStructure, BeginStructureCheck, TryBuildStructure), which records a free pose (placedPose), instead of TryBuildDepot. Vanilla kinds still use TryBuildDepot.");
            BindBelts(cfg);
            BindModels(cfg);
            BindPowerUx(cfg);
            // Box spike (belt-boxes plan, Task 1; DevBelts.cs BoxSpike).
            RegisterBoxProbe = cfg.Bind("Dev", "RegisterBoxProbe", false, "Dev build: the example plugin also registers ryanhartwig.example-box-probe, a router (splitter: In on -X; Forward, Left, Right outputs) cloned from the floodlight (kind 4) with a 1.4 x 1.4 m Footprint. 15 s after load the donor's and the probe's definitions, prefab components and colliders (and the probe view's) are logged, and three dust canisters are put on the AutoBelt belt into the probe and followed for 10 s (CargoRouted count, riders on the in and out belts).");
            SelfTest.Subscribe();
            ScreenshotMenu = cfg.Bind("Dev", "ScreenshotMenu", false, "Dev build: 5 s after the self-test, open the build menu on the example lamp and save a screenshot to BepInEx/cache/structurelib/menu.png.");
            CoreLog.Info($"[dev] AutoLoadSlot={AutoLoadSlot.Value} AllowSlot1={AllowSlot1.Value} SelfTest={RunSelfTest.Value} ScreenshotMenu={ScreenshotMenu.Value} AutoRemove={AutoRemove.Value} AutoSave={AutoSave.Value} ForcePower={ForcePower.Value} AutoBuildViaPlan={AutoBuildViaPlan.Value} AutoBuild='{AutoBuild.Value}' AutoFeed='{AutoFeed.Value}' AutoBelt='{AutoBelt.Value}' DumpCards={DumpCards.Value} RegisterBadPorts={RegisterBadPorts.Value} SunOverhead={SunOverhead.Value} RegisterBoxProbe={RegisterBoxProbe.Value}");
            // MoonStructures.Register reads this static at plugin Load time; prove the interop read works that early.
            try { CoreLog.Info($"[dev] MoonProductionSave.MaxSlots at Load = {MoonProductionSave.MaxSlots}"); }
            catch (Exception e) { CoreLog.Error("[dev] reading MoonProductionSave.MaxSlots at Load threw: " + e); }
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
            if (!autoBuilt && gameT > 10f)
            {
                autoBuilt = true;
                string refusal = SlotRefusal();
                // The self-test's exception watch starts here, before any build or save of this run, so a save the
                // game refuses (it logs "Moon save failed") fails the run.
                if (RunSelfTest.Value && refusal == null) SelfTest.StartWatch();
                if (!string.IsNullOrWhiteSpace(AutoBuild.Value))
                {
                    if (refusal != null) CoreLog.Warn("[dev] AutoBuild skipped: " + refusal);
                    else AutoBuilder.Run(AutoBuild.Value);
                }
                if (!string.IsNullOrWhiteSpace(AutoBelt.Value))
                {
                    if (refusal != null) CoreLog.Warn("[dev] AutoBelt skipped: " + refusal);
                    else beltAt = gameT + 3f;
                }
                if (!string.IsNullOrWhiteSpace(AutoFeed.Value))
                {
                    if (refusal != null) CoreLog.Warn("[dev] AutoFeed skipped: " + refusal);
                    else feedAt = gameT + 5f;
                }
                if (AutoSave.Value)
                {
                    if (refusal != null) CoreLog.Warn("[dev] AutoSave skipped: " + refusal);
                    else saveAt = gameT + 10f;
                }
            }
            if (beltAt > 0f && gameT > beltAt) { beltAt = -1f; AutoBelter.Run(AutoBelt.Value); }
            if (feedAt > 0f && gameT > feedAt) { feedAt = -1f; AutoFeeder.Run(AutoFeed.Value); }
            TickBelts(gameT);
            PowerUx.Tick(gameT);
            if (saveAt > 0f && gameT > saveAt) { saveAt = -1f; RequestSave(); }
            if (RunSelfTest.Value && !selfTestRan && gameT > 20f)
            {
                selfTestRan = true;
                string refusal = SlotRefusal();
                if (refusal != null) CoreLog.Warn("[dev] SelfTest skipped: " + refusal);
                else SelfTest.Run();
            }
            SelfTest.Tick();
            if (ScreenshotMenu.Value && !menuShot && SelfTest.FinishedAt > 0f && Time.unscaledTime > SelfTest.FinishedAt + 5f) { menuShot = true; MenuShot.Start(); }
        }

        // The same call GarageLib's AutoSave uses. The save itself runs later (and possibly off the main thread).
        internal static void RequestSave()
        {
            var w = CoreState.World ?? CoreState.FindWorld();
            if (w == null) { CoreLog.Warn("[dev] AutoSave: no MoonWorld"); return; }
            SaveSeqAtRequest = Sidecar.WriteSeq;
            SaveRequested = true;
            CoreLog.Info($"[dev] RequestSave() (sidecar writes so far: {SaveSeqAtRequest})");
            w.RequestSave();
            try { CoreLog.Info($"[dev] after RequestSave: progressDirty={w.progressDirty} nextSave={w.nextSave:F1} t={Time.time:F1} unscaled={Time.unscaledTime:F1} SoonSaveSeconds={MoonWorld.SoonSaveSeconds} ReleaseSaveSeconds={MoonWorld.ReleaseSaveSeconds} SaveRetrySeconds={MoonWorld.SaveRetrySeconds}"); }
            catch (Exception e) { CoreLog.Warn("[dev] reading save timers: " + e.Message); }
        }

        static string ActiveFolder() => (MoonSaveSlots.SlotFolder(MoonSaveSlots.Active) ?? "").Replace('\\', '/').TrimEnd('/');

        // Null when the loaded world may be touched by dev automation.
        static string SlotRefusal()
        {
            if (AllowSlot1.Value) return null;
            string active = ActiveFolder();
            return active.EndsWith("/slot-1") ? "slot-1 (the real save) is loaded and [Dev] AllowSlot1 is false" : null;
        }

        // The menu's own play methods do nothing when called from a plugin; setting MoonSaveSlots.Active then
        // StartShift() does load. The slot is matched by folder name, never by index.
        static void LoadSlot(int slot)
        {
            if (slot == 1 && !AllowSlot1.Value) { CoreLog.Warn("[dev] AutoLoadSlot: refusing slot-1 (the real save); clone it and load the clone, or set AllowSlot1"); return; }
            string suffix = "/slot-" + slot;
            int target = -1;
            for (int i = 0; i <= MoonSaveSlots.Count; i++)
            {
                string f = (MoonSaveSlots.SlotFolder(i) ?? "").Replace('\\', '/').TrimEnd('/');
                if (f.EndsWith(suffix)) { target = i; break; }
            }
            if (target < 0) { CoreLog.Warn($"[dev] AutoLoadSlot: no slot index maps to folder slot-{slot}; staying in menu"); return; }
            MoonSaveSlots.Active = target;
            string chosen = ActiveFolder();
            if (!chosen.EndsWith(suffix)) { CoreLog.Warn($"[dev] AutoLoadSlot: active slot resolved to '{chosen.Substring(chosen.LastIndexOf('/') + 1)}', refusing"); return; }
            CoreLog.Info($"[dev] AutoLoadSlot: loading slot index {target} (folder slot-{slot})");
            MoonSession.StartShift();
        }
    }

    // [Dev] AutoBuild: builds each entry through the game's own TryBuildDepot with a null placement check (fact (e)),
    // on a ring 10 m from the local walker: entry i at Ring[i] degrees from its facing, so any two structures are at most
    // 20 m apart and all sit inside the 24 m build reach. A refused site (slot-2 has vanilla structures 12 m and 22 m
    // ahead of the spawn point) tries the ring headings no entry has taken yet, in ring order. Tops the team wallet up
    // when it is short.
    internal static class AutoBuilder
    {
        const float RingMetres = 10f;
        static readonly float[] Ring = { 0f, 90f, 180f, 270f, 45f, 135f, 225f, 315f };
        static readonly HashSet<float> taken = new();

        internal static void Run(string spec)
        {
            var entries = spec.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var world = CoreState.World ?? CoreState.FindWorld();
            var walkers = UnityEngine.Object.FindObjectsByType<MoonWalker>(FindObjectsSortMode.None);
            var walker = walkers != null && walkers.Length > 0 ? walkers[0] : null;
            if (world == null || walker == null)
            {
                foreach (var e in entries) Report(e, false, -1, world == null ? "no MoonWorld" : "no MoonWalker");
                return;
            }
            SelfTest.ResetEvents(world);
            taken.Clear();
            claimed.Clear();
            if (AutoLine.On) AutoLine.Prepare(world, entries);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                string e = entries[i];
                int n = seen.TryGetValue(e, out int k) ? k + 1 : 1;
                seen[e] = n;
                DevTools.BuildResults.TryGetValue(e, out var first);
                try { Build(world, walker, e, i); }
                catch (Exception ex) { Report(e, false, -1, "threw " + ex.GetType().Name + ": " + ex.Message); }
                // An entry named more than once gets its own depot each time, under '<entry>#<n>' ([Dev] AutoBelt's '#n'
                // endpoint); the plain key keeps the first.
                if (DevTools.BuildResults.TryGetValue(e, out var r))
                {
                    DevTools.BuildResults[$"{e}#{n}"] = r;
                    if (r.ok && r.depot >= 0) claimed.Add(r.depot);
                    if (n > 1) DevTools.BuildResults[e] = first;
                }
            }
        }

        // Depots AutoBuild built or reused in this run: a repeated entry does not reuse one of them.
        static readonly HashSet<int> claimed = new();

        static int UnclaimedDepotOfKind(MoonWorld world, int kind)
        {
            if (world?.depots == null) return -1;
            foreach (var kv in world.depots)
                if (kv.Value != null && kv.Value.kind == kind && !claimed.Contains(kv.Key)) return kv.Key;
            return -1;
        }

        /// <summary>'&lt;id or kind&gt;@&lt;yaw&gt;' -> (id or kind, yaw in degrees); no '@' -> yaw 0.</summary>
        internal static (string who, float yaw) SplitYaw(string entry)
        {
            int at = entry.LastIndexOf('@');
            if (at > 0 && float.TryParse(entry.Substring(at + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float yaw))
                return (entry.Substring(0, at).Trim(), yaw);
            return (entry, 0f);
        }

        static void Build(MoonWorld world, MoonWalker walker, string entry, int index)
        {
            var (who, yaw) = SplitYaw(entry);
            int kind;
            if (!int.TryParse(who, out kind))
            {
                var d = MoonStructures.Definition(who);
                if (d == null || !MoonStructures.IsModded(d.Kind)) { Report(entry, false, -1, "no registered structure with this id"); return; }
                kind = d.Kind;
            }
            // Only a modded kind is reused (one built earlier in this run, or restored from the save once saves carry modded
            // kinds); a vanilla kind is always built anew, since slot-2 already holds vanilla structures.
            int existing = MoonStructures.IsModded(kind) ? UnclaimedDepotOfKind(world, kind) : -1;
            if (existing >= 0)
            {
                CoreLog.Info($"[dev] AutoBuild: '{entry}' (modded kind {kind}) already has depot {existing} in this world; reusing it");
                Report(entry, true, existing, "reused existing depot");
                return;
            }
            var def = world.BuildDefinition(kind);
            if (def == null) { Report(entry, false, -1, $"no build definition for kind {kind}"); return; }
            EnsureMoney(world, def.priceDollars);

            var wt = walker.transform;
            var up = wt.position.normalized;
            float dist = RingMetres;
            bool ok = false;
            string reason = "";
            int id = -1;
            bool viaPlan = DevTools.AutoBuildViaPlan.Value && MoonStructures.IsModded(kind);
            float own = Ring[index % Ring.Length];
            var headings = new List<float>();
            if (!taken.Contains(own)) headings.Add(own);
            headings.AddRange(Ring.Where(h => h != own && !taken.Contains(h)));
            // [Dev] AutoBuildLayout = line (DevBelts.cs): one site per entry on a line, no fallback heading.
            var sites = AutoLine.On
                ? new List<(float h, Vector3 at, string where)> { AutoLine.Site(wt, index) }
                : headings.Select(h => (h, wt.position + Quaternion.AngleAxis(h, up) * wt.forward * dist, $"at {dist:F0} m, heading {h:F0} deg from facing")).ToList();
            foreach (var (h, at, where) in sites)
            {
                var before = DepotIds(world);
                ok = viaPlan ? BuildViaPlan(world, walker, kind, at, wt.position.magnitude, out reason) : world.TryBuildDepot(walker, at.normalized, yaw, out reason, null, kind);
                if (ok) foreach (var kv in world.depots) if (!before.Contains(kv.Key)) { id = kv.Key; break; }
                CoreLog.Info($"[dev] AutoBuild: '{entry}' (kind {kind}) {(viaPlan ? "via the build-menu plan path " : "")}{where}{(yaw != 0f ? $", yaw {yaw:F0}" : "")}: ok={ok} reason='{reason}'");
                if (ok) { taken.Add(h); break; }
            }
            if (ok && id >= 0)
            {
                var rec = world.depots.get_Item(id);
                CoreLog.Info($"[dev] AutoBuild: depot {id} placedPose={rec.placedPose} q=({rec.qx:F3},{rec.qy:F3},{rec.qz:F3},{rec.qw:F3}) yaw={rec.yaw:F1} support={rec.support}");
            }
            if (ok && id >= 0 && MoonStructures.IsModded(kind)) SeedContribution(world, id, def.priceDollars);
            Report(entry, ok, id, reason);
        }

        // The build menu's own placement path for a structure: PlanStructure (free placement at a ground point), the sliced
        // site check run to completion, then TryBuildStructure, which records the plan's rotation as a free pose
        // (placedPose). Only reference-type outs here (no out structs; see Interop hazards).
        static bool BuildViaPlan(MoonWorld world, MoonWalker walker, int kind, Vector3 at, float radius, out string reason)
        {
            var normal = at.normalized;
            var plan = world.PlanStructure(kind, normal * radius, 0f, -1, true, 0f, 0, normal);
            if (plan == null) { reason = "PlanStructure returned null"; return false; }
            CoreLog.Info($"[dev] AutoBuildViaPlan: plan kind {plan.kind} at {plan.position} rotation {plan.rotation} support {plan.support} free {plan.free} snapped {plan.snapped} hint '{plan.hint}'");
            var check = world.BeginStructureCheck(plan);
            int slices = 0;
            while (check != null && !check.Done && slices < 5000) { check.Advance(5.0); slices++; }
            CoreLog.Info($"[dev] AutoBuildViaPlan: site check {(check == null ? "null" : $"done={check.Done} valid={check.Valid} reason='{check.Reason}'")} after {slices} slices");
            if (check != null && check.Done && !check.Valid) { reason = "site check: " + check.Reason; return false; }
            return world.TryBuildStructure(walker, plan, check, out reason);
        }

        // The lamp's own build leaves contributions empty (single player). One entry for the local player at the price
        // makes the sidecar's contribution path (Decimal conversion both ways) run on every save and load.
        static void SeedContribution(MoonWorld world, int id, int price)
        {
            try
            {
                var rec = world.depots != null && world.depots.ContainsKey(id) ? world.depots.get_Item(id) : null;
                if (rec == null) return;
                rec.contributions ??= new Il2CppSystem.Collections.Generic.List<MoonWalletSnapshot>();
                if (rec.contributions.Count > 0) return;
                var w = new MoonWalletSnapshot();
                w.player = MoonWorld.LocalPlayer;
                w.dollars = new Il2CppSystem.Decimal((double)price);
                w.pendingKilograms = new Il2CppSystem.Decimal(0.0);
                rec.contributions.Add(w);
                CoreLog.Info($"[dev] AutoBuild: depot {id} had no contributions; added {CoreLog.Redact(w.player)} at ${price} (now {rec.contributions.Count})");
            }
            catch (Exception e) { CoreLog.Warn($"[dev] AutoBuild: seeding a contribution on depot {id} failed: {e.Message}"); }
        }

        // DevAddMoney is a no-op in this build; the wallet is team-wide, so it is set directly.
        static void EnsureMoney(MoonWorld world, int price)
        {
            string player = MoonWorld.LocalPlayer;
            decimal have = SelfTest.DecimalOf(world.WalletDollars(player));
            if (have >= price) return;
            world.SetTeamDollars(new Il2CppSystem.Decimal((double)(price + 100)));
            decimal now = SelfTest.DecimalOf(world.WalletDollars(player));
            CoreLog.Info($"[dev] AutoBuild: wallet ${have} was short of ${price}; now ${now}");
        }

        static HashSet<int> DepotIds(MoonWorld world)
        {
            var s = new HashSet<int>();
            if (world.depots != null) foreach (var kv in world.depots) s.Add(kv.Key);
            return s;
        }

        static void Report(string entry, bool ok, int depot, string reason)
        {
            DevTools.BuildResults[entry] = (ok, depot, reason);
            CoreLog.Info($"[dev] built {entry} depot={depot} ok={ok} reason='{reason}'");
        }
    }

    // [Dev] AutoFeed: loads cargo into the first built depot of a modded structure through the game's own
    // CrewProcessorLoad, as the local player, and notes slot 0 before and after (in the same frame, before a cycle can
    // take from it). Cargo is Dust, Sand, Stone or a registered cargo id.
    internal static class AutoFeeder
    {
        internal sealed class Result
        {
            internal string StructureId, Error;
            internal int Depot = -1;
            internal double Kg, Before, After;
            internal float At;
        }

        /// <summary>What the last AutoFeed step did, one result per ';'-separated entry.</summary>
        internal static readonly List<Result> Results = new();

        /// <summary>The last result for a structure id; null when no entry named it.</summary>
        internal static Result For(string structureId) => Results.LastOrDefault(r => r.StructureId == structureId);

        internal static void Run(string spec)
        {
            Results.Clear();
            foreach (var entry in spec.Split(';').Select(e => e.Trim()).Where(e => e.Length > 0))
            {
                var r = new Result { At = Time.unscaledTime, StructureId = entry.Split(':')[0].Trim() };
                Results.Add(r);
                try { r.Error = Feed(entry, r); }
                catch (Exception e) { r.Error = "threw " + e.GetType().Name + ": " + e.Message; }
                CoreLog.Info($"[dev] AutoFeed '{entry}': " + (r.Error ?? $"depot {r.Depot} slot 0 held {r.Before} kg, now {r.After} kg"));
            }
        }

        static string Feed(string spec, Result r)
        {
            var parts = spec.Split(':');
            if (parts.Length != 3) return "expected '<structureId|vanillaKind>:<cargo>:<kg>'";
            r.StructureId = parts[0].Trim();
            // A vanilla kind number feeds the depot AutoBuild built for that entry (slot-2 holds other vanilla depots).
            bool vanilla = int.TryParse(r.StructureId, out int vanillaKind);
            var d = vanilla ? null : MoonStructures.Definition(r.StructureId);
            if (!vanilla && (d == null || !MoonStructures.IsModded(d.Kind))) return $"no registered structure '{r.StructureId}'";
            string cargoText = parts[1].Trim();
            MoonCargo cargo;
            var modded = MoonStructures.CargoDefinition(cargoText);
            if (modded != null) cargo = (MoonCargo)modded.Value;
            else if (!Enum.TryParse(cargoText, true, out cargo) || (byte)cargo > (byte)MoonCargo.Stone) return $"unknown cargo '{cargoText}'";
            if (!double.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double kg) || kg <= 0)
                return $"bad kilograms '{parts[2]}'";
            r.Kg = kg;
            var world = CoreState.World ?? CoreState.FindWorld();
            if (world == null) return "no MoonWorld";
            if (vanilla)
            {
                if (!DevTools.BuildResults.TryGetValue(r.StructureId, out var built) || !built.ok || built.depot < 0) return $"AutoBuild built no '{r.StructureId}' this run";
                r.Depot = built.depot;
                if (world.depots == null || !world.depots.ContainsKey(r.Depot) || world.depots.get_Item(r.Depot)?.kind != vanillaKind) return $"depot {r.Depot} is not of kind {vanillaKind}";
            }
            else r.Depot = SelfTest.DepotOfKind(world, d.Kind);
            if (r.Depot < 0) return $"no built '{r.StructureId}' in this world";
            r.Before = SelfTest.Stored(world.ProductionOf(r.Depot), 0);
            world.CrewProcessorLoad(MoonWorld.LocalPlayer, r.Depot, cargo, kg);
            r.After = SelfTest.Stored(world.ProductionOf(r.Depot), 0);
            return null;
        }
    }

    // Evidence for the build-menu check without a screen: open the first builder's menu on the example lamp's tab,
    // select the lamp, and save a screenshot to BepInEx/cache/structurelib/menu.png.
    internal static class MenuShot
    {
        internal static void Start()
        {
            if (CoreDriver.Instance == null) return;
            CoreDriver.Instance.StartCoroutine(BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(Run()));
        }

        static System.Collections.IEnumerator Run()
        {
            var lamp = MoonStructures.Definition("ryanhartwig.example-lamp");
            var builder = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>();
            if (lamp == null || builder == null) { CoreLog.Warn($"[dev] menu shot: lamp {(lamp != null)}, builder {(builder != null)}"); yield break; }
            Step("SetOpen(true)", () => builder.SetOpen(true));
            CoreLog.Info($"[dev] menu shot: IsOpen={builder.IsOpen} after SetOpen(true)");
            if (!builder.IsOpen)
            {
                Step("ToggleBuildMode()", () => builder.ToggleBuildMode());
                CoreLog.Info($"[dev] menu shot: IsOpen={builder.IsOpen} after ToggleBuildMode()");
            }
            if (!builder.IsOpen)
            {
                // Both refuse unless the build tool is in hand; show the menu's own UI objects instead.
                Step("EnsureBuildUI()", () => builder.EnsureBuildUI());
                Step("ui/menu SetActive(true)", () => { builder.ui?.SetActive(true); builder.menu?.SetActive(true); });
                Step("RefreshMenu()", () => builder.RefreshMenu());
                CoreLog.Info($"[dev] menu shot: menu active={builder.menu != null && builder.menu.activeInHierarchy}, IsOpen={builder.IsOpen}");
            }
            yield return null;
            int tab = Array.IndexOf(MoonDepotBuilder.catalogCategories?.ToArray() ?? Array.Empty<string>(), lamp.Category);
            if (tab >= 0) Step($"FilterCatalog({tab})", () => builder.FilterCatalog(tab));
            Step($"SelectStructure({lamp.Kind})", () => builder.SelectStructure(lamp.Kind));
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                string dir = Path.Combine(BepInEx.Paths.CachePath, "structurelib");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "menu.png");
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
                UnityEngine.Object.Destroy(tex);
                CoreLog.Info($"[dev] menu shot saved to {path} (selected {builder.selectedStructure}, category {builder.selectedCategory}, open {builder.IsOpen})");
            }
            catch (Exception e) { CoreLog.Warn("[dev] menu shot: capture failed: " + e.Message); }
        }

        static void Step(string what, Action a)
        {
            try { a(); CoreLog.Info("[dev] menu shot: " + what); }
            catch (Exception e) { CoreLog.Warn($"[dev] menu shot: {what} threw {e.GetType().Name}: {e.Message}"); }
        }
    }

    // In-game assertions. Line formats are fixed; tooling greps them.
    internal static partial class SelfTest
    {
        static int passed, failed;
        static string logPath;
        static long logStart = -1;
        static readonly Regex Bad = new Regex(@"Exception|Error  :", RegexOptions.Compiled);

        internal static void Run()
        {
            Begin();
            Assert("registry-frozen", MoonStructures.Frozen, "MoonStructures.Frozen is false after the world loaded");
            ContributionsRoundTrip();
            var lamp = MoonStructures.Definition("ryanhartwig.example-lamp");
            orphanMode = lamp == null && Sidecar.Orphans.Contains(LampId);
            if (orphanMode)
            {
                // ExampleStructure is not installed, and the save's sidecar holds its lamp: only the orphan checks apply.
                CoreLog.Info("[selftest] the example lamp is not registered and the sidecar keeps it as an orphan: orphan checks only");
                WaitForSave();
                return;
            }
            CargoChecks();
            CanisterRoundTrip();
            ProcessorChecks(lamp);
            PowerChecks();
            SandPowerChecks();
            PortChecks();
            BeltChecks();
            OutChecks();
            BoxChecks();
            ModelChecks();
            int expected = KindAllocator.KindOf("ryanhartwig.example-lamp");
            Assert("example-kind-hashed", lamp != null && lamp.Kind == expected,
                lamp == null ? "'ryanhartwig.example-lamp' is not registered (is ExampleStructure installed?)" : $"kind {lamp.Kind}, expected {expected}");
            HandshakeCheck(expected);
            DefinitionChecks(lamp);
            // Finishes the run (Done) itself, possibly a few frames later.
            CatalogChecks(lamp);
        }

        // The build menu lists the lamp under Services, in the static catalog and in the menu's own copy of it. Its card
        // art is polled for up to 5 s (the renderer needs a few frames); then the rendered picture itself is awaited for
        // up to 10 s and must be what CardArt answers (fallback art does not count). Then the run finishes.
        const string LampImage = "structurelib:ryanhartwig.example-lamp";
        static MoonDepotBuilder artBuilder;
        static StructureDefinition artLamp;
        static float artUntil = -1f, renderUntil = -1f;

        /// <summary>Unscaled time at which the last run printed its done line; -1 until then.</summary>
        internal static float FinishedAt = -1f;

        static void CatalogChecks(StructureDefinition lamp)
        {
            var builder = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>();
            string noLamp = lamp == null ? "example lamp not registered" : null;
            string noBuilder = builder == null ? "no MoonDepotBuilder found" : noLamp;
            Check("catalog-has-modded", noLamp, () =>
            {
                var entries = MoonDepotBuilder.baseStructures;
                if (entries == null) return (false, "baseStructures is null");
                bool ok = entries.Any(e => e != null && e.kind == lamp.Kind && e.category == "Services");
                return (ok, $"no Services entry for kind {lamp.Kind} among {entries.Length} baseStructures");
            });
            Check("catalog-instance-has-modded", noBuilder, () =>
            {
                builder.RefreshCatalog();
                var entries = builder.structureEntries;
                if (entries == null) return (false, "structureEntries is null after RefreshCatalog");
                return (entries.Any(e => e != null && e.kind == lamp.Kind), $"no entry for kind {lamp.Kind} among {entries.Length} structureEntries");
            });
            if (noBuilder != null) { Assert("cardart-modded", false, noBuilder); Assert("cardart-rendered", false, noBuilder); Finish(); return; }
            artBuilder = builder;
            artLamp = lamp;
            artUntil = Time.unscaledTime + 5f;
            renderUntil = Time.unscaledTime + 10f;
        }

        // Every frame in game: answers the pending card art checks, then finishes the run.
        internal static void Tick()
        {
            if (cycleUntil >= 0f) TickCycle();
            if (PowerPending) TickPower();
            if (sandOpen.Count > 0) TickSandPower();
            if (BeltPending) TickBeltChecks();
            if (OutPending) TickOutChecks();
            if (ModelPending) TickModels();
            if (SandChainPending) TickSandChain();
            if (BoxPending) TickBoxes();
            if (artUntil >= 0f) { TickCardArt(); return; }
            if (renderUntil >= 0f) { TickRendered(); return; }
            if (saveUntil >= 0f && cycleUntil < 0f && !PowerPending && sandOpen.Count == 0 && !BeltPending && !OutPending && !ModelPending && !SandChainPending && !BoxPending) TickSaveWait();
        }

        // cardart-modded: CardArt answers something once nothing is pending, or after 5 s.
        static void TickCardArt()
        {
            Sprite art = null;
            string error = null;
            bool timedOut = Time.unscaledTime >= artUntil;
            try
            {
                if (!(artBuilder != null && artBuilder.Pointer != IntPtr.Zero && artBuilder)) error = "the MoonDepotBuilder went away";
                else if (ArtSettled(artLamp) || timedOut) art = artBuilder.CardArt(LampImage);
            }
            catch (Exception e) { error = "threw " + e.GetType().Name + ": " + e.Message; }
            if (art == null && error == null && !timedOut) return;
            artUntil = -1f;
            Assert("cardart-modded", art != null, error ?? $"CardArt(\"{LampImage}\") returned null after 5 s");
            if (art != null)
            {
                var rendered = IconRenderer.Get(artLamp);
                string source = rendered != null && rendered.Pointer == art.Pointer ? "the rendered picture" : "fallback art";
                CoreLog.Info($"[selftest] card art for '{artLamp.Id}': sprite '{art.name}' {art.rect.width}x{art.rect.height} ({source})");
            }
        }

        // cardart-rendered: once IconRenderer has a sprite (up to 10 s), CardArt must answer that very sprite.
        static void TickRendered()
        {
            Sprite rendered = null;
            string error = null;
            bool timedOut = Time.unscaledTime >= renderUntil;
            if (!(artBuilder != null && artBuilder.Pointer != IntPtr.Zero && artBuilder)) error = "the MoonDepotBuilder went away";
            else rendered = IconRenderer.Get(artLamp);
            if (rendered == null && error == null && !timedOut) return;
            renderUntil = -1f;
            if (error == null && rendered == null) error = "IconRenderer.Get returned null after 10 s (render failed or never queued)";
            Sprite art = null;
            if (error == null)
            {
                try { art = artBuilder.CardArt(LampImage); }
                catch (Exception e) { error = "CardArt threw " + e.GetType().Name + ": " + e.Message; }
            }
            bool ok = error == null && art != null && art.Pointer == rendered.Pointer;
            Assert("cardart-rendered", ok, error ?? $"CardArt(\"{LampImage}\") returned '{art?.name ?? "null"}', not the rendered sprite '{rendered.name}'");
            artBuilder = null;
            artLamp = null;
            Finish();
        }

        // AutoBuild placed the example lamp: the [dev] built line said ok, the world holds a depot of its kind, its view
        // was decorated and renamed (and Built was raised), and removing it refunds part or all of its price, drops the
        // record and raises Removed.
        const string LampId = "ryanhartwig.example-lamp";

        static void BuildChecks(StructureDefinition lamp)
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            string noLamp = lamp == null ? "example lamp not registered" : null;
            string noWorld = world == null ? "no MoonWorld found" : noLamp;
            bool autoBuilt = DevTools.AutoBuildNames(LampId);
            if (!autoBuilt && lamp != null && world != null && DepotOfKind(world, lamp.Kind) < 0)
            {
                CoreLog.Info($"[selftest] '{LampId}' was neither built (AutoBuild) nor restored this run: build checks not applicable");
                return;
            }
            if (autoBuilt) Check("build-modded-ok", noLamp, () =>
            {
                if (!DevTools.BuildResults.TryGetValue(LampId, out var r))
                    return (false, $"no [dev] built line for '{LampId}' (AutoBuild='{DevTools.AutoBuild.Value}')");
                return (r.ok, $"[dev] built {LampId} depot={r.depot} ok={r.ok} reason='{r.reason}'");
            });
            int depot = -1;
            Check("build-modded-depot-kind", noWorld, () =>
            {
                depot = DepotOfKind(world, lamp.Kind);
                return (depot >= 0, $"world.depots ({world.depots?.Count}) has no record of kind {lamp.Kind}");
            });
            Check("build-modded-view-decorated", noLamp, () =>
            {
                string name = "ModStructure_" + lamp.Id;
                var view = GameObject.Find(name);
                if (view == null) return (false, $"no GameObject named '{name}'");
                if (view.transform.Find("ExampleLampTag") == null) return (false, $"'{name}' has no child 'ExampleLampTag'");
                if (depot >= 0 && !builtEvents.Contains(depot)) return (false, $"MoonStructures.Built was not raised for depot {depot}");
                return (true, null);
            });
            // [Dev] AutoBuildViaPlan: the lamp went in through the build menu's path, which records a free pose. This is the
            // record shape that made every later vanilla save fail before the depots stayed stripped through the Serialize.
            if (autoBuilt && DevTools.AutoBuildViaPlan.Value) Check("build-plan-pose", noWorld ?? (depot < 0 ? "no example lamp depot" : null), () =>
            {
                var rec = world.depots.get_Item(depot);
                return (rec.placedPose, $"depot {depot} built through the plan path has placedPose=false (the pose path was not exercised)");
            });
            Check("remove-modded-refund", noWorld ?? (depot < 0 ? "no example lamp depot to remove" : null), () =>
            {
                decimal refund = DecimalOf(world.StructureRefund(depot));
                decimal price = lamp.Price;
                CoreLog.Info($"[selftest] refund for depot {depot} ('{lamp.Id}', price ${price}): ${refund} = {(price != 0 ? refund / price : 0):0.###} of the price");
                if (!(refund > 0 && refund <= price)) return (false, $"StructureRefund({depot}) = {refund}, expected > 0 and <= {price}");
                if (!DevTools.AutoRemove.Value)
                {
                    CoreLog.Info($"[selftest] AutoRemove=false: depot {depot} left in place (refund checked, removal not run)");
                    return (true, null);
                }
                world.RemoveStructureNow(depot);
                if (world.depots.ContainsKey(depot)) return (false, $"depot {depot} is still in world.depots after RemoveStructureNow");
                if (!removedEvents.Contains(depot)) return (false, $"MoonStructures.Removed was not raised for depot {depot}");
                return (true, null);
            });
        }

        static readonly HashSet<int> builtEvents = new(), builtViews = new(), removedEvents = new(), cycleEvents = new();

        // Called when AutoBuild starts: forget every event id except Built for modded depots that exist in this world now
        // (raised when the save restored them), so an id from an earlier build cannot satisfy a later assertion.
        internal static void ResetEvents(MoonWorld world)
        {
            var live = new HashSet<int>();
            if (world?.depots != null)
                foreach (var kv in world.depots)
                    if (kv.Value != null && MoonStructures.IsModded(kv.Value.kind)) live.Add(kv.Key);
            builtEvents.IntersectWith(live);
            builtViews.IntersectWith(live);
            removedEvents.Clear();
            cycleEvents.Clear();
        }

        // Records the library's events so the checks can see they fired. Called once from DevTools.Bind.
        internal static void Subscribe()
        {
            MoonStructures.Built += b =>
            {
                if (b == null) return;
                builtEvents.Add(b.DepotId);
                if (b.View != null) builtViews.Add(b.DepotId);
            };
            MoonStructures.Removed += id => removedEvents.Add(id);
            MoonStructures.CycleFinished += c => { if (c != null && c.State != null && c.Definition != null) cycleEvents.Add(c.DepotId); };
        }

        internal static int DepotOfKind(MoonWorld world, int kind)
        {
            if (world?.depots == null) return -1;
            foreach (var kv in world.depots)
                if (kv.Value != null && kv.Value.kind == kind) return kv.Key;
            return -1;
        }

        internal static decimal DecimalOf(Il2CppSystem.Decimal d)
        {
            try { return decimal.Parse(d.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception) { return (decimal)(double)d; }
        }

        // True once nothing more is coming for this structure's card art (no render pending).
        static bool ArtSettled(StructureDefinition d) => !IconRenderer.Pending(d);

        static void Finish() => WaitForSave();

        // With [Dev] AutoSave, the checks wait (up to 180 s) for the requested save: a MoonDepotSave.Write after the
        // request, the world's pending save completed, then 3 s for the game to report a failed save.
        static bool orphanMode;
        static float saveUntil = -1f, saveGraceUntil = -1f, saveWaitFrom;

        static void WaitForSave()
        {
            saveGraceUntil = -1f;
            saveUntil = DevTools.AutoSave.Value ? Time.unscaledTime + 180f : Time.unscaledTime;
            saveWaitFrom = Time.unscaledTime;
            if (DevTools.AutoSave.Value) CoreLog.Info($"[selftest] waiting up to 180 s for the AutoSave save (t={saveWaitFrom:F1})");
        }

        static void TickSaveWait()
        {
            float now = Time.unscaledTime;
            if (now < saveUntil && saveGraceUntil < 0f)
            {
                if (!DevTools.SaveRequested || Sidecar.WriteSeq <= DevTools.SaveSeqAtRequest) return;
                Il2CppSystem.Threading.Tasks.Task pending = null;
                try { pending = (CoreState.World ?? CoreState.FindWorld())?.PendingSave; } catch (Exception) { }
                if (pending != null && !pending.IsCompleted) return;
                saveGraceUntil = now + 3f;
                return;
            }
            if (saveGraceUntil >= 0f && now < saveGraceUntil) return;
            if (DevTools.AutoSave.Value) CoreLog.Info($"[selftest] save wait over after {now - saveWaitFrom:F1} s ({(saveGraceUntil >= 0f ? "save finished" : "timed out")}, t={now:F1})");
            saveUntil = saveGraceUntil = -1f;
            Complete();
        }

        static void Complete()
        {
            var lamp = MoonStructures.Definition(LampId);
            if (orphanMode) OrphanChecks();
            else
            {
                // Before BuildChecks, which may remove the lamp.
                if (DevTools.AutoSave.Value) { SaveChecks(lamp); ProductionSaveCheck(); }
                ReloadChecks(lamp);
                ProductionReloadCheck();
                BuildChecks(lamp);
            }
            Done();
            FinishedAt = Time.unscaledTime;
        }

        static string SidecarPath() => Sidecar.PathFor(CoreState.SaveRoot);

        // Structure records with the given id in the sidecar on disk; -1 (and why) when it cannot be read.
        static int SidecarCount(string id, out string why)
        {
            why = null;
            string path = SidecarPath();
            if (path == null) { why = "CoreState.SaveRoot is unknown"; return -1; }
            if (!File.Exists(path)) { why = $"no {Sidecar.FileName} in the save root"; return -1; }
            try { return SidecarCodec.Deserialize(File.ReadAllText(path)).Structures.Count(s => s.Id == id); }
            catch (Exception e) { why = "reading it threw " + e.GetType().Name + ": " + e.Message; return -1; }
        }

        static Sidecar.WriteStats WriteAfterRequest(out string why)
        {
            why = null;
            if (!DevTools.SaveRequested) { why = "AutoSave never requested a save"; return null; }
            var w = Sidecar.LastWrite;
            if (w == null || w.Seq <= DevTools.SaveSeqAtRequest)
            {
                why = $"no MoonDepotSave.Write after the save request (writes: {Sidecar.WriteSeq}, at request {DevTools.SaveSeqAtRequest})";
                return null;
            }
            return w;
        }

        // [Dev] AutoSave with the lamp in the world: the Write prefix took it out of the vanilla save, the finalizer
        // put it back, the sidecar on disk has it, and the game accepted the save (no "Moon save failed").
        static void SaveChecks(StructureDefinition lamp)
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            string skip = lamp == null ? "example lamp not registered" : world == null ? "no MoonWorld found" : null;
            // The lamp checks apply when this run built the lamp or the world holds one (as BuildChecks).
            if (skip == null && !DevTools.AutoBuildNames(LampId) && DepotOfKind(world, lamp.Kind) < 0)
                CoreLog.Info($"[selftest] no '{LampId}' in the world: save-strips-modded and sidecar-written not applicable");
            else LampSaveChecks(lamp, world, skip);
            VanillaSaveCheck(world, skip);
        }

        static void LampSaveChecks(StructureDefinition lamp, MoonWorld world, string skip)
        {
            Check("save-strips-modded", skip, () =>
            {
                var w = WriteAfterRequest(out string why);
                if (w == null) return (false, why);
                int lampIdx = w.StrippedKinds.IndexOf(lamp.Kind);
                if (lampIdx < 0) return (false, $"write #{w.Seq} stripped kinds [{string.Join(",", w.StrippedKinds)}], none is {lamp.Kind}");
                if (w.PutBack != w.Stripped) return (false, $"write #{w.Seq}: the finalizer put back {w.PutBack} of {w.Stripped}");
                int depot = w.StrippedIds[lampIdx];
                if (world.depots != null && world.depots.ContainsKey(depot))
                    CoreLog.Info($"[selftest] saved depot {depot} contributions in the world: {ContributionText(world.depots.get_Item(depot))}; in its Copy(): {ContributionText(world.depots.get_Item(depot).Copy())}");
                bool inWorld = world.depots != null && world.depots.ContainsKey(depot) && world.depots.get_Item(depot)?.kind == lamp.Kind;
                return (inWorld, $"depot {depot} is not in world.depots after the save");
            });
            Check("sidecar-written", skip ?? (SidecarPath() == null ? "CoreState.SaveRoot is unknown" : null), () =>
            {
                int n = SidecarCount(LampId, out string why);
                if (n < 0) return (false, why);
                CoreLog.Info($"[selftest] sidecar {CoreLog.Redact(SidecarPath())} holds {n} '{LampId}' record(s)");
                return (n > 0, $"{Sidecar.FileName} has no structure with Id '{LampId}'");
            });
        }

        static void VanillaSaveCheck(MoonWorld world, string skip)
        {
            Check("vanilla-save-clean", skip, () =>
            {
                var w = WriteAfterRequest(out string why);
                if (w == null) return (false, why);
                int worldTotal = world.depots?.Count ?? 0, worldModded = 0;
                if (world.depots != null)
                    foreach (var kv in world.depots) if (kv.Value != null && kv.Value.kind >= KindAllocator.FirstModdedKind) worldModded++;
                string failedNote = "";
                try { failedNote = MoonWorld.saveFailed ?? ""; } catch (Exception) { }
                int refused = Sidecar.VanillaFailures - vanillaFailuresAtWatch;
                CoreLog.Info($"[selftest] save write #{w.Seq}: snapshot {w.Before} depots, {w.Modded} modded ({w.Posed} with a free pose), {w.Stripped} stripped, {w.VanillaAfter} written to the vanilla save, {w.PutBack} put back ({(w.MainThread ? "main thread" : "worker thread")}); {w.AnchorsStripped} anchor and {w.RobotsOffStripped} robots-off entries stripped, {w.SupportRefs} vanilla depots on a modded one; world {worldTotal} depots, {worldModded} modded; vanilla saves that threw since the watch started: {refused}; 'Moon save failed' seen {saveFailures}x; MoonWorld.saveFailed='{Trim(CoreLog.Redact(failedNote), 200)}'");
                if (w.Stripped != w.Modded) return (false, $"stripped {w.Stripped} of {w.Modded} modded records");
                if (w.VanillaAfter != worldTotal - worldModded) return (false, $"vanilla save got {w.VanillaAfter} depots, world has {worldTotal} - {worldModded} modded");
                // The game's read-back refusal surfaces as an exception out of MoonProgressStore.Write (seen by the save
                // finalizers), not always as a Unity "Moon save failed" error line.
                if (refused > 0) return (false, $"{refused} vanilla save(s) threw since the watch started (the game refused them; see 'the vanilla write threw' lines)");
                if (saveFailures > 0) return (false, $"the game logged 'Moon save failed' {saveFailures}x: {Trim(CoreLog.Redact(firstSaveFailure ?? ""), 300)}");
                return (true, null);
            });
        }

        // "<count>: [owner $dollars +pending kg, ...]" for a depot record (owners redacted).
        static string ContributionText(MoonDepotSnapshot rec)
        {
            var c = rec?.contributions;
            if (c == null) return "null";
            var parts = new List<string>();
            for (int i = 0; i < c.Count; i++)
            {
                var w = c.get_Item(i);
                parts.Add(w == null ? "null" : $"{CoreLog.Redact(w.player)} ${DecimalOf(w.dollars)} +{DecimalOf(w.pendingKilograms)} kg (ShareOf {Sidecar.ShareOf(w).Amount})");
            }
            return $"{c.Count}: [{string.Join(", ", parts)}]";
        }

        // The sidecar's money conversion: a MoonWalletSnapshot with $12.50 and 0.25 kg pending, through ShareOf, the JSON
        // codec and WalletOf, comes back with the same values.
        static void ContributionsRoundTrip()
        {
            Check("contributions-roundtrip", null, () =>
            {
                var w = new MoonWalletSnapshot();
                w.player = "dev-roundtrip";
                w.dollars = new Il2CppSystem.Decimal(12.5);
                w.pendingKilograms = new Il2CppSystem.Decimal(0.25);
                var share = Sidecar.ShareOf(w);
                var file = new SidecarFile { Structures = { new StructureRecord { Id = "dev.roundtrip", Contributions = { share } } } };
                var read = SidecarCodec.Deserialize(SidecarCodec.Serialize(file)).Structures[0].Contributions[0];
                var back = Sidecar.WalletOf(read);
                decimal dollars = DecimalOf(back.dollars), pending = DecimalOf(back.pendingKilograms);
                CoreLog.Info($"[selftest] contributions round trip: $12.5/0.25 kg -> share {share.Amount}/{share.Pending} -> json {read.Amount}/{read.Pending} -> wallet {dollars}/{pending} '{back.player}'");
                bool ok = share.Amount == 12.5 && share.Pending == 0.25 && dollars == 12.5m && pending == 0.25m && back.player == "dev-roundtrip";
                return (ok, $"got share {share.Amount}/{share.Pending}, wallet {dollars}/{pending} '{back.player}'");
            });
        }

        // A launch that did not build the lamp, after one that saved it: the sidecar brought it back into the world and
        // its view was spawned (Built with a view).
        static void ReloadChecks(StructureDefinition lamp)
        {
            // Not applicable when this run builds the lamp itself, or when the loaded sidecar holds no lamp record (no file,
            // or a file written after the lamp was removed: the normal state). It fails only when a saved lamp did not
            // come back into the world.
            if (lamp == null || DevTools.AutoBuildNames(LampId) || !Sidecar.LoadedIds().Contains(LampId)) return;
            var world = CoreState.World ?? CoreState.FindWorld();
            Check("reload-restores", world == null ? "no MoonWorld found" : null, () =>
            {
                int depot = DepotOfKind(world, lamp.Kind);
                if (depot < 0) return (false, $"the sidecar held '{LampId}' but world.depots ({world.depots?.Count}) has no record of kind {lamp.Kind}");
                if (!builtViews.Contains(depot)) return (false, $"MoonStructures.Built was not raised with a view for restored depot {depot}");
                CoreLog.Info($"[selftest] restored '{LampId}' is depot {depot}, Built fired with its view; contributions {ContributionText(world.depots.get_Item(depot))}");
                return (true, null);
            });
            // A lamp saved with a free pose (built through the build menu's path) comes back with the same pose.
            var saved = Sidecar.LoadedStructure(LampId);
            if (saved == null || !saved.PlacedPose) return;
            Check("reload-pose-intact", world == null ? "no MoonWorld found" : null, () =>
            {
                int depot = DepotOfKind(world, lamp.Kind);
                if (depot < 0) return (false, "no restored lamp");
                var rec = world.depots.get_Item(depot);
                static bool Near(float a, float b) => Math.Abs(a - b) < 1e-4f;
                bool same = rec.placedPose && Near(rec.qx, saved.Qx) && Near(rec.qy, saved.Qy) && Near(rec.qz, saved.Qz) && Near(rec.qw, saved.Qw);
                CoreLog.Info($"[selftest] restored lamp depot {depot}: placedPose={rec.placedPose} q=({rec.qx:F4},{rec.qy:F4},{rec.qz:F4},{rec.qw:F4}); sidecar q=({saved.Qx:F4},{saved.Qy:F4},{saved.Qz:F4},{saved.Qw:F4})");
                return (same, $"depot {depot} placedPose={rec.placedPose} q=({rec.qx},{rec.qy},{rec.qz},{rec.qw}), saved ({saved.Qx},{saved.Qy},{saved.Qz},{saved.Qw})");
            });
        }

        // ExampleStructure removed: its lamp was logged as kept, the vanilla save loaded, and the sidecar on disk (rewritten
        // by the AutoSave write, if on) still holds the record.
        static void OrphanChecks()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            Check("orphan-kept", null, () =>
            {
                if (world == null || world.depots == null || world.depots.Count == 0) return (false, "the vanilla save did not load (no world or no depots)");
                if (!Sidecar.Orphans.Contains(LampId)) return (false, $"'{LampId}' was not kept as an orphan at load");
                string rewritten = "not rewritten (AutoSave off)";
                if (DevTools.AutoSave.Value)
                {
                    var w = WriteAfterRequest(out string why);
                    if (w == null) return (false, why);
                    rewritten = $"rewritten by write #{w.Seq}";
                }
                int n = SidecarCount(LampId, out string err);
                if (n < 0) return (false, err);
                CoreLog.Info($"[selftest] orphan '{LampId}': sidecar {rewritten}, holds {n} record(s); world loaded with {world.depots.Count} depots");
                return (n > 0, $"the sidecar no longer holds '{LampId}'");
            });
        }

        // Example grit (value 250) answers the cargo switches from its CargoDefinition; an unregistered value (251) still
        // gets vanilla's out-of-range answer (dust); a canister holding grit is not dust.
        const string GritId = "ryanhartwig.example-grit";
        const byte GritValue = 250;

        // The live manifest names the lamp (hashed kind, owner version known) and grit (250), survives Encode/Decode
        // unchanged, and compares clean against itself.
        static void HandshakeCheck(int lampKind) => Check("handshake-manifest", null, () =>
        {
            var m = Handshake.Manifest();
            var bytes = HandshakePayload.Encode(m);
            var back = HandshakePayload.Decode(bytes);
            static string Text(RegistryManifest x) => string.Join(", ", x.Structures.Concat(x.Cargo).Select(e => $"{e.Id}={e.Value} {e.Owner} {e.Version}"));
            bool lamp = m.Structures.Any(e => e.Id == LampId && e.Value == lampKind && e.Version != "unknown");
            bool grit = m.Cargo.Any(e => e.Id == GritId && e.Value == GritValue);
            bool equal = Text(back) == Text(m) && back.Protocol == m.Protocol;
            var problems = HandshakePayload.Compare(m, m);
            CoreLog.Info($"[selftest] handshake manifest ({bytes.Length} bytes): {Text(m)}");
            return (lamp && grit && equal && problems.Count == 0,
                $"lamp {lamp}, grit {grit}, decoded equal {equal}, Compare(self) [{string.Join("; ", problems)}]");
        });

        static void CargoChecks()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            var grit = MoonStructures.AllCargo.FirstOrDefault(c => c.Id == GritId);
            string noGrit = grit == null ? $"'{GritId}' is not registered (is ExampleStructure installed?)" : null;
            var g = (MoonCargo)GritValue;
            Check("cargo-modded-name", noGrit, () =>
            {
                string name = MoonWorld.CargoName(g), shortName = MoonWorld.CargoShortName(g);
                CoreLog.Info($"[selftest] cargo {GritValue}: CargoName '{name}', CargoShortName '{shortName}'");
                return (name == "example grit" && shortName == "grit", $"CargoName({GritValue}) = '{name}', CargoShortName = '{shortName}'");
            });
            Check("cargo-modded-value", noGrit ?? (world == null ? "no MoonWorld found" : null), () =>
            {
                decimal value = DecimalOf(world.CargoValue(g));
                CoreLog.Info($"[selftest] cargo {GritValue}: CargoValue {value} (dust {DecimalOf(world.CargoValue(MoonCargo.Dust))}, sand {DecimalOf(world.CargoValue(MoonCargo.Sand))}, stone {DecimalOf(world.CargoValue(MoonCargo.Stone))})");
                return (value == 2m, $"CargoValue({GritValue}) = {value}");
            });
            Check("cargo-modded-kg", noGrit, () =>
            {
                double kg = MoonWorld.UnitKilograms(g);
                return (kg == 3.0, $"UnitKilograms({GritValue}) = {kg}, expected 3 (vanilla answers 1 for an unknown value)");
            });
            Check("cargo-unregistered-is-vanilla", null, () =>
            {
                string name = MoonWorld.CargoName((MoonCargo)251), dust = MoonWorld.CargoName(MoonCargo.Dust);
                return (name == "moon dust" && dust == "moon dust", $"CargoName(251) = '{name}', CargoName(Dust) = '{dust}'");
            });
            Check("cargo-isdust-false", noGrit, () =>
            {
                var c = new MoonDroppedCanister(1, "dev-canister", 5.0, 10.0, Vector3.up, Vector3.up, 0f, new Il2CppSystem.Nullable<Quaternion>(), g);
                var d = new MoonDroppedCanister(2, "dev-canister", 5.0, 10.0, Vector3.up, Vector3.up, 0f, new Il2CppSystem.Nullable<Quaternion>(), MoonCargo.Dust);
                CoreLog.Info($"[selftest] canister with cargo {(byte)c.cargo}: IsDust={c.IsDust}; with Dust: IsDust={d.IsDust}");
                return (!c.IsDust && d.IsDust, $"IsDust is {c.IsDust} for cargo {(byte)c.cargo} and {d.IsDust} for Dust");
            });
        }

        // The canister strip/put-back on a synthetic save snapshot (no safe runtime API makes a grit canister without a
        // player): dust 101, grit 102 (cargo record 250) and sand 103 (cargo record 1). The strip leaves only 101 and 103
        // (and sand's cargo record) for the vanilla save and records 102 as grit; the put-back restores both lists exactly.
        static void CanisterRoundTrip()
        {
            string noGrit = MoonStructures.AllCargo.Any(c => c.Id == GritId) ? null : $"'{GritId}' is not registered";
            Check("canister-sidecar-roundtrip", noGrit, () =>
            {
                var state = new MoonLooseDustSnapshot();
                state.canisters = new Il2CppSystem.Collections.Generic.List<MoonCanisterSnapshot>();
                state.cargo = new Il2CppSystem.Collections.Generic.List<MoonCargoSnapshot>();
                foreach (int id in new[] { 101, 102, 103 })
                {
                    var s = new MoonCanisterSnapshot();
                    s.id = id; s.owner = "dev-canister"; s.kilograms = 7.5; s.capacity = 10;
                    s.x = 1; s.y = 2; s.z = 3; s.ux = 0; s.uy = 1; s.uz = 0; s.physicalPose = true; s.qx = 0; s.qy = 0; s.qz = 0; s.qw = 1;
                    state.canisters.Add(s);
                }
                foreach (var (id, cargo) in new[] { (102, GritValue), (103, (byte)1) })
                {
                    var c = new MoonCargoSnapshot();
                    c.id = id; c.cargo = cargo;
                    state.cargo.Add(c);
                }
                var canBefore = Pointers(state.canisters);
                var cargoBefore = Pointers(state.cargo);
                var stripped = new Sidecar.StrippedCanisters();
                var records = Sidecar.StripCanisters(state, stripped);
                string afterStrip = $"canisters [{Ids(state.canisters)}], cargo [{CargoIds(state.cargo)}]";
                bool clean = Ids(state.canisters) == "101,103" && CargoIds(state.cargo) == "103:1";
                var r = records?.FirstOrDefault();
                bool recorded = records != null && records.Count == 1 && r.Id == 102 && r.CargoId == GritId && r.Kilograms == 7.5 && r.Capacity == 10 &&
                                r.Position != null && r.Position.SequenceEqual(new[] { 1f, 2f, 3f }) && r.Rotation != null && r.Rotation.SequenceEqual(new[] { 0f, 0f, 0f, 1f }) && r.PhysicalPose;
                var json = SidecarCodec.Deserialize(SidecarCodec.Serialize(new SidecarFile { Canisters = records ?? new() })).Canisters.FirstOrDefault();
                bool jsonOk = json != null && json.CargoId == GritId && json.Up != null && json.Up.SequenceEqual(new[] { 0f, 1f, 0f });
                int back = Sidecar.PutBackCanisters(state, stripped);
                bool identical = Pointers(state.canisters).SequenceEqual(canBefore) && Pointers(state.cargo).SequenceEqual(cargoBefore);
                CoreLog.Info($"[selftest] canister round trip: after strip {afterStrip}, {records?.Count ?? 0} record(s) ({r?.Id}:{r?.CargoId}), json cargo '{json?.CargoId}', put back {back}; now canisters [{Ids(state.canisters)}], cargo [{CargoIds(state.cargo)}]");
                return (clean && recorded && jsonOk && identical,
                    $"clean={clean} ({afterStrip}), recorded={recorded}, json={jsonOk}, identical after put-back={identical} (canisters [{Ids(state.canisters)}], cargo [{CargoIds(state.cargo)}])");
            });
        }

        static List<IntPtr> Pointers<T>(Il2CppSystem.Collections.Generic.List<T> l) where T : Il2CppSystem.Object
        {
            var r = new List<IntPtr>();
            for (int i = 0; i < l.Count; i++) r.Add(l.get_Item(i)?.Pointer ?? IntPtr.Zero);
            return r;
        }

        static string Ids(Il2CppSystem.Collections.Generic.List<MoonCanisterSnapshot> l)
        {
            var r = new List<string>();
            for (int i = 0; i < l.Count; i++) r.Add(l.get_Item(i)?.id.ToString() ?? "null");
            return string.Join(",", r);
        }

        static string CargoIds(Il2CppSystem.Collections.Generic.List<MoonCargoSnapshot> l)
        {
            var r = new List<string>();
            for (int i = 0; i < l.Count; i++) { var c = l.get_Item(i); r.Add(c == null ? "null" : $"{c.id}:{c.cargo}"); }
            return string.Join(",", r);
        }

        // The example grinder (a processor on the pressure chamber): InputSlot answers from its Input slots; a built or
        // restored grinder has the three slots of its definition; AutoFeed's dust lands in slot 0; within 30 s a cycle
        // puts grit into the output slot (slot 2) and raises CycleFinished. With no outfeed belt the game never calls
        // EmitCargo (fact (d)), so the output slot is what is asserted; EmitCargo's grit count is only logged.
        const string GrinderId = "ryanhartwig.example-grinder";
        static int grinderDepot = -1;
        static float cycleUntil = -1f;

        static void ProcessorChecks(StructureDefinition lamp)
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            var grinder = MoonStructures.Definition(GrinderId);
            string noGrinder = grinder == null ? $"'{GrinderId}' is not registered (is ExampleStructure installed?)" : null;
            Check("inputslot-accepts-dust", noGrinder, () =>
            {
                int i = MoonWorld.InputSlot(grinder.Kind, MoonCargo.Dust);
                return (i == 0, $"InputSlot({grinder.Kind}, Dust) = {i}");
            });
            Check("inputslot-refuses-dust", noGrinder ?? (lamp == null ? "example lamp not registered" : null), () =>
            {
                int l = MoonWorld.InputSlot(lamp.Kind, MoonCargo.Dust), s = MoonWorld.InputSlot(grinder.Kind, MoonCargo.Stone);
                return (l == -1 && s == -1, $"InputSlot(lamp {lamp.Kind}, Dust) = {l}, InputSlot(grinder {grinder.Kind}, Stone) = {s}");
            });
            grinderDepot = grinder != null && world != null ? DepotOfKind(world, grinder.Kind) : -1;
            if (grinderDepot < 0 && !DevTools.AutoBuildNames(GrinderId))
            {
                CoreLog.Info($"[selftest] '{GrinderId}' was neither built (AutoBuild) nor restored this run: processor state checks not applicable");
                return;
            }
            Check("processor-spec-slots", noGrinder ?? (grinderDepot < 0 ? "no example grinder depot in world.depots" : null), () =>
            {
                var s = world.ProductionOf(grinderDepot);
                if (s == null) return (false, $"ProductionOf({grinderDepot}) is null");
                CoreLog.Info($"[selftest] grinder depot {grinderDepot}: {StateText(s)}");
                int n = s.stores?.Length ?? -1;
                return (n == 3, $"ProductionOf({grinderDepot}).stores.Length = {n}, expected 3");
            });
            var feed = AutoFeeder.For(GrinderId);
            if (feed == null) return;
            Check("feed-lands-in-slot0", feed.Error, () => (feed.After >= feed.Kg, $"Stored(0) {feed.Before} -> {feed.After} kg after loading {feed.Kg} kg into depot {feed.Depot}"));
            if (feed.Error == null) cycleUntil = feed.At + 30f;
        }

        static void TickCycle()
        {
            MoonProductionState s = null;
            try { s = (CoreState.World ?? CoreState.FindWorld())?.ProductionOf(grinderDepot); } catch (Exception) { }
            bool grit = Stored(s, 2) > 0, raised = cycleEvents.Contains(grinderDepot);
            if (!(grit && raised) && Time.unscaledTime < cycleUntil) return;
            cycleUntil = -1f;
            CoreLog.Info($"[selftest] grinder depot {grinderDepot} after its cycle: {StateText(s)}; CycleFinished raised: {raised}; EmitCargo called with grit {Patches.ProductionPatches.ModdedCargoEmits}x (only with an outfeed belt)");
            if (DevTools.ForcePower.Value) { Assert("cycle-produces-grit", false, "[Dev] ForcePower is on: the cycle ran on forced power, which proves nothing about the grid"); return; }
            Assert("cycle-produces-grit", grit && raised, $"within 30 s of the feed: Stored(2) = {Stored(s, 2)}, CycleFinished raised: {raised}; {StateText(s)}");
        }

        // The example burner (a fuel generator) 8 m from the grinder, no vanilla node built: after a solve the burner is
        // in world.powerNodes; within 20 s of the self-test both report the same non-zero grid; within 30 s the burner has
        // stored charge, and the grinder runs at Power > 0.5 while the burner supplies it. 60 s after the burner's feed,
        // with its battery full and the grinder idle, 2 kg more grit must not start a cycle within 10 s.
        const string BurnerId = "ryanhartwig.example-burner";
        static readonly Dictionary<string, float> powerOpen = new();
        static int burnerDepot = -1, fullCycles;
        static float fullAt = -1f, fullUntil = -1f;
        static double fullGrit;

        static bool PowerPending => powerOpen.Count > 0 || fullAt >= 0f || fullUntil >= 0f;

        static void PowerChecks()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            var burner = MoonStructures.Definition(BurnerId);
            burnerDepot = burner != null && world != null ? DepotOfKind(world, burner.Kind) : -1;
            if (!DevTools.AutoBuildNames(BurnerId))
            {
                CoreLog.Info($"[selftest] '{BurnerId}' was not built by AutoBuild this run: power checks not applicable" + (burnerDepot < 0 ? "" : $" (restored burner: {PowerText(world.ProductionOf(burnerDepot))})"));
                return;
            }
            string[] names = { "power-node-registered", "power-grid-joined", "power-burner-supplies", "power-grinder-runs-on-burner", "power-no-burn-when-full" };
            string skip = burner == null ? $"'{BurnerId}' is not registered" : burnerDepot < 0 ? "no example burner depot in world.depots" : null;
            var feed = AutoFeeder.For(BurnerId);
            // Three of the checks time themselves from the burner's AutoFeed. A burner with no AutoFeed entry fed through a
            // belt box (a router) runs on whatever the chain brings, so those three do not apply; the node and grid
            // checks need no feed and still run. A burner fed straight by a machine's belt with no AutoFeed still fails.
            var into = skip == null && feed == null ? AutoBelter.Results.FirstOrDefault(r => r.Ok && r.To == burnerDepot) : null;
            var router = into != null ? RouterAt(world, into.From) : null;
            if (router != null)
            {
                CoreLog.Info($"[selftest] '{BurnerId}' has no AutoFeed entry and is fed through router '{router.Id}' (belt {into.Belt} from depot {into.From}): power-burner-supplies, power-grinder-runs-on-burner and power-no-burn-when-full not applicable; power-node-registered and power-grid-joined run; {PowerText(world.ProductionOf(burnerDepot))}");
                powerOpen["power-node-registered"] = Time.unscaledTime + 20f;
                powerOpen["power-grid-joined"] = Time.unscaledTime + 20f;
                DonorReport(world);
                return;
            }
            skip ??= feed == null ? "no AutoFeed entry for the burner" : feed.Error;
            if (skip != null) { foreach (var n in names) Assert(n, false, skip); return; }
            float now = Time.unscaledTime;
            powerOpen["power-node-registered"] = now + 20f;
            powerOpen["power-grid-joined"] = now + 20f;
            powerOpen["power-burner-supplies"] = now + 30f;
            powerOpen["power-grinder-runs-on-burner"] = now + 30f;
            fullAt = feed.At + 60f;
            DonorReport(world);
        }

        // The donor ruling (battery bank, else the solar post): does the built burner's view carry a MoonProductionView,
        // and does the game hold a power anchor for it?
        static void DonorReport(MoonWorld world)
        {
            try
            {
                var construction = UnityEngine.Object.FindFirstObjectByType<MoonDepotConstruction>();
                var view = construction?.ViewOf(burnerDepot);
                var pv = view == null ? null : view.GetComponentInChildren<MoonProductionView>(true);
                bool anchor = world.powerAnchors != null && world.powerAnchors.ContainsKey(burnerDepot);
                string at = anchor ? world.powerAnchors.get_Item(burnerDepot).ToString() : "none";
                CoreLog.Info($"[selftest] burner donor: view '{view?.name}', MoonProductionView {(pv == null ? "none" : pv.GetIl2CppType().Name)}, power anchor {at}");
            }
            catch (Exception e) { CoreLog.Warn("[selftest] burner donor report threw: " + e.Message); }
        }

        static void TickPower()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            var burner = MoonStructures.Definition(BurnerId);
            MoonProductionState b = null, g = null;
            try { b = world?.ProductionOf(burnerDepot); g = grinderDepot >= 0 ? world?.ProductionOf(grinderDepot) : null; } catch (Exception) { }
            string text = $"burner {PowerText(b)}; grinder {PowerText(g)}; powerNodes {world?.powerNodes?.Count}, nodePool {world?.nodePool?.Count}";
            Poll("power-node-registered", () =>
            {
                var nodes = world?.powerNodes;
                for (int i = 0; nodes != null && i < nodes.Count; i++)
                    if (nodes.get_Item(i)?.record?.kind == burner.Kind) return (true, null);
                return (false, $"no node of kind {burner.Kind} among {nodes?.Count ?? 0} powerNodes");
            });
            Poll("power-grid-joined", () => (b != null && g != null && b.Grid != 0 && b.Grid == g.Grid, text));
            Poll("power-burner-supplies", () => (b != null && (b.SuppliedKw > 0f || b.ChargeKwh > 0), text));
            Poll("power-grinder-runs-on-burner", () => (b != null && g != null && g.Running && g.Power > 0.5f && b.SuppliedKw > 0f, text));
            float now = Time.unscaledTime;
            if (fullAt >= 0f && now >= fullAt)
            {
                fullAt = -1f;
                float battery = burner.Power?.BatteryKwh ?? 0f;
                if (b == null || b.ChargeKwh < battery - 1e-6 || g == null || g.Running) { Assert("power-no-burn-when-full", false, $"60 s after the feed the battery is not full with the grinder idle: {text}"); return; }
                var grit = MoonStructures.CargoDefinition(GritId);
                world.CrewProcessorLoad(MoonWorld.LocalPlayer, burnerDepot, (MoonCargo)grit.Value, 2);
                fullCycles = b.Cycles;
                fullGrit = Stored(b, 0);
                fullUntil = now + 10f;
                CoreLog.Info($"[selftest] battery full: loaded 2 kg grit into the burner; {text}");
            }
            if (fullUntil >= 0f && now >= fullUntil)
            {
                fullUntil = -1f;
                bool ok = b != null && b.Cycles == fullCycles && !b.Running && Stored(b, 0) >= fullGrit - 1e-6 && fullGrit >= 2 - 1e-6;
                Assert("power-no-burn-when-full", ok, $"cycles {fullCycles} -> {b?.Cycles}, grit {fullGrit} -> {Stored(b, 0)} kg; {text}");
            }

            void Poll(string name, Func<(bool ok, string detail)> probe) => PollOpen(powerOpen, name, text, probe);
        }

        // An open assertion (name -> deadline in `open`): asserted as soon as the probe holds, or with its detail at the deadline.
        static void PollOpen(Dictionary<string, float> open, string name, string text, Func<(bool ok, string detail)> probe)
        {
            if (!open.TryGetValue(name, out float until)) return;
            bool ok; string detail;
            try { (ok, detail) = probe(); }
            catch (Exception e) { ok = false; detail = "threw " + e.GetType().Name + ": " + e.Message; }
            if (!ok && Time.unscaledTime < until) return;
            open.Remove(name);
            CoreLog.Info($"[selftest] {name}: {text}");
            Assert(name, ok, detail);
        }

        // SandPower, when AutoBuild names its compactor: both ids are registered at their hashed kinds and the briquette is
        // cargo 3. Each machine is fed by AutoFeed (the deadline runs from the feed: 30 s for the compactor, 40 s for the
        // generator) or by a belt AutoBelt built into it (the chain in DevBelts.cs: ChainSeconds from the self-test). By
        // then the compactor has finished a cycle and holds briquettes in its output slot (or EmitCargo saw a modded
        // cargo), and the generator holds charge. A run that restored both from the save runs the reload checks instead.
        const string CompactorId = "ryanhartwig.sand-compactor", GeneratorId = "ryanhartwig.briquette-generator", BriquetteId = "ryanhartwig.sand-briquette";
        static readonly Dictionary<string, float> sandOpen = new();
        static int compactorDepot = -1, generatorDepot = -1;

        static void SandPowerChecks()
        {
            if (!DevTools.AutoBuildNames(CompactorId)) { SandReloadChecks(); return; }
            var world = CoreState.World ?? CoreState.FindWorld();
            StructureDefinition c = MoonStructures.Definition(CompactorId), g = MoonStructures.Definition(GeneratorId);
            var b = MoonStructures.CargoDefinition(BriquetteId);
            Assert("sandpower-registered", c != null && c.Kind == KindAllocator.KindOf(CompactorId) && g != null && g.Kind == KindAllocator.KindOf(GeneratorId) && b?.Value == 3,
                $"compactor kind {c?.Kind}, generator kind {g?.Kind}, briquette value {b?.Value} (is SandPower installed?)");
            compactorDepot = c != null && world != null ? DepotOfKind(world, c.Kind) : -1;
            generatorDepot = g != null && world != null ? DepotOfKind(world, g.Kind) : -1;
            Open("sandpower-compactor-cycles", c, compactorDepot, CompactorId, 30f);
            Open("sandpower-briquette-out", c, compactorDepot, CompactorId, 30f);
            Open("sandpower-generator-charges", g, generatorDepot, GeneratorId, 40f);
            SandChainChecks(world);

            static void Open(string name, StructureDefinition d, int depot, string id, float seconds)
            {
                var feed = AutoFeeder.For(id);
                bool belted = depot >= 0 && BeltedInto(depot);
                string skip = d == null ? $"'{id}' is not registered" : depot < 0 ? $"no '{id}' depot in world.depots"
                    : feed == null && !belted ? $"no AutoFeed entry for '{id}' and no AutoBelt belt into it" : feed?.Error;
                if (skip != null) Assert(name, false, skip);
                else sandOpen[name] = feed != null ? feed.At + seconds : Time.unscaledTime + ChainSeconds;
            }
        }

        static void TickSandPower()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            MoonProductionState c = null, g = null;
            try { c = world?.ProductionOf(compactorDepot); g = world?.ProductionOf(generatorDepot); } catch (Exception) { }
            string text = $"compactor {StateText(c)}; generator {PowerText(g)}; modded EmitCargo calls {Patches.ProductionPatches.ModdedCargoEmits}";
            PollOpen(sandOpen, "sandpower-compactor-cycles", text, () => (c != null && c.Cycles >= 1, text));
            PollOpen(sandOpen, "sandpower-briquette-out", text, () =>
            {
                var spec = MoonWorld.Processor(MoonStructures.Definition(CompactorId).Kind);
                int o = spec.output;
                int cargo = o >= 0 ? (byte)spec.slotCargo[o] : -1;
                return ((cargo == 3 && Stored(c, o) > 0) || Patches.ProductionPatches.ModdedCargoEmits > 0, $"output slot {o} (cargo {cargo}) holds {Stored(c, o)} kg; {text}");
            });
            PollOpen(sandOpen, "sandpower-generator-charges", text, () => (g != null && g.ChargeKwh > 0, text));
        }

        static string PowerText(MoonProductionState s) => s == null ? "no state" :
            $"depot {s.Id} grid {s.Grid} power {s.Power:F2} running {s.Running} cycles {s.Cycles} charge {s.ChargeKwh:F4} kWh supplied {s.SuppliedKw:F3} kW slot0 {Stored(s, 0):0.##} kg";

        // After the AutoSave save: the sidecar on disk holds the grinder's production record, slot 2 recorded as grit.
        static void ProductionSaveCheck()
        {
            if (grinderDepot < 0) return;
            Check("production-sidecar", SidecarPath() == null ? "CoreState.SaveRoot is unknown" : null, () =>
            {
                string path = SidecarPath();
                if (!File.Exists(path)) return (false, $"no {Sidecar.FileName} in the save root");
                var file = SidecarCodec.Deserialize(File.ReadAllText(path));
                var p = file.Production.FirstOrDefault(r => r.DepotId == grinderDepot);
                if (p == null) return (false, $"{Sidecar.FileName} has no production record for depot {grinderDepot} ({file.Production.Count} record(s))");
                CoreLog.Info($"[selftest] sidecar production record: '{p.Id}' depot {p.DepotId} progress {p.Progress:F3} running {p.Running} charge {p.Charge} stores [{string.Join(" ", p.Stores.Select((s, i) => $"{i}:{s.Sum(x => x.Pending)}"))} kg] cargo [{string.Join(", ", p.SlotCargoIds)}]");
                return (p.SlotCargoIds.Count > 2 && p.SlotCargoIds[2] == GritId, $"SlotCargoIds = [{string.Join(", ", p.SlotCargoIds)}]");
            });
        }

        // A launch that did not build the grinder, after one that saved it: its production state came back at load with
        // the saved kilograms in slot 0 and in every other slot (read right after RestoreProduction, before any cycle could
        // change them), and it is still there.
        static void ProductionReloadCheck()
        {
            if (DevTools.AutoBuildNames(GrinderId) || !Sidecar.LoadedProductionIds().Contains(GrinderId)) return;
            var world = CoreState.World ?? CoreState.FindWorld();
            Check("production-reload", world == null ? "no MoonWorld found" : null, () =>
            {
                var r = Sidecar.RestoredProduction.FirstOrDefault(x => x.StructureId == GrinderId);
                if (r == null) return (false, "the sidecar held a grinder production record, but none was restored at load");
                var s = world.ProductionOf(r.Depot);
                string saved = string.Join(" ", r.Saved), loaded = string.Join(" ", r.Loaded);
                CoreLog.Info($"[selftest] restored grinder production: depot {r.Depot}, saved kg [{saved}], at load [{loaded}]; now {StateText(s)}");
                if (s == null) return (false, $"ProductionOf({r.Depot}) is null now");
                bool same = r.Saved.Length == r.Loaded.Length && r.Saved.Zip(r.Loaded, (a, b) => Math.Abs(a - b) <= 0.01).All(x => x);
                return (same, $"saved kg [{saved}], restored [{loaded}] (Stored(0) {r.Loaded.FirstOrDefault()} vs {r.Saved.FirstOrDefault()})");
            });
        }

        /// <summary>Kilograms in a slot; -1 when there is no state or no such slot.</summary>
        internal static double Stored(MoonProductionState s, int slot) => s?.stores != null && slot < s.stores.Length ? s.Stored(slot) : -1;

        static string StateText(MoonProductionState s)
        {
            if (s == null) return "no production state";
            int n = s.stores?.Length ?? 0;
            return $"kind {s.Kind} stores [{string.Join(" ", Enumerable.Range(0, n).Select(i => $"{i}:{Stored(s, i):0.##}"))}] progress {s.Progress:F3} running {s.Running} grid {s.Grid} power {s.Power:F2} cycles {s.Cycles} status '{s.Status}'";
        }

        // A modded kind answers BuildDefinition with its own clone, the build menu answers for it, and an unregistered
        // hashed kind still falls through to vanilla (null, no exception).
        static void DefinitionChecks(StructureDefinition lamp)
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            var builder = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>();
            string noWorld = world == null ? "no MoonWorld found" : lamp == null ? "example lamp not registered" : null;
            string noBuilder = builder == null ? "no MoonDepotBuilder found" : lamp == null ? "example lamp not registered" : null;

            MoonDepotBuildDefinition def = null;
            Check("builddef-modded-nonnull", noWorld, () =>
            {
                def = world.BuildDefinition(lamp.Kind);
                return (def != null, $"BuildDefinition({lamp.Kind}) returned null");
            });
            Check("builddef-modded-distinct", noWorld, () =>
            {
                if (def == null) return (false, "no modded definition");
                var donor = world.BuildDefinition(4);
                return (donor == null || def.Pointer != donor.Pointer, "the modded definition is the donor's own object");
            });
            Check("builddef-modded-named", noWorld, () =>
            {
                if (def == null) return (false, "no modded definition");
                return (def.displayName == "Example lamp" && def.priceDollars == 50, $"displayName '{def.displayName}', priceDollars {def.priceDollars}");
            });
            Check("builddef-unknown-null", world == null ? "no MoonWorld found" : null, () =>
            {
                var unknown = world.BuildDefinition(1_000_000 + 12345);
                return (unknown == null, $"BuildDefinition(1012345) returned '{unknown?.displayName}'");
            });
            Check("canbuild-modded", noBuilder, () => (builder.CanBuild(lamp.Kind), $"CanBuild({lamp.Kind}) is false"));
            Check("note-modded", noBuilder, () =>
            {
                string note = builder.ProductionNote(lamp.Kind);
                return (note == "StructureLib example.", $"ProductionNote({lamp.Kind}) = '{note}'");
            });
        }

        // An assertion that fails with `skip` as detail when it is set, and with the exception when the probe throws.
        static void Check(string name, string skip, Func<(bool ok, string detail)> probe)
        {
            if (skip != null) { Assert(name, false, skip); return; }
            try { var (ok, detail) = probe(); Assert(name, ok, detail); }
            catch (Exception e) { Assert(name, false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        static bool watching;

        // The exception watch (disk log scan from here, Unity log hook). Started at the AutoBuild step, before any build
        // or save; Begin starts it if that step did not.
        internal static void StartWatch()
        {
            if (watching) return;
            watching = true;
            unityErrors = 0;
            firstUnityError = null;
            saveFailures = 0;
            vanillaFailuresAtWatch = Sidecar.VanillaFailures;
            firstSaveFailure = null;
            logPath = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
            FlushDiskLog();
            try { logStart = File.Exists(logPath) ? new FileInfo(logPath).Length : -1; }
            catch (Exception) { logStart = -1; }
            HookUnityLog();
            CoreLog.Info("[selftest] watch started (log scan and Unity log hook)");
        }

        internal static void Begin()
        {
            passed = failed = 0;
            StartWatch();
            CoreLog.Info("[selftest] begin");
        }

        internal static void Assert(string name, bool ok, string detail = null)
        {
            if (ok) { passed++; CoreLog.Info($"[selftest] PASS {name}"); }
            else { failed++; CoreLog.Warn($"[selftest] FAIL {name}: {detail ?? "assertion false"}"); }
        }

        internal static void Done()
        {
            UnhookUnityLog();
            watching = false;
            ScanLog();
            CoreLog.Info($"[selftest] done: {passed} passed, {failed} failed");
        }

        // Any exception or error logged between begin and now fails the run: BepInEx lines from the disk log, plus Unity's
        // own Error/Exception messages (the disk log leaves those out when [Logging.Disk] WriteUnityLog is false).
        static void ScanLog()
        {
            var problems = new List<string>();
            if (logStart < 0) problems.Add($"could not read the log length at begin ({logPath})");
            else
            {
                try
                {
                    FlushDiskLog();
                    string text;
                    using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        fs.Seek(Math.Min(logStart, fs.Length), SeekOrigin.Begin);
                        using var reader = new StreamReader(fs, Encoding.UTF8);
                        text = reader.ReadToEnd();
                    }
                    var hits = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => Bad.IsMatch(l) && !l.Contains("[selftest]")).ToList();
                    if (hits.Count > 0) problems.Add($"{hits.Count} log-file line(s), first: {Trim(CoreLog.Redact(hits[0]), 300)}");
                }
                catch (Exception e) { problems.Add("reading the log failed: " + e.Message); }
            }
            if (!unityHooked) problems.Add("the Unity log hook was not installed");
            if (unityErrors > 0) problems.Add($"{unityErrors} Unity error/exception message(s), first: {Trim(CoreLog.Redact(firstUnityError), 300)}");
            Assert("no-vanilla-exceptions", problems.Count == 0, string.Join("; ", problems));
        }

        // Unity log hook. The converted delegate stays in a static field so the GC does not collect it while hooked.
        static int unityErrors, saveFailures, vanillaFailuresAtWatch;
        static string firstUnityError, firstSaveFailure;
        static bool unityHooked;
        static Application.LogCallback unityHook;

        static void HookUnityLog()
        {
            unityHooked = false;
            try
            {
                unityHook ??= Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Application.LogCallback>(
                    new Action<string, string, LogType>(OnUnityLog));
                Application.add_logMessageReceived(unityHook);
                unityHooked = true;
            }
            catch (Exception e) { CoreLog.Warn("[dev] SelfTest: hooking Application.logMessageReceived failed: " + e.Message); }
        }

        static void UnhookUnityLog()
        {
            if (!unityHooked) return;
            try { Application.remove_logMessageReceived(unityHook); }
            catch (Exception e) { CoreLog.Warn("[dev] SelfTest: unhooking Application.logMessageReceived failed: " + e.Message); }
        }

        static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (condition != null && condition.StartsWith("[selftest]", StringComparison.Ordinal)) return;
            if (condition != null && condition.Contains("Moon save failed")) { saveFailures++; firstSaveFailure ??= condition; }
            unityErrors++;
            firstUnityError ??= $"{type}: {condition}";
        }

        static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

        // The disk log is buffered (InstantFlushing=false); flush so the file holds everything logged so far.
        static void FlushDiskLog()
        {
            foreach (var l in BepInEx.Logging.Logger.Listeners.OfType<DiskLogListener>())
                try { l.LogWriter?.Flush(); } catch (Exception) { }
        }
    }
}
#endif
