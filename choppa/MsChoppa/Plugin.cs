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
        internal static ConfigEntry<float> Gravity, Scale;
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
            L.LogInfo($"Ms Choppa {Version} loaded");
        }

        void BindConfig()
        {
            const string f = "Flight", c = "Controls";
            // Moon defaults: low gravity and little air, so everything is floatier than Big Choppa's Earth tuning.
            Flight.MaxLiftG = Config.Bind(f, "MaxLiftG", 2.2f, "Lift at full collective as a multiple of moon gravity.").Value;
            Flight.ControlResponse = Config.Bind(f, "ControlResponse", 3.5f, "How quickly the airframe reaches the commanded rotation. Higher = twitchier.").Value;
            Flight.AutoLevel = Config.Bind(f, "AutoLevel", 0.25f, "Self-levelling towards the moon's up. 0 = holds whatever attitude you set.").Value;
            Flight.Weathervane = Config.Bind(f, "Weathervane", 0.3f, "How strongly the tail swings the nose into the direction of travel.").Value;
            Flight.LinearDrag = Config.Bind(f, "LinearDrag", 0.05f, "Low-speed drag in all directions (the moon has no air, so keep it low for floatiness).").Value;
            Flight.ForwardDrag = Config.Bind(f, "ForwardDrag", 0.001f, "Quadratic drag along the nose. Lower = higher top speed.").Value;
            Flight.SideDrag = Config.Bind(f, "SideDrag", 0.006f, "Quadratic drag sideways.").Value;
            Flight.VerticalDrag = Config.Bind(f, "VerticalDrag", 0.01f, "Quadratic drag up/down.").Value;
            Flight.SpinUpSeconds = Config.Bind(f, "SpinUpSeconds", 3f, "Rotor spin up/down time when the pilot gets in or out.").Value;
            Flight.YawRate = Config.Bind(f, "YawRate", 45f, "Yaw speed at full pedal (deg/s).").Value;
            Flight.MaxPitchRollRate = Config.Bind(f, "MaxPitchRollRate", 60f, "Cap on pitch/roll rotation speed (deg/s).").Value;
            Flight.CollectiveRate = Config.Bind(f, "CollectiveRate", 0.45f, "How fast holding collective keys moves it (fraction per second).").Value;
            Flight.CollectiveReturnRate = Config.Bind(f, "CollectiveReturnRate", 0.8f, "How fast the collective springs back to hover when released.").Value;
            Gravity = Config.Bind(f, "Gravity", 0f, "Gravity in m/s² the choppa feels. 0 = auto (the game's own moon gravity, else 1.62).");

            Flight.MouseSensitivity = Config.Bind(c, "MouseSensitivity", 0.3f, "Degrees of pitch/roll per unit of mouse movement.").Value;
            Flight.InvertPitch = Config.Bind(c, "InvertPitch", false, "Mouse forward = nose up instead of down.").Value;
            Flight.InvertRoll = Config.Bind(c, "InvertRoll", false, "Swap mouse roll direction.").Value;
            CollectiveUp = Config.Bind(c, "CollectiveUp", Key.W, "Raise collective.");
            CollectiveDown = Config.Bind(c, "CollectiveDown", Key.S, "Lower collective.");
            PedalLeft = Config.Bind(c, "PedalLeft", Key.A, "Yaw left.");
            PedalRight = Config.Bind(c, "PedalRight", Key.D, "Yaw right.");
            FreeLook = Config.Bind(c, "FreeLook", Key.LeftAlt, "Hold to stop the mouse tilting the choppa.");

            Scale = Config.Bind("Model", "Scale", 0.75f, "Model scale (applies to the next spawn).");
#if DEVBUILD
            DevSelfTest = Config.Bind("Dev", "SelfTest", false, "Dev build: fly the first spawned choppa unpiloted (spin up, climb, hover, descend) and log it.");
#endif
            VerboseLogging = Config.Bind("Debug", "VerboseLogging", true, "Log flight state every few seconds while piloting.");
        }
    }
}
