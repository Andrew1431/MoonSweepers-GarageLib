using System.Collections.Generic;
using Xunit;

namespace StructureLib.Tests
{
    public class PoleGraphTests
    {
        static Pole P(int id, float x, float link = 24f, float supply = 12f) => new(id, x, 0, 0, link, supply);
        static Site S(float x, float half) => new(100, x, 0, 0, half, half, 0f);

        [Fact]
        public void TwoPolesWithinRange_OneGrid()
        {
            var g = PoleGraph.Build(new List<Pole> { P(1, 0), P(2, 24) });
            Assert.Equal(1, g.GridCount);
            Assert.Equal(g.GridOf(1), g.GridOf(2));
            Assert.Equal(new[] { (1, 2) }, g.Links);
        }

        [Fact]
        public void TwoPolesBeyondRange_TwoGrids()
        {
            var g = PoleGraph.Build(new List<Pole> { P(1, 0), P(2, 25) });
            Assert.Equal(2, g.GridCount);
            Assert.NotEqual(g.GridOf(1), g.GridOf(2));
            Assert.Empty(g.Links);
        }

        [Fact]
        public void ChainOfThree_OneGrid()
        {
            var g = PoleGraph.Build(new List<Pole> { P(1, 0), P(2, 20), P(3, 40) });
            Assert.Equal(1, g.GridCount);
            Assert.Equal(new[] { (1, 2), (2, 3) }, g.Links);
            Assert.Equal(-1, g.GridOf(3));
            Assert.Equal(3, g.PolesIn(-1));
        }

        [Fact]
        public void LongPoleReachesShortPole()
        {
            var g = PoleGraph.Build(new List<Pole> { P(1, 0, link: 40), P(2, 30, link: 10) });
            Assert.Equal(new[] { (1, 2) }, g.Links);
            Assert.Equal(1, g.GridCount);
        }

        [Fact]
        public void AttachSite_NearestReachingPole()
        {
            var g = PoleGraph.Build(new List<Pole> { P(1, 0), P(2, 15) });
            int grid = g.AttachSite(S(9, 1), out int pole, out float d);
            Assert.Equal(2, pole);
            Assert.Equal(5f, d, 3);
            Assert.Equal(g.GridOf(2), grid);
        }

        [Fact]
        public void AttachSite_FootprintNotCentre()
        {
            var g = PoleGraph.Build(new List<Pole> { P(1, 0) });
            int grid = g.AttachSite(S(13, 2), out int pole, out float d);
            Assert.Equal(1, pole);
            Assert.Equal(11f, d, 3);
            Assert.Equal(-1, grid);
        }

        [Fact]
        public void AttachSite_NoneInRadius_ReturnsZero()
        {
            var g = PoleGraph.Build(new List<Pole> { P(1, 0) });
            Assert.Equal(0, g.AttachSite(S(20, 1), out int pole, out float d));
            Assert.Equal(0, pole);
            Assert.True(float.IsPositiveInfinity(d));
        }

        [Fact]
        public void SiteInTwoGrids_JoinsNearestOnly()
        {
            // Poles 30 m apart are separate grids; the site at x = 12 is within supply radius of both.
            var g = PoleGraph.Build(new List<Pole> { P(1, 0, supply: 14), P(2, 30, supply: 20) });
            Assert.Equal(2, g.GridCount);
            int grid = g.AttachSite(S(12, 0.5f), out int pole, out _);
            Assert.Equal(1, pole);
            Assert.Equal(-1, grid);
        }

        [Fact]
        public void GridIds_AreNegativeAndStable()
        {
            var a = PoleGraph.Build(new List<Pole> { P(5, 0), P(7, 10) });
            var b = PoleGraph.Build(new List<Pole> { P(5, 0), P(7, 10), P(9, 500) });
            Assert.Equal(-5, a.GridOf(7));
            Assert.Equal(a.GridOf(5), b.GridOf(5));
            Assert.Equal(a.GridOf(7), b.GridOf(7));
            Assert.Equal(-9, b.GridOf(9));
            Assert.Equal(0, b.GridOf(42));
        }
    }
}
