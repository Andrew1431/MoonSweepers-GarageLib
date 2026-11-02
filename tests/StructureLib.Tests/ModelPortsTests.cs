using System.Collections.Generic;
using StructureLib;
using Xunit;

namespace StructureLib.Tests
{
    public class ModelPortsTests
    {
        [Fact]
        public void FromNodes_ReadsPortEmpties()
        {
            var ports = ModelPorts.FromNodes(new[]
            {
                ("Port_Sand in", (1f, 0.5f, 0f), (0f, 0f, 0f, 1f), (IReadOnlyDictionary<string, object>)new Dictionary<string, object> { ["output"] = 0f, ["cargo"] = "Sand" }),
                ("Body", (0f, 0f, 0f), (0f, 0f, 0f, 1f), new Dictionary<string, object>()),
            });
            var p = Assert.Single(ports);
            Assert.Equal("Sand in", p.Name);
            Assert.False(p.Output);
            Assert.Equal("Sand", p.CargoId);
            Assert.Equal((0f, 0f, -1f), p.Dir);
        }

        [Fact]
        public void FromNodes_RotatedPortPointsAlongLocalMinusZ()
        {
            var ports = ModelPorts.FromNodes(new[]
            {
                ("Port_Out", (0f, 0f, 0f), (0f, 0.7071068f, 0f, 0.7071068f), (IReadOnlyDictionary<string, object>)new Dictionary<string, object> { ["output"] = 1f, ["cargo"] = "ryanhartwig.example-grit" }),
            });
            Assert.True(ports[0].Output);
            // A 90° yaw about Unity Y turns (0,0,-1) into (-1,0,0).
            Assert.InRange(ports[0].Dir.x, -1.01f, -0.99f);
        }
    }
}
