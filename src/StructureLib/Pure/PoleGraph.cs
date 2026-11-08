using System;
using System.Collections.Generic;

namespace StructureLib
{
    /// <summary>A power pole: where it is, how far it links to other poles, and how far it supplies members.</summary>
    public readonly record struct Pole(int Id, float X, float Y, float Z, float LinkRange, float SupplyRadius);

    /// <summary>A member's footprint: centre, half-extents along its local X and Z axes, and yaw (radians, about the vertical).</summary>
    public readonly record struct Site(int Id, float X, float Y, float Z, float HalfX, float HalfZ, float Yaw);

    /// <summary>
    /// Links power poles into grids and attaches member structures to the nearest pole that reaches them. Pure: no
    /// Unity, no game types, deterministic (poles are processed in id order).
    /// <list type="bullet">
    /// <item>Two poles link when their 3D centre distance is &lt;= max of their LinkRanges (a long pole reaches a short one).
    /// Linked poles form a grid; its id is <c>-(lowest pole id in the component)</c>, so it cannot collide with a
    /// vanilla grid id (a positive depot id) nor with 0.</item>
    /// <item>A site attaches to the pole with the smallest footprint distance, provided that distance is &lt;= the pole's
    /// SupplyRadius. Footprint distance is horizontal (Y is ignored): from the pole centre to the site's rectangle, which
    /// has half-extents HalfX/HalfZ and local X axis (cos Yaw, sin Yaw) in the (X, Z) plane; zero inside. Equal distances
    /// go to the lower pole id.</item>
    /// </list>
    /// </summary>
    public sealed class PoleGraph
    {
        readonly Pole[] _poles;                 // sorted by id, duplicate ids dropped
        readonly int[] _grid;                   // grid id of _poles[i]
        readonly Dictionary<int, int> _gridOfPole = new();
        readonly Dictionary<int, int> _polesIn = new();
        readonly List<(int A, int B)> _links = new();

        PoleGraph(Pole[] poles) { _poles = poles; _grid = new int[poles.Length]; }

        public IReadOnlyList<(int A, int B)> Links => _links;
        public int GridCount => _polesIn.Count;

        public static PoleGraph Build(IReadOnlyList<Pole> poles)
        {
            var sorted = new List<Pole>(poles);
            sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
            var unique = new List<Pole>(sorted.Count);
            foreach (var p in sorted)
                if (unique.Count == 0 || unique[unique.Count - 1].Id != p.Id) unique.Add(p);
            var g = new PoleGraph(unique.ToArray());
            g.Link();
            return g;
        }

        void Link()
        {
            int n = _poles.Length;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }

            float cell = 1f;
            for (int i = 0; i < n; i++) cell = Math.Max(cell, _poles[i].LinkRange);
            var cells = new Dictionary<(int, int, int), List<int>>();
            for (int i = 0; i < n; i++)
            {
                var key = CellOf(_poles[i], cell);
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                list.Add(i);
            }

            for (int i = 0; i < n; i++)
            {
                var (cx, cy, cz) = CellOf(_poles[i], cell);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!cells.TryGetValue((cx + dx, cy + dy, cz + dz), out var list)) continue;
                    foreach (int j in list)
                    {
                        if (j <= i) continue;
                        double range = Math.Max(_poles[i].LinkRange, _poles[j].LinkRange);
                        if (Dist2(_poles[i], _poles[j]) > range * range) continue;
                        _links.Add((_poles[i].Id, _poles[j].Id));
                        parent[Root(j)] = Root(i);
                    }
                }
            }
            _links.Sort();

            // Poles are in id order, so the first pole seen in a component is its lowest.
            var lowest = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                int r = Root(i);
                if (!lowest.ContainsKey(r)) lowest[r] = _poles[i].Id;
                int grid = -lowest[r];
                _grid[i] = grid;
                _gridOfPole[_poles[i].Id] = grid;
                _polesIn[grid] = _polesIn.TryGetValue(grid, out int c) ? c + 1 : 1;
            }
        }

        /// <summary>Grid id (negative) of a pole; 0 when the pole id is unknown.</summary>
        public int GridOf(int poleId) => _gridOfPole.TryGetValue(poleId, out int g) ? g : 0;

        /// <summary>Number of poles in the grid <paramref name="gridId"/>; 0 when unknown.</summary>
        public int PolesIn(int gridId) => _polesIn.TryGetValue(gridId, out int c) ? c : 0;

        /// <summary>
        /// Attaches a site to the nearest pole whose SupplyRadius reaches its footprint. Returns that pole's grid id, or 0
        /// when none reaches (then <paramref name="poleId"/> is 0 and <paramref name="distance"/> is +infinity).
        /// </summary>
        public int AttachSite(Site site, out int poleId, out float distance)
        {
            int best = -1;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < _poles.Length; i++)
            {
                float d = FootprintDistance(_poles[i], site);
                if (d <= _poles[i].SupplyRadius && d < bestDist) { best = i; bestDist = d; }
            }
            if (best < 0) { poleId = 0; distance = float.PositiveInfinity; return 0; }
            poleId = _poles[best].Id;
            distance = bestDist;
            return _grid[best];
        }

        static float FootprintDistance(Pole p, Site s)
        {
            double dx = p.X - s.X, dz = p.Z - s.Z;
            double cos = Math.Cos(s.Yaw), sin = Math.Sin(s.Yaw);
            double lx = dx * cos + dz * sin, lz = -dx * sin + dz * cos;
            double ox = Math.Max(0.0, Math.Abs(lx) - s.HalfX), oz = Math.Max(0.0, Math.Abs(lz) - s.HalfZ);
            return (float)Math.Sqrt(ox * ox + oz * oz);
        }

        static double Dist2(Pole a, Pole b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        static (int, int, int) CellOf(Pole p, float cell) =>
            ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell), (int)Math.Floor(p.Z / cell));
    }
}
