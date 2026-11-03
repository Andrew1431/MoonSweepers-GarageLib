using System.Linq;
using Xunit;

namespace StructureLib.Tests
{
    public class RulesTests
    {
        [Fact] public void CargoRules_DuplicateValue_ReportsBothIds() { var e = Rules.CheckCargo(new[] { ("a.x", (byte)3), ("b.y", (byte)3) }); Assert.Single(e); Assert.Contains("a.x", e[0]); Assert.Contains("b.y", e[0]); Assert.Contains("3", e[0]); }
        [Fact] public void CargoRules_ValueBelow3_Rejected() { Assert.Contains(Rules.CheckCargo(new[] { ("a.x", (byte)2) }), m => m.Contains("a.x") && m.Contains("3..255")); }
        [Fact] public void CargoRules_Valid_NoErrors() { Assert.Empty(Rules.CheckCargo(new[] { ("a.x", (byte)3), ("b.y", (byte)255) })); }
        [Fact] public void CargoOffenders_NamesOnlyTheClashingIds() { var o = Rules.CargoOffenders(new[] { ("a.x", (byte)3), ("b.y", (byte)3), ("c.z", (byte)4) }); Assert.Equal(new[] { "a.x", "b.y" }, o.OrderBy(i => i)); }
        [Fact] public void CargoOffenders_IncludesBadValueAndDuplicateId() { var o = Rules.CargoOffenders(new[] { ("low", (byte)1), ("dup", (byte)5), ("dup", (byte)6), ("ok", (byte)7) }); Assert.Equal(new[] { "dup", "low" }, o.OrderBy(i => i)); }
        [Fact] public void SlotCargoRules_InvalidHandle_Rejected() { var e = Rules.CheckSlotCargo("s", new[] { ("in", (string)null, false) }, _ => true); Assert.Single(e); Assert.Contains("'s'", e[0]); Assert.Contains("in", e[0]); }
        [Fact] public void SlotCargoRules_UnregisteredModdedId_Rejected() { var e = Rules.CheckSlotCargo("s", new[] { ("in", "a.x", true) }, _ => false); Assert.Single(e); Assert.Contains("'s'", e[0]); Assert.Contains("in", e[0]); Assert.Contains("a.x", e[0]); }
        [Fact] public void SlotCargoRules_VanillaAndRegistered_Accepted() { Assert.Empty(Rules.CheckSlotCargo("s", new[] { ("a", "dust", true), ("b", "sand", true), ("c", "stone", true), ("d", "a.x", true) }, id => id == "a.x")); }
        [Fact] public void ProcessorRules_MoreThan8Slots_Rejected() { var slots = Enumerable.Repeat((SlotRole.Input, 1.0), 9).ToList(); Assert.Contains(Rules.CheckProcessor("p", slots, false), m => m.Contains("8")); }
        [Fact] public void ProcessorRules_NeedsOneOutputUnlessPower() { Assert.NotEmpty(Rules.CheckProcessor("p", new[] { (SlotRole.Input, 1.0), (SlotRole.Batch, 1.0) }, false)); Assert.Empty(Rules.CheckProcessor("p", new[] { (SlotRole.Input, 1.0), (SlotRole.Batch, 1.0) }, true)); Assert.NotEmpty(Rules.CheckProcessor("p", new[] { (SlotRole.Input, 1.0), (SlotRole.Batch, 1.0), (SlotRole.Output, 1.0) }, true)); }
        [Fact] public void ProcessorRules_NeedsInputAndBatch() { Assert.NotEmpty(Rules.CheckProcessor("p", new[] { (SlotRole.Output, 1.0) }, false)); }
        [Fact] public void DonorRules_FoundationLikeRefused() { Assert.NotNull(Rules.CheckDonor("s", 101, new DonorTraits(true, Deck: true, false, false, false))); Assert.NotNull(Rules.CheckDonor("s", 74, new DonorTraits(true, false, BuildingPart: true, false, false))); Assert.NotNull(Rules.CheckDonor("s", 0, new DonorTraits(false, false, false, false, false))); Assert.Null(Rules.CheckDonor("s", 97, new DonorTraits(true, false, false, false, false))); }
    }
}
