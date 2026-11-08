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
    /// the build tool is held and a pole or member of a grid is aimed, an arc for every pole-to-pole link of that grid and
    /// from each pole to every member it reaches (GridState), and the ring of the aimed pole's SupplyRadius (else the
    /// grid's first pole's). Driven from CoreDriver.Update; no game method is patched. Reset on leaving the game or when
    /// the world changes.
    /// </summary>
    internal static class PowerLinksView
    {
        /// <summary>Arcs drawn in the last frame (self-test).</summary>
        internal static int ArcsDrawn;

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
#if DEVBUILD
                force = DevTools.ForceArcs != null && DevTools.ForceArcs.Value;
#endif
                int aimed = 0, aimedGrid = 0;
                bool show = force && GridState.Grids.Count > 0;
                if (!force)
                {
                    if (builder == null) { builder = inter.builder; if (builder == null) builder = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>(); }
                    if (builder != null && builder.HoldingBuildTool)
                    {
                        aimed = inter.Aimed(world);
                        show = aimed > 0 && GridState.GridOfDepot(aimed, out aimedGrid);
                    }
                }
                if (!show)
                {
                    if (drew) { links.Clear(); drew = false; }
                    ArcsDrawn = 0;
                    return;
                }

                var cyan = MoonProductionInteraction.Cyan;
                int arcs = 0;
                // MoonPowerLinks keeps one ring: the aimed pole's, else the first pole of the (first) grid drawn.
                int ringAt = aimed > 0 && Pole(world, aimed) != null ? aimed : -1;
                if (ringAt < 0)
                    foreach (var kv in GridState.Grids)
                        if ((force || kv.Key == aimedGrid) && kv.Value.PoleIds.Count > 0) { ringAt = kv.Value.PoleIds[0]; break; }
                // Set first: a throw between Begin and End still gets its Clear when the gate turns off.
                drew = true;
                links.Begin();
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
                    links.Arc(from, to, Width, cyan);
                    arcs++;
                }
                var def = ringAt > 0 ? Pole(world, ringAt) : null;
                if (def != null && Position(world, ringAt, out var centre)) links.Ring(centre, def.Power.SupplyRadius, new Color(cyan.r, cyan.g, cyan.b, RingAlpha));
                links.End();
                ArcsDrawn = arcs;
            }
            catch (Exception e)
            {
                CoreLog.Once("power-links", "PowerLinksView.Tick threw: " + e, 3);
                ArcsDrawn = 0;
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
