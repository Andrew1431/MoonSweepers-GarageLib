#if DEVBUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    // Dev tools for belts and cards: [Dev] AutoBelt builds belts between the depots AutoBuild placed, [Dev] DumpCards
    // writes every build-menu card picture to a PNG.
    internal static partial class DevTools
    {
        internal static ConfigEntry<string> AutoBelt;
        internal static ConfigEntry<bool> DumpCards;
        static bool cardsDumped;

        static void BindBelts(ConfigFile cfg)
        {
            AutoBelt = cfg.Bind("Dev", "AutoBelt", "", "Dev build: ';'-separated '<from>:<port>-><to>:<port>' belts to build 3 s after the AutoBuild step (before AutoFeed). <from>/<to> is a structure id or vanilla kind number, resolved to the depot AutoBuild built for that entry; <port> is the definition's port name (IN_SAND), a PortName or PortLabel when only one port has it, or a 0-based index into the definition's conveyorPorts (the '[dev] ports of' lines print all three); the runtime port is the one at that definition port's position. Each belt goes through the game's TryBuildConveyor.");
            DumpCards = cfg.Bind("Dev", "DumpCards", false, "Dev build: once in game (30 s after load, so modded pictures are rendered), write every build-menu card picture (vanilla and modded) to BepInEx/cache/structurelib/cards/<kind>-<name>.png.");
        }

        static void TickBelts(float gameT)
        {
            if (DumpCards.Value && !cardsDumped && gameT > 30f)
            {
                cardsDumped = true;
                try { CardDump.Run(); }
                catch (Exception e) { CoreLog.Error("[dev] DumpCards threw: " + e); }
            }
        }
    }

    // [Dev] AutoBelt. Each entry becomes a MoonWorld/ConveyorPlan from the output port to the input port, with the ports'
    // positions and directions as the game reports them for the built depots (ConveyorPorts near the depot, matched to
    // the definition's port by label), the depots as start/end structures, and the surface normal as up. The route is
    // checked with ConveyorRouteValid (no out structs; CheckConveyor has an out Decimal, see Interop hazards) for each
    // pairing of the port directions and their reverses, and the first valid one is built with TryBuildConveyor.
    internal static class AutoBelter
    {
        /// <summary>What AutoBelt did per entry: belt id (-1 if none), ok, reason; plus the two depots.</summary>
        internal sealed class Result
        {
            internal string Entry, Reason;
            internal bool Ok;
            internal int Belt = -1, From = -1, To = -1;
            internal MoonWorld.ConveyorPort FromPort, ToPort;
        }

        internal static readonly List<Result> Results = new();

        internal static void Run(string spec)
        {
            Results.Clear();
            var world = CoreState.World ?? CoreState.FindWorld();
            var walkers = UnityEngine.Object.FindObjectsByType<MoonWalker>(FindObjectsSortMode.None);
            var walker = walkers != null && walkers.Length > 0 ? walkers[0] : null;
            try
            {
                var kit = MoonConveyorKit.Load();
                if (kit != null)
                    CoreLog.Info($"[dev] conveyor kit: minLength {kit.minLength:F2} maxLength {kit.maxLength:F2} minRadius {kit.minRadius:F2} maxSlope {kit.maxSlope:F2} minHeight {kit.minHeight:F2} maxHeight {kit.maxHeight:F2} pricePerMetre {kit.pricePerMetre:F2} deckHeight {kit.deckHeight:F2}; " +
                                 $"PortTolerance {MoonWorld.PortTolerance:F2} PortReach {MoonWorld.PortReach:F2} ConveyorReach {MoonWorld.ConveyorReach:F2} BeltSpeed {MoonWorld.BeltSpeed:F2} BeltSpacing {MoonWorld.BeltSpacing:F2} BeltRideHeight {MoonWorld.BeltRideHeight:F2} ProductEmitSeconds {MoonWorld.ProductEmitSeconds:F2}");
            }
            catch (Exception e) { CoreLog.Warn("[dev] reading the conveyor kit threw: " + e.Message); }
            var logged = new HashSet<int>();
            foreach (var entry in spec.Split(';').Select(e => e.Trim()).Where(e => e.Length > 0))
            {
                var r = new Result { Entry = entry };
                Results.Add(r);
                try
                {
                    if (world == null || walker == null) r.Reason = world == null ? "no MoonWorld" : "no MoonWalker";
                    else Build(world, walker, r, logged);
                }
                catch (Exception e) { r.Reason = "threw " + e.GetType().Name + ": " + e.Message; }
                string[] sides = entry.Split(new[] { "->" }, StringSplitOptions.None);
                string from = sides[0].Trim(), to = sides.Length > 1 ? sides[1].Trim() : "?";
                CoreLog.Info($"[dev] belt {from} -> {to} id={r.Belt} ok={r.Ok} reason='{r.Reason}'");
            }
        }

        static void Build(MoonWorld world, MoonWalker walker, Result r, HashSet<int> logged)
        {
            string[] sides = r.Entry.Split(new[] { "->" }, StringSplitOptions.None);
            if (sides.Length != 2) { r.Reason = "expected '<from>:<port>-><to>:<port>'"; return; }
            string fromErr = Endpoint(world, sides[0], out r.From, out r.FromPort, logged);
            if (fromErr != null) { r.Reason = "from: " + fromErr; return; }
            string toErr = Endpoint(world, sides[1], out r.To, out r.ToPort, logged);
            if (toErr != null) { r.Reason = "to: " + toErr; return; }
            if (!r.FromPort.output) CoreLog.Warn($"[dev] AutoBelt '{r.Entry}': the start port '{r.FromPort.label}' is an input");
            if (r.ToPort.output) CoreLog.Warn($"[dev] AutoBelt '{r.Entry}': the end port '{r.ToPort.label}' is an output");

            string player = MoonWorld.LocalPlayer;
            if (SelfTest.DecimalOf(world.WalletDollars(player)) < 5000m) world.SetTeamDollars(new Il2CppSystem.Decimal(20000.0));

            Vector3 s = r.FromPort.position, e = r.ToPort.position;
            Vector3 sd = r.FromPort.direction, ed = r.ToPort.direction;
            var kit = MoonConveyorKit.Load();
            var builderAt = walker.transform.position;
            MoonWorld.ConveyorPlan chosen = null;
            string firstReason = null;
            foreach (int none in new[] { -1, 0 })
            {
                foreach (var (a, b, tag) in new[] { (sd, ed, "+start,+end"), (sd, -ed, "+start,-end"), (-sd, ed, "-start,+end"), (-sd, -ed, "-start,-end") })
                {
                    var plan = Plan(s, a, e, b, r.From, r.To, none);
                    bool valid = world.ConveyorRouteValid(plan, kit, builderAt, out MoonConveyorPath path, out string why, out bool routable);
                    CoreLog.Info($"[dev] AutoBelt '{r.Entry}': route {tag} (unset ids {none}) valid={valid} routable={routable} length={(path != null ? path.Length.ToString("F2") : "-")} reason='{why}'");
                    firstReason ??= why;
                    if (valid) { chosen = plan; break; }
                }
                if (chosen != null) break;
            }
            chosen ??= Plan(s, sd, e, ed, r.From, r.To, -1);
            r.Ok = world.TryBuildConveyor(walker, chosen, out string reason, out int id);
            r.Reason = reason;
            r.Belt = r.Ok ? id : -1;
            if (r.Ok && world.conveyors != null && world.conveyors.ContainsKey(id))
            {
                var c = world.conveyors.get_Item(id);
                CoreLog.Info($"[dev] AutoBelt: belt {id} start {V(c.start)} ({Vector3.Distance(c.start, r.FromPort.position):F3} m from depot {r.From} '{r.FromPort.label}'), end {V(c.end)} ({Vector3.Distance(c.end, r.ToPort.position):F3} m from depot {r.To} '{r.ToPort.label}' at {V(r.ToPort.position)})");
            }
            if (!r.Ok && firstReason != null) r.Reason = $"{reason} (route check: {firstReason})";
        }

        static MoonWorld.ConveyorPlan Plan(Vector3 s, Vector3 sd, Vector3 e, Vector3 ed, int from, int to, int none)
        {
            var p = new MoonWorld.ConveyorPlan();
            p.start = s; p.startDirection = sd; p.startUp = s.normalized;
            p.end = e; p.endDirection = ed; p.endUp = e.normalized;
            p.startStructure = from; p.endStructure = to;
            p.startBelt = none; p.endBelt = none; p.replaces = none;
            return p;
        }

        // "<id or kind>:<port name or index>" -> the depot AutoBuild built and that depot's runtime port.
        static string Endpoint(MoonWorld world, string text, out int depot, out MoonWorld.ConveyorPort port, HashSet<int> logged)
        {
            depot = -1; port = null;
            int colon = text.IndexOf(':');
            if (colon <= 0) return $"'{text}' is not '<structure>:<port>'";
            string who = text.Substring(0, colon).Trim(), which = text.Substring(colon + 1).Trim();
            if (DevTools.BuildResults.TryGetValue(who, out var built) && built.ok) depot = built.depot;
            else if (!int.TryParse(who, out _))
            {
                var d = MoonStructures.Definition(who);
                if (d != null) depot = SelfTest.DepotOfKind(world, d.Kind);
            }
            if (depot < 0 || world.depots == null || !world.depots.ContainsKey(depot)) return $"AutoBuild built no '{who}' this run";
            var rec = world.depots.get_Item(depot);
            var def = world.BuildDefinition(rec.kind);
            if (def == null) return $"no build definition for kind {rec.kind}";
            var runtime = RuntimePorts(world, rec);
            if (logged.Add(depot)) LogPorts(world, rec, def, runtime);
            var ports = def.conveyorPorts;
            int n = ports?.Length ?? 0;
            int index = -1;
            if (int.TryParse(which, out int i)) index = i;
            else
            {
                // The definition's own name is unique per structure; PortName / PortLabel are not (the stone press's
                // IN_DUST and IN_SAND are both "input" / "Stone press input"), so those only match when unambiguous.
                for (int k = 0; k < n && index < 0; k++)
                    if (ports[k] != null && Eq(ports[k].name, which)) index = k;
                if (index < 0)
                {
                    var hits = Enumerable.Range(0, n).Where(k => ports[k] != null && (Eq(MoonWorld.PortName(def, ports[k]), which) || Eq(MoonWorld.PortLabel(def, ports[k]), which))).ToList();
                    if (hits.Count > 1) return $"port '{which}' is ambiguous on depot {depot} (definition ports {string.Join(", ", hits.Select(k => ports[k].name))}); use the definition name or an index";
                    if (hits.Count == 1) index = hits[0];
                }
            }
            if (index < 0) return $"depot {depot} (kind {rec.kind}) has no port '{which}' ({n} definition ports)";
            if (index >= n) return $"port index {index} out of range ({n} definition ports)";
            port = Match(rec, def, ports[index], runtime);
            return port == null ? $"definition port {index} ('{ports[index].name}') of depot {depot} has no runtime port at its position ({runtime.Count} runtime ports)" : null;
        }

        static bool Eq(string a, string b) => a != null && string.Equals(a.Trim(), b, StringComparison.OrdinalIgnoreCase);

        // The definition port's runtime counterpart: the runtime port of the same direction (in/out) within PortTolerance of
        // the definition's local position placed by the depot's pose (its spawned view's position and rotation; definition
        // scale 1 for every donor seen). Only when there is no view, or nothing lies within tolerance, does it fall back to
        // a unique label match, with a warning.
        static MoonWorld.ConveyorPort Match(MoonDepotSnapshot rec, MoonDepotBuildDefinition def, MoonDepotBuildDefinition.ConveyorPort p, List<MoonWorld.ConveyorPort> runtime)
        {
            var view = UnityEngine.Object.FindFirstObjectByType<MoonDepotConstruction>()?.ViewOf(rec.id);
            if (view != null)
            {
                var expected = view.transform.position + view.transform.rotation * p.position;
                MoonWorld.ConveyorPort best = null;
                float bestD = float.MaxValue;
                foreach (var r in runtime)
                {
                    if (r.output != p.output) continue;
                    float d = Vector3.Distance(r.position, expected);
                    if (d < bestD) { bestD = d; best = r; }
                }
                if (best != null && bestD <= MoonWorld.PortTolerance)
                {
                    CoreLog.Info($"[dev] AutoBelt: depot {rec.id} port '{p.name}' local {V(p.position)} -> expected {V(expected)}, matched runtime '{best.label}' at {V(best.position)} ({bestD:F3} m)");
                    return best;
                }
                CoreLog.Warn($"[dev] AutoBelt: depot {rec.id} port '{p.name}': no runtime {(p.output ? "output" : "input")} within {MoonWorld.PortTolerance:F2} m of {V(expected)} (nearest {(best == null ? "none" : bestD.ToString("F3") + " m")}); falling back to the label");
            }
            else CoreLog.Warn($"[dev] AutoBelt: depot {rec.id} has no view; matching port '{p.name}' by label");
            string label = MoonWorld.PortLabel(def, p);
            var byLabel = runtime.Where(r => r.output == p.output && Eq(r.label, label)).ToList();
            if (byLabel.Count == 1) { CoreLog.Warn($"[dev] AutoBelt: depot {rec.id} port '{p.name}' matched by label '{label}' only"); return byLabel[0]; }
            return null;
        }

        /// <summary>The runtime ports the game reports near a depot that belong to it (ConveyorPorts, filtered by structure).</summary>
        internal static List<MoonWorld.ConveyorPort> RuntimePorts(MoonWorld world, MoonDepotSnapshot rec)
        {
            var list = new Il2CppSystem.Collections.Generic.List<MoonWorld.ConveyorPort>();
            world.ConveyorPorts(new Vector3(rec.x, rec.y, rec.z), 20f, list);
            var mine = new List<MoonWorld.ConveyorPort>();
            for (int i = 0; i < list.Count; i++)
            {
                var p = list.get_Item(i);
                if (p != null && p.structure == rec.id) mine.Add(p);
            }
            return mine;
        }

        static void LogPorts(MoonWorld world, MoonDepotSnapshot rec, MoonDepotBuildDefinition def, List<MoonWorld.ConveyorPort> runtime)
        {
            CoreLog.Info($"[dev] ports of {rec.id}: {runtime.Count} runtime (kind {rec.kind}, '{def.displayName}', {def.conveyorPorts?.Length ?? 0} in the definition)");
            foreach (var p in runtime)
                CoreLog.Info($"[dev] ports of {rec.id}: '{p.label}' {(p.output ? "out" : "in")} at {V(p.position)} dir {V(p.direction)} belt {p.belt} occupied {p.occupied}");
            var defPorts = def.conveyorPorts;
            for (int k = 0; k < (defPorts?.Length ?? 0); k++)
            {
                var p = defPorts[k];
                if (p == null) continue;
                CoreLog.Info($"[dev] ports of {rec.id}: definition [{k}] name '{p.name}' PortName '{MoonWorld.PortName(def, p)}' PortLabel '{MoonWorld.PortLabel(def, p)}' {(p.output ? "out" : "in")} local {V(p.position)} dir {V(p.direction)}");
            }
            try
            {
                var points = world.PortPoints(rec);
                var sb = new StringBuilder();
                for (int i = 0; i < (points?.Count ?? 0); i++) sb.Append(' ').Append(V(points.get_Item(i)));
                CoreLog.Info($"[dev] ports of {rec.id}: PortPoints {points?.Count ?? -1}:{sb}");
            }
            catch (Exception e) { CoreLog.Warn($"[dev] PortPoints({rec.id}) threw: {e.Message}"); }
        }

        internal static string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
    }

    // [Dev] DumpCards: every catalog entry's card picture, vanilla and modded, through MoonDepotBuilder.CardArt. A sprite's
    // texture may be an unreadable atlas, so it is blitted into a render texture and its rect read back.
    internal static class CardDump
    {
        internal static void Run()
        {
            var builder = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>();
            if (builder == null) { CoreLog.Warn("[dev] DumpCards: no MoonDepotBuilder"); return; }
            if (builder.structureEntries == null) builder.RefreshCatalog();
            var entries = new List<MoonDepotBuilder.CatalogEntry>();
            var seen = new HashSet<string>();
            foreach (var list in new[] { MoonDepotBuilder.baseStructures, builder.structureEntries })
            {
                if (list == null) continue;
                foreach (var e in list)
                    if (e != null && !string.IsNullOrEmpty(e.image) && seen.Add(e.kind + "|" + e.image)) entries.Add(e);
            }
            string dir = Path.Combine(BepInEx.Paths.CachePath, "structurelib", "cards");
            Directory.CreateDirectory(dir);
            int written = 0, failed = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                try
                {
                    var sprite = builder.CardArt(e.image);
                    if (sprite == null) { failed++; CoreLog.Warn($"[dev] DumpCards: kind {e.kind} '{e.name}' image '{e.image}': CardArt returned null"); continue; }
                    string file = $"{e.kind}-{Safe(e.name)}";
                    if (!names.Add(file)) file += "-" + Safe(e.image);
                    string path = Path.Combine(dir, file + ".png");
                    var size = Write(sprite, path);
                    written++;
                    CoreLog.Info($"[dev] DumpCards: {file}.png <- image '{e.image}' sprite '{sprite.name}' {size}");
                }
                catch (Exception ex) { failed++; CoreLog.Warn($"[dev] DumpCards: kind {e.kind} '{e.name}' threw {ex.GetType().Name}: {ex.Message}"); }
            }
            CoreLog.Info($"[dev] DumpCards: wrote {written} card picture(s) ({failed} failed) of {entries.Count} catalog entries to {dir}");
        }

        static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '-');
            return sb.ToString().Trim('-');
        }

        static string Write(Sprite sprite, string path)
        {
            var tex = sprite.texture;
            var rect = sprite.textureRect;
            int w = Mathf.Max(1, Mathf.RoundToInt(rect.width)), h = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Texture2D outTex = null;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                outTex.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0);
                outTex.Apply();
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(outTex));
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (outTex != null) UnityEngine.Object.Destroy(outTex);
            }
            return $"{w}x{h} from a {tex.width}x{tex.height} texture";
        }
    }
}
#endif
