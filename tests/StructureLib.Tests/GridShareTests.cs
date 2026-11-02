using Xunit;

namespace StructureLib.Tests
{
    public class GridShareTests
    {
        [Fact] public void Idle_WithSupply_IsSatisfied() => Assert.Equal((1f, 0f, 0f, 0f), GridShare.Draw(0, 2, 0f, 0));
        [Fact] public void Idle_NoSunNoBattery_IsZero() => Assert.Equal((0f, 0f, 0f, 0f), GridShare.Draw(0, 0, 0f, 0));
        [Fact] public void SunSurplusCoversDemand() => Assert.Equal((1f, 1f, 0f, 0f), GridShare.Draw(1, 5.5f, 0f, 0));
        [Fact] public void BatteryCoversTheRest() => Assert.Equal((1f, 0.5f, 0f, 0.5f), GridShare.Draw(1, 0.5f, 0f, 0.2));
        [Fact] public void NoBattery_PartialSatisfaction() => Assert.Equal((0.25f, 1f, 0f, 0f), GridShare.Draw(4, 1, 0f, 0));
        [Fact] public void NegativeSurplusCountsAsNone() => Assert.Equal((0f, 0f, 0f, 0f), GridShare.Draw(1, -3, 0f, 0));
        [Fact] public void BatteryOnly() => Assert.Equal((1f, 0f, 0f, 2f), GridShare.Draw(2, 0, 0f, 1.5));

        // Vanilla stored the sun surplus at ~0.92 while charging; what a modded machine used of it comes back out.
        [Fact] public void SurplusPartlyConsumedWhileCharging_TakesBackItsStoredShare() => Assert.Equal(0.3f * 0.92f, GridShare.ChargeTakenKw(0.3f, 2f), 4);
        [Fact] public void SurplusFullyConsumed_TakesBackAtMostWhatWasCharging() => Assert.Equal(0.48f, GridShare.ChargeTakenKw(1f, 0.48f), 4);
        [Fact] public void NotCharging_TakesNothing() { Assert.Equal(0f, GridShare.ChargeTakenKw(1f, 0f)); Assert.Equal(0f, GridShare.ChargeTakenKw(1f, -4.3f)); }
        [Fact] public void NoSunUsed_TakesNothing() => Assert.Equal(0f, GridShare.ChargeTakenKw(0f, 2f));

        // A modded fuel source in the network: after the sun surplus, before the grid's batteries.
        [Fact] public void FuelCoversWhatTheSunLeaves() => Assert.Equal((1f, 0.25f, 0.75f, 0f), GridShare.Draw(1, 0.25f, 5f, 0.5));
        [Fact] public void FuelShort_BatteriesCoverTheRest() => Assert.Equal((1f, 0f, 0.5f, 0.5f), GridShare.Draw(1, 0, 0.5f, 0.5));
        [Fact] public void FuelShort_NoBattery_Partial() => Assert.Equal((0.5f, 0f, 0.5f, 0f), GridShare.Draw(1, 0, 0.5f, 0));
        [Fact] public void Idle_WithFuelOnly_IsSatisfied() => Assert.Equal((1f, 0f, 0f, 0f), GridShare.Draw(0, 0, 5f, 0));
        [Fact] public void Idle_NoFuel_IsZero() => Assert.Equal((0f, 0f, 0f, 0f), GridShare.Draw(0, 0, 0f, 0));
    }
}
