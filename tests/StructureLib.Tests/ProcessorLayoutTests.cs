using System.Collections.Generic;
using Xunit;

namespace StructureLib.Tests
{
    public class ProcessorLayoutTests
    {
        static readonly List<(SlotRole Role, byte Cargo)> Grinder = new() { (SlotRole.Input, 0), (SlotRole.Batch, 0), (SlotRole.Output, 250) };
        static readonly List<(SlotRole Role, byte Cargo)> TwoInputs = new() { (SlotRole.Input, 0), (SlotRole.Input, 1), (SlotRole.Batch, 0), (SlotRole.Batch, 1), (SlotRole.Input, 0), (SlotRole.Output, 2) };

        [Fact] public void OutputIndex_IsTheOutputSlot() => Assert.Equal(2, ProcessorLayout.OutputIndex(new[] { SlotRole.Input, SlotRole.Batch, SlotRole.Output }, false));
        [Fact] public void OutputIndex_PowerProducer_IsMinusOne() => Assert.Equal(-1, ProcessorLayout.OutputIndex(new[] { SlotRole.Input, SlotRole.Batch }, true));
        [Fact] public void InputSlot_FirstMatchingInput() { Assert.Equal(0, ProcessorLayout.InputSlot(TwoInputs, 0)); Assert.Equal(1, ProcessorLayout.InputSlot(TwoInputs, 1)); }
        [Fact] public void InputSlot_IgnoresBatchAndOutputSlots() { Assert.Equal(-1, ProcessorLayout.InputSlot(Grinder, 250)); Assert.Equal(-1, ProcessorLayout.InputSlot(TwoInputs, 2)); }
        [Fact] public void InputSlot_UnknownCargo_IsMinusOne() => Assert.Equal(-1, ProcessorLayout.InputSlot(Grinder, 2));

        [Fact]
        public void AcceptsText_JoinsDistinctInputCargoWithOr()
        {
            var slots = new List<(SlotRole, string)> { (SlotRole.Input, "dust"), (SlotRole.Input, "sand"), (SlotRole.Batch, "dust"), (SlotRole.Input, "dust"), (SlotRole.Output, "stone") };
            Assert.Equal("dust or sand", ProcessorLayout.AcceptsText(slots));
        }

        [Fact] public void RecordMismatch_SameLayout_IsNull() => Assert.Null(ProcessorLayout.RecordMismatch(new[] { "dust", "dust", "a.grit" }, 3, new[] { "dust", "dust", "a.grit" }));
        [Fact] public void RecordMismatch_DifferentSlotCount() => Assert.Contains("3", ProcessorLayout.RecordMismatch(new[] { "dust", "dust", "a.grit" }, 3, new[] { "dust", "a.grit" }));
        [Fact] public void RecordMismatch_StoresCountDiffers() => Assert.NotNull(ProcessorLayout.RecordMismatch(new[] { "dust", "dust", "a.grit" }, 2, new[] { "dust", "dust", "a.grit" }));
        [Fact] public void RecordMismatch_DifferentCargo_NamesTheSlot() => Assert.Contains("a.grit", ProcessorLayout.RecordMismatch(new[] { "dust", "dust", "a.grit" }, 3, new[] { "dust", "dust", "b.sand" }));
        [Fact] public void RecordMismatch_NullCargoList() => Assert.NotNull(ProcessorLayout.RecordMismatch(null, 3, new[] { "dust", "dust", "a.grit" }));
    }
}
