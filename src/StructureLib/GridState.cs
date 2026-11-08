using System;
using System.Collections.Generic;

namespace StructureLib
{
    /// <summary>
    /// The library's view of the last power solve (Patches/PowerPatches.cs), for the look-at panels and the arcs. The pole
    /// graph and who each pole reaches are rebuilt when the depot set changes (<see cref="Version"/> bumps); the grid totals
    /// are rewritten every solve. Main thread only. Every peer solves its own world (research/POWER_UX_RECON.md, F), so a
    /// client's view is its own solve's, from replicated inputs; the pole's own state fields carry the same totals.
    /// </summary>
    internal static class GridState
    {
        internal sealed class Grid
        {
            public int Id;
            public int Poles;
            /// <summary>kW delivered to the load from solar and from fuel; StorageKw is positive while the banks discharge.</summary>
            public float SolarKw, FuelKw, PeakKw, StorageKw, DemandKw, UsedKw, Satisfaction;
            public double StoredKwh;
            public float CapacityKwh;
            public List<int> PoleIds = new();
            /// <summary>Attached members (sources, storage, modded and vanilla consumers), in depot-id order.</summary>
            public List<int> MemberIds = new();
            /// <summary>The last solve still had a deficit after solar and storage (the gate for a fuel source's next burn).</summary>
            public bool WantsFuel;
            /// <summary>Consumers the grid gave power to in the last solve (beyond what vanilla panels gave them).</summary>
            public int Fed;
        }

        static readonly Dictionary<int, Grid> grids = new();
        static readonly Dictionary<int, int> gridOf = new();
        static readonly List<(int A, int B)> links = new();
        static readonly List<(int Pole, int Member)> reach = new();

        internal static IReadOnlyDictionary<int, Grid> Grids => grids;
        internal static IReadOnlyList<(int A, int B)> PoleLinks => links;
        /// <summary>Pole → member it serves (the nearest pole whose SupplyRadius reaches the member's footprint).</summary>
        internal static IReadOnlyList<(int Pole, int Member)> Reach => reach;
        internal static int Version { get; private set; }

        /// <summary>Vanilla consumer depot → its Power as vanilla's solve left it, for those the last solve raised (self-tests).</summary>
        internal static readonly Dictionary<int, float> VanillaBefore = new();

        /// <summary>The grid of a pole or an attached member; false (gridId 0) for anything else.</summary>
        internal static bool GridOfDepot(int depotId, out int gridId) => gridOf.TryGetValue(depotId, out gridId) && gridId != 0;

        /// <summary>A fuel source may start a burn: its grid's last solve was short after solar and storage.</summary>
        internal static bool WantsFuel(int depotId) => GridOfDepot(depotId, out int g) && grids.TryGetValue(g, out var grid) && grid.WantsFuel;

        /// <summary>Machines the depot's grid powered in the last solve; 0 when the depot is in no grid.</summary>
        internal static int FedCount(int depotId) => GridOfDepot(depotId, out int g) && grids.TryGetValue(g, out var grid) ? grid.Fed : 0;

        // ---- written by PowerPatches ----

        /// <summary>Starts a new graph: forgets every grid, link and attachment, and bumps Version.</summary>
        internal static void BeginGraph()
        {
            grids.Clear();
            gridOf.Clear();
            links.Clear();
            reach.Clear();
            VanillaBefore.Clear();
            Version++;
        }

        internal static Grid AddPole(int gridId, int poleId)
        {
            if (!grids.TryGetValue(gridId, out var g)) grids[gridId] = g = new Grid { Id = gridId };
            g.PoleIds.Add(poleId);
            g.Poles = g.PoleIds.Count;
            gridOf[poleId] = gridId;
            return g;
        }

        internal static void AddMember(int gridId, int poleId, int memberId)
        {
            if (gridId == 0 || !grids.TryGetValue(gridId, out var g)) return;
            g.MemberIds.Add(memberId);
            gridOf[memberId] = gridId;
            reach.Add((poleId, memberId));
        }

        internal static void AddLink(int a, int b) => links.Add((a, b));

        /// <summary>Forgets everything (leaving the game, or the library standing down).</summary>
        internal static void Clear()
        {
            if (grids.Count == 0 && gridOf.Count == 0 && links.Count == 0) return;
            BeginGraph();
        }
    }
}
