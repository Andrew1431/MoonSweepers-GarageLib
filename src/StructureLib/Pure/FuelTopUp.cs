using System;

namespace StructureLib
{
    /// <summary>
    /// A vanilla machine within a modded fuel generator's range. Vanilla's own solve has already given it what its grid
    /// has (sun and batteries, as <c>Power</c>, the share of its demand covered); fuel covers only the unmet rest, never
    /// more than the fuel the caller still has in the machine's network. The caller subtracts the drawn kW from that fuel
    /// before serving the next machine.
    /// </summary>
    public static class FuelTopUp
    {
        /// <summary>
        /// The machine's new satisfaction (0..1) and used kW, and the kW drawn from fuel: at most
        /// <c>demand × (1 − power)</c>, at most <paramref name="fuelKw"/>, never negative. A machine with no demand
        /// keeps its power and draws nothing.
        /// </summary>
        public static (float Power, float UsedKw, float DrawnKw) Draw(float vanillaPower, float demandKw, float fuelKw)
        {
            float power = Math.Clamp(vanillaPower, 0f, 1f);
            if (demandKw <= 0f) return (power, 0f, 0f);
            float drawn = Math.Min(demandKw * (1f - power), Math.Max(0f, fuelKw));
            float used = demandKw * power + drawn;
            return (Math.Min(1f, used / demandKw), used, drawn);
        }
    }
}
