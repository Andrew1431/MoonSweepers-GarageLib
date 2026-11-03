using Xunit;

namespace StructureLib.Tests
{
    public class FuelTopUpTests
    {
        static void Equal((float Power, float UsedKw, float DrawnKw) want, (float Power, float UsedKw, float DrawnKw) got)
        {
            Assert.Equal(want.Power, got.Power, 4);
            Assert.Equal(want.UsedKw, got.UsedKw, 4);
            Assert.Equal(want.DrawnKw, got.DrawnKw, 4);
        }

        [Fact] public void Unpowered_FullyCoveredWhenFuelSuffices() => Equal((1f, 10f, 10f), FuelTopUp.Draw(0f, 10f, 20f));

        // Its own solar grid gives 0.4 of the demand: fuel adds only the unmet 0.6 share.
        [Fact] public void PartlyCoveredMachine_TopsUpTheRest() => Equal((1f, 10f, 6f), FuelTopUp.Draw(0.4f, 10f, 20f));

        [Fact] public void FuelShort_PartialShare() => Equal((0.4f, 4f, 4f), FuelTopUp.Draw(0f, 10f, 4f));
        [Fact] public void NoDemand_NothingDrawn() => Equal((0f, 0f, 0f), FuelTopUp.Draw(0f, 0f, 20f));
        [Fact] public void AlreadyFull_NothingDrawn() => Equal((1f, 10f, 0f), FuelTopUp.Draw(1f, 10f, 20f));

        // The caller subtracts what the first machine drew before serving the second: 6 kW covers only part of the
        // first, and the second gets nothing; nothing is negative and the total drawn is the 6 kW available.
        [Fact]
        public void TwoMachines_ShareWhatIsAvailable()
        {
            float fuel = 6f;
            var first = FuelTopUp.Draw(0f, 10f, fuel);
            fuel -= first.DrawnKw;
            var second = FuelTopUp.Draw(0f, 10f, fuel);
            Equal((0.6f, 6f, 6f), first);
            Equal((0f, 0f, 0f), second);
            Assert.True(second.DrawnKw >= 0f && fuel >= 0f);
            Assert.Equal(6f, first.DrawnKw + second.DrawnKw, 4);
        }

        [Fact] public void NegativeFuel_DrawsNothing() => Equal((0.5f, 5f, 0f), FuelTopUp.Draw(0.5f, 10f, -3f));
    }
}
