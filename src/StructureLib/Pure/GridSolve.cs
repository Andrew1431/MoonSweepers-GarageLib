using System;
using System.Collections.Generic;

namespace StructureLib
{
    /// <summary>A generator. Solar: <c>PeakKw × SunFactor</c> (sun 0..1). Fuel: up to <c>PeakKw</c> while <c>ChargeKwh</c> lasts.</summary>
    public readonly record struct SourceIn(int Id, bool Solar, float PeakKw, float SunFactor, double ChargeKwh);

    /// <summary>A battery bank: stored charge, capacity, and the most kW it can take or give.</summary>
    public readonly record struct StorageIn(int Id, double ChargeKwh, float CapacityKwh, float RateKw);

    /// <summary>A consumer. <c>VanillaPower</c> is the share of its demand vanilla's solve already covered (0 for modded machines with no vanilla panel).</summary>
    public readonly record struct ConsumerIn(int Id, float DemandKw, float VanillaPower);

    /// <summary>
    /// Outcome of one grid solve. <c>SolarKw</c> and <c>FuelKw</c> are what was delivered to the load (not what was produced);
    /// <c>StorageKw</c> is positive when the banks discharge into the grid and negative when they charge. Per-item arrays follow
    /// the input order; <c>StorageDeltaKwh</c> is the kWh change of each bank this step (positive = charged).
    /// </summary>
    public readonly record struct GridResult(float SolarKw, float StorageKw, float FuelKw, float DemandKw, float UnmetDemandKw, float Satisfaction,
                                             float[] SourceKw, double[] StorageDeltaKwh, float[] ConsumerPower, float[] ConsumerUsedKw);

    /// <summary>
    /// Factorio-style grid arithmetic: a grid pools its sources, spends solar first, charges banks with the solar surplus, then
    /// draws on banks and fuel, and gives every consumer the same satisfaction. Pure: no game types.
    /// </summary>
    public static class GridSolve
    {
        /// <summary>
        /// kW a fuel source can deliver in a solve of <paramref name="dt"/> seconds: its peak, at most what its stored charge
        /// lasts over the solve; 0 when it holds no charge. A charge of <c>double.PositiveInfinity</c> is unbounded: the peak
        /// (StructureLib passes it for a fuel source whose batch is burning).
        /// </summary>
        public static float AvailableKw(float peakKw, double chargeKwh, float dt)
        {
            if (chargeKwh <= 0 || peakKw <= 0f) return 0f;
            if (dt <= 0f) return peakKw;
            return (float)Math.Min(peakKw, chargeKwh * 3600.0 / dt);
        }

        /// <summary>Takes <paramref name="kw"/> (at most the total available) from the sources in proportion to what each has; returns kW per source.</summary>
        public static float[] Split(float kw, IReadOnlyList<float> availableKw)
        {
            var taken = new float[availableKw.Count];
            double total = 0;
            for (int i = 0; i < taken.Length; i++) total += Math.Max(0f, availableKw[i]);
            if (total <= 0 || kw <= 0f) return taken;
            double take = Math.Min(kw, total);
            for (int i = 0; i < taken.Length; i++) taken[i] = (float)(Math.Max(0f, availableKw[i]) * take / total);
            return taken;
        }

        /// <summary>
        /// Solves one grid for <paramref name="dt"/> seconds. Banks charge from the solar surplus in proportion to the kW each can
        /// accept (<c>min(RateKw, free capacity over dt)</c>); surplus beyond what they accept is not stored. They discharge in
        /// proportion to <c>min(RateKw, charge over dt)</c>. <paramref name="stallPower"/> is not applied: a satisfaction below it is
        /// reported as is and the game stalls on it.
        /// </summary>
        public static GridResult Solve(IReadOnlyList<SourceIn> sources, IReadOnlyList<StorageIn> storage, IReadOnlyList<ConsumerIn> consumers, float dt, float stallPower)
        {
            int ns = sources.Count, nb = storage.Count, nc = consumers.Count;

            float demand = 0f, deficit = 0f;
            for (int i = 0; i < nc; i++)
            {
                var c = consumers[i];
                demand += Math.Max(0f, c.DemandKw);
                deficit += Math.Max(0f, c.DemandKw) * (1f - Clamp01(c.VanillaPower));
            }

            // Sources: solar output, fuel availability.
            var solarOut = new float[ns];
            var fuelAvail = new float[ns];
            float solar = 0f;
            for (int i = 0; i < ns; i++)
            {
                var s = sources[i];
                if (s.Solar) { solarOut[i] = Math.Max(0f, s.PeakKw * Clamp01(s.SunFactor)); solar += solarOut[i]; }
                else fuelAvail[i] = AvailableKw(s.PeakKw, s.ChargeKwh, dt);
            }

            float fromSolar = Math.Min(deficit, solar);
            float surplus = solar - fromSolar;

            // Charge banks with the surplus.
            var delta = new double[nb];
            var accept = new float[nb];
            for (int i = 0; i < nb; i++)
            {
                var b = storage[i];
                double free = Math.Max(0.0, b.CapacityKwh - b.ChargeKwh);
                accept[i] = dt <= 0f ? 0f : (float)Math.Min(Math.Max(0f, b.RateKw), free * 3600.0 / dt);
            }
            var charged = Split(surplus, accept);
            float chargeKw = 0f;
            for (int i = 0; i < nb; i++) { chargeKw += charged[i]; delta[i] = charged[i] * (double)dt / 3600.0; }

            // Discharge banks for what solar left.
            var give = new float[nb];
            for (int i = 0; i < nb; i++)
            {
                var b = storage[i];
                give[i] = b.ChargeKwh <= 0 ? 0f : dt <= 0f ? Math.Max(0f, b.RateKw) : (float)Math.Min(Math.Max(0f, b.RateKw), b.ChargeKwh * 3600.0 / dt);
            }
            float remaining = deficit - fromSolar;
            var drawn = Split(remaining, give);
            float fromStorage = 0f;
            for (int i = 0; i < nb; i++) { fromStorage += drawn[i]; delta[i] -= drawn[i] * (double)dt / 3600.0; }

            // Fuel covers the rest.
            var fuelDrawn = Split(remaining - fromStorage, fuelAvail);
            float fromFuel = 0f;
            for (int i = 0; i < ns; i++) fromFuel += fuelDrawn[i];

            // Per-source output: solar shares what was delivered in proportion to its output.
            var sourceKw = new float[ns];
            var solarShare = Split(fromSolar, solarOut);
            for (int i = 0; i < ns; i++) sourceKw[i] = sources[i].Solar ? solarShare[i] : fuelDrawn[i];

            bool live = solar > 0f || fuelAvail.Length > 0 && Array.Exists(fuelAvail, a => a > 0f);
            for (int i = 0; i < nb && !live; i++) live = storage[i].ChargeKwh > 0;

            float delivered = fromSolar + fromStorage + fromFuel;
            float satisfaction = Clamp01(deficit > 0f ? delivered / deficit : live ? 1f : 0f);

            var power = new float[nc];
            var used = new float[nc];
            for (int i = 0; i < nc; i++)
            {
                var c = consumers[i];
                float v = Clamp01(c.VanillaPower);
                power[i] = c.DemandKw <= 0f ? (live ? 1f : 0f) : v + (1f - v) * (deficit > 0f ? satisfaction : 1f);
                used[i] = Math.Max(0f, c.DemandKw) * power[i];
            }

            return new GridResult(fromSolar, fromStorage - chargeKw, fromFuel, demand, deficit - delivered, satisfaction, sourceKw, delta, power, used);
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
