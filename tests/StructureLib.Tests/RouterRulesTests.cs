using System.Linq;
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

        // A merger with three inputs, one hop a frame at most (the output entrance clears once a frame): every input
        // with a canister waiting offers it each frame in the given order; the turn advances after each accepted hop,
        // as RouterHop does. arrives(frame, k) puts a canister at input k's end (it waits there until served).
        static System.Collections.Generic.List<(int frame, int input)> Merge(int frames, int[] order, System.Func<int, int, bool> arrives)
        {
            int turn = 0;
            var pending = new bool[3];
            var served = new System.Collections.Generic.List<(int, int)>();
            for (int f = 0; f < frames; f++)
            {
                for (int k = 0; k < 3; k++) pending[k] |= arrives(f, k);
                foreach (int input in order)
                {
                    if (!pending[input] || !RouterRules.MergerAccepts(turn, input, pending, true)) continue;
                    served.Add((f, input));
                    pending[input] = false;
                    turn = RouterRules.Advance(input, 3);
                    break;
                }
            }
            return served;
        }

        [Fact]
        public void MergerAccepts_ThreeInputsEachServedOncePerCycle()
        {
            foreach (var order in new[] { new[] { 0, 1, 2 }, new[] { 2, 1, 0 }, new[] { 1, 2, 0 } })
            {
                var s = Merge(30, order, (f, k) => true).Select(x => x.input).ToArray();
                Assert.Equal(30, s.Length);
                for (int c = 0; c + 3 <= s.Length; c += 3)
                    Assert.Equal(new[] { 0, 1, 2 }, s.Skip(c).Take(3).OrderBy(x => x).ToArray());
            }
        }

        [Fact]
        public void MergerAccepts_AlwaysWaitingInputStarvesNone()
        {
            // Input 0 always has a canister waiting; 1 gets one every 5 frames, 2 every 7: each of theirs is served
            // within three hops of arriving.
            foreach (var order in new[] { new[] { 0, 1, 2 }, new[] { 2, 1, 0 } })
            {
                var s = Merge(70, order, (f, k) => k == 0 || (k == 1 && f % 5 == 0) || (k == 2 && f % 7 == 0));
                for (int k = 1; k <= 2; k++)
                {
                    int every = k == 1 ? 5 : 7;
                    for (int arrive = 0; arrive < 60; arrive += every)
                    {
                        var hit = s.FirstOrDefault(x => x.input == k && x.frame >= arrive);
                        Assert.True(hit.frame >= arrive && hit.input == k, $"input {k} arrival at {arrive} never served");
                        Assert.True(s.Count(x => x.frame >= arrive && x.frame < hit.frame) <= 2, $"input {k} arrival at {arrive} waited past {s.Count(x => x.frame >= arrive && x.frame < hit.frame)} hops");
                    }
                }
                Assert.Contains(s, x => x.input == 0);
            }
        }

        [Fact]
        public void EmittedId_CounterAdvancedOnce()
        {
            var present = new System.Collections.Generic.HashSet<int> { 5, 6, 7 };
            var was = new System.Collections.Generic.HashSet<int> { 5, 6 };
            // Ids handed out before the increment (id = next++): next 7 -> 8, canister 7 appeared.
            Assert.Equal(7, RouterRules.EmittedId(7, 8, was.Contains, present.Contains));
            // After it (id = ++next): next 6 -> 7, canister 7 appeared.
            Assert.Equal(7, RouterRules.EmittedId(6, 7, was.Contains, present.Contains));
            // Not exactly one step, no new id, both ids new, or no lookups: -1 (the caller falls back).
            Assert.Equal(-1, RouterRules.EmittedId(6, 8, was.Contains, present.Contains));
            Assert.Equal(-1, RouterRules.EmittedId(5, 6, was.Contains, present.Contains));
            Assert.Equal(-1, RouterRules.EmittedId(7, 8, k => false, k => k == 7 || k == 8));
            Assert.Equal(-1, RouterRules.EmittedId(7, 8, null, present.Contains));
        }

        // The footprint override's half (Footprint / 2) is computed on the game's Vector2 in Injection, not in pure code;
        // the pure part is CheckDefinition at the Vector2 -> (x, y) tuple boundary.
        [Fact]
        public void CheckDefinition_FootprintTupleBoundary()
        {
            Assert.Empty(RouterRules.CheckDefinition("b", true, false, false, (1.4f, 1.4f)));
            Assert.Empty(RouterRules.CheckDefinition("b", true, false, false, null));
            Assert.Single(RouterRules.CheckDefinition("b", true, false, false, (1.4f, -0.01f)));
            Assert.Single(RouterRules.CheckDefinition("b", true, false, false, (float.NaN, 1.4f)));
            Assert.Contains("1.4 x 0", RouterRules.CheckDefinition("b", false, false, false, (1.4f, 0f))[0]);
        }
    }
}
