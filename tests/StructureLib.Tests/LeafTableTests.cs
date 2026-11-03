using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace StructureLib.Tests
{
    public class LeafTableTests
    {
        // IsSolarPanel's native answer: kinds 93..95.
        static bool Solar(long k) => k >= 93 && k <= 95;

        [Fact] public void KindKeys_AreZeroToMaxPlusExtras()
        {
            var keys = LeafTable<bool>.KindKeys(192, 1000);
            Assert.Equal(194, keys.Count);
            Assert.Equal(0, keys[0]);
            Assert.Equal(192, keys[192]);
            Assert.Equal(1000, keys[193]);
            Assert.Equal(193, LeafTable<bool>.KindKeys(192, 50).Count);   // an extra inside the range is not repeated
        }

        [Fact] public void Snapshot_CallsOnlyTheGivenKeys()
        {
            var asked = new List<long>();
            var t = LeafTable<bool>.Snapshot("IsSolarPanel", 1, LeafTable<bool>.KindKeys(192, 1000), k => { asked.Add(k); return Solar(k); }, false);
            Assert.Equal(194, asked.Count);
            Assert.Equal(194, t.Count);
            Assert.DoesNotContain(asked, k => k > 192 && k != 1000);
            Assert.Null(t.Stop);
            Assert.Equal("194 keys, 0..1000", t.Range);
            Assert.True(t.Answer(95, out bool a)); Assert.True(a);
            Assert.False(t.Answer(96, out bool b)); Assert.True(b);
        }

        [Fact] public void Answer_NotRecordedIsDefault()
        {
            var t = LeafTable<bool>.Snapshot("IsSolarPanel", 1, LeafTable<bool>.KindKeys(192, 1000), Solar, false);
            Assert.False(t.Answer(-1, out bool neg)); Assert.False(neg);
            Assert.False(t.Answer(193, out bool gap)); Assert.False(gap);
            Assert.False(t.Answer(529738499, out bool modded)); Assert.False(modded);
            var kg = LeafTable<double>.Snapshot("UnitKilograms", 6, new long[] { 0, 1, 2 }, c => c == 2 ? 2.0 : 1.0, 1.0);
            Assert.Equal(2.0, kg.Answer(2, out _));
            Assert.Equal(1.0, kg.Answer(250, out bool unknown)); Assert.False(unknown);
        }

        [Fact] public void PairKeys_SeparateKindAndCargo()
        {
            var pairs = new[] { 96, 97, 6 }.SelectMany(k => new byte[] { 0, 1, 2 }.Select(c => LeafTable<int>.PairKey(k, c))).ToList();
            var t = LeafTable<int>.Snapshot("InputSlot", 7, pairs, p => (p >> 8) == 96 && (p & 0xFF) == 1 ? 0 : -1, -1);
            Assert.Equal(9, t.Count);
            Assert.Equal(0, t.Answer(LeafTable<int>.PairKey(96, 1), out _));
            Assert.Equal(-1, t.Answer(LeafTable<int>.PairKey(97, 1), out bool rec)); Assert.True(rec);
            Assert.Equal(-1, t.Answer(LeafTable<int>.PairKey(96, 250), out bool cargo)); Assert.False(cargo);
            Assert.NotEqual(LeafTable<int>.PairKey(1, 0), LeafTable<int>.PairKey(0, 1));
        }

        [Fact] public void Snapshot_StopsAtFirstThrow()
        {
            int calls = 0;
            var t = LeafTable<int>.Snapshot("x", 2, Enumerable.Range(0, 100).Select(i => (long)i), k => { calls++; if (k == 10) throw new InvalidOperationException("boom"); return (int)k * 2; }, -1);
            Assert.Equal(10, t.Count);
            Assert.Equal(11, calls);
            Assert.Contains("key 10", t.Stop);
            Assert.Contains("boom", t.Stop);
            Assert.Equal(18, t.Answer(9, out bool a)); Assert.True(a);
            Assert.Equal(-1, t.Answer(10, out bool b)); Assert.False(b);
            Assert.Equal(-1, t.Answer(50, out _));
        }

        [Fact] public void Empty_AnswersDefaultEverywhere()
        {
            var t = LeafTable<int>.Empty("x", 3, 7, "missing method");
            Assert.Equal(0, t.Count);
            Assert.Equal("empty", t.Range);
            Assert.Equal("missing method", t.Stop);
            Assert.Equal(7, t.Answer(0, out bool inTable)); Assert.False(inTable);
        }

        [Fact] public void Snapshot_ThrowAtFirstKeyIsEmpty()
        {
            var t = LeafTable<bool>.Snapshot("x", 4, new long[] { 0, 1 }, _ => throw new MissingMethodException("gone"), false);
            Assert.Equal(0, t.Count);
            Assert.Equal("empty", t.Range);
            Assert.Contains("MissingMethodException", t.Stop);
        }
    }
}
