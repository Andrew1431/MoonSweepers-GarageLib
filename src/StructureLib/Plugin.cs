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
            "MoonCleaners.MoonWorld:IsProcessor", "MoonCleaners.MoonWorld:IsPowerNode", "MoonCleaners.MoonWorld:Processor",
            "MoonCleaners.MoonWorld:InputSlot", "MoonCleaners.MoonWorld:CargoName", "MoonCleaners.MoonWorld:CargoValue",
            "MoonCleaners.MoonDepotSave:Write", "MoonCleaners.MoonProductionSave:Write",
            "MoonCleaners.MoonDepotBuilder:baseStructures", "MoonCleaners.MoonDepotBuilder:catalogCategories",
            "MoonCleaners.MoonDepotBuilder:BuildCatalog", "MoonCleaners.MoonDepotBuilder:CardArt", "MoonCleaners.MoonDepotBuilder:ProductionNote",
            "MoonCleaners.MoonDepotConstruction:Spawn", "MoonCleaners.MoonDepotConstruction:ViewOf", "MoonCleaners.MoonWorld:RemoveStructureNow",
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
            var h = new Harmony(Guid);
            int ok = 0, failed = 0;
            foreach (var t in new[] { typeof(FreezePatches), typeof(DefinitionPatches), typeof(KindRules), typeof(CatalogPatches), typeof(ModelPatches), typeof(BuildPatches), typeof(SavePatches) })
            {
                try { h.CreateClassProcessor(t).Patch(); ok++; CoreLog.Debug($"Patched {t.Name}"); }
                catch (Exception e) { failed++; CoreLog.Error($"Patching {t.Name} failed: {e}"); }
            }
            CoreDriver.Instance = AddComponent<CoreDriver>();
            CoreLog.Info($"StructureLib {Version} loaded ({ok} patch groups, {failed} failed). Structures register from other plugins' Load().");
        }

        // Fields, properties (interop exposes game statics as properties) and methods, public or not, static or instance.
        static bool HasMember(string typeName, string member)
        {
            var t = typeof(MoonWorld).Assembly.GetType(typeName);
            return t != null && t.GetMember(member, MemberTypes.Field | MemberTypes.Property | MemberTypes.Method, AllMembers).Length > 0;
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
                        var world = CoreState.FindWorld();
                        if (world != null) Injection.Ensure(world);
                        IconRenderer.QueueAll();
                    }
                    else CoreState.World = null;
                }
                IconRenderer.Tick();
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
