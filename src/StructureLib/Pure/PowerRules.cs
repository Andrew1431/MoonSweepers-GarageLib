using System.Collections.Generic;
using System.Globalization;

namespace StructureLib
{
    /// <summary>How a power source makes its power.</summary>
    public enum SupplyMode { Solar, Fuel }

    /// <summary>What a powered structure does on the grid.</summary>
    public enum PowerRole { Source, Relay, Storage }

    /// <summary>Rules for a structure's power definition. Pure: no Unity, no game types.</summary>
    public static class PowerRules
    {
        /// <summary>
        /// Errors for a power definition, empty when valid. A Fuel source needs PeakKw and a Processor with ProducesPower
        /// (a BatteryKwh it declares is ignored); a Solar source needs PeakKw and no Processor; a Relay needs LinkRange and a SupplyRadius no
        /// larger than it, and no Processor; Storage needs BatteryKwh and RateKw and no Processor. A Processor with
        /// ProducesPower is only valid on a Fuel source.
        /// </summary>
        public static IReadOnlyList<string> CheckDefinition(string id, PowerRole role, SupplyMode mode, float peakKw, float batteryKwh, float rateKw,
            float linkRange, float supplyRadius, bool hasProcessor, bool producesPower)
        {
            var errors = new List<string>();
            void Positive(string field, float value) { if (!(value > 0f)) errors.Add(string.Format(CultureInfo.InvariantCulture, "'{0}': {1} must be positive ({2}).", id, field, value)); }
            void NoProcessor(string what) { if (hasProcessor) errors.Add($"'{id}': {what} has no Processor."); }

            switch (role)
            {
                case PowerRole.Source when mode == SupplyMode.Fuel:
                    Positive("PeakKw", peakKw);
                    // No BatteryKwh rule: a fuel source has no kWh buffer in the grid solve. It supplies PeakKw while its
                    // processor is Running (a batch burns), which every peer sees, so a BatteryKwh it declares is ignored.
                    if (!hasProcessor) errors.Add($"'{id}': a fuel source needs a Processor with ProducesPower.");
                    else if (!producesPower) errors.Add($"'{id}': a fuel source's Processor needs ProducesPower.");
                    break;
                case PowerRole.Source:
                    Positive("PeakKw", peakKw);
                    NoProcessor("a solar source");
                    break;
                case PowerRole.Relay:
                    Positive("LinkRange", linkRange);
                    Positive("SupplyRadius", supplyRadius);
                    if (supplyRadius > linkRange)
                        errors.Add(string.Format(CultureInfo.InvariantCulture, "'{0}': SupplyRadius {1} must not exceed LinkRange {2}.", id, supplyRadius, linkRange));
                    NoProcessor("a relay");
                    break;
                case PowerRole.Storage:
                    Positive("BatteryKwh", batteryKwh);
                    Positive("RateKw", rateKw);
                    NoProcessor("a storage");
                    break;
            }

            if (producesPower && !(role == PowerRole.Source && mode == SupplyMode.Fuel))
                errors.Add($"'{id}': a Processor with ProducesPower needs a Fuel power source (Role Source, SupplyMode Fuel).");
            return errors;
        }
    }
}
