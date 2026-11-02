using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using MoonCleaners;
using UnityEngine;

namespace SharedUpgrades
{
    public enum ScaleMode { Highest, Vehicle, Fixed }

    [BepInPlugin(Guid, "Shared Upgrades", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.andrew1431.sharedupgrades";
        public const string Version = "0.1.0";
        internal const string LobbyKey = "sharedupgrades";

        internal static ManualLogSource L;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<ScaleMode> Scale;
        internal static ConfigEntry<float> FixedScale, PriceMultiplier;

        public override void Load()
        {
            L = Log;
            Enabled = Config.Bind("General", "Enabled", true,
                "Share vehicle upgrade levels across the team. False: the mod only logs the current levels and changes nothing.");
            Scale = Config.Bind("Pricing", "ScaleMode", ScaleMode.Highest,
                "What a shared level costs. Highest: base price x the largest upgrade price scale among the team's owned vehicles " +
                "that can take the upgrade. Vehicle: vanilla, the scale of the vehicle you buy it on. Fixed: base price x FixedScale.");
            FixedScale = Config.Bind("Pricing", "FixedScale", 2.5f, "Price scale for ScaleMode = Fixed (vanilla scales: push 0.5, ride-on 1, crew 2, lunar 2.5, mega 3).");
            PriceMultiplier = Config.Bind("Pricing", "PriceMultiplier", 1f, "Multiplies every shared upgrade price after the scale.");
#if DEVBUILD
            DevTest.Bind(Config);
#endif
            ClassInjector.RegisterTypeInIl2Cpp<Driver>();
            if (Enabled.Value)
            {
                var h = new Harmony(Guid);
                try { h.CreateClassProcessor(typeof(PricePatches)).Patch(); }
                catch (Exception e) { L.LogError("Patching prices failed, shared levels still apply: " + e); }
            }
            AddComponent<Driver>();
            L.LogInfo($"Shared Upgrades {Version} loaded (enabled={Enabled.Value}, pricing={Scale.Value} x{PriceMultiplier.Value}).");
        }
    }

    public class Driver : MonoBehaviour
    {
        public Driver(IntPtr ptr) : base(ptr) { }
        float nextCheck, nextLobby;

        void Update()
        {
            try { Tick(); } catch (Exception e) { Plugin.L.LogError("Tick: " + e); nextCheck = Time.unscaledTime + 10f; }
        }

        void Tick()
        {
#if DEVBUILD
            DevTest.Tick();
#endif
            if (!MoonSession.InGame) { Shared.Reset(); return; }
            float now = Time.unscaledTime;
            if (now >= nextLobby) { nextLobby = now + 5f; Lobby.Refresh(); }
            if (now < nextCheck) return;
            nextCheck = now + 0.5f;
            var w = Shared.FindWorld();
            if (w != null) Shared.Check(w);
        }
    }
}
