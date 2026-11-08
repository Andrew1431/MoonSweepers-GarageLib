using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using MoonCleaners;
using StructureLib.Patches;
using UnityEngine;

namespace StructureLib
{
    [BepInPlugin(Guid, "StructureLib", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.ryanhartwig.structurelib";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> VerboseLogging;

        // A game update that renames these would make the library half-work; check up front instead.
        static readonly string[] NeededMembers =
        {
            "MoonCleaners.MoonWorld:BuildDefinition", "MoonCleaners.MoonWorld:RestoreDepots", "MoonCleaners.MoonWorld:TryBuildDepot",
            "MoonCleaners.MoonWorld:IsProcessorId", "MoonCleaners.MoonWorld:IsPowerNode", "MoonCleaners.MoonWorld:Processor",
            "MoonCleaners.MoonWorld:InputSlot", "MoonCleaners.MoonWorld:CargoName", "MoonCleaners.MoonWorld:CargoValue",
            "MoonCleaners.MoonWorld:CargoShortName", "MoonCleaners.MoonWorld:UnitKilograms", "MoonCleaners.MoonWorld:RestoreCanisters",
            "MoonCleaners.MoonWorld:RestoreProduction", "MoonCleaners.MoonWorld:MoveCanisterRecord", "MoonCleaners.MoonWorld:RemoveCanister",
            "MoonCleaners.MoonProgressStore:Serialize", "MoonCleaners.MoonLooseDustSnapshot:cargo",
            "MoonCleaners.MoonDepotSave:Write", "MoonCleaners.MoonProductionSave:Write", "MoonCleaners.MoonLooseDustSnapshot:production",
            "MoonCleaners.MoonWorld:AcceptsText", "MoonCleaners.MoonWorld:FinishCycle", "MoonCleaners.MoonWorld:EmitCargo", "MoonCleaners.MoonWorld:ProductionOf",
            "MoonCleaners.MoonWorld:CanStartCycle", "MoonCleaners.MoonWorld:TryStartCycle", "MoonCleaners.MoonWorld:CrewProcessorLoad", "MoonCleaners.MoonWorld:SolvePower",
            "MoonCleaners.MoonWorld:powerNodes", "MoonCleaners.MoonWorld:powerGrids", "MoonCleaners.MoonWorld:FootprintDistance", "MoonCleaners.MoonWorld:Put", "MoonCleaners.MoonWorld:Move",
            "MoonCleaners.MoonWorld:EmitProduct", "MoonCleaners.MoonWorld:IsSolarPanel", "MoonCleaners.MoonWorld:PowerRange", "MoonCleaners.MoonWorld:PostPanel",
            "MoonCleaners.MoonDepotBuilder:baseStructures", "MoonCleaners.MoonDepotBuilder:catalogCategories",
            "MoonCleaners.MoonDepotBuilder:BuildCatalog", "MoonCleaners.MoonDepotBuilder:CardArt", "MoonCleaners.MoonDepotBuilder:ProductionNote",
            "MoonCleaners.MoonDepotConstruction:Spawn", "MoonCleaners.MoonDepotConstruction:ViewOf", "MoonCleaners.MoonWorld:RemoveStructureNow",
            "MoonCleaners.MoonWorld:PortCargo", "MoonCleaners.MoonDepotBuildDefinition:conveyorPorts",
            "MoonCleaners.MoonWorld:HandOff", "MoonCleaners.MoonWorld:MachineWithPort", "MoonCleaners.MoonWorld:canisters", "MoonCleaners.MoonProductionState:Flow",
            "MoonCleaners.MoonConveyorSnapshot:start", "MoonCleaners.MoonConveyorSnapshot:id",
            "MoonCleaners.MoonWorld:EntranceClear", "MoonCleaners.MoonWorld:WaitingAtEnd", "MoonCleaners.MoonWorld:BeltStartingAt", "MoonCleaners.MoonWorld:BeltEndingAt",
            "MoonCleaners.MoonDepotBuildDefinition:halfFootprint", "MoonCleaners.MoonWorld:nextCanisterId", "MoonCleaners.MoonWorld:riders",
            // The leaf tables (Patches/LeafTables.cs) and the game domain they are bounded to.
            "MoonCleaners.MoonWorld:HexPieceKind", "MoonCleaners.MoonWorld:IsRobotDock", "MoonCleaners.MoonWorld:MapKindOf",
            "MoonCleaners.MoonDepotSave:MaxStructureKind", "MoonCleaners.MoonDepotBuilder:ConveyorKind",
            // The look-at panel (Patches/PanelPatches.cs).
            "MoonCleaners.MoonProductionInteraction:ShowPanel", "MoonCleaners.MoonProductionInteraction:Aimed", "MoonCleaners.MoonProductionInteraction:look",
            "MoonCleaners.MoonLookPanel:Show", "MoonCleaners.MoonLookPanel:SetRow", "MoonCleaners.MoonLookPanel:Rows", "MoonCleaners.MoonLookPanel:Hide",
            // The power arcs (PowerLinksView.cs).
            "MoonCleaners.MoonPowerLinks:.ctor(MoonWorld,Transform)", "MoonCleaners.MoonPowerLinks:Begin", "MoonCleaners.MoonPowerLinks:Arc",
            "MoonCleaners.MoonPowerLinks:Ring", "MoonCleaners.MoonPowerLinks:End", "MoonCleaners.MoonPowerLinks:Clear", "MoonCleaners.MoonPowerLinks:Dispose",
            "MoonCleaners.MoonWorld:PowerAnchor", "MoonCleaners.MoonDepotBuilder:HoldingBuildTool", "MoonCleaners.MoonProductionInteraction:builder",
            "MoonCleaners.MoonDepotBuilder:structurePlan", "MoonCleaners.MoonDepotBuilder:selectedBuildId", "MoonCleaners.MoonWorld+StructurePlan:position",
            // The power grid (Patches/PowerPatches.cs, Sun.cs).
            "MoonCleaners.MoonSolarSystem:Daylight", "MoonCleaners.MoonSolarSystem:SunDirectionAt", "MoonCleaners.MoonSolarSystem:LunarTime",
            "MoonCleaners.MoonWorld:solarSystem", "MoonCleaners.MoonWorld:NewProduction", "MoonCleaners.MoonWorld:StallPower",
            // The canister tint (Patches/CanisterPatches.cs).
            "MoonCleaners.MoonDustCanisterView:ShowCargo", "MoonCleaners.MoonDustCanisterView:properties", "MoonCleaners.MoonDustCanisterView:dustRenderer",
        };

        public override void Load()
        {
            CoreLog.Source = Log;
            VerboseLogging = Config.Bind("Debug", "VerboseLogging", true, "Extra log lines while the library is in early development.");
            CoreLog.Verbose = VerboseLogging.Value;
#if DEVBUILD
            DevTools.Bind(Config);
#endif

            var missing = MemberCheck.Missing(NeededMembers, HasMember);
            if (missing.Count > 0)
            {
                CoreLog.Error($"Game members StructureLib relies on are missing (game update?): {string.Join(", ", missing)}. StructureLib is disabled.");
                return;
            }

            ClassInjector.RegisterTypeInIl2Cpp<CoreDriver>();
            // Before any patch: the short native leaves' prefixes answer vanilla kinds from these tables and never call
            // the original (calling it through the detour crashes; see Patches/LeafTables.cs).
            LeafTables.Snapshot();
            var h = new Harmony(Guid);
            int ok = 0, failed = 0;
            foreach (var t in new[] { typeof(FreezePatches), typeof(DefinitionPatches), typeof(PowerPatches), typeof(CatalogPatches), typeof(ModelPatches), typeof(BuildPatches), typeof(SavePatches), typeof(CargoPatches), typeof(ProductionPatches), typeof(HandshakePatches), typeof(PortPatches), typeof(BeltPatches), typeof(PanelPatches), typeof(CanisterPatches) })
            {
                try { h.CreateClassProcessor(t).Patch(); ok++; CoreLog.Debug($"Patched {t.Name}"); }
                catch (Exception e) { failed++; CoreLog.Error($"Patching {t.Name} failed: {e}"); }
            }
            if (failed > 0)
            {
                // A half-patched library could strip a record on save and never restore it, or the reverse. The registry
                // never freezes while Disabled, so no modded kind or cargo exists and every guarded patch stands down.
                MoonStructures.Disabled = true;
                CoreLog.Error($"StructureLib is disabled: {failed} of {ok + failed} patch groups failed to apply (each logged above); no modded structures or cargo this session.");
            }
            CoreDriver.Instance = AddComponent<CoreDriver>();
            CoreLog.Info($"StructureLib {Version} loaded ({ok} patch groups, {failed} failed). Structures register from other plugins' Load().");
        }

        // Fields, properties (interop exposes game statics as properties) and methods, public or not, static or instance.
        // ".ctor(A,B)" is an instance constructor with exactly those parameter type names (every interop type has a
        // .ctor(IntPtr), so a bare ".ctor" would prove nothing).
        static bool HasMember(string typeName, string member)
        {
            var t = typeof(MoonWorld).Assembly.GetType(typeName);
            if (t == null) return false;
            if (member.StartsWith(".ctor(", StringComparison.Ordinal) && member.EndsWith(")", StringComparison.Ordinal))
            {
                var want = member.Substring(6, member.Length - 7).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var ps = c.GetParameters();
                    if (ps.Length != want.Length) continue;
                    bool match = true;
                    for (int i = 0; i < ps.Length && match; i++) match = ps[i].ParameterType.Name == want[i];
                    if (match) return true;
                }
                return false;
            }
            return t.GetMember(member, MemberTypes.Field | MemberTypes.Property | MemberTypes.Method, AllMembers).Length > 0;
        }

        const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    }

