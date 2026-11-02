using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace StructureLib.Tests
{
    public class ProcessorCycleTests
    {
        // Grinder: Input dust 10, Batch dust 2, Output grit 10.
        static readonly List<(SlotRole Role, byte Cargo, double Capacity)> Grinder = new() { (SlotRole.Input, 0, 10), (SlotRole.Batch, 0, 2), (SlotRole.Output, 250, 10) };
        // Two inputs feeding two batches, one output (press-like).
        static readonly List<(SlotRole Role, byte Cargo, double Capacity)> Press = new() { (SlotRole.Input, 0, 12), (SlotRole.Input, 1, 12), (SlotRole.Batch, 0, 1), (SlotRole.Batch, 1, 1), (SlotRole.Output, 2, 10) };

        [Fact]
        public void StartMoves_FillsEachBatchFromItsInput()
        {
            var moves = ProcessorCycle.StartMoves(Grinder, new[] { 6.0, 0, 0 }, false);
            Assert.Equal(new[] { (0, 1, 2.0) }, moves);
        }

        [Fact]
        public void StartMoves_TwoBatches_TwoInputs()
        {
            var moves = ProcessorCycle.StartMoves(Press, new[] { 3.0, 3.0, 0, 0, 0 }, false);
            Assert.Equal(new[] { (0, 2, 1.0), (1, 3, 1.0) }, moves);
        }

        [Fact] public void StartMoves_NotEnoughInput_IsNull() => Assert.Null(ProcessorCycle.StartMoves(Grinder, new[] { 1.5, 0, 0 }, false));
        [Fact] public void StartMoves_OneInputShort_IsNull() => Assert.Null(ProcessorCycle.StartMoves(Press, new[] { 3.0, 0.5, 0, 0, 0 }, false));
        [Fact] public void StartMoves_BatchStillLoaded_IsNull() => Assert.Null(ProcessorCycle.StartMoves(Grinder, new[] { 6.0, 2, 0 }, false));
        [Fact] public void StartMoves_OutputWouldOverflow_IsNull() => Assert.Null(ProcessorCycle.StartMoves(Grinder, new[] { 6.0, 0, 9 }, false));
        [Fact] public void StartMoves_OutputExactlyFills_Starts() => Assert.NotNull(ProcessorCycle.StartMoves(Grinder, new[] { 6.0, 0, 8 }, false));

        [Fact]
        public void StartMoves_TwoBatchesShareOneInput_NeedBothAmounts()
        {
            var slots = new List<(SlotRole, byte, double)> { (SlotRole.Input, 0, 10), (SlotRole.Batch, 0, 2), (SlotRole.Batch, 0, 3), (SlotRole.Output, 1, 10) };
            Assert.Null(ProcessorCycle.StartMoves(slots, new[] { 4.0, 0, 0, 0 }, false));
            Assert.Equal(new[] { (0, 1, 2.0), (0, 2, 3.0) }, ProcessorCycle.StartMoves(slots, new[] { 5.0, 0, 0, 0 }, false));
        }

        [Fact]
        public void StartMoves_PowerProducer_HasNoOutputToFill()
        {
            var slots = new List<(SlotRole, byte, double)> { (SlotRole.Input, 3, 10), (SlotRole.Batch, 3, 1) };
            Assert.Equal(new[] { (0, 1, 1.0) }, ProcessorCycle.StartMoves(slots, new[] { 1.0, 0 }, true));
        }

        [Fact] public void StartMoves_NoMatchingInput_IsNull() => Assert.Null(ProcessorCycle.StartMoves(new List<(SlotRole, byte, double)> { (SlotRole.Input, 1, 10), (SlotRole.Batch, 0, 2), (SlotRole.Output, 2, 10) }, new[] { 10.0, 0, 0 }, false));

        [Fact] public void BatchSlots_AreTheBatchIndexes() => Assert.Equal(new[] { 2, 3 }, ProcessorCycle.BatchSlots(Press.Select(s => s.Role).ToList()));

        // A fuel generator (2 kWh battery): each burned batch stores half the battery, capped; no burn into a full battery.
        [Fact] public void Burn_AddsHalfTheBattery() => Assert.Equal(1.0, ProcessorCycle.ChargeAfterBurn(0, 2));
        [Fact] public void Burn_TwoCyclesFill() => Assert.Equal(2.0, ProcessorCycle.ChargeAfterBurn(ProcessorCycle.ChargeAfterBurn(0, 2), 2));
        [Fact] public void Burn_IsCappedAtTheBattery() => Assert.Equal(2.0, ProcessorCycle.ChargeAfterBurn(1.9, 2));
        [Fact] public void CanBurn_BelowFull() => Assert.True(ProcessorCycle.CanBurn(1.999, 2));
        [Fact] public void CanBurn_NotWhenFull() { Assert.False(ProcessorCycle.CanBurn(2, 2)); Assert.False(ProcessorCycle.CanBurn(2.5, 2)); }
    }
}
