using System;
using System.Collections.Generic;

namespace StructureLib
{
    /// <summary>
    /// Grid membership of modded machines and modded fuel generators. The game's own solve never sees them (its node
    /// list comes from an inlined kind check), so StructureLib groups them after it:
    /// <list type="bullet">
    /// <item>A fuel source links every other modded member within its range (centre to centre), sources included; the
    /// linked members form one network and share the sources' stored charge.</item>
    /// <item>A member a vanilla node reaches keeps that vanilla grid id. A member no vanilla node reaches takes its
    /// network's id: the lowest vanilla grid id of any member in the network, else, when the network holds a fuel source,
    /// a StructureLib grid id <c>-(lowest depot id in the network)</c>, which cannot collide with vanilla's (vanilla grid
    /// ids are depot ids, so positive). A member in no network and out of vanilla range gets 0 (no grid).</item>
    /// </list>
    /// </summary>
    public static class FuelGrid
    {
        public readonly record struct Member(int Id, float X, float Y, float Z, float SourceRange, int VanillaGrid)
        {
            public bool IsSource => SourceRange > 0f;
        }

        /// <summary>Grid id of each member, and the index of its network (members with the same network share fuel).</summary>
        public static (int[] Grid, int[] Network) Assign(IReadOnlyList<Member> members)
        {
            int n = members.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }

            for (int i = 0; i < n; i++)
            {
                if (!members[i].IsSource) continue;
                for (int j = 0; j < n; j++)
                    if (j != i && Distance(members[i], members[j]) <= members[i].SourceRange) parent[Root(j)] = Root(i);
            }

            var networkGrid = new Dictionary<int, int>();
            var lowestVanilla = new Dictionary<int, int>();
            var lowestId = new Dictionary<int, int>();
            var hasSource = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                int r = Root(i);
                var m = members[i];
                if (m.VanillaGrid > 0 && (!lowestVanilla.TryGetValue(r, out int v) || m.VanillaGrid < v)) lowestVanilla[r] = m.VanillaGrid;
                if (!lowestId.TryGetValue(r, out int id) || m.Id < id) lowestId[r] = m.Id;
                if (m.IsSource) hasSource.Add(r);
            }
            var grid = new int[n];
            var network = new int[n];
            for (int i = 0; i < n; i++)
            {
                int r = Root(i);
                network[i] = r;
                int own = lowestVanilla.TryGetValue(r, out int v) ? v : hasSource.Contains(r) ? -lowestId[r] : 0;
                grid[i] = members[i].VanillaGrid > 0 ? members[i].VanillaGrid : own;
            }
            return (grid, network);
        }

        /// <summary>
        /// kW a fuel source can deliver in a solve of <paramref name="dt"/> seconds: its peak, at most what its stored charge
        /// lasts over the solve; 0 when it holds no charge.
        /// </summary>
        public static float AvailableKw(float peakKw, double chargeKwh, float dt)
        {
            if (chargeKwh <= 0 || peakKw <= 0f) return 0f;
            if (dt <= 0f) return peakKw;
            return (float)Math.Min(peakKw, chargeKwh * 3600.0 / dt);
        }

        /// <summary>Takes <paramref name="kw"/> from the sources in order, each up to what it has left; returns kW per source.</summary>
        public static float[] Split(float kw, IReadOnlyList<float> availableKw)
        {
            var taken = new float[availableKw.Count];
            float left = Math.Max(0f, kw);
            for (int i = 0; i < availableKw.Count && left > 0f; i++)
            {
                taken[i] = Math.Min(left, Math.Max(0f, availableKw[i]));
                left -= taken[i];
            }
            return taken;
        }

        static float Distance(Member a, Member b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
