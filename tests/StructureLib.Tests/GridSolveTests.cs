using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace StructureLib.Tests
{
    public class GridSolveTests
    {
        const float Dt = 0.25f;

        static SourceIn Solar(int id, float peak, float sun) => new(id, true, peak, sun, 0);
        static SourceIn Fuel(int id, float peak, double charge) => new(id, false, peak, 0f, charge);
        static StorageIn Bank(int id, double charge, float capacity, float rate) => new(id, charge, capacity, rate);
        static ConsumerIn Load(int id, float demand, float vanilla = 0f) => new(id, demand, vanilla);

        static GridResult Run(SourceIn[] s, StorageIn[] b, ConsumerIn[] c, float stall = 0f) => GridSolve.Solve(s, b, c, Dt, stall);

        [Fact]
        public void SolarCoversDemand_FuelIdle()
        {
            var r = Run(new[] { Solar(1, 10, 1), Fuel(2, 10, 6) }, new StorageIn[0], new[] { Load(1, 8) });
            Assert.Equal(8f, r.SolarKw, 1e-3);
            Assert.Equal(0f, r.FuelKw, 1e-3);
            Assert.Equal(1f, r.Satisfaction, 1e-3);
            Assert.Equal(0f, r.SourceKw[1], 1e-3);
        }

        // A fuel source supplies PeakKw while its processor is Running: the postfix passes ChargeKwh = +infinity then.
        [Fact] public void Available_UnboundedCharge_IsPeak() => Assert.Equal(12f, GridSolve.AvailableKw(12, double.PositiveInfinity, Dt));

        [Fact]
        public void RunningFuel_CoversTheDeficit_NotMore()
        {
            var r = Run(new[] { Fuel(1, 12, double.PositiveInfinity), Fuel(2, 12, 0) }, new StorageIn[0], new[] { Load(1, 1), Load(2, 10) });
            Assert.Equal(11f, r.FuelKw, 1e-3);
            Assert.Equal(11f, r.SourceKw[0], 1e-3);
            Assert.Equal(0f, r.SourceKw[1], 1e-3);
            Assert.Equal(1f, r.Satisfaction, 1e-3);
            Assert.False(float.IsNaN(r.ConsumerPower[0]) || float.IsInfinity(r.ConsumerUsedKw[1]));
        }

        [Fact]
        public void SolarSurplusChargesBank()
        {
            var r = Run(new[] { Solar(1, 10, 1) }, new[] { Bank(1, 0, 100, 10) }, new[] { Load(1, 8) });
            Assert.Equal(-2f, r.StorageKw, 1e-3);
            Assert.Equal(2.0 * Dt / 3600.0, r.StorageDeltaKwh[0], 1e-9);
        }

        [Fact]
        public void SurplusCappedByRate()
        {
            var r = Run(new[] { Solar(1, 20, 1) }, new[] { Bank(1, 0, 100, 10) }, new[] { Load(1, 0) });
            Assert.Equal(10.0 * Dt / 3600.0, r.StorageDeltaKwh[0], 1e-9);
            Assert.True(r.StorageDeltaKwh[0] >= 0);
            Assert.Equal(-10f, r.StorageKw, 1e-3);
        }

        [Fact]
        public void FullBankTakesNothing()
        {
            var r = Run(new[] { Solar(1, 10, 1) }, new[] { Bank(1, 100, 100, 10) }, new[] { Load(1, 4) });
            Assert.Equal(0.0, r.StorageDeltaKwh[0]);
            Assert.Equal(0f, r.StorageKw, 1e-3);
        }

        [Fact]
        public void NightBankCarriesLoad()
        {
            var r = Run(new[] { Solar(1, 10, 0) }, new[] { Bank(1, 5, 100, 10) }, new[] { Load(1, 8) });
            Assert.Equal(8f, r.StorageKw, 1e-3);
            Assert.Equal(1f, r.Satisfaction, 1e-3);
            Assert.Equal(-8.0 * Dt / 3600.0, r.StorageDeltaKwh[0], 1e-9);
        }

        [Fact]
        public void FuelCoversOnlyDeficit()
        {
            var r = Run(new[] { Solar(1, 10, 0), Fuel(2, 10, 6), Fuel(3, 10, 6) }, new StorageIn[0], new[] { Load(1, 12) });
            Assert.Equal(6f, r.SourceKw[1], 1e-3);
            Assert.Equal(6f, r.SourceKw[2], 1e-3);
            Assert.Equal(12f, r.FuelKw, 1e-3);
        }

        [Fact]
        public void EvenShareWhenShort()
        {
            var r = Run(new[] { Solar(1, 5, 1) }, new StorageIn[0], new[] { Load(1, 6), Load(2, 4) });
            Assert.Equal(0.5f, r.Satisfaction, 1e-3);
            Assert.Equal(0.5f, r.ConsumerPower[0], 1e-3);
            Assert.Equal(0.5f, r.ConsumerPower[1], 1e-3);
            Assert.Equal(5f, r.UnmetDemandKw, 1e-3);
        }

        [Fact]
        public void Review5_VanillaPartlyPowered()
        {
            // Demand 10, vanilla covered 0.4 -> unmet 6; solar 3 -> satisfaction 0.5 -> 0.4 + 0.6 * 0.5 = 0.7.
            var r = Run(new[] { Solar(1, 3, 1) }, new StorageIn[0], new[] { Load(1, 10, 0.4f) });
            Assert.Equal(0.5f, r.Satisfaction, 1e-3);
            Assert.Equal(0.7f, r.ConsumerPower[0], 1e-3);
            Assert.Equal(7f, r.ConsumerUsedKw[0], 1e-3);
        }

        [Fact]
        public void Review2_ZeroDemandLiveGrid_IsOne()
        {
            var r = Run(new[] { Solar(1, 10, 1) }, new StorageIn[0], new[] { Load(1, 0) });
            Assert.Equal(1f, r.ConsumerPower[0]);
            Assert.Equal(1f, r.Satisfaction);
        }

        [Fact]
        public void Review2_ZeroDemandDeadGrid_IsZero()
        {
            var r = Run(new[] { Solar(1, 10, 0), Fuel(2, 10, 0) }, new[] { Bank(1, 0, 10, 5) }, new[] { Load(1, 0) });
            Assert.Equal(0f, r.ConsumerPower[0]);
            Assert.Equal(0f, r.Satisfaction);
        }

        [Fact]
        public void Review2_SatisfactionAtStall_Reported()
        {
            var r = Run(new[] { Solar(1, 0.8f, 1) }, new StorageIn[0], new[] { Load(1, 10) }, stall: 0.08f);
            Assert.Equal(0.08f, r.Satisfaction, 1e-6);
            Assert.Equal(0.08f, r.ConsumerPower[0], 1e-6);
        }

        [Fact]
        public void Split_Proportional()
        {
            var t = GridSolve.Split(20f, new float[] { 10f, 30f });
            Assert.Equal(5f, t[0], 1e-4);
            Assert.Equal(15f, t[1], 1e-4);
        }

        public static IEnumerable<object[]> ConservationCases()
        {
            // sources, banks, consumers
            yield return new object[] { new[] { Solar(1, 10, 1) }, new[] { Bank(1, 1, 5, 4) }, new[] { Load(1, 8) } };
            yield return new object[] { new[] { Solar(1, 10, 0.3f), Fuel(2, 6, 2) }, new[] { Bank(1, 0.5, 5, 4) }, new[] { Load(1, 8), Load(2, 4, 0.5f) } };
            yield return new object[] { new[] { Solar(1, 10, 0) }, new[] { Bank(1, 3, 5, 2), Bank(2, 1, 5, 6) }, new[] { Load(1, 9) } };
            yield return new object[] { new[] { Solar(1, 20, 1) }, new[] { Bank(1, 0, 5, 3), Bank(2, 4.9, 5, 6) }, new[] { Load(1, 2) } };
            yield return new object[] { new[] { Fuel(1, 5, 0.001), Fuel(2, 5, 9) }, new StorageIn[0], new[] { Load(1, 12), Load(2, 3, 1f) } };
            yield return new object[] { new SourceIn[0], new StorageIn[0], new[] { Load(1, 5, 0.2f) } };
            var rng = new Random(12345);
            var s = Enumerable.Range(0, 4).Select(i => i % 2 == 0 ? Solar(i, (float)rng.NextDouble() * 20, (float)rng.NextDouble()) : Fuel(i, (float)rng.NextDouble() * 10, rng.NextDouble() * 0.05)).ToArray();
            var b = Enumerable.Range(0, 3).Select(i => Bank(i, rng.NextDouble() * 5, 5, (float)rng.NextDouble() * 8)).ToArray();
            var c = Enumerable.Range(0, 5).Select(i => Load(i, (float)rng.NextDouble() * 10, (float)rng.NextDouble() * 0.5f)).ToArray();
            yield return new object[] { s, b, c };
        }

        [Theory]
        [MemberData(nameof(ConservationCases))]
        public void Conservation(SourceIn[] sources, StorageIn[] banks, ConsumerIn[] consumers)
        {
            var r = GridSolve.Solve(sources, banks, consumers, Dt, 0f);
            float used = r.ConsumerUsedKw.Sum();
            float vanilla = consumers.Sum(c => c.DemandKw * c.VanillaPower);
            float discharge = Math.Max(0f, r.StorageKw);
            Assert.Equal(used - vanilla, r.SolarKw + r.FuelKw + discharge, 1e-3);

            float solar = sources.Where(x => x.Solar).Sum(x => x.PeakKw * x.SunFactor);
            float charge = Math.Max(0f, -r.StorageKw);
            Assert.True(charge <= solar - r.SolarKw + 1e-3f);
            Assert.True(r.SolarKw <= solar + 1e-3f);
            Assert.All(r.ConsumerPower, p => Assert.InRange(p, 0f, 1f + 1e-6f));
            for (int i = 0; i < banks.Length; i++)
            {
                double after = banks[i].ChargeKwh + r.StorageDeltaKwh[i];
                Assert.InRange(after, -1e-9, banks[i].CapacityKwh + 1e-9);
            }
        }
    }
}
