using System;
using System.Collections.Generic;
using System.Linq;

namespace StructureLib
{
    /// <summary>What a processor slot does: <c>Input</c> is fed by the player, <c>Batch</c> is consumed per cycle, <c>Output</c> is produced.</summary>
    public enum SlotRole { Input, Batch, Output }

    /// <summary>The bits of a donor build definition the rules care about. The caller reads them from the game.</summary>
    public readonly record struct DonorTraits(bool Exists, bool Deck, bool BuildingPart, bool CatwalkSupport, bool WorldGrid);

    /// <summary>
    /// Validation rules applied when the registry freezes. Every method returns readable sentences that name the
    /// structure or cargo id between single quotes (an empty list or null means the input is fine).
    /// </summary>
    public static class Rules
    {
        /// <summary>Lowest cargo value a mod may use; 0..2 are the game's dust, sand and stone.</summary>
        public const int FirstModdedCargoValue = 3;

        public static IReadOnlyList<string> CheckCargo(IEnumerable<(string Id, byte Value)> cargo)
        {
            var items = cargo.ToList();
            var errors = new List<string>();

            foreach (var (id, value) in items)
                if (value < FirstModdedCargoValue)
                    errors.Add($"Cargo '{id}' uses value {value}; modded cargo values must be in the range 3..255 (0..2 are dust, sand and stone).");

            foreach (var group in items.GroupBy(c => c.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
                errors.Add($"Cargo id '{group.Key}' is registered {group.Count()} times.");

            foreach (var group in items.Where(c => c.Value >= FirstModdedCargoValue).GroupBy(c => c.Value).Where(g => g.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() > 1))
            {
                var ids = group.Select(c => $"'{c.Id}'").Distinct(StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal);
                errors.Add($"Cargo {string.Join(" and ", ids)} share the value {group.Key}; each cargo needs its own value.");
            }
            return errors;
        }

        /// <summary>
        /// The cargo ids named by <see cref="CheckCargo"/> errors (bad value, duplicate id, shared value), so callers can
        /// drop them without parsing the error text.
        /// </summary>
        public static ISet<string> CargoOffenders(IEnumerable<(string Id, byte Value)> cargo)
        {
            var items = cargo.ToList();
            var offenders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (id, value) in items)
                if (value < FirstModdedCargoValue) offenders.Add(id);
            foreach (var group in items.GroupBy(c => c.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
                offenders.Add(group.Key);
            foreach (var group in items.Where(c => c.Value >= FirstModdedCargoValue).GroupBy(c => c.Value))
            {
                var ids = group.Select(c => c.Id).Distinct(StringComparer.Ordinal).ToList();
                if (ids.Count > 1) offenders.UnionWith(ids);
            }
            return offenders;
        }

        /// <summary>
        /// Checks the cargo of a structure's slots. A slot is bad when its handle is invalid (a rejected registration),
        /// or when its cargo id is neither vanilla (dust, sand, stone) nor accepted by <paramref name="cargoRegistered"/>.
        /// </summary>
        public static IReadOnlyList<string> CheckSlotCargo(string id, IEnumerable<(string SlotName, string CargoId, bool CargoValid)> slots, Func<string, bool> cargoRegistered)
        {
            var errors = new List<string>();
            foreach (var (slotName, cargoId, valid) in slots)
            {
                if (!valid)
                    errors.Add($"Structure '{id}': slot '{slotName}' has no valid cargo (Cargo.None, from a rejected RegisterCargo?).");
                else if (!IsVanillaCargo(cargoId) && !cargoRegistered(cargoId))
                    errors.Add($"Structure '{id}': slot '{slotName}' uses cargo '{cargoId}', which is not registered.");
            }
            return errors;
        }

        static bool IsVanillaCargo(string cargoId) => cargoId == "dust" || cargoId == "sand" || cargoId == "stone";

        public static IReadOnlyList<string> CheckProcessor(string id, IReadOnlyList<(SlotRole Role, double Capacity)> slots, bool producesPower, int maxSlots = 8)
        {
            var errors = new List<string>();
            if (slots.Count > maxSlots)
                errors.Add($"Structure '{id}' has {slots.Count} slots; the game supports at most {maxSlots}.");
            if (!slots.Any(s => s.Role == SlotRole.Input))
                errors.Add($"Structure '{id}' needs at least one Input slot.");
            if (!slots.Any(s => s.Role == SlotRole.Batch))
                errors.Add($"Structure '{id}' needs at least one Batch slot.");

            int outputs = slots.Count(s => s.Role == SlotRole.Output);
            if (producesPower)
            {
                if (outputs != 0)
                    errors.Add($"Structure '{id}' produces power, so it must not have an Output slot (it has {outputs}).");
            }
            else if (outputs != 1)
            {
                errors.Add($"Structure '{id}' needs exactly one Output slot (it has {outputs}).");
            }
            return errors;
        }

        public static string CheckDonor(string id, int baseKind, DonorTraits traits)
        {
            if (!traits.Exists)
                return $"Structure '{id}': base structure {baseKind} has no definition.";
            string flag =
                traits.Deck ? "Deck" :
                traits.BuildingPart ? "BuildingPart" :
                traits.CatwalkSupport ? "CatwalkSupport" :
                traits.WorldGrid ? "WorldGrid" : null;
            if (flag != null)
                return $"Structure '{id}': base structure {baseKind} is a {flag} foundation; foundations and decks are not supported in this milestone.";
            return null;
        }
    }
}
