using System;

namespace StructureLib
{
    /// <summary>
    /// How a modded machine attached to a vanilla grid is powered, from what the grid reports after the vanilla solve:
    /// first from the sun surplus (supply the vanilla machines left unused), then from the grid's batteries, which S0 saw
    /// discharge at whatever rate the demand needed. Mirrors vanilla's machine accounting: satisfaction is the share of
    /// the demand covered; an idle machine reads 1 when the grid has any supply or charge, else 0.
    /// </summary>
    public static class GridShare
    {
        /// <summary>Satisfaction (0..1) and how many kW come from the sun surplus and from the batteries.</summary>
        public static (float Satisfaction, float FromSunKw, float FromBatteryKw) Draw(float demandKw, float sunSurplusKw, double storedKwh)
        {
            float surplus = Math.Max(0f, sunSurplusKw);
            bool charged = storedKwh > 0;
            if (demandKw <= 0f) return (surplus > 0f || charged ? 1f : 0f, 0f, 0f);
            float fromSun = Math.Min(demandKw, surplus);
            float fromBattery = charged ? demandKw - fromSun : 0f;
            return ((fromSun + fromBattery) / demandKw, fromSun, fromBattery);
        }
    }
}
