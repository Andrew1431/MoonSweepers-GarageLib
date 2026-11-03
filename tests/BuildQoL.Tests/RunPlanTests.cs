using BuildQoL.Pure;
using Xunit;

namespace BuildQoL.Tests
{
    public class RunPlanTests
    {
        [Theory]
        [InlineData(1, 1f, 10, 2)]
        [InlineData(1, 120f, 10, 2)]   // raw Windows notch: only the sign counts
        [InlineData(2, -1f, 10, 1)]
        [InlineData(1, -1f, 10, 1)]    // never below 1
        [InlineData(10, 1f, 10, 10)]   // never above max
        [InlineData(5, 0f, 10, 5)]
        [InlineData(32, 1f, 99, 32)]   // max hard-capped at 32
        [InlineData(3, 1f, 0, 1)]      // max clamped up to 1
        public void NextLength_StepsByWheelSign(int n, float wheel, int max, int expected) =>
            Assert.Equal(expected, RunPlan.NextLength(n, wheel, max));

        [Theory]
        [InlineData(10, 10)]
        [InlineData(0, 1)]
        [InlineData(-5, 1)]
        [InlineData(33, 32)]
        public void ClampMax_Bounds(int configured, int expected) => Assert.Equal(expected, RunPlan.ClampMax(configured));

        static float Cos(float deg) => (float)System.Math.Cos(deg * System.Math.PI / 180.0);

        [Fact]
        public void PickAxis_NoPrevious_TakesNearest()
        {
            // facing 30 deg off +fwd, 60 off +right
            var dots = new[] { Cos(30), Cos(150), Cos(60), Cos(120) };
            Assert.Equal(0, RunPlan.PickAxis(dots, -1));
        }

        [Fact]
        public void PickAxis_KeepsPreviousNear45()
        {
            // facing 44 deg off +fwd, 46 off +right: previously +right, the 2 deg gain is under the 10 deg hysteresis
            var dots = new[] { Cos(44), Cos(136), Cos(46), Cos(134) };
            Assert.Equal(2, RunPlan.PickAxis(dots, 2));
        }

        [Fact]
        public void PickAxis_SwitchesWhenClearlyBetter()
        {
            // facing 30 off +fwd, 60 off +right: 30 deg better, switch from +right
            var dots = new[] { Cos(30), Cos(150), Cos(60), Cos(120) };
            Assert.Equal(0, RunPlan.PickAxis(dots, 2));
        }

        [Fact]
        public void PickAxis_EitherSideOfHysteresis()
        {
            Assert.Equal(2, RunPlan.PickAxis(new[] { Cos(40.5f), Cos(139.5f), Cos(49.5f), Cos(130.5f) }, 2, 10f)); // 9 deg better: keep
            Assert.Equal(0, RunPlan.PickAxis(new[] { Cos(39.5f), Cos(140.5f), Cos(50.5f), Cos(129.5f) }, 2, 10f)); // 11 deg better: switch
        }

        [Fact]
        public void PickAxis_BadInput()
        {
            Assert.Equal(-1, RunPlan.PickAxis(new float[0], 0));
            Assert.Equal(1, RunPlan.PickAxis(new[] { 0f, 1f }, 7)); // out-of-range previous is ignored
        }

        [Theory]
        [InlineData(7.962f, true)]
        [InlineData(7.5f, true)]
        [InlineData(8.5f, true)]
        [InlineData(7.49f, false)]
        [InlineData(15.9f, false)]
        [InlineData(4000f, false)]
        [InlineData(0f, false)]
        public void StepOk_OnlySingleCellSteps(float m, bool ok) => Assert.Equal(ok, RunPlan.StepOk(m));

        [Theory]
        [InlineData(388087)]
        [InlineData(0)]
        public void EncodeDecode_RoundTrip(int id)
        {
            Assert.True(RunPlan.Encode(id) < 0);
            Assert.Equal(id, RunPlan.Decode(RunPlan.Encode(id)));
        }

