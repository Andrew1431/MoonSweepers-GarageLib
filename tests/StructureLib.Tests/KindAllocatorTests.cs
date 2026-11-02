using Xunit;

namespace StructureLib.Tests
{
    public class KindAllocatorTests
    {
        [Fact]
        public void Fnv1a_KnownVectors()
        {
            Assert.Equal(0x811C9DC5u, Fnv1a.Hash32(""));
            Assert.Equal(0xE40C292Cu, Fnv1a.Hash32("a"));
        }

        [Fact]
        public void KindOf_IsHashFoldedIntoRange()
        {
            Assert.Equal(1_680_518_573, KindAllocator.KindOf("a"));
        }

        [Fact]
        public void KindOf_NeverBelowFirstModdedKind()
        {
            foreach (var id in new[] { "", "a", "ryanhartwig.sand-compactor", "zzz" })
                Assert.InRange(KindAllocator.KindOf(id), 1_000_000, int.MaxValue);
        }

        [Fact]
        public void KindOf_IsDeterministicAndOrderIndependent()
        {
            Assert.Equal(KindAllocator.KindOf("x.one"), KindAllocator.Assign(new[] { "x.two", "x.one" })["x.one"]);
        }

        [Fact]
        public void Assign_Collision_NamesBothIds()
        {
            var e = Assert.Throws<KindCollisionException>(() => KindAllocator.Assign(new[] { "a", "b" }, _ => 1_000_001));
            Assert.Equal("a", e.IdA);
            Assert.Equal("b", e.IdB);
            Assert.Contains("'a'", e.Message);
            Assert.Contains("'b'", e.Message);
            Assert.Equal(1_000_001, e.Kind);
        }

        [Fact]
        public void IsModded_SplitsAtFirstModdedKind()
        {
            Assert.False(KindAllocator.IsModded(192));
            Assert.False(KindAllocator.IsModded(1000));
            Assert.True(KindAllocator.IsModded(1_000_000));
        }
    }
}
