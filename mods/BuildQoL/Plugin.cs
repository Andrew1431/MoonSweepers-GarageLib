using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using MoonCleaners;
using UnityEngine;

namespace BuildQoL
{
    [BepInPlugin(Guid, "Build QoL", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.ryanhartwig.buildqol";
        public const string Version = "0.1.0";

        internal static ManualLogSource L;
        internal static ConfigEntry<bool> KeepSelection;

        // Every game member the tweak touches. A game update that renames one turns the tweak off instead of half-working.
        static readonly string[] NeededMembers =
        {
            "MoonCleaners.MoonDepotBuilder:NoteBuilt", "MoonCleaners.MoonDepotBuilder:MachineBuilt",
            "MoonCleaners.MoonDepotBuilder:SelectStructure", "MoonCleaners.MoonDepotBuilder:BeginPlacement",
            "MoonCleaners.MoonDepotBuilder:CanBuild", "MoonCleaners.MoonDepotBuilder:ConveyorKind",
            "MoonCleaners.MoonDepotBuilder:selectedStructure", "MoonCleaners.MoonDepotBuilder:yaw",
            "MoonCleaners.MoonDepotBuilder:IsPlacing", "MoonCleaners.MoonDepotBuilder:ConveyorMode", "MoonCleaners.MoonDepotBuilder:noticeUntil",
        };

        public override void Load()
        {
            L = Log;
            KeepSelection = Config.Bind("Tweaks", "KeepSelection", true,
                "After placing a structure with the build tool, keep that structure selected with a new ghost ready for the next copy " +
                "(rotation kept). Belts, and anything you cannot afford another of, behave as in the base game. False: base-game behaviour, nothing is patched.");
#if DEVBUILD
            DevTools.Bind(Config);
#endif
            ClassInjector.RegisterTypeInIl2Cpp<Driver>();
            var h = new Harmony(Guid);
            bool patched = false;
            if (KeepSelection.Value)
            {
                var missing = new List<string>();
                foreach (var m in NeededMembers) { var p = m.Split(':'); if (!HasMember(p[0], p[1])) missing.Add(m); }
                if (missing.Count > 0)
                    L.LogError($"Game members KeepSelection relies on are missing (game update?): {string.Join(", ", missing)}. KeepSelection is off.");
                else
                {
                    try { h.CreateClassProcessor(typeof(KeepSelectionPatches)).Patch(); patched = true; }
                    catch (Exception e) { L.LogError("Patching KeepSelection failed, the tweak is off: " + e); }
                }
            }
#if DEVBUILD
            DevTools.Patch(h);
#endif
            AddComponent<Driver>();
            L.LogInfo($"Build QoL {Version} loaded (KeepSelection={KeepSelection.Value}, patched={patched}).");
        }

        static readonly HashSet<string> logged = new();

        /// <summary>LogError the first time a key is seen, silent after.</summary>
        internal static void Once(string key, string message)
        {
            if (logged.Add(key)) L.LogError(message + " (logged once)");
        }

        // Fields, properties (interop exposes game fields and statics as properties) and methods, public or not.
        internal static bool HasMember(string typeName, string member)
        {
            var t = typeof(MoonWorld).Assembly.GetType(typeName);
            if (t == null) return false;
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            return t.GetMember(member, MemberTypes.Field | MemberTypes.Property | MemberTypes.Method, all).Length > 0;
        }
    }

    public class Driver : MonoBehaviour
    {
        public Driver(IntPtr ptr) : base(ptr) { }

        void Update()
        {
#if DEVBUILD
            try { DevTools.Tick(); } catch (Exception e) { Plugin.Once("dev-tick", "[dev] Tick threw: " + e); }
#endif
        }
    }
}
