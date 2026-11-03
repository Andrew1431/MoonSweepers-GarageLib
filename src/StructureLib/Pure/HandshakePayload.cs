using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace StructureLib
{
    /// <summary>
    /// What one player has registered, as the co-op handshake sends it. Registry-agnostic: a structure's Value is its
    /// kind, a cargo's its byte value; another registry (GarageLib vehicles) can fill the same lists.
    /// </summary>
    public sealed class RegistryManifest
    {
        public int Protocol { get; set; } = 1;
        public List<ManifestEntry> Structures { get; set; } = new();
        public List<ManifestEntry> Cargo { get; set; } = new();
    }

    public sealed class ManifestEntry
    {
        public string Id { get; set; }
        public int Value { get; set; }
        /// <summary>Plugin GUID of the mod that registered it.</summary>
        public string Owner { get; set; }
        /// <summary>That plugin's version.</summary>
        public string Version { get; set; }
    }

    /// <summary>
    /// The host manifest a joining client compares against, or why there is none to trust. Exactly one of the two is set.
    /// </summary>
    public sealed class HostManifestRead
    {
        public RegistryManifest Host { get; init; }
        /// <summary>Set when the client cannot tell what the host has: the join goes ahead unchecked.</summary>
        public string Uncertain { get; init; }
    }

    /// <summary>Encodes, decodes and compares manifests.</summary>
    public static class HandshakePayload
    {
        static readonly byte[] Magic = { (byte)'S', (byte)'L', (byte)'H' };

        /// <summary>Steam's limit for one lobby data value (k_cubChatMetadataMax).</summary>
        public const int MaxValueLength = 8192;

        /// <summary>Published instead of a manifest that would not fit in one lobby data value.</summary>
        public const string TooBigMarker = "too-big";

        /// <summary>What a player publishes under the handshake key: the manifest in base64, or <see cref="TooBigMarker"/>.</summary>
        public static string PublishedValue(RegistryManifest m)
        {
            string value = Convert.ToBase64String(Encode(m));
            return value.Length >= MaxValueLength ? TooBigMarker : value;
        }

        /// <summary>
        /// Reads the host's published value: its lobby data first (readable before the client is a member), then its
        /// member data. Nothing published means "host without StructureLib" only when the client is verifiably in the
        /// host's lobby (Steam returns member data only to members); otherwise, and for the too-big marker or an unreadable
        /// value, the answer is uncertain.
        /// </summary>
        public static HostManifestRead ReadHost(string lobbyData, string memberData, bool clientInLobby)
        {
            string value = !string.IsNullOrEmpty(lobbyData) ? lobbyData : memberData;
            if (string.IsNullOrEmpty(value))
                return clientInLobby
                    ? new HostManifestRead { Host = new RegistryManifest() }
                    : new HostManifestRead { Uncertain = "nothing published that this client can read: it is not in the host's lobby (yet)" };
            if (value == TooBigMarker)
                return new HostManifestRead { Uncertain = "the host's manifest is too big for a Steam lobby value" };
            try { return new HostManifestRead { Host = Decode(Convert.FromBase64String(value)) }; }
            catch (Exception e) when (e is FormatException || e is InvalidDataException)
            {
                return new HostManifestRead { Uncertain = "the host's manifest is unreadable (" + e.Message + ")" };
            }
        }

        /// <summary>Magic bytes, then the deflated body (protocol, structures, cargo).</summary>
        public static byte[] Encode(RegistryManifest m)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            using var output = new MemoryStream();
            output.Write(Magic, 0, Magic.Length);
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            using (var w = new BinaryWriter(deflate, Encoding.UTF8))
            {
                w.Write(m.Protocol);
                WriteList(w, m.Structures);
                WriteList(w, m.Cargo);
            }
            return output.ToArray();
        }

        /// <summary>Throws <see cref="InvalidDataException"/> on bytes that are not a manifest.</summary>
        public static RegistryManifest Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < Magic.Length || !bytes.Take(Magic.Length).SequenceEqual(Magic))
                throw new InvalidDataException("not a StructureLib handshake manifest");
            try
            {
                using var input = new MemoryStream(bytes, Magic.Length, bytes.Length - Magic.Length);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var r = new BinaryReader(deflate, Encoding.UTF8);
                return new RegistryManifest { Protocol = r.ReadInt32(), Structures = ReadList(r), Cargo = ReadList(r) };
            }
            catch (Exception e) when (e is EndOfStreamException || e is IOException)
            {
                throw new InvalidDataException("truncated or corrupt handshake manifest", e);
            }
        }

        /// <summary>
        /// One message per problem between the host's and a client's manifest; empty when they agree. Entries match by
        /// id; a mod's version is taken from its entries.
        /// </summary>
        public static IReadOnlyList<string> Compare(RegistryManifest host, RegistryManifest client)
        {
            host ??= new RegistryManifest();
            client ??= new RegistryManifest();
            var problems = new List<string>();
            if (host.Protocol != client.Protocol)
            {
                problems.Add($"handshake protocol mismatch: host {host.Protocol} client {client.Protocol}");
                return problems;
            }

            Missing(host.Structures, client.Structures, "structure", problems);
            Missing(host.Cargo, client.Cargo, "cargo", problems);

            var hostVersions = Versions(host);
            var clientVersions = Versions(client);
            foreach (var kv in hostVersions)
                if (clientVersions.TryGetValue(kv.Key, out var cv) && cv != kv.Value)
                    problems.Add($"version mismatch {kv.Key}: host {kv.Value} client {cv}");

            var clientCargo = ById(client.Cargo);
            foreach (var h in Sorted(host.Cargo))
                if (clientCargo.TryGetValue(h.Id, out var c) && c.Value != h.Value)
                    problems.Add($"cargo value conflict {h.Id}: host {h.Value} client {c.Value}");

            foreach (var owner in clientVersions.Keys)
                if (!hostVersions.ContainsKey(owner))
                    problems.Add($"extra mod on client {owner}");
            return problems;
        }

        static void Missing(List<ManifestEntry> host, List<ManifestEntry> client, string what, List<string> problems)
        {
            var have = ById(client);
            foreach (var h in Sorted(host))
                if (!have.ContainsKey(h.Id))
                    problems.Add($"missing mod {h.Owner} {h.Version} ({what} {h.Id})");
        }

        static IEnumerable<ManifestEntry> Entries(List<ManifestEntry> list) => (list ?? new List<ManifestEntry>()).Where(e => e?.Id != null);

        static IEnumerable<ManifestEntry> Sorted(List<ManifestEntry> list) =>
            Entries(list).OrderBy(e => e.Owner, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal);

        static Dictionary<string, ManifestEntry> ById(List<ManifestEntry> list)
        {
            var d = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
            foreach (var e in Entries(list)) d[e.Id] = e;
            return d;
        }

        // Owner -> version, in owner order. An owner whose entries disagree keeps the first version seen.
        static SortedDictionary<string, string> Versions(RegistryManifest m)
        {
            var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in Entries(m.Structures).Concat(Entries(m.Cargo)))
                if (!d.ContainsKey(e.Owner ?? "")) d[e.Owner ?? ""] = e.Version ?? "";
            return d;
        }

        static void WriteList(BinaryWriter w, List<ManifestEntry> list)
        {
            var entries = Entries(list).ToList();
            w.Write(entries.Count);
            foreach (var e in entries)
            {
                w.Write(e.Id);
                w.Write(e.Value);
                w.Write(e.Owner ?? "");
                w.Write(e.Version ?? "");
            }
        }

        static List<ManifestEntry> ReadList(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 100_000) throw new InvalidDataException($"handshake manifest claims {n} entries");
            var list = new List<ManifestEntry>(n);
            for (int i = 0; i < n; i++)
                list.Add(new ManifestEntry { Id = r.ReadString(), Value = r.ReadInt32(), Owner = r.ReadString(), Version = r.ReadString() });
            return list;
        }
    }
}
