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

        static string Published(RegistryManifest m) => System.Convert.ToBase64String(HandshakePayload.Encode(m));

        [Fact]
        public void ReadHost_PrefersLobbyData_EvenOutsideTheLobby()
        {
            var r = HandshakePayload.ReadHost(Published(Manifest(("a.x", "com.a", "0.1.0"))), "", clientInLobby: false);
            Assert.Null(r.Uncertain);
            Assert.Equal("a.x", r.Host.Structures.Single().Id);
        }

        [Fact]
        public void ReadHost_FallsBackToMemberData()
        {
            var r = HandshakePayload.ReadHost("", Published(Manifest(("b.y", "com.b", "1"))), clientInLobby: true);
            Assert.Null(r.Uncertain);
            Assert.Equal("b.y", r.Host.Structures.Single().Id);
        }

        [Fact]
        public void ReadHost_AbsentInsideTheLobby_IsAHostWithoutStructureLib()
        {
            var r = HandshakePayload.ReadHost(null, "", clientInLobby: true);
            Assert.Null(r.Uncertain);
            Assert.Empty(r.Host.Structures);
            Assert.Empty(r.Host.Cargo);
        }

        [Fact]
        public void ReadHost_AbsentOutsideTheLobby_IsUncertain()
        {
            var r = HandshakePayload.ReadHost("", null, clientInLobby: false);
            Assert.Null(r.Host);
            Assert.Contains("not in the host's lobby", r.Uncertain);
        }

        [Fact]
        public void ReadHost_TooBigMarker_IsUncertain()
        {
            var r = HandshakePayload.ReadHost(HandshakePayload.TooBigMarker, "", clientInLobby: true);
            Assert.Null(r.Host);
            Assert.Contains("too big", r.Uncertain);
        }

        [Fact]
        public void ReadHost_Garbage_IsUncertain()
        {
            var r = HandshakePayload.ReadHost("not base64 at all!", "", clientInLobby: true);
            Assert.Null(r.Host);
            Assert.Contains("unreadable", r.Uncertain);
        }

        [Fact]
        public void PublishedValue_OverTheLimit_IsTheMarker()
        {
            var m = new RegistryManifest();
            for (int i = 0; i < 2000; i++) m.Structures.Add(new ManifestEntry { Id = "mod.structure-" + i + "-" + System.Guid.NewGuid(), Value = i, Owner = "com.m" + i, Version = "1" });
            Assert.Equal(HandshakePayload.TooBigMarker, HandshakePayload.PublishedValue(m));
            Assert.Equal(Published(Manifest(("a.x", "com.a", "0.1.0"))), HandshakePayload.PublishedValue(Manifest(("a.x", "com.a", "0.1.0"))));
        }
    }
}
