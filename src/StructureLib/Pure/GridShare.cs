using System;

namespace StructureLib
{
    /// <summary>
    /// How a modded machine is powered, from what its grid reports after the vanilla solve: first from the sun surplus
    /// (supply the vanilla machines left unused), then from the stored charge of modded fuel generators in its network,
    /// then from the vanilla grid's batteries, which S0 saw discharge at whatever rate the demand needed. Mirrors
    /// vanilla's machine accounting: satisfaction is the share of the demand covered.
    /// </summary>
    public static class GridShare
    {
        /// <summary>
        /// Satisfaction (0..1) and the kW taken from the sun surplus, from fuel sources (at most <paramref name="fuelKw"/>;
        /// the pole grid passes 0, its fuel is pooled in GridSolve) and from the batteries. An idle machine reads 1 when any of the three has something to give, else 0.
        /// </summary>
        public static (float Satisfaction, float FromSunKw, float FromFuelKw, float FromBatteryKw) Draw(float demandKw, float sunSurplusKw, float fuelKw, double storedKwh)
        {
            float surplus = Math.Max(0f, sunSurplusKw), fuel = Math.Max(0f, fuelKw);
            bool charged = storedKwh > 0;
            if (demandKw <= 0f) return (surplus > 0f || fuel > 0f || charged ? 1f : 0f, 0f, 0f, 0f);
            float fromSun = Math.Min(demandKw, surplus);
            float fromFuel = Math.Min(demandKw - fromSun, fuel);
            float fromBattery = charged ? demandKw - fromSun - fromFuel : 0f;
            return ((fromSun + fromFuel + fromBattery) / demandKw, fromSun, fromFuel, fromBattery);
        }

        /// <summary>Share of a sun surplus that vanilla stores while charging (S0: 5.52 - 5.00 kW surplus charged 0.48 kW).</summary>
        public const float ChargeEfficiency = 0.92f;

        /// <summary>
        /// While the grid's batteries charge, vanilla has already stored the sun surplus (× <see cref="ChargeEfficiency"/>)
        /// that a modded machine then used. The kW to take back out of the batteries so the same energy is not counted
        /// twice: the stored share of what the machine took from the sun, at most what was charging.
        /// </summary>
        public static float ChargeTakenKw(float fromSunKw, float chargingKw) =>
            Math.Min(Math.Max(0f, fromSunKw) * ChargeEfficiency, Math.Max(0f, chargingKw));
    }
}
