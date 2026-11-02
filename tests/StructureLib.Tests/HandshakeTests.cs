using System.Linq;
using Xunit;

namespace StructureLib.Tests
{
    public class HandshakeTests
    {
        static RegistryManifest Manifest(params (string id, string owner, string version)[] structures)
        {
            var m = new RegistryManifest();
            foreach (var (id, owner, version) in structures)
                m.Structures.Add(new ManifestEntry { Id = id, Value = KindAllocator.KindOf(id), Owner = owner, Version = version });
            return m;
        }

        [Fact] public void Handshake_RoundTrip() { var m = new RegistryManifest { Structures = { new ManifestEntry { Id = "a.x", Value = 1_000_001, Owner = "com.a", Version = "0.1.0" } } }; var back = HandshakePayload.Decode(HandshakePayload.Encode(m)); Assert.Equal("a.x", back.Structures.Single().Id); Assert.Equal(1_000_001, back.Structures.Single().Value); }
        [Fact] public void Handshake_Compare_MissingOnClient() { var host = Manifest(("a.x", "com.a", "0.1.0")); var client = new RegistryManifest(); var p = HandshakePayload.Compare(host, client); Assert.Contains(p, s => s.Contains("missing mod com.a 0.1.0")); }
        [Fact] public void Handshake_Compare_VersionMismatch() { var p = HandshakePayload.Compare(Manifest(("a.x", "com.a", "0.1.0")), Manifest(("a.x", "com.a", "0.2.0"))); Assert.Contains(p, s => s.Contains("version mismatch com.a")); }
        [Fact] public void Handshake_Compare_CargoValueConflict() { var host = new RegistryManifest { Cargo = { new ManifestEntry { Id = "c", Value = 3, Owner = "com.a", Version = "1" } } }; var client = new RegistryManifest { Cargo = { new ManifestEntry { Id = "c", Value = 4, Owner = "com.a", Version = "1" } } }; Assert.Contains(HandshakePayload.Compare(host, client), s => s.Contains("cargo value conflict c")); }
        [Fact] public void Handshake_Compare_Identical_NoProblems() { Assert.Empty(HandshakePayload.Compare(Manifest(("a.x", "com.a", "0.1.0")), Manifest(("a.x", "com.a", "0.1.0")))); }
    }
}
