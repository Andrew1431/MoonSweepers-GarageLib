using System;
using System.Linq;
using Xunit;

namespace StructureLib.Tests
{
    public class SidecarCodecTests
    {
        [Fact] public void SidecarCodec_RoundTrip_KeepsOrphansVerbatim() { var loaded = new SidecarFile { Structures = { new StructureRecord { Id = "gone.mod", DepotId = 7, Owner = "steam-1", X = 1, Stored = 2.5 } } }; var live = new SidecarFile { Structures = { new StructureRecord { Id = "here.mod", DepotId = 9 } } }; var merged = SidecarCodec.Merge(live, loaded, id => id == "here.mod"); var back = SidecarCodec.Deserialize(SidecarCodec.Serialize(merged)); Assert.Equal(2, back.Structures.Count); var orphan = back.Structures.Single(s => s.Id == "gone.mod"); Assert.Equal(7, orphan.DepotId); Assert.Equal(2.5, orphan.Stored); Assert.Equal("steam-1", orphan.Owner); }
        [Fact] public void SidecarCodec_Merge_LiveReplacesRegistered() { var loaded = new SidecarFile { Structures = { new StructureRecord { Id = "here.mod", DepotId = 1 } } }; var live = new SidecarFile(); var merged = SidecarCodec.Merge(live, loaded, _ => true); Assert.Empty(merged.Structures); }
        [Fact] public void SidecarCodec_Merge_KeepsNullIdRecords() { var loaded = new SidecarFile { Structures = { new StructureRecord { Id = null, DepotId = 3 }, new StructureRecord { Id = "", DepotId = 4 } } }; var merged = SidecarCodec.Merge(new SidecarFile(), loaded, _ => true); Assert.Equal(new[] { 3, 4 }, merged.Structures.Select(s => s.DepotId).ToArray()); }
        [Fact] public void SidecarCodec_Deserialize_Garbage_Throws() { Assert.ThrowsAny<Exception>(() => SidecarCodec.Deserialize("{not json")); }

        [Fact]
        public void SidecarCodec_CanisterRecord_RoundTrips()
        {
            var file = new SidecarFile { Canisters = { new CanisterRecord { Id = 7, Owner = "steam-1", CargoId = "a.grit", Kilograms = 0.75, Capacity = 1, Position = new[] { -0.5f, 1992.25f, -29.75f }, Up = new[] { 0f, 0.99f, -0.14f }, Rotation = new[] { 0f, 0f, 0f, 1f }, PhysicalPose = true } } };
            var back = SidecarCodec.Deserialize(SidecarCodec.Serialize(file)).Canisters.Single();
            Assert.Equal(7, back.Id);
            Assert.Equal("steam-1", back.Owner);
            Assert.Equal("a.grit", back.CargoId);
            Assert.Equal(0.75, back.Kilograms);
            Assert.Equal(1, back.Capacity);
            Assert.Equal(new[] { -0.5f, 1992.25f, -29.75f }, back.Position);
            Assert.Equal(new[] { 0f, 0.99f, -0.14f }, back.Up);
            Assert.Equal(new[] { 0f, 0f, 0f, 1f }, back.Rotation);
            Assert.True(back.PhysicalPose);
        }

        [Fact]
        public void SidecarCodec_ProductionRecord_RoundTrips()
        {
            var file = new SidecarFile { Production = { new ProductionRecord { Id = "a.grinder", DepotId = 12, Progress = 0.5f, Running = true, Charge = 1.25,
                Stores = { new() { new ShareRecord { Owner = "steam-1", Pending = 4 } }, new(), new() { new ShareRecord { Owner = "steam-1", Pending = 2 }, new ShareRecord { Owner = "steam-2", Pending = 0.5 } } },
                SlotCargoIds = { "dust", "dust", "a.grit" } } } };
            var back = SidecarCodec.Deserialize(SidecarCodec.Serialize(file)).Production.Single();
            Assert.Equal(("a.grinder", 12, 0.5f, true, 1.25), (back.Id, back.DepotId, back.Progress, back.Running, back.Charge));
            Assert.Equal(new[] { 1, 0, 2 }, back.Stores.Select(s => s.Count).ToArray());
            Assert.Equal(0.5, back.Stores[2][1].Pending);
            Assert.Equal(new[] { "dust", "dust", "a.grit" }, back.SlotCargoIds);
        }

        [Fact]
        public void SidecarCodec_Deserialize_ProductionNullLists_BecomeEmpty()
        {
            var p = SidecarCodec.Deserialize("{\"Production\":[{\"Id\":\"a.grinder\",\"Stores\":null,\"SlotCargoIds\":null}]}").Production.Single();
            Assert.Empty(p.Stores);
            Assert.Empty(p.SlotCargoIds);
        }

        [Fact]
        public void SidecarCodec_Merge_KeepsProductionOfMissingStructureOrCargo()
        {
            var loaded = new SidecarFile { Production = {
                new ProductionRecord { Id = "gone.grinder", DepotId = 1, SlotCargoIds = { "dust" } },
                new ProductionRecord { Id = "here.grinder", DepotId = 2, SlotCargoIds = { "dust", "gone.grit" } },
                new ProductionRecord { Id = "here.grinder", DepotId = 3, SlotCargoIds = { "dust", "here.grit" } } } };
            var live = new SidecarFile { Production = { new ProductionRecord { Id = "here.grinder", DepotId = 9 } } };
            var merged = SidecarCodec.Merge(live, loaded, id => id == "here.grinder" || id == "here.grit" || id == "dust");
            Assert.Equal(new[] { 9, 1, 2 }, merged.Production.Select(p => p.DepotId).ToArray());
        }

        [Fact]
        public void SidecarCodec_Merge_KeepsCanistersOfUnregisteredCargo()
        {
            var loaded = new SidecarFile { Canisters = { new CanisterRecord { Id = 1, CargoId = "gone.grit" }, new CanisterRecord { Id = 2, CargoId = "here.grit" }, new CanisterRecord { Id = 3, CargoId = null } } };
            var live = new SidecarFile { Canisters = { new CanisterRecord { Id = 9, CargoId = "here.grit" } } };
            var merged = SidecarCodec.Merge(live, loaded, id => id == "here.grit");
            Assert.Equal(new[] { 9, 1, 3 }, merged.Canisters.Select(c => c.Id).ToArray());
        }
    }
}
