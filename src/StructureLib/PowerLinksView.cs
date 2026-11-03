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
                // A Unity object destroyed under us compares == null; its LineRenderers went with its transform.
                if (inter == null)
                {
                    if (links != null) Reset();
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
                    var builder = inter.builder;
                    if (builder == null) builder = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>();
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
                links.Begin();
                foreach (var kv in PowerPatches.LastSolve.Fed)
                {
                    int gen = kv.Key;
                    if (!netOf.TryGetValue(gen, out int net) || (!force && !networks.Contains(net))) continue;
                    if (ringAt < 0 && Generator(world, gen) != null) ringAt = gen;
                    var from = End(world, gen);
                    foreach (int member in kv.Value)
                    {
                        links.Arc(from, End(world, member), Width, cyan);
                        arcs++;
                    }
                }
                var def = ringAt > 0 ? Generator(world, ringAt) : null;
                if (def != null) links.Ring(Position(world, ringAt), def.Power.Range, new Color(cyan.r, cyan.g, cyan.b, RingAlpha));
                links.End();
                drew = true;
                ArcsDrawn = arcs;
            }
            catch (Exception e)
            {
                CoreLog.Once("power-links", "PowerLinksView.Tick threw: " + e, 3);
                ArcsDrawn = 0;
            }
        }

        /// <summary>Disposes the library's MoonPowerLinks and forgets the interaction (leaving the game, a world change).</summary>
        internal static void Reset()
        {
            try { if (links != null) links.Dispose(); }
            catch (Exception e) { CoreLog.Once("power-links-dispose", "Disposing the power arcs threw: " + e, 3); }
            links = null;
            inter = null;
            world0 = IntPtr.Zero;
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

        static Vector3 Position(MoonWorld world, int id)
        {
            var depots = world.depots;
            if (depots == null || !depots.ContainsKey(id)) return Vector3.zero;
            var r = depots.get_Item(id);
            return new Vector3(r.x, r.y, r.z);
        }

        // Vanilla's anchor when it has one; else 2 m above the depot (the moon is a sphere centred on the origin).
        static Vector3 End(MoonWorld world, int id)
        {
            if (world.PowerAnchor(id, out Vector3 p)) return p;
            var pos = Position(world, id);
            return pos + pos.normalized * 2f;
        }
    }
}
