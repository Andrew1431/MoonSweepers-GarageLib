using System.Linq;
using Xunit;

namespace StructureLib.Tests
{
    public class RulesTests
    {
        [Fact] public void CargoRules_DuplicateValue_ReportsBothIds() { var e = Rules.CheckCargo(new[] { ("a.x", (byte)3), ("b.y", (byte)3) }); Assert.Single(e); Assert.Contains("a.x", e[0]); Assert.Contains("b.y", e[0]); Assert.Contains("3", e[0]); }
        [Fact] public void CargoRules_ValueBelow3_Rejected() { Assert.Contains(Rules.CheckCargo(new[] { ("a.x", (byte)2) }), m => m.Contains("a.x") && m.Contains("3..255")); }
        [Fact] public void CargoRules_Valid_NoErrors() { Assert.Empty(Rules.CheckCargo(new[] { ("a.x", (byte)3), ("b.y", (byte)255) })); }
        [Fact] public void ProcessorRules_MoreThan8Slots_Rejected() { var slots = Enumerable.Repeat((SlotRole.Input, 1.0), 9).ToList(); Assert.Contains(Rules.CheckProcessor("p", slots, false), m => m.Contains("8")); }
        [Fact] public void ProcessorRules_NeedsOneOutputUnlessPower() { Assert.NotEmpty(Rules.CheckProcessor("p", new[] { (SlotRole.Input, 1.0), (SlotRole.Batch, 1.0) }, false)); Assert.Empty(Rules.CheckProcessor("p", new[] { (SlotRole.Input, 1.0), (SlotRole.Batch, 1.0) }, true)); Assert.NotEmpty(Rules.CheckProcessor("p", new[] { (SlotRole.Input, 1.0), (SlotRole.Batch, 1.0), (SlotRole.Output, 1.0) }, true)); }
        [Fact] public void ProcessorRules_NeedsInputAndBatch() { Assert.NotEmpty(Rules.CheckProcessor("p", new[] { (SlotRole.Output, 1.0) }, false)); }
        [Fact] public void DonorRules_FoundationLikeRefused() { Assert.NotNull(Rules.CheckDonor("s", 101, new DonorTraits(true, Deck: true, false, false, false))); Assert.NotNull(Rules.CheckDonor("s", 74, new DonorTraits(true, false, BuildingPart: true, false, false))); Assert.NotNull(Rules.CheckDonor("s", 0, new DonorTraits(false, false, false, false, false))); Assert.Null(Rules.CheckDonor("s", 97, new DonorTraits(true, false, false, false, false))); }
    }
}
