using StructureLib;
using Xunit;

namespace StructureLib.Tests
{
    public class PortRulesTests
    {
        [Fact]
        public void CheckPorts_InputCargoMustMatchASlot()
        {
            var e = PortRules.CheckPorts("g", new[] { ("Dust in", false, "dust") }, new[] { (SlotRole.Input, "sand") });
            Assert.Contains(e, s => s.Contains("port 'Dust in' takes dust"));
        }

        [Fact]
        public void CheckPorts_OutputCargoMustBeTheOutputSlots()
        {
            var e = PortRules.CheckPorts("g", new[] { ("Grit out", true, "dust") }, new[] { (SlotRole.Input, "dust"), (SlotRole.Output, "ryanhartwig.example-grit") });
            Assert.Single(e);
            Assert.Contains("emits dust but the output slot holds ryanhartwig.example-grit", e[0]);
        }

        [Fact]
        public void CheckPorts_DuplicateNameAndSecondOutputRefused()
        {
            var e = PortRules.CheckPorts("g", new[] { ("A", true, "x"), ("A", true, "x") }, new[] { (SlotRole.Output, "x") });
            Assert.Equal(2, e.Count);
        }

        [Fact]
        public void CheckPorts_Valid()
        {
            Assert.Empty(PortRules.CheckPorts("g", new[] { ("Dust in", false, "dust"), ("Grit out", true, "grit") },
                new[] { (SlotRole.Input, "dust"), (SlotRole.Batch, "dust"), (SlotRole.Output, "grit") }));
        }

        [Fact]
        public void WarnOutsideFootprint_OnlyBeyondReach()
        {
            Assert.Null(PortRules.WarnOutsideFootprint("g", "p", (2f, 0f, 0f), (1.5f, 1.5f), 1f));
            Assert.NotNull(PortRules.WarnOutsideFootprint("g", "p", (4f, 0f, 0f), (1.5f, 1.5f), 1f));
        }

        [Fact]
        public void PortName_RoundTripsTheCargoByte()
        {
            Assert.Equal("sl:250:Grit out", PortRules.EncodeName(250, "Grit out"));
            Assert.True(PortRules.TryDecodeCargo("sl:250:Grit out", out byte c) && c == 250);
            Assert.True(PortRules.TryDecodeCargo("sl:0:a:b", out byte d) && d == 0);
        }

        [Fact]
        public void PortName_VanillaAndMalformedNamesAreNotDecoded()
        {
            foreach (var n in new[] { "IN_SAND", "OUT", null, "sl:", "sl::x", "sl:256:x", "sl:-1:x", "sl:12" })
                Assert.False(PortRules.TryDecodeCargo(n, out _), n ?? "null");
        }
    }
}
