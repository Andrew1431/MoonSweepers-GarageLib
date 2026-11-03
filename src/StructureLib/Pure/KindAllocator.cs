using System;
using System.Collections.Generic;
using System.Linq;

namespace StructureLib
{
    /// <summary>Thrown when two structure ids hash to the same kind number.</summary>
    public sealed class KindCollisionException : Exception
    {
        public string IdA { get; }
        public string IdB { get; }
        public int Kind { get; }

        public KindCollisionException(string idA, string idB, int kind)
            : base($"Structure ids '{idA}' and '{idB}' both map to kind {kind}. Rename one of them.")
        {
            IdA = idA;
            IdB = idB;
            Kind = kind;
        }
    }

    /// <summary>Maps string structure ids to stable integer kinds that never overlap the game's own kinds.</summary>
    public static class KindAllocator
    {
        /// <summary>Kinds below this belong to the base game; modded kinds start here.</summary>
        public const int FirstModdedKind = 1_000_000;

        public static int KindOf(string id)
        {
            return FirstModdedKind + (int)(Fnv1a.Hash32(id) % (uint)(int.MaxValue - FirstModdedKind));
        }

        public static bool IsModded(int kind)
        {
            return kind >= FirstModdedKind;
        }

        /// <summary>
        /// Assigns a kind to every id. Ids are visited in ordinal order so the collision reported
        /// (the first two ids sharing a kind) is the same on every machine.
        /// </summary>
        public static IReadOnlyDictionary<string, int> Assign(IEnumerable<string> ids, Func<string, int> kindOf = null)
        {
            kindOf = kindOf ?? KindOf;
            var byKind = new Dictionary<int, string>();
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in ids.Distinct(StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal))
            {
                int kind = kindOf(id);
                if (byKind.TryGetValue(kind, out var other))
                    throw new KindCollisionException(other, id, kind);
                byKind[kind] = id;
                result[id] = kind;
            }
            return result;
        }
    }
}
