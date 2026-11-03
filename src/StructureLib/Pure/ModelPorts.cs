using System;
using System.Collections.Generic;

namespace StructureLib
{
    /// <summary>
    /// Belt ports read from a model file's nodes (shared/blender/kit.py <c>port</c>): every node named <c>Port_&lt;name&gt;</c>.
    /// Positions and rotations are already in Unity axes (the exporter maps Blender (x, y, z) to Unity (-x, z, -y)), so the
    /// empty's Blender local +Y flow axis is Unity -Z: direction = rotation * (0, 0, -1).
    /// Node transforms in the file are parent-relative: <see cref="Compose"/> takes them into model-root space (as
    /// MeshModel.Instantiate builds them under the holder), and <see cref="ApplyHolder"/> takes a model-root point into
    /// structure space through the holder (uniform scale, then the floor offset along Y).
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
                result.Add((n.Name.Substring(Prefix.Length), n.Pos, Direction(n.Rot), output, cargo));
            }
            return result;
        }

        /// <summary>The flow direction of a node with this rotation: rotation * (0, 0, -1), normalised.</summary>
        public static (float x, float y, float z) Direction((float x, float y, float z, float w) rot)
        {
            var d = Rotate(rot, (0f, 0f, -1f));
            float len = (float)Math.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z);
            if (len < 1e-6f) return (0f, 0f, -1f);
            return (Clean(d.x / len), Clean(d.y / len), Clean(d.z / len));
        }

        /// <summary>
        /// Model-root-space transform of every node, from the file's parent-relative ones. Node 0 is the model root itself
        /// (MeshModel.Instantiate maps it onto the holder, ignoring its own transform), so it is the identity; a node whose
        /// parent index is negative hangs off the root. Position = parentPos + parentRot * (parentScale * localPos),
        /// rotation = parentRot * localRot, scale = parentScale * localScale (component-wise).
        /// </summary>
        public static ((float x, float y, float z) Pos, (float x, float y, float z, float w) Rot, (float x, float y, float z) Scale)[] Compose(
            IReadOnlyList<(int Parent, (float x, float y, float z) Pos, (float x, float y, float z, float w) Rot, (float x, float y, float z) Scale)> nodes)
        {
            var result = new ((float x, float y, float z), (float x, float y, float z, float w), (float x, float y, float z))[nodes?.Count ?? 0];
            for (int i = 0; i < result.Length; i++)
            {
                if (i == 0) { result[i] = ((0f, 0f, 0f), (0f, 0f, 0f, 1f), (1f, 1f, 1f)); continue; }
                var n = nodes[i];
                int p = n.Parent >= 0 && n.Parent < i ? n.Parent : 0;
                var (pp, pr, ps) = result[p];
                result[i] = (Point((pp, pr, ps), n.Pos), Multiply(pr, n.Rot), (ps.x * n.Scale.x, ps.y * n.Scale.y, ps.z * n.Scale.z));
            }
            return result;
        }

        /// <summary>A point in a composed node's local space, in model-root space.</summary>
        public static (float x, float y, float z) Point(((float x, float y, float z) Pos, (float x, float y, float z, float w) Rot, (float x, float y, float z) Scale) node, (float x, float y, float z) local)
        {
            var r = Rotate(node.Rot, (node.Scale.x * local.x, node.Scale.y * local.y, node.Scale.z * local.z));
            return (node.Pos.x + r.x, node.Pos.y + r.y, node.Pos.z + r.z);
        }

        /// <summary>A model-root point in structure space through the holder: uniform scale, then the offset along Y.</summary>
        public static (float x, float y, float z) ApplyHolder((float x, float y, float z) pos, float holderScale, float holderOffsetY) =>
            (pos.x * holderScale, pos.y * holderScale + holderOffsetY, pos.z * holderScale);

        /// <summary>The holder's Y offset that puts the model's lowest vertex (model-root space) on structure y = floorY.</summary>
        public static float HolderOffset(float lowestModelY, float holderScale, float floorY = 0f) => floorY - lowestModelY * holderScale;

        public static (float x, float y, float z, float w) Multiply((float x, float y, float z, float w) a, (float x, float y, float z, float w) b) =>
            (a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
             a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
             a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
             a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        // q * v * q^-1.
        public static (float x, float y, float z) Rotate((float x, float y, float z, float w) q, (float x, float y, float z) v)
        {
            float tx = 2f * (q.y * v.z - q.z * v.y);
            float ty = 2f * (q.z * v.x - q.x * v.z);
            float tz = 2f * (q.x * v.y - q.y * v.x);
            return (v.x + q.w * tx + (q.y * tz - q.z * ty),
                    v.y + q.w * ty + (q.z * tx - q.x * tz),
                    v.z + q.w * tz + (q.x * ty - q.y * tx));
        }

        // Rounds float noise (1e-8) to an exact 0 so an axis-aligned port prints and compares cleanly.
        static float Clean(float a) => Math.Abs(a) < 1e-6f ? 0f : a;
    }
}
