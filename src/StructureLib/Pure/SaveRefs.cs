using System.Collections.Generic;

namespace StructureLib
{
    /// <summary>
    /// Rules for the per-depot sections of a vanilla save. The game reads every save back after writing it, and a section
    /// that names a depot missing from the written depot list (a stripped modded one) makes it refuse the save; so does a
    /// record of a modded kind. Pure: callers pass plain ids and kinds.
    /// </summary>
    public static class SaveRefs
    {
        /// <summary>
        /// Indexes, ascending, of the entries that must leave the vanilla save with the stripped depots: an entry whose id
        /// is a stripped depot id, or whose kind is in the modded range. Pass kind 0 for entries that carry no kind.
        /// </summary>
        public static List<int> IndexesToStrip(IReadOnlyList<(int id, int kind)> entries, ICollection<int> strippedIds, int firstModdedKind)
        {
            var take = new List<int>();
            if (entries == null) return take;
            for (int i = 0; i < entries.Count; i++)
            {
                var (id, kind) = entries[i];
                if (kind >= firstModdedKind || (strippedIds != null && strippedIds.Contains(id))) take.Add(i);
            }
            return take;
        }

        /// <summary>
        /// The depot id -> kind map of the stripped modded depots: what the production strip matches production states
        /// against while the depots are out of the save snapshot. Entries below <paramref name="firstModdedKind"/> are
        /// ignored; a repeated id keeps its first kind.
        /// </summary>
        public static Dictionary<int, int> ModdedKinds(IEnumerable<(int id, int kind)> depots, int firstModdedKind)
        {
            var map = new Dictionary<int, int>();
            if (depots == null) return map;
            foreach (var (id, kind) in depots)
                if (kind >= firstModdedKind) map.TryAdd(id, kind);
            return map;
        }

        /// <summary>
        /// After a load's RestoreDepots, whether it returned or threw: the injected records the world does not hold with
        /// their kind. Those stop counting as in the world (they are written back verbatim on the next save), so a
        /// restore that failed halfway never drops them. <paramref name="worldKindOf"/> answers a depot's kind in the
        /// world, null when the world has no such depot.
        /// </summary>
        public static List<T> Unrestored<T>(IEnumerable<(T record, int depot, int kind)> injected, System.Func<int, int?> worldKindOf)
        {
            var release = new List<T>();
            if (injected == null) return release;
            foreach (var (record, depot, kind) in injected)
            {
                int? there = worldKindOf?.Invoke(depot);
                if (there != kind) release.Add(record);
            }
            return release;
        }
    }
}
