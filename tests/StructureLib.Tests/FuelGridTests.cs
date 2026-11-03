using System.Collections.Generic;
using Xunit;
using M = StructureLib.FuelGrid.Member;

namespace StructureLib.Tests
{
    public class FuelGridTests
    {
        // Burner (depot 30, range 20) and grinder (depot 31) 8 m apart, no vanilla node: one StructureLib grid -30.
        [Fact]
        public void BurnerAndGrinder_NoVanilla_ShareANegativeGrid()
        {
            var (grid, net) = FuelGrid.Assign(new List<M> { new(30, 0, 0, 0, 20, 0), new(31, 8, 0, 0, 0, 0) });
            Assert.Equal(new[] { -30, -30 }, grid);
            Assert.Equal(net[0], net[1]);
        }

        [Fact]
        public void GrinderOutOfBurnerRange_HasNoGrid()
        {
            var (grid, net) = FuelGrid.Assign(new List<M> { new(30, 0, 0, 0, 20, 0), new(31, 25, 0, 0, 0, 0) });
            Assert.Equal(new[] { -30, 0 }, grid);
            Assert.NotEqual(net[0], net[1]);
        }

        // Task 10's rule is unchanged: a member a vanilla node reaches keeps that vanilla grid.
        [Fact]
        public void VanillaInRangeOfBoth_UsesTheVanillaGrid()
        {
            var (grid, _) = FuelGrid.Assign(new List<M> { new(30, 0, 0, 0, 20, 26), new(31, 8, 0, 0, 0, 26) });
            Assert.Equal(new[] { 26, 26 }, grid);
        }

        [Fact]
        public void MemberReachedOnlyThroughTheBurner_JoinsTheBurnersVanillaGrid()
        {
            var (grid, _) = FuelGrid.Assign(new List<M> { new(30, 0, 0, 0, 20, 1), new(31, 8, 0, 0, 0, 0) });
            Assert.Equal(new[] { 1, 1 }, grid);
        }

        [Fact]
        public void NoSource_NoVanilla_IsZero()
        {
            var (grid, _) = FuelGrid.Assign(new List<M> { new(31, 0, 0, 0, 0, 0), new(32, 1, 0, 0, 0, 0) });
            Assert.Equal(new[] { 0, 0 }, grid);
        }

        // Sources chain: A reaches B, B reaches the machine; all one network, id from the lowest depot id.
        [Fact]
        public void SourcesChain_IntoOneNetwork()
        {
            var (grid, net) = FuelGrid.Assign(new List<M> { new(40, 0, 0, 0, 10, 0), new(35, 9, 0, 0, 10, 0), new(50, 18, 0, 0, 0, 0) });
            Assert.Equal(new[] { -35, -35, -35 }, grid);
            Assert.True(net[0] == net[1] && net[1] == net[2]);
        }

        [Fact] public void Available_IsPeak_WhenChargeLasts() => Assert.Equal(5f, FuelGrid.AvailableKw(5, 2, 0.25f));
        [Fact] public void Available_IsWhatTheChargeLasts() => Assert.Equal(3.6f, FuelGrid.AvailableKw(5, 0.00025, 0.25f), 4);
        [Fact] public void Available_IsZero_WhenEmpty() => Assert.Equal(0f, FuelGrid.AvailableKw(5, 0, 0.25f));

        [Fact] public void Split_TakesInOrder() => Assert.Equal(new[] { 2f, 1f, 0f }, FuelGrid.Split(3, new[] { 2f, 5f, 5f }));
        [Fact] public void Split_ShortOfDemand_TakesAll() => Assert.Equal(new[] { 1f, 0.5f }, FuelGrid.Split(4, new[] { 1f, 0.5f }));
    }
}
