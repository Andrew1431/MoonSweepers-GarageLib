using System;
using System.Collections.Generic;
using MoonCleaners;
using StructureLib.Patches;
using UnityEngine;

namespace StructureLib
{
    /// <summary>
    /// Power arcs for pole grids. Vanilla's MoonPowerLinks.ShowGrid never includes modded members (it takes them from
    /// grid membership, research/POWER_UX_RECON.md "C. Arcs"), so the library owns one MoonPowerLinks and draws, while
    /// the build tool is held and a pole or member of a grid is aimed, an arc for every pole-to-pole link of that grid
    /// (Width) and from each pole to every member it reaches (Width x 0.6; GridState), and the ring of the aimed pole's
    /// SupplyRadius (else the grid's first pole's). While the held tool has a Relay kind selected and a placement ghost
    /// exists, it also draws an arc from the ghost to every pole it would link to (PoleGraph with the ghost added) and
    /// the ghost's SupplyRadius ring. Nothing is drawn for vanilla grids (GridState holds pole grids only). Driven from
    /// CoreDriver.Update; no game method is patched. Reset on leaving the game or when the world changes.
    /// </summary>
    internal static class PowerLinksView
    {
        /// <summary>Arcs drawn in the last frame (self-test).</summary>
        internal static int ArcsDrawn;
        /// <summary>Of those, the preview's arcs from the ghost to existing poles (self-test).</summary>
        internal static int PreviewArcs;
        /// <summary>Centre of the ring drawn in the last frame, or null (self-test).</summary>
        internal static Vector3? RingAt;
#if DEVBUILD
        /// <summary>Self-test: a ghost position used as if the held tool's plan were there (arc-preview); also stands in for the held tool.</summary>
        internal static Vector3? DevGhost;
#endif

        const float Width = 0.06f;
        const float RingAlpha = 0.35f;

        static MoonPowerLinks links;
        static MoonProductionInteraction inter;
        static MoonDepotBuilder builder;
        static IntPtr world0;
        static bool drew;

        internal static void Tick()
        {
            try
            {
                if (!MoonSession.InGame || !MoonStructures.Frozen || MoonStructures.Disabled) return;
                var world = CoreState.World;
                if (world == null) return;
                if (world.Pointer != world0) { Reset(); world0 = world.Pointer; }
                // A Unity object destroyed under us compares == null; its LineRenderers went with its transform. Only the
                // objects go: the world is the same one.
                if (inter == null)
                {
                    if (links != null) DropObjects();
                    inter = UnityEngine.Object.FindFirstObjectByType<MoonProductionInteraction>();
                    if (inter == null) return;
                }
                if (links == null)
                {
                    links = new MoonPowerLinks(world, inter.transform);
                    CoreLog.Debug($"PowerLinksView: created MoonPowerLinks on '{inter.name}'");
                }

                bool force = false;
                Vector3? devGhost = null;
#if DEVBUILD
                force = DevTools.ForceArcs != null && DevTools.ForceArcs.Value;
                devGhost = DevGhost;
#endif
                if (builder == null) { builder = inter.builder; if (builder == null) builder = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>(); }
                bool held = devGhost.HasValue;
                if (!held && builder != null)
                {
                    try { held = builder.HoldingBuildTool; }
                    catch (Exception e) { CoreLog.Once("power-links-held", "HoldingBuildTool threw: " + e.Message, 3); }
                }

                int aimed = 0, aimedGrid = 0;
                bool show = force && GridState.Grids.Count > 0;
                if (!force && held && !devGhost.HasValue)
                {
                    aimed = inter.Aimed(world);
                    show = aimed > 0 && GridState.GridOfDepot(aimed, out aimedGrid);
                }
                // The placement preview: a Relay kind selected in the held tool and a ghost (the builder's plan, or the dev override).
                StructureDefinition ghostDef = null;
                Vector3 ghost = Vector3.zero;
                if (held) ghostDef = GhostRelay(devGhost, out ghost);
                if (!show && ghostDef == null)
                {
                    if (drew) { links.Clear(); drew = false; }
                    ArcsDrawn = 0;
                    PreviewArcs = 0;
                    RingAt = null;
                    return;
                }

                var cyan = MoonProductionInteraction.Cyan;
                int arcs = 0, previewArcs = 0;
                Vector3? ring = null;
                float ringRadius = 0f;
                if (show)
                {
                    // MoonPowerLinks keeps one ring: the aimed pole's, else the first pole of the (first) grid drawn.
                    int ringAt = aimed > 0 && Pole(world, aimed) != null ? aimed : -1;
                    if (ringAt < 0)
                        foreach (var kv in GridState.Grids)
                            if ((force || kv.Key == aimedGrid) && kv.Value.PoleIds.Count > 0) { ringAt = kv.Value.PoleIds[0]; break; }
                    var def = ringAt > 0 ? Pole(world, ringAt) : null;
                    if (def != null && Position(world, ringAt, out var centre)) { ring = centre; ringRadius = def.Power.SupplyRadius; }
                }
                if (ghostDef != null) { ring = ghost; ringRadius = ghostDef.Power.SupplyRadius; }
                // Set first: a throw between Begin and End still gets its Clear when the gate turns off.
                drew = true;
                links.Begin();
                if (show)
                {
                    // A depot removed since the last rebuild has no position: no arc from or to it.
                    foreach (var (a, b) in GridState.PoleLinks)
                    {
                        if (!force && !(GridState.GridOfDepot(a, out int g) && g == aimedGrid)) continue;
                        if (!End(world, a, out var from) || !End(world, b, out var to)) continue;
                        links.Arc(from, to, Width, cyan);
                        arcs++;
                    }
                    foreach (var (pole, member) in GridState.Reach)
                    {
                        if (!force && !(GridState.GridOfDepot(pole, out int g) && g == aimedGrid)) continue;
                        if (!End(world, pole, out var from) || !End(world, member, out var to)) continue;
                        links.Arc(from, to, Width * 0.6f, cyan);
                        arcs++;
                    }
                }
                if (ghostDef != null)
                {
                    var from = ghost + ghost.normalized * 2f;
                    foreach (int id in GhostLinks(world, ghost, ghostDef))
                    {
                        if (!End(world, id, out var to)) continue;
                        links.Arc(from, to, Width, cyan);
                        previewArcs++;
                    }
                    arcs += previewArcs;
                }
                if (ring.HasValue) links.Ring(ring.Value, ringRadius, new Color(cyan.r, cyan.g, cyan.b, RingAlpha));
                links.End();
                ArcsDrawn = arcs;
                PreviewArcs = previewArcs;
                RingAt = ring;
            }
            catch (Exception e)
            {
                CoreLog.Once("power-links", "PowerLinksView.Tick threw: " + e, 3);
                ArcsDrawn = 0;
                PreviewArcs = 0;
                RingAt = null;
            }
        }

