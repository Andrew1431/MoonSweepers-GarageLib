using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StructureLib
{
    /// <summary>Reads, writes and merges structurelib.json.</summary>
    public static class SidecarCodec
    {
        static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public static string Serialize(SidecarFile f) => JsonSerializer.Serialize(f ?? new SidecarFile(), Options);

        /// <summary>Parses a sidecar. Throws on anything that is not a sidecar object (garbage, "null").</summary>
        public static SidecarFile Deserialize(string json)
        {
            var f = JsonSerializer.Deserialize<SidecarFile>(json, Options) ?? throw new InvalidDataException("the sidecar is empty (null)");
            f.Structures ??= new();
            f.Production ??= new();
            f.Canisters ??= new();
            foreach (var s in f.Structures) if (s != null) s.Contributions ??= new();
            foreach (var p in f.Production)
                if (p != null)
                {
                    p.Stores ??= new();
                    p.SlotCargoIds ??= new();
                    for (int i = 0; i < p.Stores.Count; i++) p.Stores[i] ??= new();
                }
            f.Structures.RemoveAll(s => s == null);
            f.Production.RemoveAll(p => p == null);
            f.Canisters.RemoveAll(c => c == null);
            return f;
        }

        /// <summary>
        /// The file to write: every record of <paramref name="live"/> (what the world holds now), plus every record of
        /// <paramref name="loaded"/> whose mod is missing, verbatim. A loaded record is missing its mod when its
        /// structure id (or a canister's cargo id) is null, empty or not registered, or a cargo id in one of its slots is not registered. <paramref name="idIsRegistered"/> answers for
        /// structure ids and cargo ids alike (the game's own cargo names count as registered). Loaded records whose ids
        /// are all registered are dropped: the live world is the truth for them.
        /// </summary>
        public static SidecarFile Merge(SidecarFile live, SidecarFile loaded, Func<string, bool> idIsRegistered)
        {
            live ??= new SidecarFile();
            loaded ??= new SidecarFile();
            if (idIsRegistered == null) throw new ArgumentNullException(nameof(idIsRegistered));
            // A record without an id names no mod that could own it: it is an orphan and kept as it is.
            bool Missing(string id) => string.IsNullOrEmpty(id) || !idIsRegistered(id);
            // An empty processor slot names no cargo; that does not make the record an orphan.
            bool SlotMissing(string cargo) => !string.IsNullOrEmpty(cargo) && !idIsRegistered(cargo);
            return new SidecarFile
            {
                Version = live.Version,
                Written = live.Written,
                Structures = (live.Structures ?? new()).Concat((loaded.Structures ?? new()).Where(s => s != null && Missing(s.Id))).ToList(),
                Production = (live.Production ?? new()).Concat((loaded.Production ?? new()).Where(p => p != null &&
                    (Missing(p.Id) || (p.SlotCargoIds ?? new()).Any(SlotMissing)))).ToList(),
                Canisters = (live.Canisters ?? new()).Concat((loaded.Canisters ?? new()).Where(c => c != null && Missing(c.CargoId))).ToList(),
            };
        }
    }
}
