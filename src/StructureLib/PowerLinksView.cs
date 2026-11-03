using System;
using System.Collections.Generic;
using MoonCleaners;
using StructureLib.Patches;
using UnityEngine;

namespace StructureLib
{
    /// <summary>
    /// Power arcs for fuel networks. Vanilla's MoonPowerLinks.ShowGrid never includes modded members (it takes them from
    /// grid membership, research/POWER_UX_RECON.md "C. Arcs"), so the library owns one MoonPowerLinks and draws, while
    /// the build tool is held and a fuel-network member is aimed, an arc from each generator of that network to every
    /// member it fed in the last solve (PowerPatches.LastSolve) and the ring of one generator's Range. Driven from
    /// CoreDriver.Update; no game method is patched. Reset on leaving the game or when the world changes.
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
        static readonly HashSet<int> networks = new();

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

                var netOf = PowerPatches.LastSolve.NetworkOf;
                bool force = false;
#if DEVBUILD
                force = DevTools.ForceArcs != null && DevTools.ForceArcs.Value;
#endif
                int aimed = 0;
                bool show = force;
                if (!show)
                {
                    if (builder == null) { builder = inter.builder; if (builder == null) builder = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>(); }
                    if (builder != null && builder.HoldingBuildTool)
                    {
                        aimed = inter.Aimed(world);
                        show = aimed > 0 && netOf.ContainsKey(aimed);
                    }
                }
                if (!show)
                {
                    if (drew) { links.Clear(); drew = false; }
                    ArcsDrawn = 0;
                    return;
                }

                networks.Clear();
                if (!force) networks.Add(netOf[aimed]);
                var cyan = MoonProductionInteraction.Cyan;
                int arcs = 0;
                // MoonPowerLinks keeps one ring: the aimed generator's, else the first generator of the network drawn.
                int ringAt = aimed > 0 && Generator(world, aimed) != null ? aimed : -1;
                // Set first: a throw between Begin and End still gets its Clear when the gate turns off.
                drew = true;
                links.Begin();
                foreach (var kv in PowerPatches.LastSolve.Fed)
                {
                    int gen = kv.Key;
                    if (!netOf.TryGetValue(gen, out int net) || (!force && !networks.Contains(net))) continue;
                    // A depot removed since the last solve has no position: no arc from or to it.
                    if (!End(world, gen, out var from)) continue;
                    if (ringAt < 0 && Generator(world, gen) != null) ringAt = gen;
                    foreach (int member in kv.Value)
                    {
                        if (!End(world, member, out var to)) continue;
                        links.Arc(from, to, Width, cyan);
                        arcs++;
                    }
                }
                var def = ringAt > 0 ? Generator(world, ringAt) : null;
                if (def != null && Position(world, ringAt, out var centre)) links.Ring(centre, def.Power.Range, new Color(cyan.r, cyan.g, cyan.b, RingAlpha));
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

        // The modded fuel generator's definition for a depot, or null.
        static StructureDefinition Generator(MoonWorld world, int id)
        {
            var depots = world.depots;
            if (depots == null || !depots.ContainsKey(id)) return null;
            var d = Remap.Live(depots.get_Item(id).kind);
            return d?.Power != null ? d : null;
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
