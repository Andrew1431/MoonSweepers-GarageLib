using System;
using System.Collections.Generic;

namespace StructureLib
{
    /// <summary>
    /// The answers of a pure native leaf (a static kind -> value switch), recorded once for an explicit list of keys the
    /// game defines, by calling the unpatched original. A Harmony prefix can then answer every key itself and never fall
    /// through to the original. See Patches/LeafTables.cs for why. A key that was not recorded gets <see cref="Default"/>.
    /// </summary>
    public sealed class LeafTable<T>
    {
        readonly Dictionary<long, T> answers;

        /// <summary>The leaf's name, for logs.</summary>
        public string Name { get; }
        /// <summary>Small id that keys the per-(leaf, key) log line.</summary>
        public int Id { get; }
        /// <summary>The answer for a key that was not recorded.</summary>
        public T Default { get; }
        /// <summary>Keys recorded (each one original call).</summary>
        public int Count => answers.Count;
        /// <summary>Why recording stopped before the last key (the first throw), or null if every key was recorded.</summary>
        public string Stop { get; }
        /// <summary>The recorded keys, for logs: "count keys, min..max", or "empty".</summary>
        public string Range { get; }

        LeafTable(string name, int id, Dictionary<long, T> answers, T fallback, string stop, string range)
        {
            Name = name;
            Id = id;
            this.answers = answers;
            Default = fallback;
            Stop = stop;
            Range = range;
        }

        /// <summary>Records original(key) for each key in order, stopping at the first throw (that key and the rest answer the default).</summary>
        public static LeafTable<T> Snapshot(string name, int id, IEnumerable<long> keys, Func<long, T> original, T fallback)
        {
            var map = new Dictionary<long, T>();
            string stop = null;
            long min = long.MaxValue, max = long.MinValue;
            foreach (long k in keys)
            {
                T v;
                try { v = original(k); }
                catch (Exception e)
                {
                    stop = $"key {k} threw {e.GetType().Name}: {e.Message}";
                    break;
                }
                map[k] = v;
                if (k < min) min = k;
                if (k > max) max = k;
            }
            return new LeafTable<T>(name, id, map, fallback, stop, map.Count == 0 ? "empty" : $"{map.Count} keys, {min}..{max}");
        }

        /// <summary>A table with nothing recorded: every key answers the default.</summary>
        public static LeafTable<T> Empty(string name, int id, T fallback, string why) => new LeafTable<T>(name, id, new Dictionary<long, T>(), fallback, why, "empty");

        /// <summary>The recorded answer for <paramref name="key"/>, or <see cref="Default"/> (inTable false) for a key not recorded.</summary>
        public T Answer(long key, out bool inTable)
        {
            inTable = answers.TryGetValue(key, out T v);
            return inTable ? v : Default;
        }

        /// <summary>The kinds the game defines: 0..maxKind, then each extra kind above it (e.g. the belt kind), without duplicates.</summary>
        public static List<long> KindKeys(int maxKind, params int[] extra)
        {
            var keys = new List<long>();
            for (int k = 0; k <= maxKind; k++) keys.Add(k);
            foreach (int e in extra) if (e > maxKind && !keys.Contains(e)) keys.Add(e);
            return keys;
        }

        /// <summary>Key of a (kind, cargo) pair for a two-argument leaf.</summary>
        public static long PairKey(int kind, byte cargo) => ((long)kind << 8) | cargo;
    }
}
