using Xunit;

namespace StructureLib.Tests
{
    public class GridShareTests
    {
        [Fact] public void Idle_WithSupply_IsSatisfied() => Assert.Equal((1f, 0f, 0f), GridShare.Draw(0, 2, 0));
        [Fact] public void Idle_NoSunNoBattery_IsZero() => Assert.Equal((0f, 0f, 0f), GridShare.Draw(0, 0, 0));
        [Fact] public void SunSurplusCoversDemand() => Assert.Equal((1f, 1f, 0f), GridShare.Draw(1, 5.5f, 0));
        [Fact] public void BatteryCoversTheRest() => Assert.Equal((1f, 0.5f, 0.5f), GridShare.Draw(1, 0.5f, 0.2));
        [Fact] public void NoBattery_PartialSatisfaction() => Assert.Equal((0.25f, 1f, 0f), GridShare.Draw(4, 1, 0));
        [Fact] public void NegativeSurplusCountsAsNone() => Assert.Equal((0f, 0f, 0f), GridShare.Draw(1, -3, 0));
        [Fact] public void BatteryOnly() => Assert.Equal((1f, 0f, 2f), GridShare.Draw(2, 0, 1.5));
    }
}
