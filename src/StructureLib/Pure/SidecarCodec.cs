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
            f.Structures.RemoveAll(s => s == null);
            f.Production.RemoveAll(p => p == null);
            f.Canisters.RemoveAll(c => c == null);
            return f;
        }

        /// <summary>
        /// The file to write: every record of <paramref name="live"/> (what the world holds now), plus every record of
        /// <paramref name="loaded"/> whose mod is missing, verbatim. A loaded record is missing its mod when its
        /// structure id, or any cargo id it names, is not registered. <paramref name="idIsRegistered"/> answers for
        /// structure ids and cargo ids alike (the game's own cargo names count as registered). Loaded records whose ids
        /// are all registered are dropped: the live world is the truth for them.
        /// </summary>
        public static SidecarFile Merge(SidecarFile live, SidecarFile loaded, Func<string, bool> idIsRegistered)
        {
            live ??= new SidecarFile();
            loaded ??= new SidecarFile();
            if (idIsRegistered == null) throw new ArgumentNullException(nameof(idIsRegistered));
            bool Missing(string id) => !string.IsNullOrEmpty(id) && !idIsRegistered(id);
            return new SidecarFile
            {
                Version = live.Version,
                Written = live.Written,
                Structures = (live.Structures ?? new()).Concat((loaded.Structures ?? new()).Where(s => s != null && Missing(s.Id))).ToList(),
                Production = (live.Production ?? new()).Concat((loaded.Production ?? new()).Where(p => p != null &&
                    (Missing(p.Id) || (p.Slots ?? new()).Any(s => s != null && Missing(s.Cargo))))).ToList(),
                Canisters = (live.Canisters ?? new()).Concat((loaded.Canisters ?? new()).Where(c => c != null && Missing(c.Cargo))).ToList(),
            };
        }
    }
}
