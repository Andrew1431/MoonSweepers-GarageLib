using Xunit;

namespace StructureLib.Tests
{
    public class CommitOrderTests
    {
        [Fact] public void CommitOrder_FirstWrite_Commits() => Assert.True(CommitOrder.ShouldCommit(0, -1));
        [Fact] public void CommitOrder_NewerSnapshot_Commits() => Assert.True(CommitOrder.ShouldCommit(5, 4));
        [Fact] public void CommitOrder_SameSnapshot_Commits() => Assert.True(CommitOrder.ShouldCommit(4, 4));
        [Fact] public void CommitOrder_OlderSnapshotAfterNewer_IsDropped() => Assert.False(CommitOrder.ShouldCommit(3, 4));
        [Fact] public void CommitOrder_TmpPath_IsPerWrite() => Assert.NotEqual(CommitOrder.TmpPath("a/structurelib.json", 1), CommitOrder.TmpPath("a/structurelib.json", 2));
    }
}
