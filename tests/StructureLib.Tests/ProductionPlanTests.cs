using Xunit;

namespace StructureLib.Tests
{
    public class ProductionPlanTests
    {
        const string Fits = null;

        [Fact] public void FittingRecord_IsRestored() => Assert.Equal(ProductionRestoreAction.Restore, ProductionPlan.Decide(true, true, false, false, Fits));
        [Fact] public void ChangedLayout_RestoresAnEmptyState() => Assert.Equal(ProductionRestoreAction.RestoreEmpty, ProductionPlan.Decide(true, true, false, false, "slot 2 held 'a.grit'"));
        [Fact] public void SupersededRecord_IsNotReOffered() { Assert.Equal(ProductionRestoreAction.Keep, ProductionPlan.Decide(true, true, false, true, Fits)); Assert.Equal(ProductionRestoreAction.Keep, ProductionPlan.Decide(true, true, false, true, "changed")); }
        [Fact] public void DepotAlreadyHasAState_Keeps() => Assert.Equal(ProductionRestoreAction.Keep, ProductionPlan.Decide(true, true, true, false, "changed"));
        [Fact] public void StructureNotInWorld_Keeps() => Assert.Equal(ProductionRestoreAction.Keep, ProductionPlan.Decide(true, false, false, false, Fits));
        [Fact] public void NoRegisteredProcessor_Keeps() => Assert.Equal(ProductionRestoreAction.Keep, ProductionPlan.Decide(false, true, false, false, Fits));
    }
}
