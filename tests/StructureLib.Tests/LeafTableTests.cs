using System;
using Xunit;

namespace StructureLib.Tests
{
    public class LeafTableTests
    {
        // IsSolarPanel's native answer: kinds 93..95.
        static bool Solar(int k) => k >= 93 && k <= 95;

        [Fact] public void Snapshot_RecordsWholeBound()
        {
            var t = LeafTable<bool>.Snapshot("IsSolarPanel", 1, 1024, Solar, false);
            Assert.Equal(1024, t.Count);
            Assert.Null(t.Stop);
            Assert.Equal("0..1023", t.Range);
            for (int k = 0; k < 1024; k++) { Assert.Equal(Solar(k), t.Answer(k, out bool inTable)); Assert.True(inTable); }
        }

        [Fact] public void Snapshot_StopsAtFirstThrow()
        {
            int calls = 0;
            var t = LeafTable<int>.Snapshot("x", 2, 100, k => { calls++; if (k == 10) throw new InvalidOperationException("boom"); return k * 2; }, -1);
            Assert.Equal(10, t.Count);
            Assert.Equal(11, calls);
            Assert.Contains("key 10", t.Stop);
            Assert.Contains("boom", t.Stop);
            Assert.Equal(18, t.Answer(9, out bool a)); Assert.True(a);
            Assert.Equal(-1, t.Answer(10, out bool b)); Assert.False(b);
            Assert.Equal(-1, t.Answer(50, out _));
        }

        [Fact] public void Answer_OutsideTableIsDefault()
        {
            var t = LeafTable<bool>.Snapshot("IsSolarPanel", 1, 1024, Solar, false);
            Assert.False(t.Answer(-1, out bool neg)); Assert.False(neg);
            Assert.False(t.Answer(1024, out bool hi)); Assert.False(hi);
            Assert.False(t.Answer(529738499, out bool modded)); Assert.False(modded);
            var d = LeafTable<double>.Snapshot("UnitKilograms", 6, 256, c => c == 2 ? 2.0 : 1.0, 1.0);
            Assert.Equal(2.0, d.Answer(2, out _));
            Assert.Equal(1.0, d.Answer(300, out bool outside)); Assert.False(outside);
        }

        [Fact] public void Empty_AnswersDefaultEverywhere()
        {
            var t = LeafTable<int>.Empty("x", 3, 7, "missing method");
            Assert.Equal(0, t.Count);
            Assert.Equal("empty", t.Range);
            Assert.Equal("missing method", t.Stop);
            Assert.Equal(7, t.Answer(0, out bool inTable)); Assert.False(inTable);
        }

        [Fact] public void Snapshot_ThrowAtZeroIsEmpty()
        {
            var t = LeafTable<bool>.Snapshot("x", 4, 10, _ => throw new MissingMethodException("gone"), false);
            Assert.Equal(0, t.Count);
            Assert.Contains("MissingMethodException", t.Stop);
        }
    }
}
