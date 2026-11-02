using Xunit;

namespace StructureLib.Tests
{
    public class BeltIntakeTests
    {
        [Fact] public void Decide_TakesIntoMatchingInputSlot() { var r = BeltIntake.Decide(new[]{(SlotRole.Input,"sand",16.0,4.0),(SlotRole.Batch,"sand",4.0,0.0),(SlotRole.Output,"b",10.0,0.0)}, "sand", 5.0); Assert.Equal((0, 5.0, (string)null), r); }
        [Fact] public void Decide_PartialWhenNearlyFull() { var r = BeltIntake.Decide(new[]{(SlotRole.Input,"sand",16.0,15.0)}, "sand", 5.0); Assert.Equal((0, 1.0, (string)null), r); }
        [Fact] public void Decide_FullWaits() { Assert.Equal("full", BeltIntake.Decide(new[]{(SlotRole.Input,"sand",16.0,16.0)}, "sand", 5.0).Refusal); }
        [Fact] public void Decide_WrongCargoRefused() { Assert.Equal("dust does not go in here", BeltIntake.Decide(new[]{(SlotRole.Input,"sand",16.0,0.0)}, "dust", 5.0).Refusal); }
        [Fact] public void CacheValid_SameDepotSameKind() { Assert.True(BeltIntake.CacheValid(27, 529738499, 529738499)); }
        [Fact] public void CacheValid_FalseWhenDepotGone() { Assert.False(BeltIntake.CacheValid(27, 529738499, null)); }
        [Fact] public void CacheValid_FalseWhenKindChanged() { Assert.False(BeltIntake.CacheValid(27, 529738499, 96)); Assert.False(BeltIntake.CacheValid(27, 96, 529738499)); }
        [Fact] public void CacheValid_FalseForNoDepot() { Assert.False(BeltIntake.CacheValid(-1, -1, null)); }
    }
}
