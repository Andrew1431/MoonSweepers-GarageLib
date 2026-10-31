using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using MoonCleaners;
using MoonVehicleCore.Patches;
using UnityEngine;

namespace MoonVehicleCore
{
    [BepInPlugin(Guid, "Moon Vehicle Core", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.andrew1431.moonvehiclecore";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> VerboseLogging;

        public override void Load()
        {
            CoreLog.Source = Log;
            VerboseLogging = Config.Bind("Debug", "VerboseLogging", true, "Extra log lines while the core is in early development.");
            CoreLog.Verbose = VerboseLogging.Value;

            if (!MembersPresent()) { CoreLog.Error("Game members the core relies on are missing (game update?). Moon Vehicle Core is disabled."); return; }

            ClassInjector.RegisterTypeInIl2Cpp<VehicleController>();
            ClassInjector.RegisterTypeInIl2Cpp<CoreDriver>();
#if DEVBUILD
            DevTools.Bind(Config);
#endif
            var h = new Harmony(Guid);
            int ok = 0, failed = 0;
            foreach (var t in new[] { typeof(CatalogPatches), typeof(SeatPatches), typeof(GarageMenuPatches), typeof(GarageTraces),
                                      typeof(ModelPatches), typeof(ControllerPatches), typeof(PurchasePatches),
                                      typeof(UpgradeRulePatches), typeof(SavePatches) })
            {
                try { h.CreateClassProcessor(t).Patch(); ok++; CoreLog.Debug($"Patched {t.Name}"); }
                catch (Exception e) { failed++; CoreLog.Error($"Patching {t.Name} failed: {e}"); }
            }
            AddComponent<CoreDriver>();
            CoreLog.Info($"Moon Vehicle Core {Version} loaded ({ok} patch groups, {failed} failed). Vehicles register from other plugins' Load().");
        }

        // A game update that renames these would make the core half-work; check up front instead.
        static bool MembersPresent()
        {
            string[] needed =
            {
                "MoonCleaners.MoonPrototypeCatalog:Vehicles", "MoonCleaners.MoonWorld:GarageOffers", "MoonCleaners.MoonWorld:GarageVehicleMask",
                "MoonCleaners.MoonWorld:TryBuyVehicle", "MoonCleaners.MoonWorld:RestoreOwnedVehicles", "MoonCleaners.MoonOwnedVehicleSave:Write",
                "MoonCleaners.MoonPrototypeModels:Vehicle", "MoonCleaners.MoonPrototypeVehicle:Initialize", "MoonCleaners.MoonWorld:VehicleUpgradeLevel",
            };
            bool all = true;
            foreach (var n in needed)
            {
                var parts = n.Split(':');
                var t = typeof(MoonWorld).Assembly.GetType(parts[0]);
                bool found = t != null && t.GetMember(parts[1], BindingFlags).Length > 0;
                if (!found) { CoreLog.Error("Missing game member " + n); all = false; }
            }
            return all;
        }

        const System.Reflection.BindingFlags BindingFlags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
    }

    // Keeps the static tables extended and the world reference fresh.
    public class CoreDriver : MonoBehaviour
    {
        public CoreDriver(IntPtr ptr) : base(ptr) { }
        float nextEnsure;
        bool wasInGame;

        void Update()
        {
            try
            {
                if (Time.unscaledTime >= nextEnsure)
                {
                    nextEnsure = Time.unscaledTime + 3f;
                    Injection.Ensure();
                }
                bool inGame = MoonSession.InGame;
                if (inGame != wasInGame)
                {
                    wasInGame = inGame;
                    CoreLog.Info(inGame ? "Session in game" : "Session left game");
                    if (inGame) { CoreState.FindWorld(); Injection.Ensure(); }
                    else CoreState.World = null;
                }
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