        /// <summary>Disposes the library's MoonPowerLinks and forgets the cached objects and world (leaving the game, a world change).</summary>
        internal static void Reset()
        {
            DropObjects();
            world0 = IntPtr.Zero;
        }

        static void DropObjects()
        {
            try { if (links != null) links.Dispose(); }
            catch (Exception e) { CoreLog.Once("power-links-dispose", "Disposing the power arcs threw: " + e, 3); }
            links = null;
            inter = null;
            builder = null;
            drew = false;
            ArcsDrawn = 0;
            PreviewArcs = 0;
            RingAt = null;
        }

        // The Relay definition of the kind selected in the builder when a ghost exists (the dev override, else the
        // builder's plan position), else null.
        static StructureDefinition GhostRelay(Vector3? devGhost, out Vector3 ghost)
        {
            ghost = Vector3.zero;
            try
            {
                if (builder == null) return null;
                var d = Remap.Live(builder.selectedBuildId);
                if (d?.Power == null || d.Power.Role != PowerRole.Relay) return null;
                if (devGhost.HasValue) { ghost = devGhost.Value; return d; }
                var plan = builder.structurePlan;
                if (plan == null) return null;
                ghost = plan.position;
                return d;
            }
            catch (Exception e) { CoreLog.Once("power-links-ghost", "Reading the placement ghost threw: " + e, 3); return null; }
        }

        // Ids of the existing poles the ghost would link to: the pure graph with the ghost added (3D distance within the
        // larger of the two LinkRanges).
        static List<int> GhostLinks(MoonWorld world, Vector3 ghost, StructureDefinition def)
        {
            const int GhostId = int.MaxValue;
            var poles = new List<StructureLib.Pole>();
            foreach (var kv in GridState.Grids)
                foreach (int id in kv.Value.PoleIds)
                {
                    var pd = Pole(world, id);
                    if (pd != null && Position(world, id, out var at))
                        poles.Add(new StructureLib.Pole(id, at.x, at.y, at.z, pd.Power.LinkRange, pd.Power.SupplyRadius));
                }
            poles.Add(new StructureLib.Pole(GhostId, ghost.x, ghost.y, ghost.z, def.Power.LinkRange, def.Power.SupplyRadius));
            var result = new List<int>();
            foreach (var (a, b) in PoleGraph.Build(poles).Links)
            {
                if (a == GhostId) result.Add(b);
                else if (b == GhostId) result.Add(a);
            }
            return result;
        }

        // The modded pole's (Relay) definition for a depot, or null.
        static StructureDefinition Pole(MoonWorld world, int id)
        {
            var depots = world.depots;
            if (depots == null || !depots.ContainsKey(id)) return null;
            var d = Remap.Live(depots.get_Item(id).kind);
            return d?.Power != null && d.Power.Role == PowerRole.Relay ? d : null;
        }

        static bool Position(MoonWorld world, int id, out Vector3 p)
        {
            p = Vector3.zero;
            var depots = world.depots;
            if (depots == null || !depots.ContainsKey(id)) return false;
            var r = depots.get_Item(id);
            p = new Vector3(r.x, r.y, r.z);
            return true;
        }

        // Vanilla's anchor when it has one; else 2 m above the depot (the moon is a sphere centred on the origin). False
        // when the depot no longer exists.
        static bool End(MoonWorld world, int id, out Vector3 p)
        {
            if (!Position(world, id, out var pos)) { p = Vector3.zero; return false; }
            if (world.PowerAnchor(id, out p)) return true;
            p = pos + pos.normalized * 2f;
            return true;
        }
    }
}
