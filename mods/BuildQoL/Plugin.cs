using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
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

        // Every tweak the mod ships. Order is the order they are bound, checked and patched.
        internal static readonly Tweak[] All =
        {
            new KeepSelectionTweak(),
            new ScrapBeltContentsTweak(),
        };

        public override void Load()
        {
            L = Log;
            foreach (var t in All) t.Bind(Config);
#if DEVBUILD
            DevTools.Bind(Config);
            foreach (var t in All) t.DevBind(Config);
#endif
            ClassInjector.RegisterTypeInIl2Cpp<Driver>();
            var h = new Harmony(Guid);
            var status = new List<string>();
            foreach (var t in All)
            {
                if (!t.Enabled.Value) { status.Add($"{t.Name}=off"); continue; }
                var missing = new List<string>();
                foreach (var m in t.Needed) { var p = m.Split(':'); if (!HasMember(p[0], p[1])) missing.Add(m); }
                if (missing.Count > 0)
                {
                    L.LogError($"Game members {t.Name} relies on are missing (game update?): {string.Join(", ", missing)}. {t.Name} is off.");
                    status.Add($"{t.Name}=missing-members");
                    continue;
                }
                try
                {
                    foreach (var patch in t.Patches) h.CreateClassProcessor(patch).Patch();
                    t.Active = true;
                    status.Add($"{t.Name}=on");
                }
                catch (Exception e)
                {
                    L.LogError($"Patching {t.Name} failed, the tweak is off: " + e);
                    status.Add($"{t.Name}=patch-failed");
                }
            }
#if DEVBUILD
            DevTools.Patch(h);
            foreach (var t in All) t.DevPatch(h);
#endif
            AddComponent<Driver>();
            L.LogInfo($"Build QoL {Version} loaded ({string.Join(", ", status)}).");
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
            foreach (var t in Plugin.All)
            {
                if (!t.Active) continue;
                try { t.Update(); } catch (Exception e) { Plugin.Once("update-" + t.Name, $"{t.Name}: Update threw: " + e); }
            }
        }

        void LateUpdate()
        {
            foreach (var t in Plugin.All)
            {
                if (!t.Active) continue;
                try { t.LateUpdate(); } catch (Exception e) { Plugin.Once("late-" + t.Name, $"{t.Name}: LateUpdate threw: " + e); }
            }
        }
    }
}
