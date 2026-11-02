using StructureLib;
using Xunit;

namespace StructureLib.Tests
{
    public class RouterRulesTests
    {
        [Fact] public void CheckDefinition_RouterExcludesProcessorAndPower() { var e = RouterRules.CheckDefinition("b", true, true, true, null); Assert.Equal(2, e.Count); }
        [Fact] public void CheckDefinition_FootprintMustBePositive() { Assert.Single(RouterRules.CheckDefinition("b", false, false, false, (0f, 1.4f))); }
        [Fact] public void CheckPorts_SplitterNeedsOneInput() { var e = RouterRules.CheckPorts("b", RouterMode.Splitter, new[] { ("A", false), ("B", false), ("Out", true) }); Assert.Single(e); Assert.Contains("'b'", e[0]); }
        [Fact] public void CheckPorts_SplitterOneToThreeOutputs() { Assert.Empty(RouterRules.CheckPorts("b", RouterMode.Splitter, new[] { ("In", false), ("F", true), ("L", true), ("R", true) })); Assert.Single(RouterRules.CheckPorts("b", RouterMode.Splitter, new[] { ("In", false), ("F", true), ("L", true), ("R", true), ("X", true) })); }
        [Fact] public void CheckPorts_MergerOneOutput() { Assert.Single(RouterRules.CheckPorts("b", RouterMode.Merger, new[] { ("L", false), ("Out", true), ("Out2", true) })); }
        [Fact] public void PickOutput_RoundRobinSkipsBlocked() { Assert.Equal(2, RouterRules.PickOutput(1, new[] { true, false, true })); Assert.Equal(0, RouterRules.PickOutput(2, new[] { true, false, false })); Assert.Equal(-1, RouterRules.PickOutput(0, new[] { false, false })); }
        [Fact] public void PickOutput_SingleOutput() { Assert.Equal(0, RouterRules.PickOutput(5, new[] { true })); }
        [Fact] public void Advance_Wraps() { Assert.Equal(0, RouterRules.Advance(2, 3)); Assert.Equal(0, RouterRules.Advance(0, 0)); }
        [Fact] public void RouterPorts_AreDust() { Assert.Equal("dust", RouterRules.PortCargoId); Assert.Equal(PortRules.VanillaCargoId("IN"), RouterRules.PortCargoId); }
        [Fact] public void MergerAccepts_YieldsToEarlierWaitingInput() { Assert.False(RouterRules.MergerAccepts(0, 2, new[] { true, false, true }, true)); Assert.True(RouterRules.MergerAccepts(0, 2, new[] { false, false, true }, true)); Assert.True(RouterRules.MergerAccepts(2, 2, new[] { true, true, true }, true)); }
        [Fact] public void MergerAccepts_OutputBlocked() { Assert.False(RouterRules.MergerAccepts(0, 0, new[] { true }, false)); }
        [Fact] public void MergerAccepts_SingleInput() { Assert.True(RouterRules.MergerAccepts(0, 0, new[] { true }, true)); }
    }
}
