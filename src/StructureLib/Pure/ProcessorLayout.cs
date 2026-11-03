using System;
using System.Collections.Generic;
using System.Linq;

namespace StructureLib
{
    /// <summary>
    /// Slot layout answers for a modded processor, game-independent: what its synthesized ProcessorSpec and the
    /// InputSlot / AcceptsText prefixes report, and whether a saved production record still fits its definition.
    /// </summary>
    public static class ProcessorLayout
    {
        /// <summary>Index of the Output slot; -1 for a power producer (it has none).</summary>
        public static int OutputIndex(IReadOnlyList<SlotRole> roles, bool producesPower)
        {
            if (producesPower) return -1;
            for (int i = 0; i < roles.Count; i++)
                if (roles[i] == SlotRole.Output) return i;
            return -1;
        }

        /// <summary>Index of the first Input slot holding <paramref name="cargo"/> (a cargo byte); -1 when none takes it.</summary>
        public static int InputSlot(IReadOnlyList<(SlotRole Role, byte Cargo)> slots, byte cargo)
        {
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].Role == SlotRole.Input && slots[i].Cargo == cargo) return i;
            return -1;
        }

        /// <summary>The Input slots' cargo names, each once, in slot order, joined by " or " ("dust or sand").</summary>
        public static string AcceptsText(IReadOnlyList<(SlotRole Role, string ShortName)> slots) =>
            string.Join(" or ", slots.Where(s => s.Role == SlotRole.Input).Select(s => s.ShortName).Distinct(StringComparer.Ordinal));

        /// <summary>
        /// Null when a saved record (its slot cargo ids and its number of stores) fits a definition's slots: same count and
        /// the same cargo in every slot. Otherwise why not; such a record is not restored (it is kept in the sidecar).
        /// </summary>
        public static string RecordMismatch(IReadOnlyList<string> savedCargoIds, int savedStores, IReadOnlyList<string> definitionCargoIds)
        {
            if (savedCargoIds == null) return "the record has no slot cargo ids";
            if (savedCargoIds.Count != definitionCargoIds.Count)
                return $"the record has {savedCargoIds.Count} slots, the definition {definitionCargoIds.Count}";
            if (savedStores != savedCargoIds.Count)
                return $"the record has {savedStores} stores for {savedCargoIds.Count} slots";
            for (int i = 0; i < savedCargoIds.Count; i++)
                if (!string.Equals(savedCargoIds[i], definitionCargoIds[i], StringComparison.Ordinal))
                    return $"slot {i} held '{savedCargoIds[i]}', the definition now says '{definitionCargoIds[i]}'";
            return null;
        }
    }
}
