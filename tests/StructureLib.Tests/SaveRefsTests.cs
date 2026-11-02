using System.Collections.Generic;
using Xunit;

namespace StructureLib.Tests
{
    public class SaveRefsTests
    {
        const int Modded = KindAllocator.FirstModdedKind;

        [Fact]
        public void IndexesToStrip_TakesStrippedIdsAndModdedKinds()
        {
            var entries = new List<(int, int)> { (3, 98), (28, 0), (4, Modded + 7), (5, 96), (29, 0) };
            Assert.Equal(new[] { 1, 2, 4 }, SaveRefs.IndexesToStrip(entries, new HashSet<int> { 28, 29 }, Modded));
        }

        [Fact]
        public void IndexesToStrip_NothingStripped_KeepsVanilla()
        {
            var entries = new List<(int, int)> { (3, 98), (4, 96) };
            Assert.Empty(SaveRefs.IndexesToStrip(entries, new HashSet<int>(), Modded));
            Assert.Empty(SaveRefs.IndexesToStrip(null, null, Modded));
        }

        [Fact]
        public void ModdedKinds_MapsOnlyModdedDepots()
        {
            var map = SaveRefs.ModdedKinds(new[] { (26, 94), (27, Modded + 1), (28, Modded + 2), (27, Modded + 9) }, Modded);
            Assert.Equal(2, map.Count);
            Assert.Equal(Modded + 1, map[27]);
            Assert.Equal(Modded + 2, map[28]);
        }

        [Fact]
        public void Unrestored_ReleasesRecordsTheWorldDoesNotHold()
        {
            var world = new Dictionary<int, int> { [28] = Modded + 2, [30] = 94 };
            var injected = new[] { ("lamp", 28, Modded + 2), ("grinder", 29, Modded + 1), ("burner", 30, Modded + 3) };
            Assert.Equal(new[] { "grinder", "burner" }, SaveRefs.Unrestored(injected, id => world.TryGetValue(id, out int k) ? k : null));
        }

        // RestoreDepots threw before restoring anything: every injected record is released, none is kept as in the world.
        [Fact]
        public void Unrestored_AfterAThrowingRestore_ReleasesEverything()
        {
            var injected = new[] { ("lamp", 28, Modded + 2), ("grinder", 29, Modded + 1) };
            Assert.Equal(new[] { "lamp", "grinder" }, SaveRefs.Unrestored(injected, _ => null));
            Assert.Equal(new[] { "lamp", "grinder" }, SaveRefs.Unrestored(injected, null));
        }

        [Fact]
        public void EmptySnapshot_HasOneSlotPerCurrentSlot()
        {
            Assert.Equal(3, ProductionPlan.EmptySlotCount(new List<string> { "dust", "dust", "a.grit" }));
            Assert.Equal(0, ProductionPlan.EmptySlotCount(null));
        }
    }
}
