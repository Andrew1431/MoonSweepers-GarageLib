using System;
using System.Collections.Generic;

namespace StructureLib
{
    /// <summary>
    /// The answers of a pure native leaf (a static kind -> value switch), recorded once for keys 0..bound-1 by calling
    /// the unpatched original, so a Harmony prefix can answer every key itself and never fall through to the original.
    /// See Patches/LeafTables.cs for why. A key outside the recorded range gets <see cref="Default"/>.
    /// </summary>
    public sealed class LeafTable<T>
    {
        readonly T[] answers;

        /// <summary>The leaf's name, for logs.</summary>
        public string Name { get; }
        /// <summary>Small id that keys the per-(leaf, key) log line.</summary>
        public int Id { get; }
        /// <summary>The answer for a key outside the recorded range.</summary>
        public T Default { get; }
        /// <summary>Recorded keys: 0..Count-1.</summary>
        public int Count => answers.Length;
        /// <summary>Why recording stopped before the bound (the first throw), or null if the whole bound was recorded.</summary>
        public string Stop { get; }
        /// <summary>"0..N" or "empty".</summary>
        public string Range => answers.Length == 0 ? "empty" : "0.." + (answers.Length - 1);

        LeafTable(string name, int id, T[] answers, T fallback, string stop)
        {
            Name = name;
            Id = id;
            this.answers = answers;
            Default = fallback;
            Stop = stop;
        }

        /// <summary>Records original(k) for k = 0..bound-1, stopping at the first throw (keys from there on answer the default).</summary>
        public static LeafTable<T> Snapshot(string name, int id, int bound, Func<int, T> original, T fallback)
        {
            var list = new List<T>(Math.Max(bound, 0));
            string stop = null;
            for (int k = 0; k < bound; k++)
            {
                T v;
                try { v = original(k); }
                catch (Exception e)
                {
                    stop = $"key {k} threw {e.GetType().Name}: {e.Message}";
                    break;
                }
                list.Add(v);
            }
            return new LeafTable<T>(name, id, list.ToArray(), fallback, stop);
        }

        /// <summary>A table with nothing recorded: every key answers the default.</summary>
        public static LeafTable<T> Empty(string name, int id, T fallback, string why) => new LeafTable<T>(name, id, Array.Empty<T>(), fallback, why);

        /// <summary>The recorded answer for <paramref name="key"/>, or <see cref="Default"/> (inTable false) outside 0..Count-1.</summary>
        public T Answer(int key, out bool inTable)
        {
            inTable = key >= 0 && key < answers.Length;
            return inTable ? answers[key] : Default;
        }
    }
}
