using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StructureLib
{
    /// <summary>
    /// Rules for routers (compact belt splitters and mergers): definition and port validation, and the round-robin
    /// and merge-priority selection. Pure: no Unity, no game types.
    /// </summary>
    public static class RouterRules
    {
        /// <summary>
        /// Errors for a router's definition: a router has no processor and no power; a footprint, on any structure that
        /// sets it, must be positive in both directions.
        /// </summary>
        public static IReadOnlyList<string> CheckDefinition(string id, bool hasRouter, bool hasProcessor, bool hasPower, (float x, float y)? footprint)
        {
            var errors = new List<string>();
            if (hasRouter && hasProcessor) errors.Add($"'{id}': a router has no processor.");
            if (hasRouter && hasPower) errors.Add($"'{id}': a router has no power.");
            if (footprint.HasValue && (!(footprint.Value.x > 0f) || !(footprint.Value.y > 0f)))
                errors.Add(string.Format(CultureInfo.InvariantCulture, "'{0}': footprint {1} x {2} must be positive.", id, footprint.Value.x, footprint.Value.y));
            return errors;
        }

        /// <summary>
        /// Errors for a router's ports: a duplicate name; a splitter needs exactly one input and one to three outputs;
        /// a merger one to three inputs and exactly one output.
        /// </summary>
        public static IReadOnlyList<string> CheckPorts(string id, RouterMode mode, IReadOnlyList<(string Name, bool Output)> ports)
        {
            var errors = new List<string>();
            ports ??= Array.Empty<(string, bool)>();

            foreach (var group in ports.GroupBy(p => p.Name ?? "", StringComparer.Ordinal).Where(g => g.Count() > 1))
                errors.Add($"'{id}': port '{group.Key}' is declared {group.Count()} times; port names must be unique.");

            int inputs = ports.Count(p => !p.Output), outputs = ports.Count(p => p.Output);
            if (mode == RouterMode.Splitter)
            {
                if (inputs != 1) errors.Add($"'{id}': a splitter has exactly one input port ({inputs} declared).");
                if (outputs < 1 || outputs > 3) errors.Add($"'{id}': a splitter has one to three output ports ({outputs} declared).");
            }
            else
            {
                if (inputs < 1 || inputs > 3) errors.Add($"'{id}': a merger has one to three input ports ({inputs} declared).");
                if (outputs != 1) errors.Add($"'{id}': a merger has exactly one output port ({outputs} declared).");
            }
            return errors;
        }

        /// <summary>
        /// The first output index at or after <paramref name="nextOut"/> (wrapping) that can take cargo, else -1.
        /// A <paramref name="nextOut"/> out of range is treated as 0; an empty list gives -1.
        /// </summary>
        public static int PickOutput(int nextOut, IReadOnlyList<bool> canTake)
        {
            int n = canTake?.Count ?? 0;
            if (n == 0) return -1;
            if (nextOut < 0 || nextOut >= n) nextOut = 0;
            for (int k = 0; k < n; k++)
            {
                int i = (nextOut + k) % n;
                if (canTake[i]) return i;
            }
            return -1;
        }

        /// <summary>
        /// The cargo id every router port carries: dust, which is what vanilla's own splitter ports answer to PortCargo.
        /// The port is written into the clone as <c>sl:0:&lt;Name&gt;</c>; the hop passes any cargo and never reads it.
        /// </summary>
        public const string PortCargoId = "dust";

        /// <summary>
        /// The round-robin index after index <paramref name="used"/> was served: <c>(used + 1) % count</c>; 0 when
        /// <paramref name="count"/> is not positive.
        /// </summary>
        public static int Advance(int used, int count) => count <= 0 ? 0 : (used + 1) % count;

        /// <summary>
        /// Whether a merger takes cargo from input <paramref name="input"/> now. False when the output is not clear;
        /// otherwise inputs are walked round-robin from <paramref name="turn"/>: the first one reached that is
        /// <paramref name="input"/> wins, an earlier one with cargo waiting makes it yield. An out-of-range input is false.
        /// </summary>
        public static bool MergerAccepts(int turn, int input, IReadOnlyList<bool> waiting, bool outputClear)
        {
            if (!outputClear) return false;
            int count = waiting?.Count ?? 0;
            if (input < 0 || input >= count) return false;
            if (turn < 0 || turn >= count) turn = 0;
            for (int k = 0; k < count; k++)
            {
                int j = (turn + k) % count;
                if (j == input) return true;
                if (waiting[j]) return false;
            }
            return false;
        }
    }

    /// <summary>What a router does with the belts at its ports.</summary>
    public enum RouterMode
    {
        /// <summary>One input, one to three outputs: cargo goes to each output in turn, skipping a blocked one.</summary>
        Splitter,
        /// <summary>One to three inputs, one output: inputs take turns, an earlier waiting input going first.</summary>
        Merger,
    }
}
