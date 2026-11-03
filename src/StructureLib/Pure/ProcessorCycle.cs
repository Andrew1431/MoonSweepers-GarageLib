using System;
using System.Collections.Generic;

namespace StructureLib
{
    /// <summary>
    /// The cycle of a modded processor. The game's own cycle logic only knows the pressure chamber and the stone press
    /// (for any other kind it never starts a cycle), so StructureLib runs a generic one: a cycle starts when every Batch
    /// slot is empty and can be filled to its capacity from the first Input slot holding the same cargo, and the Output
    /// slot has room for all of it; it finishes by moving the batch into the Output slot at the same mass (as the chamber
    /// does) or, for a power producer, by burning it.
    /// </summary>
    public static class ProcessorCycle
    {
        const double Epsilon = 1e-9;

        /// <summary>
        /// The (input slot, batch slot, kilograms) moves that start a cycle, in slot order; null when a cycle cannot start.
        /// <paramref name="stored"/> is the kilograms in each slot now.
        /// </summary>
        public static List<(int From, int To, double Kg)> StartMoves(IReadOnlyList<(SlotRole Role, byte Cargo, double Capacity)> slots, IReadOnlyList<double> stored, bool producesPower)
        {
            var moves = new List<(int From, int To, double Kg)>();
            var left = new double[slots.Count];
            for (int i = 0; i < slots.Count; i++) left[i] = stored[i];
            double total = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Role != SlotRole.Batch) continue;
                if (stored[i] > Epsilon) return null;
                int from = -1;
                for (int j = 0; j < slots.Count && from < 0; j++)
                    if (slots[j].Role == SlotRole.Input && slots[j].Cargo == slots[i].Cargo) from = j;
                double kg = slots[i].Capacity;
                if (from < 0 || left[from] + Epsilon < kg) return null;
                left[from] -= kg;
                total += kg;
                moves.Add((from, i, kg));
            }
            if (moves.Count == 0) return null;
            if (!producesPower)
            {
                var roles = new List<SlotRole>(slots.Count);
                foreach (var s in slots) roles.Add(s.Role);
                int output = ProcessorLayout.OutputIndex(roles, false);
                if (output < 0 || stored[output] + total > slots[output].Capacity + Epsilon) return null;
            }
            return moves;
        }

        /// <summary>Indexes of the Batch slots (what a finishing cycle empties).</summary>
        public static List<int> BatchSlots(IReadOnlyList<SlotRole> roles)
        {
            var batch = new List<int>();
            for (int i = 0; i < roles.Count; i++)
                if (roles[i] == SlotRole.Batch) batch.Add(i);
            return batch;
        }

        /// <summary>A fuel generator's stored charge after burning one batch: half its battery more, capped at the battery.</summary>
        public static double ChargeAfterBurn(double chargeKwh, double batteryKwh) => Math.Min(Math.Max(0, chargeKwh) + batteryKwh / 2, batteryKwh);

        /// <summary>False when a fuel generator's battery is full: it does not burn what it cannot store.</summary>
        public static bool CanBurn(double chargeKwh, double batteryKwh) => chargeKwh < batteryKwh;
    }
}
