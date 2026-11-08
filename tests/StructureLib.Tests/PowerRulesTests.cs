using StructureLib;
using Xunit;

namespace StructureLib.Tests
{
    public class PowerRulesTests
    {
        // role, mode, peakKw, batteryKwh, rateKw, linkRange, supplyRadius, hasProcessor, producesPower, expected errors, field named in the first error
        [Theory]
        [InlineData(PowerRole.Source, SupplyMode.Fuel, 10f, 6f, 0f, 0f, 0f, true, true, 0, null)]
        [InlineData(PowerRole.Source, SupplyMode.Fuel, 0f, 6f, 0f, 0f, 0f, true, true, 1, "PeakKw")]
        [InlineData(PowerRole.Source, SupplyMode.Fuel, 10f, 0f, 0f, 0f, 0f, true, true, 1, "BatteryKwh")]
        [InlineData(PowerRole.Source, SupplyMode.Fuel, 10f, 6f, 0f, 0f, 0f, false, false, 1, "Processor")]
        [InlineData(PowerRole.Source, SupplyMode.Fuel, 10f, 6f, 0f, 0f, 0f, true, false, 1, "ProducesPower")]
        [InlineData(PowerRole.Source, SupplyMode.Solar, 5f, 0f, 0f, 0f, 0f, false, false, 0, null)]
        [InlineData(PowerRole.Source, SupplyMode.Solar, 0f, 0f, 0f, 0f, 0f, false, false, 1, "PeakKw")]
        [InlineData(PowerRole.Source, SupplyMode.Solar, 5f, 0f, 0f, 0f, 0f, true, false, 1, "Processor")]
        [InlineData(PowerRole.Relay, SupplyMode.Fuel, 0f, 0f, 0f, 24f, 12f, false, false, 0, null)]
        [InlineData(PowerRole.Relay, SupplyMode.Fuel, 0f, 0f, 0f, 0f, 12f, false, false, 2, "LinkRange")]
        [InlineData(PowerRole.Relay, SupplyMode.Fuel, 0f, 0f, 0f, 24f, 0f, false, false, 1, "SupplyRadius")]
        [InlineData(PowerRole.Relay, SupplyMode.Fuel, 0f, 0f, 0f, 24f, 12f, true, false, 1, "Processor")]
        [InlineData(PowerRole.Storage, SupplyMode.Fuel, 0f, 20f, 5f, 0f, 0f, false, false, 0, null)]
        [InlineData(PowerRole.Storage, SupplyMode.Fuel, 0f, 0f, 5f, 0f, 0f, false, false, 1, "BatteryKwh")]
        [InlineData(PowerRole.Storage, SupplyMode.Fuel, 0f, 20f, 0f, 0f, 0f, false, false, 1, "RateKw")]
        [InlineData(PowerRole.Storage, SupplyMode.Fuel, 0f, 20f, 5f, 0f, 0f, true, false, 1, "Processor")]
        public void CheckDefinition_Roles(PowerRole role, SupplyMode mode, float peak, float battery, float rate, float link, float radius,
            bool hasProcessor, bool produces, int errorCount, string field)
        {
            var e = PowerRules.CheckDefinition("mod.thing", role, mode, peak, battery, rate, link, radius, hasProcessor, produces);
            Assert.Equal(errorCount, e.Count);
            if (field != null) { Assert.Contains("mod.thing", e[0]); Assert.Contains(field, e[0]); }
        }

        [Theory]
        [InlineData(24f, 12f, 0)]
        [InlineData(24f, 24f, 0)]
        [InlineData(24f, 25f, 1)]
        public void Relay_SupplyRadiusAboveLinkRange_IsAnError(float link, float radius, int errorCount)
        {
            var e = PowerRules.CheckDefinition("r", PowerRole.Relay, SupplyMode.Fuel, 0f, 0f, 0f, link, radius, false, false);
            Assert.Equal(errorCount, e.Count);
            if (errorCount > 0) Assert.Contains("SupplyRadius", e[0]);
        }

        [Fact]
        public void PowerProducingProcessor_NeedsFuelSource()
        {
            // A Processor with ProducesPower on a non-Fuel-Source role (here a Relay with a Processor) is an error for the producer too.
            var e = PowerRules.CheckDefinition("p", PowerRole.Storage, SupplyMode.Fuel, 0f, 20f, 5f, 0f, 0f, true, true);
            Assert.Equal(2, e.Count);
            Assert.All(e, m => Assert.Contains("'p'", m));
            Assert.Contains(e, m => m.Contains("ProducesPower"));
        }
    }
}
