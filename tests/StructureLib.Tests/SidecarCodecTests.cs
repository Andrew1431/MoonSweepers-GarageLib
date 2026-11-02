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
    }
}
