using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using MoonCleaners;
using MoonVehicleCore;
using Rotorcraft;
using UnityEngine.InputSystem;

namespace MsChoppa
{
    [BepInPlugin(Guid, "Ms Choppa", Version)]
    [BepInDependency(MoonVehicleCore.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.andrew1431.mschoppa";
        public const string Version = "0.1.0";
        public const string VehicleId = "andrew1431.utility-choppa";

        internal static ManualLogSource L;
        internal static FlightSettings Flight = new();
        internal static ConfigEntry<float> Gravity, Scale, ChaseDistance, ChaseHeight;
        internal static ConfigEntry<Key> CollectiveUp, CollectiveDown, PedalLeft, PedalRight, FreeLook;
        internal static ConfigEntry<bool> VerboseLogging;
#if DEVBUILD
        internal static ConfigEntry<bool> DevSelfTest;
#endif

        public override void Load()
        {
            L = Log;
            BindConfig();
            MeshModel.Log = m => L.LogInfo(m);

            MoonVehicles.Register(new VehicleDefinition
            {
                Id = VehicleId,
                Name = "Utility Choppa",
                Summary = "A boxy utility helicopter: lift off from any garage and float over the moon with your crew. W/S collective, A/D pedals, mouse to tilt.",
                Price = 12000m,
                BaseVehicle = MoonPrototypeCatalog.CrewTransport,
                Work = MoonPrototypeCatalog.Work.Transport,
                Hint = "W/S collective | A/D pedals | mouse tilts",
                Controller = typeof(ChoppaController),
                DecorateModel = ChoppaModel.Decorate,
                UpgradeKinds = new[] { MoonVehicleUpgrades.Kind.Speed, MoonVehicleUpgrades.Kind.Lights },
            }, Guid);
            new HarmonyLib.Harmony(Guid).PatchAll(typeof(CameraPatch));
            L.LogInfo($"Ms Choppa {Version} loaded");
        }

        const int CurrentConfigVersion = 2;

        void BindConfig()
        {
            const string f = "Flight", c = "Controls";
            var version = Config.Bind("Internal", "ConfigVersion", 0, "Used to update old defaults. Don't edit.");
            int from = version.Value;
            // Big Choppa's tuning under a stronger-than-moon gravity: the real 1.62 m/s² made it float about aimlessly.
            var maxLift = Config.Bind(f, "MaxLiftG", 2.0f, "Lift at full collective as a multiple of Gravity. 2.0 = hover at about half collective.");
            var response = Config.Bind(f, "ControlResponse", 6f, "How quickly the airframe reaches the commanded rotation. Higher = twitchier.");
            var autoLevel = Config.Bind(f, "AutoLevel", 0f, "Self-levelling towards the moon's up. 0 = holds whatever attitude you set (Big Choppa).");
            var vane = Config.Bind(f, "Weathervane", 0.5f, "How strongly the tail swings the nose into the direction of travel.");
            var lin = Config.Bind(f, "LinearDrag", 0.15f, "Low-speed drag in all directions (stops endless drifting).");
            var fwd = Config.Bind(f, "ForwardDrag", 0.003f, "Quadratic drag along the nose. Lower = higher top speed.");
            var side = Config.Bind(f, "SideDrag", 0.02f, "Quadratic drag sideways.");
            var vert = Config.Bind(f, "VerticalDrag", 0.04f, "Quadratic drag up/down.");
            var spin = Config.Bind(f, "SpinUpSeconds", 3f, "Rotor spin up/down time when the pilot gets in or out.");
            var yaw = Config.Bind(f, "YawRate", 65f, "Yaw speed at full pedal (deg/s).");
            var maxRate = Config.Bind(f, "MaxPitchRollRate", 90f, "Cap on pitch/roll rotation speed (deg/s).");
            var colRate = Config.Bind(f, "CollectiveRate", 0.45f, "How fast holding collective keys moves it (fraction per second).");
            var colReturn = Config.Bind(f, "CollectiveReturnRate", 1.0f, "How fast the collective springs back to hover when released.");
            Gravity = Config.Bind(f, "Gravity", 6.5f, "Gravity in m/s² the choppa feels. 0 = the game's moon gravity (1.62, very floaty).");
            var smoothing = Config.Bind(c, "MouseSmoothing", 0f, "Seconds of smoothing on mouse cyclic input. 0 = none (Big Choppa).");

            if (from < 2)
            {
                Upgrade(maxLift, 2.2f); Upgrade(response, 3.5f); Upgrade(autoLevel, 0.25f); Upgrade(vane, 0.3f);
                Upgrade(lin, 0.05f); Upgrade(fwd, 0.001f); Upgrade(side, 0.006f); Upgrade(vert, 0.01f);
                Upgrade(yaw, 45f); Upgrade(maxRate, 60f); Upgrade(colReturn, 0.8f); Upgrade(Gravity, 0f);
            }
            if (from != CurrentConfigVersion) { L.LogInfo($"Config migrated from version {from} to {CurrentConfigVersion}"); version.Value = CurrentConfigVersion; }

            Flight.MaxLiftG = maxLift.Value; Flight.ControlResponse = response.Value; Flight.AutoLevel = autoLevel.Value;
            Flight.Weathervane = vane.Value; Flight.LinearDrag = lin.Value; Flight.ForwardDrag = fwd.Value;
            Flight.SideDrag = side.Value; Flight.VerticalDrag = vert.Value; Flight.SpinUpSeconds = spin.Value;
            Flight.YawRate = yaw.Value; Flight.MaxPitchRollRate = maxRate.Value; Flight.CollectiveRate = colRate.Value;
            Flight.CollectiveReturnRate = colReturn.Value; Flight.MouseSmoothing = smoothing.Value;

            Flight.MouseSensitivity = Config.Bind(c, "MouseSensitivity", 0.3f, "Degrees of pitch/roll per unit of mouse movement.").Value;
            Flight.InvertPitch = Config.Bind(c, "InvertPitch", false, "Mouse forward = nose up instead of down.").Value;
            Flight.InvertRoll = Config.Bind(c, "InvertRoll", false, "Swap mouse roll direction.").Value;
            CollectiveUp = Config.Bind(c, "CollectiveUp", Key.W, "Raise collective.");
            CollectiveDown = Config.Bind(c, "CollectiveDown", Key.S, "Lower collective.");
            PedalLeft = Config.Bind(c, "PedalLeft", Key.A, "Yaw left.");
            PedalRight = Config.Bind(c, "PedalRight", Key.D, "Yaw right.");
            FreeLook = Config.Bind(c, "FreeLook", Key.LeftAlt, "Hold to stop the mouse tilting the choppa.");

            Scale = Config.Bind("Model", "Scale", 0.75f, "Model scale (applies to the next spawn).");
            ChaseDistance = Config.Bind("Camera", "ChaseDistance", 14f, "Chase camera distance behind the choppa while piloting.");
            ChaseHeight = Config.Bind("Camera", "ChaseHeight", 4f, "Chase camera height above the choppa while piloting.");
#if DEVBUILD
            DevSelfTest = Config.Bind("Dev", "SelfTest", false, "Dev build: fly the first spawned choppa unpiloted (spin up, climb, hover, descend) and log it.");
#endif
            VerboseLogging = Config.Bind("Debug", "VerboseLogging", true, "Log flight state every few seconds while piloting.");
        }

        // Only rewrites values still at the old default, so players' own tweaks survive.
        static void Upgrade<T>(ConfigEntry<T> e, T oldDefault)
        {
            if (!Equals(e.Value, oldDefault)) return;
            e.Value = (T)e.DefaultValue;
            L.LogInfo($"Config: [{e.Definition.Section}] {e.Definition.Key} {oldDefault} -> {e.Value}");
        }
    }

    [HarmonyLib.HarmonyPatch]
    internal static class CameraPatch
    {
        [HarmonyLib.HarmonyPatch(typeof(MoonWalker), nameof(MoonWalker.LateUpdate)), HarmonyLib.HarmonyPostfix]
        static void AfterWalker(MoonWalker __instance)
        {
            if (ChoppaController.ByVehicle.Count == 0) return;
            var v = __instance.mountedVehicle;
            if (v == null || !ChoppaController.ByVehicle.TryGetValue(v.Pointer, out var c) || c == null || !c.CameraOverride) return;
            try { c.PlaceCamera(__instance); }
            catch (System.Exception e) { Plugin.L.LogError("Choppa camera: " + e); ChoppaController.ByVehicle.Remove(v.Pointer); }
        }
    }
}
