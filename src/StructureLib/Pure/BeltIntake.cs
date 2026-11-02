using System;
using System.Collections.Generic;

namespace StructureLib
{
    /// <summary>
    /// What a modded machine does with a canister that has reached the end of a belt joined to one of its input ports:
    /// the first Input slot holding the canister's cargo with room takes as much as fits; otherwise the canister waits
    /// at the belt end with a refusal. Cargo ids are the <see cref="Cargo"/> handles' ids.
    /// </summary>
    public static class BeltIntake
    {
        const double Epsilon = 1e-9;

        /// <summary>The refusal when nothing in the machine takes this cargo (also used for a port of another cargo).</summary>
        public static string NotHere(string cargoId) => $"{cargoId} does not go in here";

        /// <summary>The refusal when every Input slot of the cargo is full.</summary>
        public const string Full = "full";

        /// <summary>
        /// The slot that takes the canister and the kilograms it takes (<c>min(canisterKg, room)</c>), with a null
        /// refusal; or slot -1, 0 kg and the refusal: <see cref="NotHere"/> when no Input slot holds the cargo,
        /// <see cref="Full"/> when none has room.
        /// </summary>
        public static (int Slot, double Kilograms, string Refusal) Decide(IReadOnlyList<(SlotRole Role, string CargoId, double Capacity, double Stored)> slots, string canisterCargoId, double canisterKg)
        {
            bool any = false;
            for (int i = 0; i < (slots?.Count ?? 0); i++)
            {
                var s = slots[i];
                if (s.Role != SlotRole.Input || !string.Equals(s.CargoId, canisterCargoId, StringComparison.Ordinal)) continue;
                any = true;
                double room = s.Capacity - s.Stored;
                if (room <= Epsilon) continue;
                return (i, Math.Min(canisterKg, room), null);
            }
            return (-1, 0, any ? Full : NotHere(canisterCargoId));
        }
    }
}
