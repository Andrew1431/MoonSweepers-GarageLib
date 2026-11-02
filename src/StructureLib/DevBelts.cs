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
        internal static ConfigEntry<bool> DumpCards, RegisterBadPorts, SunOverhead;
        static bool cardsDumped, sunSet;

        static void BindBelts(ConfigFile cfg)
        {
            AutoBelt = cfg.Bind("Dev", "AutoBelt", "", "Dev build: ';'-separated '<from>:<port>-><to>:<port>' belts to build 3 s after the AutoBuild step (before AutoFeed). <from>/<to> is a structure id or vanilla kind number, resolved to the depot AutoBuild built for that entry; <port> is the definition's port name (IN_SAND), a PortName or PortLabel when only one port has it, or a 0-based index into the definition's conveyorPorts (the '[dev] ports of' lines print all three); the runtime port is the one at that definition port's position. Each belt goes through the game's TryBuildConveyor.");
            DumpCards = cfg.Bind("Dev", "DumpCards", false, "Dev build: once in game (30 s after load, so modded pictures are rendered), write every build-menu card picture (vanilla and modded) to BepInEx/cache/structurelib/cards/<kind>-<name>.png.");
            RegisterBadPorts = cfg.Bind("Dev", "RegisterBadPorts", false, "Dev build: the example plugin also registers ryanhartwig.example-bad-ports, whose input port takes a cargo no slot holds; the registry must drop it at freeze (self-test ports-rules-logged).");
            SunOverhead = cfg.Bind("Dev", "SunOverhead", false, "Dev build: 5 s after a world loads, set the lunar clock (MoonSolarSystem.SetLunarTime) so the sun stands overhead at the walker, so vanilla solar panels supply power (the belt checks need the pressure chamber to make sand). The clock runs in real time, so a cloned save is often at night.");
        }

        static void TickBelts(float gameT)
        {
            if (SunOverhead.Value && !sunSet && gameT > 5f) { sunSet = true; SetSunOverhead(); }
            if (DumpCards.Value && !cardsDumped && gameT > 30f)
            {
                cardsDumped = true;
                try { CardDump.Run(); }
                catch (Exception e) { CoreLog.Error("[dev] DumpCards threw: " + e); }
            }
        }

        // [Dev] SunOverhead: the lunar time whose sun direction is closest to the walker's up (as the S0 probe did).
        static void SetSunOverhead()
        {
            try
            {
                var world = CoreState.World ?? CoreState.FindWorld();
                var sun = world?.solarSystem ?? UnityEngine.Object.FindFirstObjectByType<MoonSolarSystem>();
                var walkers = UnityEngine.Object.FindObjectsByType<MoonWalker>(FindObjectsSortMode.None);
                if (sun == null || walkers == null || walkers.Length == 0) { CoreLog.Warn($"[dev] SunOverhead: solar system {(sun != null)}, walker {(walkers?.Length ?? 0)}"); return; }
                var up = walkers[0].transform.position.normalized;
                float target = 0f, best = -2f;
                for (int i = 0; i < 400; i++)
                {
                    float lt = i / 400f, d = Vector3.Dot(MoonSolarSystem.SunDirectionAt(lt), up);
                    if (d > best) { best = d; target = lt; }
                }
                float was = sun.LunarTime;
                sun.SetLunarTime(target);
                CoreLog.Info($"[dev] SunOverhead: LunarTime {was:F3} -> {sun.LunarTime:F3} (sun dot up {best:F3}), Daylight {MoonSolarSystem.Daylight:F2}");
            }
            catch (Exception e) { CoreLog.Error("[dev] SunOverhead threw: " + e); }
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
                // A modded clone's port is named "sl:<cargo>:<Name>"; its declared <Name> ("Dust in") matches too.
                for (int k = 0; k < n && index < 0; k++)
                    if (ports[k] != null && (Eq(ports[k].name, which) || Eq(DeclaredName(ports[k].name), which))) index = k;
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

        // "sl:<cargo>:<Name>" -> "<Name>"; null for any other name.
        static string DeclaredName(string name)
        {
            if (!PortRules.TryDecodeCargo(name, out _)) return null;
            int colon = name.IndexOf(':', PortRules.NamePrefix.Length);
            return name.Substring(colon + 1);
        }

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

    // Port self-tests: the clones carry the declared ports under "sl:" names, the burner gained the port its donor lacks,
    // PortCargo answers a modded port's cargo, and a structure whose port cargo matches no slot was dropped at freeze.
    internal static partial class SelfTest
    {
        const string BadPortsId = "ryanhartwig.example-bad-ports";

        static void PortChecks()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            var grinder = MoonStructures.Definition(GrinderId);
            var burner = MoonStructures.Definition(BurnerId);
            string noWorld = world == null ? "no MoonWorld found" : null;
            string noGrinder = noWorld ?? (grinder == null ? $"'{GrinderId}' is not registered (is ExampleStructure installed?)" : null);

            Check("ports-on-clone", noGrinder, () =>
            {
                var ports = world.BuildDefinition(grinder.Kind)?.conveyorPorts;
                var names = Names(ports);
                CoreLog.Info($"[selftest] grinder clone conveyorPorts: {Describe(ports)}");
                bool ok = names.Count == 2 && names[0] == "sl:0:Dust in" && names[1] == "sl:250:Grit out" && !ports[0].output && ports[1].output &&
                          Vector3.Distance(ports[0].position, new Vector3(-2.95f, 0.85f, 0f)) < 0.01f && Vector3.Distance(ports[1].position, new Vector3(2.95f, 0.85f, 0f)) < 0.01f;
                return (ok, $"expected 'sl:0:Dust in' in at (-2.95,0.85,0) and 'sl:250:Grit out' out at (2.95,0.85,0); got {Describe(ports)}");
            });

            Check("ports-burner-added", noWorld ?? (burner == null ? $"'{BurnerId}' is not registered" : null), () =>
            {
                var donor = world.BuildDefinition(MoonWorld.SolarPostKind)?.conveyorPorts;
                var mine = world.BuildDefinition(burner.Kind)?.conveyorPorts;
                CoreLog.Info($"[selftest] solar post ports: {Describe(donor)}; burner clone ports: {Describe(mine)}");
                var names = Names(mine);
                return ((donor?.Length ?? 0) == 0 && names.Count == 1 && names[0] == "sl:250:Grit in" && !mine[0].output,
                    $"solar post has {donor?.Length ?? -1} port(s), burner clone {Describe(mine)}; expected 0 and one input 'sl:250:Grit in'");
            });

            int depot = grinder != null && world != null ? DepotOfKind(world, grinder.Kind) : -1;
            Check("ports-cargo-answer", noGrinder ?? (depot < 0 ? "no example grinder depot (AutoBuild it)" : null), () => CargoAnswer(world, depot));

            if (!DevTools.RegisterBadPorts.Value)
                CoreLog.Info("[selftest] [Dev] RegisterBadPorts is false: ports-rules-logged not applicable");
            else Check("ports-rules-logged", null, () =>
            {
                const string expected = "port 'Sand in' takes sand but no input slot does";
                string text = ReadWholeLog();
                bool logged = text != null && text.Contains($"'{BadPortsId}': {expected}") && text.Contains($"Structure '{BadPortsId}' dropped.");
                bool gone = MoonStructures.Definition(BadPortsId) == null;
                return (logged && gone, $"Definition('{BadPortsId}') {(gone ? "is null" : "still exists")}; the log {(text == null ? "could not be read" : logged ? "has" : "lacks")} \"'{BadPortsId}': {expected}\" and the drop line");
            });
        }

        // The grinder's runtime ports (ConveyorPorts near the depot, its own) matched to the clone's definition ports by
        // position under the depot's view pose; PortCargo is asked for each definition port (it takes the definition's
        // struct, not the runtime one). PortName and PortLabel are logged to show what the game displays.
        static (bool, string) CargoAnswer(MoonWorld world, int depot)
        {
            var rec = world.depots.get_Item(depot);
            var def = world.BuildDefinition(rec.kind);
            var runtime = AutoBelter.RuntimePorts(world, rec);
            var view = UnityEngine.Object.FindFirstObjectByType<MoonDepotConstruction>()?.ViewOf(rec.id);
            if (view == null) return (false, $"depot {depot} has no view");
            var ports = def.conveyorPorts;
            var problems = new List<string>();
            var answers = new Dictionary<string, MoonCargo>();
            for (int i = 0; i < (ports?.Length ?? 0); i++)
            {
                var p = ports[i];
                var at = view.transform.position + view.transform.rotation * p.position;
                var match = runtime.Where(r => r.output == p.output).OrderBy(r => Vector3.Distance(r.position, at)).FirstOrDefault();
                float dist = match == null ? -1f : Vector3.Distance(match.position, at);
                var cargo = MoonWorld.PortCargo(p);
                answers[p.name] = cargo;
                CoreLog.Info($"[selftest] grinder depot {depot} port '{p.name}': PortName '{MoonWorld.PortName(def, p)}' PortLabel '{MoonWorld.PortLabel(def, p)}' PortCargo {(int)cargo}; runtime {(match == null ? "none" : $"'{match.label}' at {AutoBelter.V(match.position)} ({dist:F3} m)")}");
                if (match == null || dist > MoonWorld.PortTolerance) problems.Add($"no runtime port at '{p.name}' ({runtime.Count} runtime)");
            }
            if (!answers.TryGetValue("sl:0:Dust in", out var dust) || dust != MoonCargo.Dust) problems.Add($"'Dust in' -> {(answers.ContainsKey("sl:0:Dust in") ? ((int)dust).ToString() : "missing")}, expected Dust (0)");
            if (!answers.TryGetValue("sl:250:Grit out", out var grit) || grit != (MoonCargo)250) problems.Add($"'Grit out' -> {(answers.ContainsKey("sl:250:Grit out") ? ((int)grit).ToString() : "missing")}, expected 250");
            return (problems.Count == 0, problems.Count == 0 ? "" : string.Join("; ", problems));
        }

        static List<string> Names(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<MoonDepotBuildDefinition.ConveyorPort> ports)
        {
            var names = new List<string>();
            for (int i = 0; i < (ports?.Length ?? 0); i++) names.Add(ports[i]?.name);
            return names;
        }

        static string Describe(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<MoonDepotBuildDefinition.ConveyorPort> ports)
        {
            if (ports == null) return "null";
            var parts = new List<string>();
            for (int i = 0; i < ports.Length; i++)
            {
                var p = ports[i];
                parts.Add(p == null ? "null" : $"'{p.name}' {(p.output ? "out" : "in")} {AutoBelter.V(p.position)} dir {AutoBelter.V(p.direction)}");
            }
            return $"{ports.Length} [{string.Join(", ", parts)}]";
        }

        // The whole BepInEx log so far (freeze errors come before the self-test's watch starts).
        static string ReadWholeLog()
        {
            foreach (var l in BepInEx.Logging.Logger.Listeners.OfType<BepInEx.Logging.DiskLogListener>())
                try { l.LogWriter?.Flush(); } catch (Exception) { }
            string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (Exception) { return null; }
        }

        // Belt input, on the belt AutoBelt built into the example grinder's 'Dust in' port (config: AutoBuild
        // 96,ryanhartwig.example-grinder,94; AutoFeed 96:Dust:10; AutoBelt 96:output->ryanhartwig.example-grinder:Dust in).
        // Four phases, one after the other on that one belt (the grinder has a single input port):
        //  1 belt-in-accepts: three 1 kg dust canisters put on the belt just short of its end (fact (c)'s way) are taken:
        //    CargoEntered for slot 0 adds up to their 3 kg within 30 s, they are gone, and the machine's mass grew by as
        //    much (the mass across all slots: a powered grinder moves dust on into its batch slot at once).
        //  2 belt-in-waits-when-full: with slot 0 filled to 10 kg (CrewProcessorLoad, as AutoFeed does) and the output slot
        //    full (no cycle can drain slot 0), one more dust canister at the end is still there 10 s later, still at the
        //    belt end, with no CargoEntered and no duplicate. Slot 0 is then emptied, which lets it in.
        //  3 belt-in-refuses-cargo: the chamber's sand reaches the end and waits there for 5 s (same canister) with no
        //    CargoEntered of sand and the grinder's status saying a capsule is waiting (within 90 s).
        //  4 belt-canister-modded: a grit canister put at the belt's start rides to the far end (to the end or the queue
        //    waiting there) and still holds cargo 250 (within 40 s).
        static readonly string[] BeltNames = { "belt-in-accepts", "belt-in-waits-when-full", "belt-in-refuses-cargo", "belt-canister-modded" };
        static readonly List<CargoEntered> entered = new();
        static bool enteredHooked;
        static int beltPhase, inBelt = -1, inDepot = -1, waitId = -1, releasedId = -1, enteredMark;
        static float phaseUntil = -1f, waitSince = -1f;
        static double massMark;
        static MoonConveyorPath inPath;
        static readonly List<int> spawned = new();

        internal static bool BeltPending => beltPhase != 0;

        static void BeltChecks()
        {
            if (!enteredHooked)
            {
                enteredHooked = true;
                MoonStructures.CargoEntered += e => { if (e != null) entered.Add(e); };
            }
            var world = CoreState.World ?? CoreState.FindWorld();
            var grinder = MoonStructures.Definition(GrinderId);
            if (world == null || grinder == null) { CoreLog.Info("[selftest] no world or no example grinder: belt checks not applicable"); return; }
            var r = AutoBelter.Results.FirstOrDefault(x => x.To >= 0 && world.depots != null && world.depots.ContainsKey(x.To) && world.depots.get_Item(x.To).kind == grinder.Kind);
            if (r == null) { CoreLog.Info("[selftest] AutoBelt built no belt into the example grinder this run: belt checks not applicable"); return; }
            if (!r.Ok) { foreach (var n in BeltNames) Assert(n, false, $"AutoBelt '{r.Entry}' did not build: {r.Reason}"); return; }
            inBelt = r.Belt;
            inDepot = r.To;
            inPath = world.BeltPath(inBelt);
            if (inPath == null) { foreach (var n in BeltNames) Assert(n, false, $"BeltPath({inBelt}) is null"); return; }
            CoreLog.Info($"[selftest] belt checks on belt {inBelt} (length {inPath.Length:F2} m) into grinder depot {inDepot}; {StateText(world.ProductionOf(inDepot))}");
            StartAccept(world);
        }

        static void StartAccept(MoonWorld world)
        {
            beltPhase = 1;
            spawned.Clear();
            enteredMark = entered.Count;
            massMark = Mass(world.ProductionOf(inDepot));
            float len = inPath.Length;
            foreach (float d in new[] { len - 0.4f, len - 1.15f, len - 1.9f })
            {
                int id = Spawn(world, d, MoonCargo.Dust, out string why);
                if (id < 0) { FailFrom(1, "spawning a dust canister: " + why); return; }
                spawned.Add(id);
            }
            CoreLog.Info($"[selftest] belt-in-accepts: dust canisters [{string.Join(", ", spawned)}] put on belt {inBelt}; grinder mass {massMark} kg");
            phaseUntil = Time.unscaledTime + 30f;
        }

        static void TickBeltChecks()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            float now = Time.unscaledTime;
            try
            {
                switch (beltPhase)
                {
                    case 1: TickAccept(world, now); break;
                    case 2: TickFull(world, now); break;
                    case 3: TickRefuse(world, now); break;
                    case 4: TickGrit(world, now); break;
                }
            }
            catch (Exception e) { FailFrom(beltPhase, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        static void TickAccept(MoonWorld world, float now)
        {
            var s = world.ProductionOf(inDepot);
            var mine = entered.Skip(enteredMark).Where(e => e.DepotId == inDepot).ToList();
            double kg = mine.Where(e => e.Slot == 0 && e.Cargo.Id == "dust").Sum(e => e.Kilograms);
            bool gone = spawned.All(id => !world.canisters.ContainsKey(id));
            double grew = Mass(s) - massMark;
            bool ok = gone && kg >= 3 - 1e-6 && grew >= kg - 1e-6;
            if (!ok && now < phaseUntil) return;
            CoreLog.Info($"[selftest] belt-in-accepts: CargoEntered {mine.Count}x [{string.Join(", ", mine.Select(e => $"slot {e.Slot} {e.Cargo} {e.Kilograms} kg owner {(e.Owner == MoonWorld.LocalPlayer ? "local" : "other")}"))}]; {StateText(s)}");
            Assert("belt-in-accepts", ok, $"canisters gone {gone}, CargoEntered slot-0 dust {kg} kg (want 3), machine mass grew {grew:0.###} kg; {StateText(s)}");
            StartFull(world);
        }

        static void StartFull(MoonWorld world)
        {
            beltPhase = 2;
            var s = world.ProductionOf(inDepot);
            string player = MoonWorld.LocalPlayer;
            // Output first, so no cycle can start between the two fills.
            double outRoom = 10 - s.Stored(2);
            if (outRoom > 0) MoonWorld.Put(s.stores[2], player, outRoom);
            double inRoom = 10 - s.Stored(0);
            if (inRoom > 0) world.CrewProcessorLoad(player, inDepot, MoonCargo.Dust, inRoom);
            waitId = Spawn(world, inPath.Length - 0.4f, MoonCargo.Dust, out string why);
            if (waitId < 0) { FailFrom(2, "spawning a dust canister: " + why); return; }
            enteredMark = entered.Count;
            phaseUntil = Time.unscaledTime + 10f;
            CoreLog.Info($"[selftest] belt-in-waits-when-full: slot 0 and the output filled, dust canister {waitId} put at the belt end; {StateText(s)}");
        }

        static void TickFull(MoonWorld world, float now)
        {
            if (now < phaseUntil) return;
            var s = world.ProductionOf(inDepot);
            bool present = world.canisters.ContainsKey(waitId);
            bool rider = Rider(world, waitId, out int belt, out float d, out bool moving);
            int newEvents = entered.Skip(enteredMark).Count(e => e.DepotId == inDepot);
            int near = 0;
            if (present)
            {
                var at = world.canisters.get_Item(waitId).position;
                foreach (var kv in world.canisters)
                    if (Vector3.Distance(kv.Value.position, at) < 0.3f) near++;
            }
            string status = SafeStatus(s);
            bool ok = present && rider && belt == inBelt && d >= inPath.Length - 0.1f && !moving && newEvents == 0 && s.Stored(0) >= 10 - 1e-6 && near == 1;
            Assert("belt-in-waits-when-full", ok, $"canister {waitId} present {present}, rider {rider} belt {belt} d {d:F2}/{inPath.Length:F2} moving {moving}, CargoEntered since {newEvents}, canisters within 0.3 m {near}, Stored(0) {s.Stored(0)}; status '{status}'");
            CoreLog.Info($"[selftest] belt-in-waits-when-full: status '{status}'; WaitingAtEnd({inBelt}) {world.WaitingAtEnd(inBelt)}; emptying slot 0 to let it in");
            s.stores[0].Clear();
            releasedId = waitId;
            beltPhase = 3;
            waitId = -1;
            waitSince = -1f;
            enteredMark = entered.Count;
            phaseUntil = Time.unscaledTime + 90f;
        }

        static void TickRefuse(MoonWorld world, float now)
        {
            int atEnd = -1;
            float len = inPath.Length;
            foreach (var kv in world.riders)
            {
                var r = kv.Value;
                if (r.belt != inBelt || r.moving || r.distance < len - 0.1f || !world.canisters.ContainsKey(kv.Key)) continue;
                if (world.canisters.get_Item(kv.Key).cargo == MoonCargo.Sand) atEnd = kv.Key;
            }
            if (atEnd >= 0 && atEnd != waitId) { waitId = atEnd; waitSince = now; }
            if (atEnd < 0) { waitId = -1; waitSince = -1f; }
            bool held = waitId >= 0 && now - waitSince >= 5f;
            if (!held && now < phaseUntil) return;
            var s = world.ProductionOf(inDepot);
            string status = SafeStatus(s);
            int sandIn = entered.Skip(enteredMark).Count(e => e.DepotId == inDepot && e.Cargo.Id == "sand");
            bool releasedTaken = releasedId >= 0 && !world.canisters.ContainsKey(releasedId);
            bool ok = held && world.canisters.ContainsKey(waitId) && sandIn == 0 && status != null && status.Contains("waiting");
            CoreLog.Info($"[selftest] belt-in-refuses-cargo: riders on belt {inBelt}: {RidersText(world)}; released dust canister {releasedId} taken {releasedTaken}");
            Assert("belt-in-refuses-cargo", ok, $"sand canister waiting at the end {(waitId >= 0 ? $"{waitId} for {now - waitSince:F1} s" : "none")}, CargoEntered of sand {sandIn}, status '{status}'");
            StartGrit(world);
        }

        static void StartGrit(MoonWorld world)
        {
            beltPhase = 4;
            var grit = MoonStructures.CargoDefinition(GritId);
            if (grit == null) { FailFrom(4, $"'{GritId}' is not registered"); return; }
            waitId = Spawn(world, 0.5f, (MoonCargo)grit.Value, out string why);
            if (waitId < 0) { FailFrom(4, "spawning a grit canister: " + why); return; }
            phaseUntil = Time.unscaledTime + 40f;
            CoreLog.Info($"[selftest] belt-canister-modded: grit canister {waitId} put at the start of belt {inBelt}");
        }

        static void TickGrit(MoonWorld world, float now)
        {
            bool present = world.canisters.ContainsKey(waitId);
            int cargo = present ? (byte)world.canisters.get_Item(waitId).cargo : -1;
            bool rider = Rider(world, waitId, out int belt, out float d, out bool moving);
            int ahead = 0;
            if (rider)
                foreach (var kv in world.riders)
                    if (kv.Key != waitId && kv.Value.belt == inBelt && kv.Value.distance > d) ahead++;
            bool ok = present && rider && belt == inBelt && !moving && d > 1.5f && d >= inPath.Length - MoonWorld.BeltSpacing * ahead - 0.2f && cargo == GritValue;
            if (!ok && now < phaseUntil) return;
            CoreLog.Info($"[selftest] belt-canister-modded: riders on belt {inBelt}: {RidersText(world)}");
            Assert("belt-canister-modded", ok, $"canister {waitId} present {present} cargo {cargo}, rider {rider} belt {belt} d {d:F2}/{inPath.Length:F2} moving {moving}, {ahead} ahead");
            beltPhase = 0;
        }

        static void FailFrom(int phase, string why)
        {
            for (int i = Math.Max(1, phase) - 1; i < BeltNames.Length; i++) Assert(BeltNames[i], false, why);
            beltPhase = 0;
        }

        // A 1 kg canister at distance d along the belt: packed from the walker's carried dust (DevSetCarriedDust +
        // PackDustCanister), then moved onto the belt at the ride height with the given cargo through MoveCanisterRecord
        // (recon fact (c)); the game boards it.
        static int Spawn(MoonWorld world, float d, MoonCargo cargo, out string why)
        {
            why = null;
            var p = inPath.Point(d);
            var up = inPath.Up(p);
            if (up.sqrMagnitude < 0.5f) up = p.normalized;
            string player = MoonWorld.LocalPlayer;
            world.DevSetCarriedDust(player, 1.0);
            if (!world.PackDustCanister(player, p + up * 0.5f, up, out int id)) { why = "PackDustCanister refused"; return -1; }
            if (!world.canisters.ContainsKey(id)) { why = $"canister {id} is not in world.canisters"; return -1; }
            var c = world.canisters.get_Item(id);
            var at = p + up * MoonWorld.BeltRideHeight;
            var rotation = new Il2CppSystem.Nullable<Quaternion>(c.rotation);
            var moved = new MoonDroppedCanister(c.id, c.owner, c.kilograms, c.capacity, at, up, c.createdAt, rotation, cargo);
            moved.physicalPose = c.physicalPose;
            world.MoveCanisterRecord(moved, at, up, rotation);
            return id;
        }

        static bool Rider(MoonWorld world, int id, out int belt, out float d, out bool moving)
        {
            belt = -1; d = -1f; moving = false;
            var riders = world.riders;
            if (riders == null || !riders.ContainsKey(id)) return false;
            var r = riders.get_Item(id);
            belt = r.belt; d = r.distance; moving = r.moving;
            return true;
        }

        static string RidersText(MoonWorld world)
        {
            var parts = new List<(float d, string text)>();
            foreach (var kv in world.riders)
            {
                if (kv.Value.belt != inBelt) continue;
                int cargo = world.canisters.ContainsKey(kv.Key) ? (byte)world.canisters.get_Item(kv.Key).cargo : -1;
                parts.Add((kv.Value.distance, $"[{kv.Key} cargo {cargo} d {kv.Value.distance:F2} moving {kv.Value.moving}]"));
            }
            return parts.Count == 0 ? "none" : string.Join(" ", parts.OrderByDescending(x => x.d).Select(x => x.text));
        }

        static double Mass(MoonProductionState s)
        {
            double m = 0;
            for (int i = 0; s?.stores != null && i < s.stores.Length; i++) m += s.Stored(i);
            return m;
        }

        static string SafeStatus(MoonProductionState s)
        {
            try { return s?.Status; } catch (Exception e) { return "threw " + e.Message; }
        }
    }
}
#endif
