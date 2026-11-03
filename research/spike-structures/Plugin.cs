using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using MoonCleaners;
using UnityEngine;

namespace StructureSpike
{
    // Research-only: logs how the game defines, lists, builds, powers, runs and saves structures (depots, machines,
    // solar panels, conveyors) and cargo. Only WorldProbe changes game state (money, lunar clock, two structures, a
    // save), and it refuses to run unless [Dev] AutoLoadSlot is 2 or more AND the loaded slot's folder is that
    // slot-<n> (WorldProbe.Allowed). In any other world, including slot-1 loaded by hand, it logs a refusal and does
    // nothing.
    [BepInPlugin("com.ryanhartwig.ms.structurespike", "Structure Spike", "0.0.1")]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource L;
        internal static ConfigEntry<int> AutoLoadSlot;
        internal static ConfigEntry<bool> Probe, ProbeLoad, ProbeSave, ProbeSpec, SunOverhead, SunExperiment;
        internal static ConfigEntry<double> LoadKg;
        internal static ConfigEntry<float> ForceLunarTime;

        public override void Load()
        {
            L = Log;
            AutoLoadSlot = Config.Bind("Dev", "AutoLoadSlot", -1, "Load the existing save in folder slot-<n> from the main menu so the world-time dump and probe run. -1 = off. Refuses slot 1 (the real save); clone it first with research/tools/copy-slot.ps1.");
            Probe = Config.Bind("Probe", "Build", true, "20 s into a loaded world: add money, build a solar twin and a pressure chamber in front of the walker, then log production and power every second for 120 s.");
            ProbeLoad = Config.Bind("Probe", "LoadChamber", true, "30 s after building: CrewProcessorLoad LoadKg of dust into the chamber.");
            LoadKg = Config.Bind("Probe", "LoadKg", 10.0, "Kilograms of dust for LoadChamber (the chamber input slot holds 20). More than 10 tests whether the 10 kg sand output slot stalls the cycle.");
            ProbeSave = Config.Bind("Probe", "SaveAtEnd", true, "After the 120 s window: RequestSave() into the loaded slot so the save-side traces run.");
            ProbeSpec = Config.Bind("Probe", "ReadPanelSpec", true, "Let the SolvePower trace read PowerNode.spec (a non-blittable interop struct). Turn off if the game dies right after '[trace] SolvePower reading node.spec'.");
            ForceLunarTime = Config.Bind("Probe", "ForceLunarTime", -1f, "If >= 0, MoonSolarSystem.SetLunarTime(value) before building (0..1 lunar-day fraction), for a save that is at night. -1 = leave the clock alone.");
            SunOverhead = Config.Bind("Probe", "SunOverhead", true, "When ForceLunarTime is -1: set the lunar clock so the sun is overhead at the walker, so the solar twin supplies power during the probe.");
            SunExperiment = Config.Bind("Probe", "SunFactorExperiment", true, "10 s after building: set the twin's state.SunFactor to 0.25 just before one SolvePower call and log what SolvePower makes of it (does it read the field or compute it?).");
            ClassInjector.RegisterTypeInIl2Cpp<SpikeDriver>();
            AddComponent<SpikeDriver>();
            Traces.PatchAll(new Harmony("com.ryanhartwig.ms.structurespike"));
            L.LogInfo($"Structure spike loaded: AutoLoadSlot={AutoLoadSlot.Value} Probe={Probe.Value} LoadChamber={ProbeLoad.Value} LoadKg={LoadKg.Value} SaveAtEnd={ProbeSave.Value} ReadPanelSpec={ProbeSpec.Value} ForceLunarTime={ForceLunarTime.Value} SunOverhead={SunOverhead.Value}");
        }
    }

    public class SpikeDriver : MonoBehaviour
    {
        public SpikeDriver(IntPtr ptr) : base(ptr) { }
        float t, inGameT;
        bool menuDumped, loadRequested, worldDumped, probeStarted, probeStopped;

        void Update()
        {
            try { Tick(); } catch (Exception e) { Plugin.L.LogError("Tick: " + e); menuDumped = loadRequested = worldDumped = probeStarted = probeStopped = true; }
        }

        void Tick()
        {
            t += Time.unscaledDeltaTime;
            if (!menuDumped && t > 6f)
            {
                menuDumped = true;
                // Safest first: a native crash in a later section must not lose the earlier ones.
                Run("catalog", MenuDump.Catalog);
                Run("cargo", MenuDump.Cargo);
                Run("definitions", MenuDump.Definitions);
                Run("kinds", MenuDump.Kinds);
                Run("specs", MenuDump.Specs);
            }
            if (menuDumped && !loadRequested && t > 9f && MoonSession.InMenu && !MoonSession.Loading && Plugin.AutoLoadSlot.Value >= 0)
            {
                loadRequested = true;
                Run("autoload", () => AutoLoad.Load(Plugin.AutoLoadSlot.Value));
            }
            if (!MoonSession.InGame) return;
            inGameT += Time.unscaledDeltaTime;
            if (inGameT > 20f && !worldDumped)
            {
                worldDumped = true;
                Run("world", WorldDump.All);
            }
            if (worldDumped && !probeStarted && Plugin.Probe.Value)
            {
                probeStarted = true;
                string why = WorldProbe.Allowed();
                if (why != null) { probeStopped = true; Plugin.L.LogWarning("[probe] refused, nothing changed: " + why); return; }
                try { WorldProbe.Start(inGameT); } catch (Exception e) { probeStopped = true; Plugin.L.LogError("[probe] start failed: " + e); }
            }
            if (probeStarted && !probeStopped)
            {
                try { probeStopped = WorldProbe.Tick(inGameT); } catch (Exception e) { probeStopped = true; Plugin.L.LogError("[probe] tick failed, probe stopped: " + e); }
            }
        }

        static void Run(string name, Action a)
        {
            try { a(); } catch (Exception e) { Plugin.L.LogError($"[{name}] {e}"); }
        }

        internal static string Safe(Func<string> f) { try { return f(); } catch (Exception e) { return "ERR:" + e.GetType().Name; } }
    }

    // Loads an existing save slot from the main menu (GarageLib DevTools.LoadSlot2, generalised to slot-<n>). The menu's
    // own play methods do nothing when called from a plugin; setting MoonSaveSlots.Active then StartShift() does load.
    static class AutoLoad
    {
        internal static void Load(int slot)
        {
            var L = Plugin.L;
            if (slot == 1) { L.LogWarning("[autoload] refusing slot-1 (the real save); clone it with research/tools/copy-slot.ps1 and load the clone"); return; }
            string suffix = "/slot-" + slot;
            int target = -1;
            for (int i = 0; i <= MoonSaveSlots.Count; i++)
            {
                string f = (MoonSaveSlots.SlotFolder(i) ?? "").Replace('\\', '/').TrimEnd('/');
                if (f.EndsWith(suffix)) { target = i; break; }
            }
            if (target < 0) { L.LogWarning($"[autoload] no slot index maps to folder slot-{slot}; staying in menu"); return; }
            MoonSaveSlots.Active = target;
            string chosen = (MoonSaveSlots.SlotFolder(MoonSaveSlots.Active) ?? "").Replace('\\', '/').TrimEnd('/');
            if (!chosen.EndsWith(suffix)) { L.LogWarning($"[autoload] active slot resolved to '{chosen}', refusing"); return; }
            L.LogInfo($"[autoload] loading slot index {target} (folder slot-{slot}, used={SpikeDriver.Safe(() => MoonSaveSlots.Used(target).ToString())})");
            MoonSession.StartShift();
        }
    }

    // Everything that can be read without a loaded world.
    static class MenuDump
    {
        static ManualLogSource L => Plugin.L;

        // Every static int on MoonWorld / MoonDepotBuilder / save classes that looks like a kind or a limit.
        internal static void Kinds()
        {
            foreach (var (type, tag) in new[] { (typeof(MoonWorld), "MoonWorld"), (typeof(MoonDepotBuilder), "MoonDepotBuilder"), (typeof(MoonDepotSave), "MoonDepotSave"), (typeof(MoonProductionSave), "MoonProductionSave"), (typeof(MoonConveyorSave), "MoonConveyorSave") })
            {
                var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(p => p.PropertyType == typeof(int) && (p.Name.EndsWith("Kind") || p.Name.EndsWith("Foundation") || p.Name.StartsWith("Max") || p.Name.EndsWith("Tab") || p.Name.StartsWith("Chamber") || p.Name.StartsWith("Press")))
                    .OrderBy(p => p.Name);
                foreach (var p in props)
                {
                    int v;
                    try { v = (int)p.GetValue(null); } catch (Exception e) { L.LogWarning($"[kinds] {tag}.{p.Name} threw {e.GetType().Name}"); continue; }
                    L.LogInfo($"[kinds] {tag}.{p.Name} = {v}");
                }
            }
            int max = MoonDepotSave.MaxStructureKind;
            L.LogInfo($"[kinds] per-kind static answers for 0..{max + 2} (IsSolarPanel/IsProcessor/IsPowerNode/HexPieceKind/PowerRange/Processor/TryPanel):");
            for (int k = 0; k <= max + 2; k++)
            {
                // TryPanel(kind, out PanelSpec) hard-crashed the game at the first solar kind (interop out-struct); the
                // panel specs are read from the static PostPanel/TwinPanel/TrackerPanel fields in Specs() instead.
                string proc = "";
                try { var sp = MoonWorld.Processor(k); proc = sp == null ? "" : $" processor='{sp.name}'"; } catch (Exception e) { proc = " processor=ERR:" + e.GetType().Name; }
                L.LogInfo($"[kinds] kind {k}: solar={Try(() => MoonWorld.IsSolarPanel(k))} processor={Try(() => MoonWorld.IsProcessor(k))} powerNode={Try(() => MoonWorld.IsPowerNode(k))} hexPiece={Try(() => MoonWorld.HexPieceKind(k))} powerRange={Try(() => MoonWorld.PowerRange(k))}{proc}");
            }
        }

        internal static void Catalog()
        {
            var sections = MoonDepotBuilder.structureSections;
            L.LogInfo($"[catalog] structureSections=[{Join(sections)}] catalogCategories=[{Join(MoonDepotBuilder.catalogCategories)}] StructuresTab={MoonDepotBuilder.StructuresTab} ConveyorKind={MoonDepotBuilder.ConveyorKind} Columns={MoonDepotBuilder.Columns}");
            var entries = MoonDepotBuilder.baseStructures;
            if (entries == null) { L.LogInfo("[catalog] baseStructures is null"); return; }
            L.LogInfo($"[catalog] baseStructures={entries.Length}");
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                L.LogInfo($"[catalog] [{i}] kind={e?.kind} name='{e?.name}' category='{e?.category}' image='{e?.image}'");
            }
        }

        internal static void Specs()
        {
            foreach (var (name, spec) in new[] { ("ChamberSpec", MoonWorld.ChamberSpec), ("PressSpec", MoonWorld.PressSpec) })
            {
                if (spec == null) { L.LogInfo($"[specs] {name} is null"); continue; }
                var slots = new List<string>();
                for (int i = 0; i < (spec.slotNames?.Length ?? 0); i++) slots.Add($"{i}:'{spec.slotNames[i]}' cargo={spec.slotCargo[i]} cap={spec.slotCapacity[i]}");
                L.LogInfo($"[specs] {name}: name='{spec.name}' product='{spec.product}' runningKw={spec.runningKw} cycleSeconds={spec.cycleSeconds} output={spec.output} slots=[{string.Join(" | ", slots)}]");
            }
            foreach (var (name, p) in new[] { ("PostPanel", MoonWorld.PostPanel), ("TwinPanel", MoonWorld.TwinPanel), ("TrackerPanel", MoonWorld.TrackerPanel) })
                L.LogInfo($"[specs] {name}: name='{p.name}' peakKw={p.peakKw} batteryKwh={p.batteryKwh} range={p.range} tiltDeg={p.tiltDeg} maxTiltDeg={p.maxTiltDeg} tracker={p.tracker}");
            L.LogInfo($"[specs] ChamberBatchKg={MoonWorld.ChamberBatchKg} PressDustPerBlockKg={MoonWorld.PressDustPerBlockKg} PressSandPerBlockKg={MoonWorld.PressSandPerBlockKg} ChamberPartBatchSeconds={MoonWorld.ChamberPartBatchSeconds} StallPower={MoonWorld.StallPower} PackagerStallSeconds={MoonWorld.PackagerStallSeconds} RetiredDepotPrice={MoonWorld.RetiredDepotPrice.ToString()}");
            L.LogInfo($"[specs] MoonProductionSave MaxSlots={MoonProductionSave.MaxSlots} MaxShares={MoonProductionSave.MaxShares}; MoonDepotSave.MaxStructureKind={MoonDepotSave.MaxStructureKind}");
        }

        internal static void Cargo()
        {
            foreach (MoonCargo c in Enum.GetValues(typeof(MoonCargo)))
                L.LogInfo($"[cargo] {c}={(int)c} name='{Try(() => MoonWorld.CargoName(c))}' short='{Try(() => MoonWorld.CargoShortName(c))}' unitKg={Try(() => MoonWorld.UnitKilograms(c))}");
            // What does the game do with a cargo value outside the enum? Decides whether new cargo kinds are viable.
            var bogus = (MoonCargo)7;
            L.LogInfo($"[cargo] out-of-range (7): name='{Try(() => MoonWorld.CargoName(bogus))}' short='{Try(() => MoonWorld.CargoShortName(bogus))}' unitKg={Try(() => MoonWorld.UnitKilograms(bogus))}");
            int max = MoonDepotSave.MaxStructureKind;
            for (int k = 0; k <= max; k++)
                if (Try(() => MoonWorld.IsProcessor(k)) == "True")
                    foreach (MoonCargo c in Enum.GetValues(typeof(MoonCargo)))
                        L.LogInfo($"[cargo] InputSlot(kind {k}, {c}) = {Try(() => MoonWorld.InputSlot(k, c))}");
        }

        // Every MoonDepotBuildDefinition asset the game has loaded, and how the kind table hangs off the root one.
        internal static void Definitions()
        {
            var defs = Resources.FindObjectsOfTypeAll<MoonDepotBuildDefinition>();
            L.LogInfo($"[defs] loaded MoonDepotBuildDefinition assets: {defs?.Length ?? 0}");
            if (defs == null) return;
            foreach (var d in defs) Describe("[defs]", d, -1);
            foreach (var d in defs)
            {
                var extra = d.additionalStructures;
                if (extra == null || extra.Length == 0) continue;
                L.LogInfo($"[defs] '{d.name}' additionalStructures={extra.Length} (kind table candidate)");
                for (int i = 0; i < extra.Length; i++) Describe("[defs]   ", extra[i], i);
            }
            var kits = Resources.FindObjectsOfTypeAll<MoonConveyorKit>();
            L.LogInfo($"[defs] MoonConveyorKit assets: {kits?.Length ?? 0}" + (kits != null && kits.Length > 0 ? $" first='{kits[0].name}'" : ""));
        }

        static void Describe(string tag, MoonDepotBuildDefinition d, int index)
        {
            if (d == null) { L.LogInfo($"{tag} [{index}] null"); return; }
            var ports = new List<string>();
            var cp = d.conveyorPorts;
            for (int i = 0; i < (cp?.Length ?? 0); i++) ports.Add($"'{cp[i].name}' {(cp[i].output ? "out" : "in")} @{cp[i].position}");
            L.LogInfo($"{tag} [{index}] asset='{d.name}' display='{d.displayName}' price={d.priceDollars} prefab='{d.prefab?.name}' scale={d.scale} half={d.halfFootprint} height={d.height} reach={d.reach} standsOnDust={d.standsOnDust} " +
                      $"skins={d.skinPrefabs?.Length ?? 0} additional={d.additionalStructures?.Length ?? 0} module='{d.module?.assetId}' shape={d.module?.shape} ports=[{string.Join("; ", ports)}]");
        }

        static string Join(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray a) => a == null ? "null" : string.Join(", ", a.Select(s => "'" + s + "'"));
        internal static string Try<T>(Func<T> f) { try { return f()?.ToString() ?? "null"; } catch (Exception e) { return "ERR:" + e.GetType().Name; } }
    }

    // Needs a loaded world: the instance-side kind table, what is built, production and power state.
    static class WorldDump
    {
        static ManualLogSource L => Plugin.L;

        internal static void All()
        {
            var world = UnityEngine.Object.FindFirstObjectByType<MoonWorld>();
            if (world == null) { L.LogWarning("[world] no MoonWorld instance found"); return; }
            var root = world.DepotDefinition;
            L.LogInfo($"[world] DepotDefinition='{root?.name}' additional={root?.additionalStructures?.Length ?? 0} StructureSignature={world.StructureSignature} BuiltDepotCount={world.BuiltDepotCount} BuiltConveyorCount={world.BuiltConveyorCount} FoundationStyle={world.FoundationStyle}");
            int max = MoonDepotSave.MaxStructureKind;
            for (int k = 0; k <= max + 2; k++)
            {
                MoonDepotBuildDefinition d = null;
                string err = null;
                try { d = world.BuildDefinition(k); } catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
                L.LogInfo($"[world] BuildDefinition({k}) -> {(err != null ? "ERR " + err : d == null ? "null" : $"'{d.name}' display='{d.displayName}' price={d.priceDollars}")} accepts='{MenuDump.Try(() => world.AcceptsText(k))}'");
            }
            if (world.depots != null)
                foreach (var kv in world.depots)
                {
                    var d = kv.Value;
                    L.LogInfo($"[world] built id={d.id} kind={d.kind} owner={Redact(d.owner)} pos=({d.x:F0},{d.y:F0},{d.z:F0}) yaw={d.yaw:F0} support={d.support} stored={d.stored} style={d.style}");
                }
            var prod = world.production;
            L.LogInfo($"[world] production entries={prod?.Count ?? 0} powerNodes={world.powerNodes?.Count ?? 0} powerGrids={world.powerGrids?.Count ?? 0} powerLinks={world.powerLinks?.Count ?? 0}");
            if (prod != null)
                foreach (var kv in prod)
                {
                    var s = kv.Value;
                    L.LogInfo($"[world] production id={s.Id} kind={s.Kind} running={s.Running} progress={s.Progress:F2} power={s.Power:F2} demandKw={s.DemandKw} usedKw={s.UsedKw} grid={s.Grid} chargeKwh={s.ChargeKwh:F2} suppliedKw={s.SuppliedKw} batteryKw={s.BatteryKw} sun={s.SunFactor:F2} status='{s.Status}' cycles={s.Cycles} stores={s.stores?.Length ?? 0}");
                }
            if (world.powerGrids != null)
                foreach (var kv in world.powerGrids)
                {
                    var g = kv.Value;
                    L.LogInfo($"[world] grid id={g.id} panels={g.panels} machines={g.machines} supplyKw={g.supplyKw} demandKw={g.demandKw} usedKw={g.usedKw} storedKwh={g.storedKwh:F2} capacityKwh={g.capacityKwh:F2} satisfaction={g.satisfaction:F2}");
                }
            foreach (MoonCargo c in Enum.GetValues(typeof(MoonCargo)))
                L.LogInfo($"[world] CargoValue({c}) = {MenuDump.Try(() => world.CargoValue(c).ToString())}");
            var builder = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>();
            if (builder != null)
            {
                var entries = builder.structureEntries;
                L.LogInfo($"[world] builder structureEntries={entries?.Length ?? 0} (instance copy of the catalog)");
                for (int k = 0; k <= max; k++) L.LogInfo($"[world] builder CanBuild({k})={MenuDump.Try(() => builder.CanBuild(k))} note='{MenuDump.Try(() => builder.ProductionNote(k))}'");
            }
            else L.LogInfo("[world] no MoonDepotBuilder instance");
        }

        internal static string Redact(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int i = s.IndexOf("steam-", StringComparison.Ordinal);
            return i < 0 ? s : s.Substring(0, i) + "steam-…";
        }
    }

    // Builds a solar twin and a pressure chamber in front of the local walker in the loaded (cloned) save, then logs
    // production and power once a second. Answers: does TryBuildDepot take a null check, what SolvePower does per node,
    // the FinishCycle mass rule, and whether a machine emits without an outfeed belt. Every step is logged before it
    // runs so a native crash still shows which call it was.
    static class WorldProbe
    {
        static ManualLogSource L => Plugin.L;
        const float Window = 120f, LoadAt = 30f, ExperimentAt = 10f, SaveTimeout = 30f;
        static bool experimented;
        internal static int TwinId = -1, ChamberId = -1;
        static MoonWorld world;
        static MoonWalker walker;
        static string player;
        static float buildT = -1, nextSample, saveT = -1;
        static bool loaded;

        internal static bool IsProbe(int id) => id >= 0 && (id == TwinId || id == ChamberId);

        // Null when the probe may change this world; otherwise the reason it must not. The probe writes money, the
        // lunar clock, structures and a save, so it only runs in the cloned slot AutoLoad was told to load (never slot-1).
        internal static string Allowed()
        {
            int slot = Plugin.AutoLoadSlot.Value;
            if (slot < 2) return $"[Dev] AutoLoadSlot={slot}; the probe only runs in a cloned slot (2 or more) loaded by AutoLoad";
            string active = (SpikeDriver.Safe(() => MoonSaveSlots.SlotFolder(MoonSaveSlots.Active)) ?? "").Replace('\\', '/').TrimEnd('/');
            if (!active.EndsWith("/slot-" + slot)) return $"the loaded slot (index {MoonSaveSlots.Active}, folder '{active.Substring(active.LastIndexOf('/') + 1)}') is not slot-{slot}";
            return null;
        }

        internal static void Start(float now)
        {
            L.LogInfo("[probe] step: find MoonWorld");
            world = UnityEngine.Object.FindFirstObjectByType<MoonWorld>();
            if (world == null) throw new InvalidOperationException("no MoonWorld");
            L.LogInfo("[probe] step: find MoonWalker (no static MoonWalker.Local in this build)");
            var walkers = UnityEngine.Object.FindObjectsByType<MoonWalker>(FindObjectsSortMode.None);
            walker = walkers != null && walkers.Length > 0 ? walkers[0] : null;
            if (walker == null) throw new InvalidOperationException("no MoonWalker");
            player = MoonWorld.LocalPlayer;
            var wt = walker.transform;
            L.LogInfo($"[probe] walkers={walkers.Length} walker='{walker.name}' pos={wt.position} |pos|={wt.position.magnitude:F1} fwd={wt.forward} up.radial={Vector3.Dot(wt.up, wt.position.normalized):F3} player={Id(player)}");

            L.LogInfo("[probe] step: solar clock");
            var sun = world.solarSystem ?? UnityEngine.Object.FindFirstObjectByType<MoonSolarSystem>();
            L.LogInfo($"[probe] solarSystem field={(world.solarSystem != null ? "set" : "null")} found={(sun != null ? "yes" : "no")}");
            if (sun != null)
            {
                L.LogInfo($"[probe] sun: LunarTime={sun.LunarTime:F3} LunarDay={sun.LunarDay} Daylight={MoonSolarSystem.Daylight:F2} SunHeight={sun.SunHeight:F2} lunarDaySeconds={sun.lunarDaySeconds} TimeSpeed={MoonSolarSystem.TimeSpeed}");
                float target = Plugin.ForceLunarTime.Value;
                if (target < 0 && Plugin.SunOverhead.Value)
                {
                    // The lunar time whose sun direction is closest to straight up at the walker (the panels need local sun).
                    var up = wt.position.normalized;
                    float best = -2f;
                    for (int i = 0; i < 400; i++)
                    {
                        float lt = i / 400f, d = Vector3.Dot(MoonSolarSystem.SunDirectionAt(lt), up);
                        if (d > best) { best = d; target = lt; }
                    }
                    L.LogInfo($"[probe] sun overhead at LunarTime={target:F4} (dot={best:F3}; now dot={Vector3.Dot(MoonSolarSystem.SunDirectionAt(sun.LunarTime), up):F3})");
                }
                if (target >= 0)
                {
                    L.LogInfo($"[probe] step: SetLunarTime({target:F4})");
                    sun.SetLunarTime(target);
                    L.LogInfo($"[probe] sun now: LunarTime={sun.LunarTime:F3} LunarDay={sun.LunarDay} Daylight={MoonSolarSystem.Daylight:F2} SunHeight={sun.SunHeight:F2}");
                }
            }

            L.LogInfo("[probe] step: DevAddMoney(player, 20000)");
            string before = SpikeDriver.Safe(() => world.WalletDollars(player).ToString());
            var amount = new Il2CppSystem.Decimal(20000);
            L.LogInfo($"[probe] decimal check: new Decimal(20000)='{SpikeDriver.Safe(() => amount.ToString())}' new Decimal(20000.0)='{SpikeDriver.Safe(() => new Il2CppSystem.Decimal(20000.0).ToString())}'");
            world.DevAddMoney(player, amount);
            string after = SpikeDriver.Safe(() => world.WalletDollars(player).ToString());
            L.LogInfo($"[probe] wallet {before} -> {after} after DevAddMoney (team balance {SpikeDriver.Safe(() => world.wallets.Balance.ToString())})");
            if (after == before)
            {
                // DevAddMoney is a no-op in this build; the wallet is team-wide, so set it directly.
                double have = double.TryParse(before, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 0;
                L.LogInfo($"[probe] step: SetTeamDollars({have + 20000:F2})");
                world.SetTeamDollars(new Il2CppSystem.Decimal(Math.Round(have + 20000, 2)));
                L.LogInfo($"[probe] wallet {before} -> {SpikeDriver.Safe(() => world.WalletDollars(player).ToString())} after SetTeamDollars");
            }
            L.LogInfo($"[probe] kinds: SolarTwinKind={MoonWorld.SolarTwinKind} PressureChamberKind={MoonWorld.PressureChamberKind} PowerRange(twin)={MoonWorld.PowerRange(MoonWorld.SolarTwinKind)} PowerRange(chamber)={MoonWorld.PowerRange(MoonWorld.PressureChamberKind)}");

            // Twin 12 m ahead, chamber 10 m further on the same heading. If the twin's site is refused, turn 90 deg and retry.
            for (int turn = 0; turn < 4 && TwinId < 0; turn++)
            {
                var heading = Quaternion.AngleAxis(90f * turn, wt.position.normalized) * wt.forward;
                var twinAt = wt.position + heading * 12f;
                var chamberAt = wt.position + heading * 22f;
                L.LogInfo($"[probe] heading {turn * 90} deg: twin at {twinAt}, chamber at {chamberAt}");
                TwinId = Place("solar twin", MoonWorld.SolarTwinKind, twinAt);
                if (TwinId >= 0) ChamberId = Place("chamber", MoonWorld.PressureChamberKind, chamberAt);
            }
            if (TwinId >= 0)
            {
                // What placement asks the power system: which existing nodes a new chamber at this spot would join.
                var at = world.depots.get_Item(TwinId);
                var p = new Vector3(at.x, at.y, at.z) + wt.forward * 8f;
                var result = new Il2CppSystem.Collections.Generic.List<int>();
                L.LogInfo($"[probe] step: PowerCandidates(chamber, 8 m from twin)");
                world.PowerCandidates(MoonWorld.PressureChamberKind, p, Quaternion.LookRotation(wt.forward, p.normalized), result);
                L.LogInfo($"[probe] PowerCandidates(chamber) -> {result.Count} ids");
            }
            L.LogInfo($"[probe] rig: twin id={TwinId} chamber id={ChamberId} depots={world.depots?.Count} production={world.production?.Count} powerNodes={world.powerNodes?.Count} grids={world.powerGrids?.Count} links={world.powerLinks?.Count}");
            buildT = now;
            nextSample = now;
        }

        // Returns the new depot id, or -1.
        static int Place(string name, int kind, Vector3 position)
        {
            var dir = position.normalized;
            var before = DepotIds();
            L.LogInfo($"[probe] step: TryBuildDepot({name}, kind {kind}, check=null) dir={dir}");
            string reason;
            bool ok = world.TryBuildDepot(walker, dir, 0f, out reason, null, kind);
            string route = "null-check";
            L.LogInfo($"[probe] {name} with null check: ok={ok} reason='{reason}'");
            if (!ok)
            {
                L.LogInfo($"[probe] step: BeginDepotCheck({name}) + Advance(50 ms) until Done");
                var check = world.BeginDepotCheck(dir, 0f, kind);
                int n = 0;
                while (!check.Done && n < 400) { n++; check.Advance(50.0); }
                if (!check.Done)
                {
                    L.LogInfo($"[probe] {name} check not Done after {n} Advance calls; stepping DepotPlacementSteps");
                    var steps = world.DepotPlacementSteps(check);
                    while (!check.Done && n < 100000) { n++; if (!steps.MoveNext()) break; }
                }
                L.LogInfo($"[probe] {name} check: steps={n} Done={check.Done} Valid={check.Valid} Reason='{check.Reason}' Slices={check.Slices}");
                L.LogInfo($"[probe] step: TryBuildDepot({name}, kind {kind}, check=completed)");
                ok = world.TryBuildDepot(walker, dir, 0f, out reason, check, kind);
                route = "completed-check";
            }
            int id = -1;
            foreach (var kv in world.depots) if (!before.Contains(kv.Key)) { id = kv.Key; break; }
            L.LogInfo($"[probe] built {name} ok={ok} route={route} reason='{reason}' id={id} production={(id >= 0 && world.production.ContainsKey(id) ? "yes" : "no")}");
            return ok ? id : -1;
        }

        static HashSet<int> DepotIds()
        {
            var s = new HashSet<int>();
            foreach (var kv in world.depots) s.Add(kv.Key);
            return s;
        }

        // Returns true once the probe has finished.
        internal static bool Tick(float now)
        {
            if (buildT < 0) return true;
            float rel = now - buildT;
            if (rel <= Window && now >= nextSample)
            {
                nextSample += 1f;
                Sample(rel);
            }
            if (!experimented && rel >= ExperimentAt && TwinId >= 0 && Plugin.SunExperiment.Value)
            {
                experimented = true;
                L.LogInfo($"[probe] step: arm SolvePower experiment (twin {TwinId} SunFactor := 0.25 just before the next solve)");
                Traces.ArmSunExperiment(TwinId);
            }
            if (!loaded && rel >= LoadAt)
            {
                loaded = true;
                if (Plugin.ProbeLoad.Value && ChamberId >= 0) LoadChamber();
            }
            if (rel > Window && saveT < 0)
            {
                Summary();
                if (!Plugin.ProbeSave.Value) { L.LogInfo("[probe] done (no save)"); return true; }
                L.LogInfo("[probe] step: RequestSave()");
                world.RequestSave();
                saveT = now;
            }
            if (saveT >= 0)
            {
                var pending = world.PendingSave;
                bool finished = pending == null || pending.IsCompleted;
                if (finished || now - saveT > SaveTimeout)
                {
                    L.LogInfo($"[probe] save {(finished ? "finished" : "still pending after " + SaveTimeout + " s")}");
                    L.LogInfo("[probe] done");
                    return true;
                }
            }
            return false;
        }

        static void LoadChamber()
        {
            L.LogInfo($"[probe] chamber before load: {Stores(world.ProductionOf(ChamberId))}");
            L.LogInfo($"[probe] step: CrewProcessorLoad(player, {ChamberId}, Dust, {Plugin.LoadKg.Value})");
            world.CrewProcessorLoad(player, ChamberId, MoonCargo.Dust, Plugin.LoadKg.Value);
            L.LogInfo($"[probe] chamber after load: {Stores(world.ProductionOf(ChamberId))}");
        }

        static void Sample(float rel)
        {
            foreach (int id in new[] { TwinId, ChamberId })
            {
                if (id < 0) continue;
                var s = world.ProductionOf(id);
                if (s == null) { L.LogInfo($"[probe] t={rel:F0}s id={id} no production state"); continue; }
                L.LogInfo($"[probe] t={rel:F0}s prod id={s.Id} kind={s.Kind} running={s.Running} progress={s.Progress:F3} power={s.Power:F3} demandKw={s.DemandKw:F2} usedKw={s.UsedKw:F2} grid={s.Grid} chargeKwh={s.ChargeKwh:F4} suppliedKw={s.SuppliedKw:F2} batteryKw={s.BatteryKw:F2} sun={s.SunFactor:F3} daylight={s.Daylight} cycles={s.Cycles} status='{s.Status}' stores={Stores(s)}");
            }
            int n = 0;
            foreach (var kv in world.powerGrids)
            {
                if (n++ >= 6) break;
                var g = kv.Value;
                L.LogInfo($"[probe] t={rel:F0}s grid key={kv.Key} id={g.id} panels={g.panels} machines={g.machines} supplyKw={g.supplyKw:F2} demandKw={g.demandKw:F2} usedKw={g.usedKw:F2} peakKw={g.peakKw:F2} satisfaction={g.satisfaction:F3} storedKwh={g.storedKwh:F4} capacityKwh={g.capacityKwh:F2}");
            }
        }

        static void Summary()
        {
            L.LogInfo($"[probe] summary: depots={world.depots.Count} production={world.production.Count} powerNodes={world.powerNodes.Count} grids={world.powerGrids.Count} links={world.powerLinks.Count} canisters={world.canisters?.Count}");
            foreach (int id in new[] { TwinId, ChamberId })
                if (id >= 0 && world.depots.ContainsKey(id))
                {
                    var d = world.depots.get_Item(id);
                    L.LogInfo($"[probe] summary depot id={d.id} kind={d.kind} owner={Id(d.owner)} pos=({d.x:F1},{d.y:F1},{d.z:F1}) yaw={d.yaw:F0} support={d.support} stored={d.stored}");
                }
        }

        // Kilograms per slot as "slot:kg(owners)". Owner ids are never logged.
        internal static string Stores(MoonProductionState s)
        {
            if (s?.stores == null) return "none";
            var parts = new List<string>();
            for (int i = 0; i < s.stores.Length; i++)
            {
                var st = s.stores[i];
                parts.Add($"{i}:{s.Stored(i):F2}({st?.Count ?? 0})");
            }
            return "[" + string.Join(" ", parts) + "]";
        }

        // Player / owner ids are Steam ids: keep a short prefix only.
        internal static string Id(string s)
        {
            if (string.IsNullOrEmpty(s)) return "'" + s + "'";
            int i = s.IndexOf("steam-", StringComparison.Ordinal);
            if (i >= 0) return s.Substring(0, i) + "steam-...";
            return s.Length <= 4 ? s : s.Substring(0, 4) + "...";
        }
    }

    // Bounded call traces: which game paths touch kinds, definitions, production, power and the save, in what order.
    // Each concern is its own patch class so one missing target does not take the others down.
    static class Traces
    {
        static readonly Dictionary<string, int> counts = new();
        internal static string Phase = "";

        internal static void PatchAll(Harmony h)
        {
            foreach (var t in new[] { typeof(General), typeof(Views), typeof(Production), typeof(Power), typeof(Canisters) })
            {
                try { h.PatchAll(t); Plugin.L.LogInfo($"[trace] patched {t.Name}"); }
                catch (Exception e) { Plugin.L.LogError($"[trace] patching {t.Name} failed: {e}"); }
            }
        }

        internal static void ArmSunExperiment(int id) => Power.experimentId = id;

        // True (and counted) while the key is under its limit.
        internal static bool Want(string key, int limit = 20)
        {
            counts.TryGetValue(key, out int n);
            if (n >= limit) return false;
            counts[key] = n + 1;
            return true;
        }

        static void T(string what, int limit = 20)
        {
            if (Want(what.Split('(')[0], limit)) Log(what);
        }

        static void Log(string what) => Plugin.L.LogInfo($"[trace] f{Time.frameCount}{(Phase.Length > 0 ? " in " + Phase : "")} {what}");

        static string Name(GameObject go) => go == null ? "null" : $"'{go.name}' parent='{(go.transform.parent == null ? "" : go.transform.parent.name)}'";

        static class General
        {
            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.BuildDefinition)), HarmonyPostfix]
            static void BuildDefinition(int kind, MoonDepotBuildDefinition __result) => T($"BuildDefinition(kind={kind}) -> '{__result?.name}'", 40);

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryBuildDepot)), HarmonyPrefix]
            static void TryBuildDepotPre(int kind, MoonWorld.DepotPlacementCheck checkedSite)
            {
                Phase = $"TryBuildDepot(kind {kind})";
                T($"TryBuildDepot.pre(kind={kind}, check={(checkedSite == null ? "null" : $"Done={checkedSite.Done} Valid={checkedSite.Valid}")})");
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryBuildDepot)), HarmonyPostfix]
            static void TryBuildDepot(bool __result, int kind, string reason)
            {
                Phase = "";
                T($"TryBuildDepot(kind={kind}) -> {__result} reason='{reason}'");
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.ValidateDepotPlacement)), HarmonyPostfix]
            static void Validate(bool __result, int kind, string reason) => T($"ValidateDepotPlacement(kind={kind}) -> {__result} '{reason}'", 6);

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreDepots)), HarmonyPrefix]
            static void RestoreDepots(MoonLooseDustSnapshot snapshot)
            {
                Phase = "RestoreDepots";
                T($"RestoreDepots(depots={snapshot?.depots?.Count}, nextDepotId={snapshot?.nextDepotId})");
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreDepots)), HarmonyPostfix]
            static void RestoreDepotsPost(MoonWorld __instance)
            {
                Phase = "";
                T($"RestoreDepots.post(depots={__instance.depots?.Count}, production={__instance.production?.Count})");
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.SnapshotDepots)), HarmonyPostfix]
            static void SnapshotDepots(MoonLooseDustSnapshot snapshot) => T($"SnapshotDepots(depots={snapshot?.depots?.Count})");

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreProduction)), HarmonyPrefix]
            static void RestoreProduction(MoonLooseDustSnapshot snapshot) => T($"RestoreProduction(production={snapshot?.production?.Count})");

            [HarmonyPatch(typeof(MoonDepotSave), nameof(MoonDepotSave.Write)), HarmonyPrefix]
            static void DepotWrite(MoonLooseDustSnapshot state) => T($"MoonDepotSave.Write(depots={state?.depots?.Count})");

            [HarmonyPatch(typeof(MoonDepotSave), nameof(MoonDepotSave.Read)), HarmonyPostfix]
            static void DepotRead(MoonLooseDustSnapshot state, int version) => T($"MoonDepotSave.Read(version={version}) -> depots={state?.depots?.Count}");

            [HarmonyPatch(typeof(MoonProductionSave), nameof(MoonProductionSave.Write)), HarmonyPrefix]
            static void ProdWrite(MoonLooseDustSnapshot state) => T($"MoonProductionSave.Write(production={state?.production?.Count})");

            [HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.BuildCatalog)), HarmonyPrefix]
            static void BuildCatalog(MoonDepotBuilder __instance) => T($"MoonDepotBuilder.BuildCatalog(entries={__instance.structureEntries?.Length})");

            [HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.SelectStructure)), HarmonyPrefix]
            static void SelectStructure(int kind) => T($"MoonDepotBuilder.SelectStructure(kind={kind})");

            [HarmonyPatch(typeof(MoonDepotBuilder), nameof(MoonDepotBuilder.BeginPlacementFromShop)), HarmonyPrefix]
            static void FromShop(int kind) => T($"MoonDepotBuilder.BeginPlacementFromShop(kind={kind})");
        }

        // Fact (a): who instantiates the built view, on build and on restore. Phase says whether a call happened inside
        // TryBuildDepot or RestoreDepots; the frame number says whether it was the same frame or later.
        static class Views
        {
            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RevealBuilt)), HarmonyPostfix]
            static void RevealBuilt(GameObject view, int belt)
            {
                if (!Want("RevealBuilt")) return;
                var pv = view == null ? null : view.GetComponentInChildren<MoonProductionView>(true);
                Log($"RevealBuilt(view={Name(view)}, belt={belt}) productionView={(pv == null ? "none" : pv.GetIl2CppType().Name + " id=" + pv.id)}");
            }

            // MoonBuildReveal has no Awake/Start in this build; Play(root, up, finished) is its static entry point.
            [HarmonyPatch(typeof(MoonBuildReveal), nameof(MoonBuildReveal.Play)), HarmonyPostfix]
            static void RevealPlay(GameObject root, MoonBuildReveal __result) => T($"MoonBuildReveal.Play(root={Name(root)}) -> {(__result == null ? "null" : "reveal")}");

            [HarmonyPatch(typeof(MoonBuildReveal), nameof(MoonBuildReveal.Begin)), HarmonyPostfix]
            static void RevealBegin(MoonBuildReveal __instance, bool __result) => T($"MoonBuildReveal.Begin(go={Name(__instance.gameObject)}) -> {__result}");

            [HarmonyPatch(typeof(MoonDepotConstruction), nameof(MoonDepotConstruction.Awake)), HarmonyPostfix]
            static void ConstructionAwake(MoonDepotConstruction __instance) => T($"MoonDepotConstruction.Awake(go='{__instance.gameObject.name}')");

            [HarmonyPatch(typeof(MoonDepotConstruction), nameof(MoonDepotConstruction.RestoreExistingViews)), HarmonyPrefix]
            static void RestoreExisting(MoonDepotConstruction __instance) => T($"MoonDepotConstruction.RestoreExistingViews(propViews={__instance.propViews?.Count}, depots={__instance.world?.depots?.Count})");

            [HarmonyPatch(typeof(MoonDepotConstruction), nameof(MoonDepotConstruction.Spawn)), HarmonyPostfix]
            static void Spawn(MoonDepotConstruction __instance, MoonDepotSnapshot data, bool __result)
            {
                if (data == null || !(WorldProbe.IsProbe(data.id) || Want("Spawn"))) return;
                GameObject v = null;
                try { v = __instance.ViewOf(data.id); } catch { }
                Log($"MoonDepotConstruction.Spawn(id={data.id}, kind={data.kind}) -> {__result} view={Name(v)} pending={__instance.PendingSpawns}");
            }

            [HarmonyPatch(typeof(MoonDepotConstruction), nameof(MoonDepotConstruction.Keep)), HarmonyPrefix]
            static void Keep(int id, GameObject root) { if (WorldProbe.IsProbe(id) || Want("Keep")) Log($"MoonDepotConstruction.Keep(id={id}, root={Name(root)})"); }

            [HarmonyPatch(typeof(MoonProductionView), nameof(MoonProductionView.Setup)), HarmonyPostfix]
            static void Setup(MoonProductionView __instance, int id)
            {
                if (!(WorldProbe.IsProbe(id) || Want("Setup"))) return;
                var root = __instance.transform.root;
                Log($"MoonProductionView.Setup(id={id}) type={__instance.GetIl2CppType().Name} go='{__instance.gameObject.name}' root='{(root == null ? "" : root.name)}'");
            }
        }

        // Facts (c) and (d): the cycle mass rule and whether output needs an outfeed belt.
        static class Production
        {
            static bool finishLogged, emitLogged;

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.NewProduction)), HarmonyPostfix]
            static void NewProduction(int id, int kind, MoonProductionState __result) => T($"NewProduction(id={id}, kind={kind}) -> stores={__result?.stores?.Length}");

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryStartCycle)), HarmonyPrefix]
            static void TryStartCycle(MoonProductionState s)
            {
                // Called every frame for every machine; only the calls with something in the machine are interesting.
                if (s?.stores == null || s.Stored(0) <= 0) return;
                T($"TryStartCycle(id={s.Id}, kind={s.Kind}) running={s.Running} stores={WorldProbe.Stores(s)}");
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.FinishCycle)), HarmonyPrefix]
            static void FinishCyclePre(MoonProductionState s)
            {
                finishLogged = s != null && Want("FinishCycle");
                if (finishLogged) Log($"FinishCycle pre id={s.Id} kind={s.Kind} cycles={s.Cycles} progress={s.Progress:F3} stores={WorldProbe.Stores(s)}");
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.FinishCycle)), HarmonyPostfix]
            static void FinishCyclePost(MoonProductionState s)
            {
                if (finishLogged) Log($"FinishCycle post id={s.Id} kind={s.Kind} cycles={s.Cycles} progress={s.Progress:F3} stores={WorldProbe.Stores(s)}");
                finishLogged = false;
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.EmitProduct)), HarmonyPrefix]
            static void EmitProductPre(MoonProductionState s, MoonWorld.ProcessorSpec spec)
            {
                // Called on a timer even when the output slot is empty; log only when there is product to emit.
                emitLogged = s != null && spec != null && s.Stored(spec.output) > 0 && Want("EmitProduct");
                if (emitLogged) Log($"EmitProduct pre id={s.Id} kind={s.Kind} outputSlot={spec?.output} stores={WorldProbe.Stores(s)}");
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.EmitProduct)), HarmonyPostfix]
            static void EmitProductPost(MoonProductionState s)
            {
                if (emitLogged) Log($"EmitProduct post id={s.Id} stores={WorldProbe.Stores(s)} status='{s.Status}'");
                emitLogged = false;
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.EmitCargo)), HarmonyPostfix]
            static void EmitCargo(MoonConveyorSnapshot outfeed, string owner, bool __result, MoonCargo cargo, double kg, string reason) =>
                T($"EmitCargo(outfeed={(outfeed == null ? "null" : "belt " + outfeed.id)}, owner={WorldProbe.Id(owner)}, cargo={cargo}, kg={kg}) -> {__result} '{reason}'");

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.CrewProcessorLoad)), HarmonyPrefix]
            static void ProcessorLoad(int machine, MoonCargo cargo, double kilograms) => T($"CrewProcessorLoad(machine={machine}, cargo={cargo}, kg={kilograms})");
        }

        // Fact (b): how power nodes are built and what SolvePower reads and writes per node. Sampled: logs when the node
        // count changes, otherwise every 15 s, 20 samples at most.
        static class Power
        {
            static int lastCount = -1, logs;
            static float lastAt = -100f;
            static bool sampling, specWarned;
            static IntPtr firstPtr;
            internal static int experimentId = -1, experimentCheck = -1;

            static bool Sample(MoonWorld w)
            {
                if (logs >= 20) return false;
                int c = w.powerNodes?.Count ?? -1;
                if (c == lastCount && Time.unscaledTime - lastAt < 15f) return false;
                lastCount = c;
                lastAt = Time.unscaledTime;
                logs++;
                return true;
            }

            static string Node(MoonWorld.PowerNode n)
            {
                if (n == null) return "null";
                var st = n.state;
                string spec = "";
                if (n.panel && Plugin.ProbeSpec.Value)
                {
                    if (!specWarned) { specWarned = true; Plugin.L.LogInfo("[trace] SolvePower reading node.spec (first time)"); }
                    var sp = n.spec;
                    spec = $" spec='{sp.name}' peakKw={sp.peakKw} batteryKwh={sp.batteryKwh}";
                }
                return $"{{id={n.record?.id} kind={n.record?.kind} panel={n.panel} parent={n.parent}{spec} sun={st?.SunFactor:F3} supplied={st?.SuppliedKw:F2} battery={st?.BatteryKw:F2} charge={st?.ChargeKwh:F4} demand={st?.DemandKw:F2} used={st?.UsedKw:F2} power={st?.Power:F3} grid={st?.Grid}}}";
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.SolvePower)), HarmonyPrefix]
            static void SolvePowerPre(MoonWorld __instance, float dt)
            {
                sampling = Sample(__instance) || experimentId >= 0;
                if (!sampling) return;
                var nodes = __instance.powerNodes;
                if (experimentId >= 0)
                {
                    // Does SolvePower read state.SunFactor, or work it out itself? Plant a value and see what comes out.
                    var st = __instance.ProductionOf(experimentId);
                    if (st != null)
                    {
                        Log($"SolvePower experiment: id={experimentId} sun={st.SunFactor:F3} used={st.UsedKw:F2} -> setting SunFactor=0.250");
                        st.SunFactor = 0.25f;
                    }
                    experimentCheck = experimentId;
                    experimentId = -1;
                }
                firstPtr = nodes != null && nodes.Count > 0 ? nodes.get_Item(0).Pointer : IntPtr.Zero;
                Log($"SolvePower pre dt={dt:F4} nodes={nodes?.Count} pool={__instance.nodePool?.Count} first={(nodes != null && nodes.Count > 0 ? Node(nodes.get_Item(0)) : "none")}");
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.SolvePower)), HarmonyPostfix]
            static void SolvePowerPost(MoonWorld __instance)
            {
                if (!sampling) return;
                sampling = false;
                var nodes = __instance.powerNodes;
                var parts = new List<string>();
                for (int i = 0; i < (nodes?.Count ?? 0) && i < 4; i++) parts.Add(Node(nodes.get_Item(i)));
                bool same = nodes != null && nodes.Count > 0 && nodes.get_Item(0).Pointer == firstPtr;
                Log($"SolvePower post nodes={nodes?.Count} pool={__instance.nodePool?.Count} grids={__instance.powerGrids?.Count} links={__instance.powerLinks?.Count} firstNodeSameObject={same} nodes=[{string.Join(" ", parts)}]");
                if (experimentCheck >= 0)
                {
                    var st = __instance.ProductionOf(experimentCheck);
                    Log($"SolvePower experiment result: id={experimentCheck} sun={st?.SunFactor:F3} used={st?.UsedKw:F2} supplied={st?.SuppliedKw:F2} battery={st?.BatteryKw:F2} (sun 0.250 / used ~1.88 = read from state; sun back near the real value = computed in SolvePower)");
                    experimentCheck = -1;
                }
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.PowerCandidates)), HarmonyPostfix]
            static void PowerCandidates(int kind, Vector3 position, Il2CppSystem.Collections.Generic.List<int> result) =>
                T($"PowerCandidates(kind={kind}, at={position}) -> {result?.Count} [{(result == null ? "" : string.Join(",", Enumerable.Range(0, Math.Min(result.Count, 8)).Select(i => result.get_Item(i))))}]");
        }

        // Do canisters keep their cargo through a save? MoonCanisterSnapshot has no cargo field in this build.
        static class Canisters
        {
            static string Live(MoonWorld w)
            {
                var c = w?.canisters;
                if (c == null) return "none";
                var byCargo = new Dictionary<MoonCargo, int>();
                var first = new List<string>();
                foreach (var kv in c)
                {
                    var can = kv.Value;
                    if (can == null) continue;
                    byCargo.TryGetValue(can.cargo, out int n);
                    byCargo[can.cargo] = n + 1;
                    if (first.Count < 10) first.Add($"#{can.id}:{can.cargo}:{can.kilograms:F1}kg");
                }
                return $"{c.Count} live ({string.Join(", ", byCargo.Select(p => p.Key + "=" + p.Value))}) [{string.Join(" ", first)}]";
            }

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.SnapshotCanisters)), HarmonyPostfix]
            static void Snapshot(MoonWorld __instance, MoonLooseDustSnapshot snapshot) => T($"SnapshotCanisters(snapshot={snapshot?.canisters?.Count}) world={Live(__instance)}");

            [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreCanisters)), HarmonyPostfix]
            static void Restore(MoonWorld __instance, MoonLooseDustSnapshot snapshot) => T($"RestoreCanisters(snapshot={snapshot?.canisters?.Count}) world={Live(__instance)}");
        }
    }
}
