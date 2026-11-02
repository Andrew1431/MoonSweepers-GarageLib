using BepInEx.Configuration;
using MoonCleaners;

namespace GarageLib
{
    // Gameplay fixes to vanilla rules. ECO suction only narrowed the strip (0.8x width at NORMAL's speed), so nobody
    // used it; here it trades that width for speed instead.
    internal static class Tweaks
    {
        static ConfigEntry<float> ecoSpeed;

        internal static void Bind(ConfigFile config)
        {
            ecoSpeed = config.Bind("Tweaks", "EcoSuctionSpeed", 1.3f,
                "Top-speed multiplier while sweeping on ECO suction (vanilla 1.0, same as NORMAL). ECO still sweeps 0.8x width.");
        }

        // Called with Injection.Ensure every few seconds: cheap, and survives the game rebuilding the table.
        internal static void Ensure()
        {
            var speeds = MoonVehicleUpgrades.SuctionSpeed;
            if (speeds == null || speeds.Length == 0) return;
            float want = ecoSpeed?.Value ?? 1f;
            if (System.Math.Abs(speeds[0] - want) < 0.0001f) return;
            float was = speeds[0];
            speeds[0] = want;
            CoreLog.Info($"ECO suction speed {was:F2} -> {MoonVehicleUpgrades.SuctionSpeed[0]:F2}");
        }
    }
}
