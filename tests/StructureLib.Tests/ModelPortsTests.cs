using System;
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

        [Fact]
        public void Compose_PortUnderRotatedScaledParent()
        {
            const float s = 0.7071068f;
            var nodes = new (int, (float, float, float), (float, float, float, float), (float, float, float))[]
            {
                (-1, (5f, 5f, 5f), (0f, s, 0f, s), (3f, 3f, 3f)),  // the model root: ignored (it is the holder)
                (0, (1f, 0f, 0f), (0f, s, 0f, s), (2f, 2f, 2f)),   // a frame: at x 1, yawed 90 deg, scaled 2
                (1, (0f, 0.5f, -1f), (0f, 0f, 0f, 1f), (1f, 1f, 1f)), // the port under it
            };
            var c = ModelPorts.Compose(nodes);
            // (1,0,0) + yaw90 * (2 * (0,0.5,-1)) = (1,0,0) + (-2,1,0) = (-1,1,0); its flow (0,0,-1) turns to (-1,0,0).
            Assert.InRange(c[2].Pos.x, -1.001f, -0.999f);
            Assert.InRange(c[2].Pos.y, 0.999f, 1.001f);
            Assert.InRange(c[2].Pos.z, -0.001f, 0.001f);
            var d = ModelPorts.Direction(c[2].Rot);
            Assert.Equal((-1f, 0f, 0f), (MathF.Round(d.x, 4), MathF.Round(d.y, 4), MathF.Round(d.z, 4)));
            Assert.Equal((0f, 0f, 0f), c[0].Pos);
        }

        [Fact]
        public void Holder_ScalesThenOffsetsPortsLikeGeometry()
        {
            // A model whose lowest vertex is at y -0.1, under a holder of scale 2 (a donor of scale 0.5): offset +0.2.
            float offset = ModelPorts.HolderOffset(-0.1f, 2f);
            Assert.Equal(0.2f, offset, 4);
            var p = ModelPorts.ApplyHolder((1f, 0.425f, -2f), 2f, offset);
            Assert.Equal(2f, p.x, 4);
            Assert.Equal(1.05f, p.y, 4);
            Assert.Equal(-4f, p.z, 4);
            Assert.Equal(-0.3f, ModelPorts.HolderOffset(0.3f, 1f), 4);
        }
    }
}
