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
    // solar panels, conveyors) and cargo. Nothing here changes game state unless [Dev] AutoStartSlot is set.
    [BepInPlugin("com.ryanhartwig.ms.structurespike", "Structure Spike", "0.0.1")]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource L;
        internal static ConfigEntry<int> AutoStartSlot;

        public override void Load()
        {
            L = Log;
            AutoStartSlot = Config.Bind("Dev", "AutoStartSlot", -1, "Start the game on this save-slot index from the main menu so the world-time dump runs. -1 = off. Refuses a slot that already has a save.");
            ClassInjector.RegisterTypeInIl2Cpp<SpikeDriver>();
            AddComponent<SpikeDriver>();
            var h = new Harmony("com.ryanhartwig.ms.structurespike");
            try { h.PatchAll(typeof(Traces)); } catch (Exception e) { L.LogError("PatchAll: " + e); }
            L.LogInfo("Structure spike loaded");
        }
    }

    public class SpikeDriver : MonoBehaviour
    {
        public SpikeDriver(IntPtr ptr) : base(ptr) { }
        float t, inGameT;
        bool menuDumped, started, worldDumped;

        void Update()
        {
            try { Tick(); } catch (Exception e) { Plugin.L.LogError("Tick: " + e); menuDumped = started = worldDumped = true; }
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
            if (menuDumped && !started && t > 9f && MoonSession.InMenu && Plugin.AutoStartSlot.Value >= 0)
            {
                started = true;
                AutoStart(Plugin.AutoStartSlot.Value);
            }
            if (MoonSession.InGame && !worldDumped)
            {
                inGameT += Time.unscaledDeltaTime;
                if (inGameT > 20f)
                {
                    worldDumped = true;
                    Run("world", WorldDump.All);
                }
            }
        }

        static void Run(string name, Action a)
        {
            try { a(); } catch (Exception e) { Plugin.L.LogError($"[{name}] {e}"); }
        }

        // Starts a fresh game in the given slot. Refuses a slot that is already used so no existing save is touched.
        static void AutoStart(int slot)
        {
            var L = Plugin.L;
            bool used = true;
            try { used = MoonSaveSlots.Used(slot); } catch (Exception e) { L.LogWarning($"[autostart] Used({slot}) threw {e.GetType().Name}; refusing"); return; }
            if (used) { L.LogWarning($"[autostart] slot {slot} already has a save; refusing to start on it"); return; }
            MoonSaveSlots.Active = slot;
            L.LogInfo($"[autostart] starting a new game in slot {slot} ({Safe(() => MoonSaveSlots.SlotFolder(slot))})");
            // MoonSession.StartShift() alone does nothing from the menu (tried: no load, no save folder). The menu's own
            // play path is what boots a world: pick the slot, then PlayChosen().
            var menu = UnityEngine.Object.FindFirstObjectByType<MoonMainMenu>();
            if (menu == null) { L.LogWarning("[autostart] no MoonMainMenu instance; falling back to MoonSession.StartShift()"); MoonSession.StartShift(); return; }
            menu.chosenSlot = slot;
            try { menu.ClickSlot(slot); } catch (Exception e) { L.LogWarning("[autostart] ClickSlot threw " + e.GetType().Name); }
            menu.PlayChosen();
            L.LogInfo($"[autostart] PlayChosen() called: Loading={MoonSession.Loading} InMenu={MoonSession.InMenu} starting={menu.starting} active={MoonSaveSlots.Active}");
        }

        internal static string Safe(Func<string> f) { try { return f(); } catch (Exception e) { return "ERR:" + e.GetType().Name; } }
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

    // Bounded call traces: which game paths touch kinds, definitions, production and the save, and in what order.
    static class Traces
    {
        static readonly Dictionary<string, int> counts = new();
        static void T(string what, int limit = 12)
        {
            string key = what.Split('(')[0];
            counts.TryGetValue(key, out int n);
            if (n >= limit) return;
            counts[key] = n + 1;
            Plugin.L.LogInfo("[trace] " + what);
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.BuildDefinition)), HarmonyPostfix]
        static void BuildDefinition(int kind, MoonDepotBuildDefinition __result) => T($"BuildDefinition(kind={kind}) -> '{__result?.name}'", 40);

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryBuildDepot)), HarmonyPostfix]
        static void TryBuildDepot(bool __result, int kind, string reason) => T($"TryBuildDepot(kind={kind}) -> {__result} reason='{reason}'");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.ValidateDepotPlacement)), HarmonyPostfix]
        static void Validate(bool __result, int kind, string reason) => T($"ValidateDepotPlacement(kind={kind}) -> {__result} '{reason}'", 6);

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.NewProduction)), HarmonyPostfix]
        static void NewProduction(int id, int kind, MoonProductionState __result) => T($"NewProduction(id={id}, kind={kind}) -> stores={__result?.stores?.Length}");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.EmitCargo)), HarmonyPostfix]
        static void EmitCargo(bool __result, MoonCargo cargo, double kg, string reason) => T($"EmitCargo(cargo={cargo}, kg={kg}) -> {__result} '{reason}'");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryStartCycle)), HarmonyPrefix]
        static void TryStartCycle(MoonProductionState s) => T($"TryStartCycle(id={s?.Id}, kind={s?.Kind})", 6);

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.FinishCycle)), HarmonyPrefix]
        static void FinishCycle(MoonProductionState s) => T($"FinishCycle(id={s?.Id}, kind={s?.Kind})", 6);

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.CrewProcessorLoad)), HarmonyPrefix]
        static void ProcessorLoad(int machine, MoonCargo cargo, double kilograms) => T($"CrewProcessorLoad(machine={machine}, cargo={cargo}, kg={kilograms})");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.SolvePower)), HarmonyPostfix]
        static void SolvePower(MoonWorld __instance) => T($"SolvePower(nodes={__instance.powerNodes?.Count}, grids={__instance.powerGrids?.Count})", 3);

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreDepots)), HarmonyPrefix]
        static void RestoreDepots(MoonLooseDustSnapshot snapshot) => T($"RestoreDepots(depots={snapshot?.depots?.Count}, nextDepotId={snapshot?.nextDepotId})");

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
}