        [Fact]
        public void Decode_NonSquareCell() => Assert.Equal(-1, RunPlan.Decode(5));

        [Fact]
        public void Prompt_Clear()
        {
            Assert.Equal("Run 6 · $12", RunPlan.Prompt(6, 2, 0, 0, true, out bool ok));
            Assert.True(ok);
        }

        [Fact]
        public void Prompt_Blocked()
        {
            Assert.Equal("Run 10 · 1 blocked", RunPlan.Prompt(10, 2, 1, 0, true, out bool ok));
            Assert.False(ok);
        }

        [Fact]
        public void Prompt_NoMoney()
        {
            Assert.Equal("Run 4 · $8 · not enough money", RunPlan.Prompt(4, 2, 0, 0, false, out bool ok));
            Assert.False(ok);
        }

        [Fact]
        public void Prompt_OutOfReach()
        {
            Assert.Equal("Run 6 · $12 · 3 out of reach", RunPlan.Prompt(6, 2, 0, 3, true, out bool ok));
            Assert.False(ok);
        }

        [Theory]
        [InlineData(0, true, false)]
        [InlineData(1, true, true)]
        [InlineData(0, false, true)]
        public void Refuse(int blocked, bool affordable, bool refuse) => Assert.Equal(refuse, RunPlan.Refuse(blocked, affordable));

        [Theory]
        [InlineData("Move closer to the build site", RunPlan.OutOfReach)]
        [InlineData("Blocked by a player or object", RunPlan.Blocked)]
        [InlineData("Not enough money", RunPlan.NoMoney)]
        [InlineData("", RunPlan.Refused)]
        [InlineData(null, RunPlan.Refused)]
        [InlineData("Something else", RunPlan.Refused)]
        public void Category_MapsGameReasons(string reason, string expected) => Assert.Equal(expected, RunPlan.Category(reason));

        [Theory]
        [InlineData("99980", 99980)]
        [InlineData("99,980.00", 99980)]
        [InlineData("$12", 12)]
        public void TryParseMoney_Parses(string s, double expected)
        {
            Assert.True(RunPlan.TryParseMoney(s, out decimal v));
            Assert.Equal((decimal)expected, v);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("lots")]
        public void TryParseMoney_Rejects(string s) => Assert.False(RunPlan.TryParseMoney(s, out _));

        [Theory]
        [InlineData(30f, 25f, 30f)]
        [InlineData(30f, 32f, 30f)]
        [InlineData(30f, 40f, 39f)]
        public void ReachFor(float reach, float dist, float expected) => Assert.Equal(expected, RunPlan.ReachFor(reach, dist), 3);

        [Fact]
        public void Tally_AllBuilt()
        {
            var t = new RunTally(6);
            for (int i = 0; i < 6; i++) t.AddBuilt();
            Assert.Equal("Built 6 of 6", t.Notice());
        }

        [Fact]
        public void Tally_ByReason()
        {
            var t = new RunTally(10);
            for (int i = 0; i < 8; i++) t.AddBuilt();
            t.AddRefused(RunPlan.Blocked);
            t.AddRefused(RunPlan.OutOfReach);
            Assert.Equal("Built 8 of 10 (1 blocked, 1 out of reach)", t.Notice());
            Assert.Equal(1, t.Count(RunPlan.Blocked));
        }

        [Fact]
        public void Tally_Stopped()
        {
            var t = new RunTally(6);
            t.AddBuilt();
            t.Stop(RunPlan.OutOfReach);
            t.Stop(RunPlan.Blocked); // first stop wins
            Assert.Equal("Built 1 of 6 (stopped: out of reach)", t.Notice());
        }

        [Fact]
        public void Tally_SameReasonCounted()
        {
            var t = new RunTally(3);
            t.AddBuilt();
            t.AddRefused(RunPlan.Blocked);
            t.AddRefused(RunPlan.Blocked);
            Assert.Equal("Built 1 of 3 (2 blocked)", t.Notice());
        }
    }
}