    // Keeps the world reference fresh and drives the dev tools.
    public class CoreDriver : MonoBehaviour
    {
        public CoreDriver(IntPtr ptr) : base(ptr) { }
        internal static CoreDriver Instance;
        float nextEnsure;
        bool wasInGame;

        void Update()
        {
            try
            {
                Sidecar.NoteMainThread();
                if (Time.unscaledTime >= nextEnsure)
                {
                    nextEnsure = Time.unscaledTime + 3f;
                    if (CoreState.World != null) Injection.Ensure(CoreState.World);
                }
                bool inGame = MoonSession.InGame;
                if (inGame != wasInGame)
                {
                    wasInGame = inGame;
                    CoreLog.Info(inGame ? "Session in game" : "Session left game");
                    if (inGame)
                    {
                        Sidecar.BeginSession();
                        var world = CoreState.FindWorld();
                        if (world != null) Injection.Ensure(world);
                        IconRenderer.QueueAll();
                    }
                    else
                    {
                        // The next session must not save into this one's folder, nor match its snapshot by address.
                        CoreState.World = null;
                        CoreState.SaveRoot = null;
                        Sidecar.EndSession();
                        PowerLinksView.Reset();
                    }
                }
                // Its own guard: a throw here must not stop the card renders that follow.
                try { StructureMaterials.Upgrade(); }
                catch (Exception e) { CoreLog.Once("materials-upgrade", "Upgrading model materials threw: " + e, 3); }
                IconRenderer.Tick();
                // Its own guard too: a throw every frame here must not starve the handshake, the dev tools or Ensure.
                try { PowerLinksView.Tick(); }
                catch (Exception e) { CoreLog.Once("power-links-tick", "PowerLinksView.Tick threw: " + e, 3); }
                Handshake.Tick();
#if DEVBUILD
                DevTools.Tick();
#endif
            }
            catch (Exception e)
            {
                CoreLog.Once("driver", "CoreDriver.Update: " + e, 3);
                nextEnsure = Time.unscaledTime + 10f;
            }
        }
    }
}
