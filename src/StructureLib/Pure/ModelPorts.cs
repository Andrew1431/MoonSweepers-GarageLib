using System;
using System.Collections.Generic;

namespace StructureLib
{
    /// <summary>
    /// Belt ports read from a model file's nodes (shared/blender/kit.py <c>port</c>): every node named <c>Port_&lt;name&gt;</c>.
    /// Positions and rotations are already in Unity axes (the exporter maps Blender (x, y, z) to Unity (-x, z, -y)), so the
    /// empty's Blender local +Y flow axis is Unity -Z: direction = rotation * (0, 0, -1).
    /// </summary>
    public static class ModelPorts
    {
        public const string Prefix = "Port_";

        public static List<(string Name, (float x, float y, float z) Pos, (float x, float y, float z) Dir, bool Output, string CargoId)> FromNodes(
            IEnumerable<(string Name, (float, float, float) Pos, (float x, float y, float z, float w) Rot, IReadOnlyDictionary<string, object> Props)> nodes)
        {
            var result = new List<(string, (float, float, float), (float, float, float), bool, string)>();
            if (nodes == null) return result;
            foreach (var n in nodes)
            {
                if (n.Name == null || !n.Name.StartsWith(Prefix, StringComparison.Ordinal) || n.Name.Length == Prefix.Length) continue;
                var props = n.Props;
                bool output = props != null && props.TryGetValue("output", out var o) && o is float f && f != 0f;
                string cargo = props != null && props.TryGetValue("cargo", out var c) ? c as string : null;
                result.Add((n.Name.Substring(Prefix.Length), n.Pos, Rotate(n.Rot, (0f, 0f, -1f)), output, cargo));
            }
            return result;
        }

        // q * v * q^-1, normalised (a non-unit quaternion from a scaled empty still gives a unit direction).
        static (float x, float y, float z) Rotate((float x, float y, float z, float w) q, (float x, float y, float z) v)
        {
            float tx = 2f * (q.y * v.z - q.z * v.y);
            float ty = 2f * (q.z * v.x - q.x * v.z);
            float tz = 2f * (q.x * v.y - q.y * v.x);
            float x = v.x + q.w * tx + (q.y * tz - q.z * ty);
            float y = v.y + q.w * ty + (q.z * tx - q.x * tz);
            float z = v.z + q.w * tz + (q.x * ty - q.y * tx);
            float len = (float)Math.Sqrt(x * x + y * y + z * z);
            if (len < 1e-6f) return v;
            return (Clean(x / len), Clean(y / len), Clean(z / len));
        }

        // Rounds float noise (1e-8) to an exact 0 so an axis-aligned port prints and compares cleanly.
        static float Clean(float a) => Math.Abs(a) < 1e-6f ? 0f : a;
    }
}
