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
        internal static ConfigEntry<string> AutoBelt, AutoBuildLayout, AutoBuildGap;
        internal static ConfigEntry<bool> DumpCards, RegisterBadPorts, SunOverhead, RegisterBoxProbe, LogSun;
        static bool cardsDumped, sunSet;

        static void BindBelts(ConfigFile cfg)
        {
            AutoBelt = cfg.Bind("Dev", "AutoBelt", "", "Dev build: ';'-separated '<from>:<port>-><to>:<port>' belts to build 3 s after the AutoBuild step (before AutoFeed). <from>/<to> is a structure id or vanilla kind number, resolved to the depot AutoBuild built for that entry ('<id>#<n>' picks the n-th entry of an id AutoBuild names more than once); <port> is the definition's port name (IN_SAND), a PortName or PortLabel when only one port has it, or a 0-based index into the definition's conveyorPorts (the '[dev] ports of' lines print all three); the runtime port is the one at that definition port's position. Each belt goes through the game's TryBuildConveyor.");
            DumpCards = cfg.Bind("Dev", "DumpCards", false, "Dev build: once in game (30 s after load, so modded pictures are rendered), write every build-menu card picture (vanilla and modded) to BepInEx/cache/structurelib/cards/<kind>-<name>.png.");
            RegisterBadPorts = cfg.Bind("Dev", "RegisterBadPorts", false, "Dev build: the example plugin also registers ryanhartwig.example-bad-ports, whose input port takes a cargo no slot holds; the registry must drop it at freeze (self-test ports-rules-logged).");
            SunOverhead = cfg.Bind("Dev", "SunOverhead", false, "Dev build: 5 s after a world loads, set the lunar clock (MoonSolarSystem.SetLunarTime) so the sun stands overhead at the walker, so vanilla solar panels supply power (the belt checks need the pressure chamber to make sand). The clock runs in real time, so a cloned save is often at night.");
            LogSun = cfg.Bind("Dev", "LogSun", false, "Dev build: once a second, log every vanilla solar node in world.powerNodes ([sun] lines): kind, SunFactor, Daylight, SunAim, TrackerTilt, MoonSolarSystem.Daylight, LunarTime, dot(sun, up), the spec tilt, and the SunFactor the library predicts from SolvePower's formula (research/POWER_UX_RECON.md section E) with its error. Drive the hour with DevMoney's [Time] TimeOfDay.");
            AutoBuildLayout = cfg.Bind("Dev", "AutoBuildLayout", "ring", "Dev build: where AutoBuild puts its entries. 'ring': 10 m from the walker at 0, 90, 180, 270, 45, ... deg from its facing (a refused site tries the next free heading). 'line': in entry order along the structures' +X (their belt flow axis at yaw 0), AutoBuildGap metres between neighbouring footprints, centred 6 m to the walker's side, so each entry's output faces the next entry's input (a chain of three or more machines).");
            AutoBuildGap = cfg.Bind("Dev", "AutoBuildGap", "1.3", "Dev build: with AutoBuildLayout = line, the metres between neighbouring footprints. One number is every gap; a comma-separated list gives the gaps in entry order (the first is between entries 1 and 2) and its last number repeats for any later gap. A belt box's side belts need room to swing round (at 1.3 m a side-port U-turn is refused: 'Turn too tight'), and the end entries must stay within the build reach (18 m from the walker is refused), so widen only the gaps that need it.");
            CoreLog.Info($"[dev] AutoBuildLayout={AutoBuildLayout.Value} AutoBuildGap={AutoBuildGap.Value}");
        }

        static void TickBelts(float gameT)
        {
            SelfTest.HookBeltEvents();
            SelfTest.TrackChain();
            BoxSpike.Tick(gameT);
            if (SunOverhead.Value && !sunSet && gameT > 5f) { sunSet = true; SetSunOverhead(); }
            if (LogSun.Value) SunLog.Tick(gameT);
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

    // [Dev] LogSun: once a second, one [sun] line per vanilla solar node in world.powerNodes, for fitting the sun factor
    // (research/POWER_UX_RECON.md section E). Besides the game's fields, each line carries the factor Predict computes
    // from SolvePower's own arithmetic, read from GameAssembly.dll, and its error against the game's SunFactor.
    internal static class SunLog
    {
        static float next;
        static string lastSession;

        internal static void Tick(float gameT)
        {
            if (gameT < next && next - gameT <= 1f) return; // gameT restarts at 0 on each world load
            next = gameT + 1f;
            try
            {
                var world = CoreState.World ?? CoreState.FindWorld();
                if (world == null) return;
                var sun = world.solarSystem;
                float lunar = sun != null ? sun.LunarTime : float.NaN;
                Vector3 s = world.SunDirection;
                Vector3 sAt = sun != null ? MoonSolarSystem.SunDirectionAt(lunar) : Vector3.zero;
                string session = $"InLobby {MoonSteamLobby.InLobby} IsHost {MoonSteamLobby.IsHost} MoonCrew.Joined {MoonCrew.Joined}";
                if (session != lastSession) { lastSession = session; CoreLog.Info("[sun] session: " + session); }
                var nodes = world.powerNodes;
                int panels = 0;
                for (int i = 0; nodes != null && i < nodes.Count; i++)
                {
                    var n = nodes.get_Item(i);
                    if (n == null || !n.panel || n.record == null || n.state == null) continue;
                    panels++;
                    try
                    {
                        int kind = n.record.kind;
                        string name = kind == MoonWorld.SolarPostKind ? "post" : kind == MoonWorld.SolarTwinKind ? "twin" : kind == MoonWorld.SolarTrackerKind ? "tracker" : "kind" + kind;
                        var spec = n.spec;
                        var st = n.state;
                        float predicted = Predict(s, n.up, n.rotation, spec.tiltDeg, spec.maxTiltDeg, spec.tracker);
                        float err = Mathf.Abs(predicted - st.SunFactor);
                        CoreLog.Info($"[sun] t {gameT:F0} depot {n.record.id} {name}: SunFactor {st.SunFactor:F4} predicted {predicted:F4} err {err:F4}" +
                            $" Daylight {st.Daylight} SunAim {V(st.SunAim)} TrackerTilt {st.TrackerTilt:F2} | solar.Daylight {MoonSolarSystem.Daylight} LunarTime {lunar:F4}" +
                            $" dot(SunDirectionAt, up) {Vector3.Dot(sAt, n.up):F4} dot(world.SunDirection, up) {Vector3.Dot(s, n.up):F4}" +
                            $" | spec tilt {spec.tiltDeg:F1} max {spec.maxTiltDeg:F1} tracker {spec.tracker} peak {spec.peakKw} UsedKw {st.UsedKw:F3} up {V(n.up)}");
                    }
                    catch (Exception e) { CoreLog.Once("sunlog-node", "[sun] node read threw: " + e.Message); }
                }
                if (panels == 0) CoreLog.Once("sunlog-none", "[sun] no vanilla solar node in world.powerNodes (build a post 93, twin 94 or tracker 95)", 3);
            }
            catch (Exception e) { CoreLog.Once("sunlog", "[sun] LogSun threw: " + e); }
        }

        // MoonWorld.SolvePower's per-panel sun factor (GameAssembly.dll RVA 0x744AC0, +0xDA0..+0x124E), s = world.SunDirection:
        //   h = dot(up, s); d = smoothstep(clamp01((h + 0.0096) / 0.0192)); Daylight = d > 0.5
        //   fixed:   n = rotation * (0, cos tilt, -sin tilt)
        //   tracker: p = normalize(s - up * dot(s, up) / |up|^2) (rotation * back if that is ~0); zenith = 90 - asin(h) deg;
        //            tilt = tiltDeg > zenith ? tiltDeg : min(maxTiltDeg, zenith); n = up * cos tilt + p * sin tilt
        //   SunFactor = max(0, dot(s, n)) * d
        internal static float Predict(Vector3 s, Vector3 up, Quaternion rotation, float tiltDeg, float maxTiltDeg, bool tracker)
        {
            float h = Vector3.Dot(up, s);
            float t = Mathf.Clamp01((h + 0.0096f) / 0.0192f);
            float d = t * t * (3f - 2f * t);
            Vector3 n;
            if (!tracker)
            {
                float r = tiltDeg * Mathf.Deg2Rad;
                n = rotation * new Vector3(0f, Mathf.Cos(r), -Mathf.Sin(r));
            }
            else
            {
                Vector3 p = s;
                float upSq = up.sqrMagnitude;
                if (upSq >= 1e-6f) p = s - up * (Vector3.Dot(s, up) / upSq);
                if (p.sqrMagnitude < 1e-6f) p = rotation * Vector3.back;
                float m = p.magnitude;
                p = m > 1e-5f ? p / m : Vector3.zero;
                float zenith = 90f - Mathf.Asin(Mathf.Clamp(h, -1f, 1f)) * Mathf.Rad2Deg;
                float tilt = tiltDeg > zenith ? tiltDeg : Mathf.Min(maxTiltDeg, zenith);
                float r = tilt * Mathf.Deg2Rad;
                n = up * Mathf.Cos(r) + p * Mathf.Sin(r);
            }
            return Mathf.Max(0f, Vector3.Dot(s, n)) * d;
        }

        static string V(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";
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

        /// <summary>
        /// Builds one more '&lt;from&gt;:&lt;port&gt;-&gt;&lt;to&gt;:&lt;port&gt;' belt now and appends its result to Results (the belt-boxes
        /// self-test rebuilds the belt it removed). Null when there is no world or walker.
        /// </summary>
        internal static Result BuildOne(string entry)
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            var walkers = UnityEngine.Object.FindObjectsByType<MoonWalker>(FindObjectsSortMode.None);
            var walker = walkers != null && walkers.Length > 0 ? walkers[0] : null;
            if (world == null || walker == null) return null;
            var r = new Result { Entry = entry };
            Results.Add(r);
            try { Build(world, walker, r, new HashSet<int>()); }
            catch (Exception e) { r.Reason = "threw " + e.GetType().Name + ": " + e.Message; }
            CoreLog.Info($"[dev] belt (rebuilt) {entry} id={r.Belt} ok={r.Ok} reason='{r.Reason}'");
            return r;
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
            if (depot < 0 && grinder != null && !DevTools.AutoBuildNames(GrinderId))
                CoreLog.Info($"[selftest] '{GrinderId}' was neither built (AutoBuild) nor restored this run: ports-cargo-answer not applicable");
            else Check("ports-cargo-answer",noGrinder ?? (depot < 0 ? "no example grinder depot (AutoBuild it)" : null), () => CargoAnswer(world, depot));

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
        static bool sandSeen;
        static double massMark;
        static MoonConveyorPath inPath;
        static readonly List<int> spawned = new();

        internal static bool BeltPending => beltPhase != 0;

        static void BeltChecks()
        {
            HookBeltEvents();
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
                if (r.belt != inBelt || !world.canisters.ContainsKey(kv.Key) || world.canisters.get_Item(kv.Key).cargo != MoonCargo.Sand) continue;
                sandSeen = true;
                if (!r.moving && r.distance >= len - 0.1f) atEnd = kv.Key;
            }
            if (atEnd >= 0 && atEnd != waitId) { waitId = atEnd; waitSince = now; }
            if (atEnd < 0) { waitId = -1; waitSince = -1f; }
            bool held = waitId >= 0 && now - waitSince >= 5f;
            if (!held && now < phaseUntil) return;
            // Judged by what happened, not by the config: when no sand canister rode this belt in the whole window, the
            // chamber made none (it needs power, e.g. the solar tracker 94 with SunOverhead), and there was nothing to refuse.
            if (!held && !sandSeen)
            {
                CoreLog.Info($"[selftest] no sand canister rode belt {inBelt} into the grinder within 90 s (the chamber made no sand): belt-in-refuses-cargo not applicable");
                StartGrit(world);
                return;
            }
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
        static int Spawn(MoonWorld world, float d, MoonCargo cargo, out string why) => Spawn(world, inPath, d, cargo, out why);

        internal static int Spawn(MoonWorld world, MoonConveyorPath path, float d, MoonCargo cargo, out string why) => Spawn(world, path, d, cargo, 1.0, out why);

        // The same with kg in the canister (its capacity at least kg): the belt-boxes heavy-canister check puts 3 kg.
        internal static int Spawn(MoonWorld world, MoonConveyorPath path, float d, MoonCargo cargo, double kg, out string why)
        {
            why = null;
            var p = path.Point(d);
            var up = path.Up(p);
            if (up.sqrMagnitude < 0.5f) up = p.normalized;
            string player = MoonWorld.LocalPlayer;
            world.DevSetCarriedDust(player, kg);
            if (!world.PackDustCanister(player, p + up * 0.5f, up, out int id)) { why = "PackDustCanister refused"; return -1; }
            if (!world.canisters.ContainsKey(id)) { why = $"canister {id} is not in world.canisters"; return -1; }
            var c = world.canisters.get_Item(id);
            var at = p + up * MoonWorld.BeltRideHeight;
            var rotation = new Il2CppSystem.Nullable<Quaternion>(c.rotation);
            double kilograms = kg == 1.0 ? c.kilograms : kg;
            var moved = new MoonDroppedCanister(c.id, c.owner, kilograms, Math.Max(c.capacity, kilograms), at, up, c.createdAt, rotation, cargo);
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

        static string RidersText(MoonWorld world) => RidersText(world, inBelt);

        internal static string RidersText(MoonWorld world, int beltId)
        {
            var parts = new List<(float d, string text)>();
            foreach (var kv in world.riders)
            {
                if (kv.Value.belt != beltId) continue;
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

    // Belt output and save safety with belts (config: AutoBuild ryanhartwig.example-grinder,ryanhartwig.example-burner,94,
    // ryanhartwig.example-lamp; AutoFeed ryanhartwig.example-grinder:Dust:6;ryanhartwig.example-burner:ryanhartwig.example-grit:4;
    // AutoBelt ryanhartwig.example-grinder:Grit out->ryanhartwig.example-burner:Grit in; AutoSave true). The game emits a
    // modded machine's output by itself (recon fact (f)); these checks follow it end to end:
    //  1 belt-out-emits: a CargoEmitted of grit (250) from the grinder onto that belt, and a grit canister riding it (40 s).
    //  2 belt-chain-feeds: a CargoEntered of grit into the burner's slot 0 (60 s).
    //  3 belt-save-clean, first run: a sand canister put at the belt's end (the burner refuses it, so it waits there) and a
    //    grit canister right behind it; once both wait, a save is requested. The write after it must record the grit
    //    canister in the sidecar, with the belt still in world.conveyors, the canister still riding with cargo 250, and no
    //    refused save ("Moon save failed", a vanilla write that threw); vanilla-save-clean then checks the same write.
    //    Second run (AutoBuild, AutoFeed, AutoBelt empty, the first run's slot): the belt from the restored grinder to the
    //    restored burner is in world.conveyors, a grit canister the sidecar held is riding it with cargo 250 (within 20 s),
    //    and the burner's production state came back with the grit it held when saved.
    internal static partial class SelfTest
    {
        const string BurnerOutId = "ryanhartwig.example-burner";
        static readonly string[] OutNames = { "belt-out-emits", "belt-chain-feeds", "belt-save-clean" };
        static readonly List<(float t, CargoEmitted e)> emitted = new();
        static bool emittedHooked;
        static int outPhase, outBelt = -1, outFrom = -1, outTo = -1, blockId = -1, keepId = -1, outSaveSeq = -1;
        static float outUntil = -1f, outGrace = -1f, outPhaseFrom;
        static string firstRider;
        static MoonConveyorPath outPath;
        static List<int> loadedGrit;

        internal static bool OutPending => outPhase != 0;

        // Hooked from the first in-game frame, so the grinder's first emits (they can come before the self-test starts)
        // and intakes are seen.
        internal static void HookBeltEvents()
        {
            if (!enteredHooked)
            {
                enteredHooked = true;
                MoonStructures.CargoEntered += e => { if (e != null) { entered.Add(e); enteredTimes.Add(Time.unscaledTime); } };
            }
            if (!emittedHooked)
            {
                emittedHooked = true;
                MoonStructures.CargoEmitted += e => { if (e != null) emitted.Add((Time.unscaledTime, e)); };
            }
        }

        static void OutChecks()
        {
            HookBeltEvents();
            var world = CoreState.World ?? CoreState.FindWorld();
            var grinder = MoonStructures.Definition(GrinderId);
            var burner = MoonStructures.Definition(BurnerOutId);
            if (world == null || grinder == null || burner == null) { CoreLog.Info("[selftest] no world, example grinder or example burner: belt output checks not applicable"); return; }
            var r = AutoBelter.Results.FirstOrDefault(x => x.From >= 0 && world.depots != null && world.depots.ContainsKey(x.From) && world.depots.get_Item(x.From).kind == grinder.Kind);
            if (r == null) { StartReloadChecks(world, grinder, burner); return; }
            if (!r.Ok) { foreach (var n in OutNames) Assert(n, false, $"AutoBelt '{r.Entry}' did not build: {r.Reason}"); return; }
            outBelt = r.Belt;
            outFrom = r.From;
            outTo = r.To;
            outPath = world.BeltPath(outBelt);
            if (outPath == null) { foreach (var n in OutNames) Assert(n, false, $"BeltPath({outBelt}) is null"); return; }
            outRouter = RouterAt(world, outTo);
            CoreLog.Info($"[selftest] belt output checks on belt {outBelt} (length {outPath.Length:F2} m) from grinder depot {outFrom} to depot {outTo}{(outRouter != null ? $" (router '{outRouter.Id}')" : "")}; grinder {StateText(world.ProductionOf(outFrom))}");
            outPhase = 1;
            outPhaseFrom = Time.unscaledTime;
            outUntil = outPhaseFrom + 40f;
        }

        static void TickOutChecks()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            float now = Time.unscaledTime;
            try
            {
                switch (outPhase)
                {
                    case 1: TickEmits(world, now); break;
                    case 2: TickChain(world, now); break;
                    case 3: TickBlocked(world, now); break;
                    case 4: TickOutSave(world, now); break;
                    case 5: TickReload(world, now); break;
                }
            }
            catch (Exception e) { FailOut(outPhase, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        static bool GritRider(MoonWorld world, int beltId, out int id, out float d)
        {
            id = -1; d = -1f;
            foreach (var kv in world.riders)
            {
                if (kv.Value.belt != beltId || !world.canisters.ContainsKey(kv.Key)) continue;
                if ((byte)world.canisters.get_Item(kv.Key).cargo != GritValue) continue;
                id = kv.Key; d = kv.Value.distance;
                return true;
            }
            return false;
        }

        static void TickEmits(MoonWorld world, float now)
        {
            if (firstRider == null && GritRider(world, outBelt, out int rid, out float rd))
            {
                var c = world.canisters.get_Item(rid);
                firstRider = $"canister {rid} cargo {(byte)c.cargo} {c.kilograms} kg (capacity {c.capacity}) d {rd:F2}";
            }
            var mine = emitted.Where(x => x.e.DepotId == outFrom && x.e.BeltId == outBelt && x.e.Cargo.Value == GritValue).ToList();
            bool ok = mine.Count > 0 && firstRider != null && mine.All(x => x.e.Definition?.Id == GrinderId);
            if (!ok && now < outUntil) return;
            var gaps = mine.Zip(mine.Skip(1), (a, b) => b.t - a.t).ToList();
            CoreLog.Info($"[selftest] belt-out-emits: CargoEmitted {mine.Count}x from depot {outFrom} on belt {outBelt} [{string.Join(", ", mine.Take(8).Select(x => $"{x.e.Cargo} {x.e.Kilograms} kg"))}]; gaps between emits [{string.Join(" ", gaps.Take(8).Select(g => g.ToString("F2")))}] s; first grit rider: {firstRider ?? "none"}; riders on belt {outBelt}: {RidersText(world, outBelt)}");
            Assert("belt-out-emits", ok, $"CargoEmitted of grit from depot {outFrom} on belt {outBelt}: {mine.Count} (all events {emitted.Count}); grit rider on the belt: {firstRider ?? "none"}; grinder {StateText(world.ProductionOf(outFrom))}");
            // Both later checks take the belt's end to be a machine: a router takes no cargo in (it raises CargoRouted, not
            // CargoEntered) and passes any cargo on, so a canister it refuses never waits there.
            if (outRouter != null)
            {
                CoreLog.Info($"[selftest] the grinder's belt {outBelt} ends at router '{outRouter.Id}' depot {outTo}, not a machine: belt-chain-feeds and belt-save-clean not applicable (the beltboxes-* checks cover that chain)");
                outPhase = 0;
                return;
            }
            outPhase = 2;
            outUntil = outPhaseFrom + 60f;
        }

        static void TickChain(MoonWorld world, float now)
        {
            var mine = entered.Where(e => e.DepotId == outTo && e.Slot == 0 && e.Cargo.Id == GritId).ToList();
            bool ok = mine.Count > 0;
            if (!ok && now < outUntil) return;
            CoreLog.Info($"[selftest] belt-chain-feeds: CargoEntered into burner depot {outTo}: {mine.Count}x [{string.Join(", ", mine.Take(8).Select(e => $"slot {e.Slot} {e.Cargo} {e.Kilograms} kg"))}]; burner {StateText(world.ProductionOf(outTo))}");
            Assert("belt-chain-feeds", ok, $"no CargoEntered of grit into slot 0 of burner depot {outTo} within 60 s; burner {StateText(world.ProductionOf(outTo))}");
            StartBlocked(world);
        }

        // A sand canister at the belt's end (the burner refuses it, so it waits there for good) and a grit canister behind it.
        static void StartBlocked(MoonWorld world)
        {
            outPhase = 3;
            blockId = Spawn(world, outPath, outPath.Length - 0.4f, MoonCargo.Sand, out string why);
            if (blockId < 0) { FailOut(3, "spawning the sand canister: " + why); return; }
            keepId = Spawn(world, outPath, outPath.Length - 0.4f - MoonWorld.BeltSpacing, (MoonCargo)GritValue, out why);
            if (keepId < 0) { FailOut(3, "spawning the grit canister: " + why); return; }
            CoreLog.Info($"[selftest] belt-save-clean: sand canister {blockId} put at the end of belt {outBelt}, grit canister {keepId} behind it");
            outUntil = Time.unscaledTime + 20f;
        }

        static void TickBlocked(MoonWorld world, float now)
        {
            bool sand = Rider(world, blockId, out int sb, out float sd, out bool sm) && sb == outBelt && !sm && sd >= outPath.Length - 0.1f;
            bool grit = Rider(world, keepId, out int gb, out _, out bool gm) && gb == outBelt && !gm;
            if (!(sand && grit) && now < outUntil) return;
            CoreLog.Info($"[selftest] belt-save-clean: riders on belt {outBelt} before the save: {RidersText(world, outBelt)}; burner status '{SafeStatus(world.ProductionOf(outTo))}'");
            if (!(sand && grit)) { FailOut(3, $"the canisters did not settle on belt {outBelt} within 20 s: sand {blockId} waiting {sand}, grit {keepId} waiting {grit}"); return; }
            DevTools.RequestSave();
            outSaveSeq = DevTools.SaveSeqAtRequest;
            outPhase = 4;
            outGrace = -1f;
            outUntil = now + 120f;
        }

        static void TickOutSave(MoonWorld world, float now)
        {
            if (outGrace < 0f)
            {
                bool written = Sidecar.WriteSeq > outSaveSeq;
                Il2CppSystem.Threading.Tasks.Task pending = null;
                try { pending = world?.PendingSave; } catch (Exception) { }
                if (written && (pending == null || pending.IsCompleted)) outGrace = now + 3f;
                else if (now < outUntil) return;
            }
            if (outGrace >= 0f && now < outGrace) return;
            outPhase = 0;
            var w = Sidecar.LastWrite;
            bool beltThere = world.conveyors != null && world.conveyors.ContainsKey(outBelt);
            bool riding = Rider(world, keepId, out int gb, out float gd, out _) && gb == outBelt;
            int cargo = world.canisters.ContainsKey(keepId) ? (byte)world.canisters.get_Item(keepId).cargo : -1;
            string recorded = "no sidecar";
            bool inSidecar = false;
            string path = SidecarPath();
            if (path != null && File.Exists(path))
            {
                var file = SidecarCodec.Deserialize(File.ReadAllText(path));
                var rec = file.Canisters.FirstOrDefault(c => c.Id == keepId);
                inSidecar = rec != null && rec.CargoId == GritId;
                recorded = $"{file.Canisters.Count} canister record(s) [{string.Join(", ", file.Canisters.Select(c => $"{c.Id}:{c.CargoId} {c.Kilograms} kg"))}]";
            }
            int refused = Sidecar.VanillaFailures - vanillaFailuresAtWatch;
            CoreLog.Info($"[selftest] belt-save-clean: write #{w?.Seq} after the request (#{outSaveSeq}); belt {outBelt} in world.conveyors {beltThere}; grit canister {keepId} riding {riding} d {gd:F2} cargo {cargo}; sidecar {recorded}; 'Moon save failed' {saveFailures}x, vanilla writes that threw {refused}");
            bool ok = w != null && w.Seq > outSaveSeq && beltThere && riding && cargo == GritValue && inSidecar && saveFailures == 0 && refused == 0;
            Assert("belt-save-clean", ok, $"write after the request {(w != null && w.Seq > outSaveSeq)}, belt in world {beltThere}, grit canister riding {riding} cargo {cargo}, in the sidecar {inSidecar}, 'Moon save failed' {saveFailures}x, refused writes {refused}");
        }

        // Second run: the first run's belt, grit canister and burner state, restored from the save and its sidecar.
        static void StartReloadChecks(MoonWorld world, StructureDefinition grinder, StructureDefinition burner)
        {
            if (DevTools.AutoBuildNames(GrinderId) || DevTools.AutoBuildNames(BurnerOutId)) { CoreLog.Info("[selftest] AutoBelt built no belt out of the example grinder this run: belt output checks not applicable"); return; }
            loadedGrit = Sidecar.LoadedCanisters().Where(c => c.CargoId == GritId).Select(c => c.Id).ToList();
            outFrom = DepotOfKind(world, grinder.Kind);
            outTo = DepotOfKind(world, burner.Kind);
            if (outFrom >= 0)
                foreach (var kv in world.conveyors)
                {
                    if (kv.Value == null || world.MachineWithPort(kv.Value.start, true) != outFrom) continue;
                    var router = RouterAt(world, world.MachineWithPort(kv.Value.end, false));
                    if (router == null) continue;
                    CoreLog.Info($"[selftest] the restored grinder's belt {kv.Key} ends at router '{router.Id}', not the burner: belt-save-clean (reload) not applicable (beltboxes-reload covers that chain)");
                    return;
                }
            if (outFrom < 0 || outTo < 0 || loadedGrit.Count == 0)
            {
                CoreLog.Info($"[selftest] no restored grinder ({outFrom}), burner ({outTo}) or sidecar grit canister ({loadedGrit.Count}): belt output checks not applicable");
                return;
            }
            outBelt = -1;
            foreach (var kv in world.conveyors)
                if (kv.Value != null && world.MachineWithPort(kv.Value.start, true) == outFrom && world.MachineWithPort(kv.Value.end, false) == outTo) { outBelt = kv.Key; break; }
            CoreLog.Info($"[selftest] belt-save-clean (reload): restored grinder depot {outFrom}, burner depot {outTo}, belt between them {outBelt} ({world.conveyors.Count} belts); sidecar grit canisters [{string.Join(", ", loadedGrit)}]");
            outPhase = 5;
            outUntil = Time.unscaledTime + 20f;
        }

        static void TickReload(MoonWorld world, float now)
        {
            int found = -1;
            float d = -1f;
            if (outBelt >= 0)
                foreach (var kv in world.riders)
                {
                    if (kv.Value.belt != outBelt || !loadedGrit.Contains(kv.Key) || !world.canisters.ContainsKey(kv.Key)) continue;
                    if ((byte)world.canisters.get_Item(kv.Key).cargo != GritValue) continue;
                    found = kv.Key; d = kv.Value.distance;
                    break;
                }
            var r = Sidecar.RestoredProduction.FirstOrDefault(x => x.StructureId == BurnerOutId);
            bool burnerOk = r != null && r.Saved.Sum() > 0 && r.Saved.Length == r.Loaded.Length && r.Saved.Zip(r.Loaded, (a, b) => Math.Abs(a - b) <= 0.01).All(x => x);
            bool ok = outBelt >= 0 && found >= 0 && burnerOk;
            if (!ok && now < outUntil) return;
            outPhase = 0;
            var canisters = loadedGrit.Select(id => world.canisters.ContainsKey(id)
                ? $"{id}: cargo {(byte)world.canisters.get_Item(id).cargo} {world.canisters.get_Item(id).kilograms} kg rider {(Rider(world, id, out int b, out float rd, out bool m) ? $"belt {b} d {rd:F2} moving {m}" : "no")}"
                : $"{id}: not in world.canisters").ToList();
            string burnerText = r == null ? "no burner production restored" : $"burner depot {r.Depot} saved kg [{string.Join(" ", r.Saved)}], at load [{string.Join(" ", r.Loaded)}]";
            CoreLog.Info($"[selftest] belt-save-clean (reload): riders on belt {outBelt}: {(outBelt >= 0 ? RidersText(world, outBelt) : "no belt")}; sidecar grit canisters now [{string.Join("; ", canisters)}]; {burnerText}; burner now {StateText(world.ProductionOf(outTo))}");
            Assert("belt-save-clean", ok, $"belt from grinder {outFrom} to burner {outTo}: {(outBelt >= 0 ? outBelt.ToString() : "none")}; sidecar grit canister riding it: {(found >= 0 ? $"{found} d {d:F2}" : "none")}; {burnerText}");
        }

        static StructureDefinition outRouter;

        /// <summary>The router definition of a depot (a belt box), or null for a machine, a vanilla kind or no depot.</summary>
        internal static StructureDefinition RouterAt(MoonWorld world, int depot)
        {
            if (depot < 0 || world?.depots == null || !world.depots.ContainsKey(depot)) return null;
            var d = MoonStructures.Definition(world.depots.get_Item(depot)?.kind ?? -1);
            return d?.Router != null ? d : null;
        }

        static void FailOut(int phase, string why)
        {
            int from = phase >= 3 ? 2 : Math.Max(1, phase) - 1;
            for (int i = from; i < OutNames.Length; i++) Assert(OutNames[i], false, why);
            outPhase = 0;
        }
    }

    // [Dev] AutoBuildLayout = line: AutoBuild puts its entries on one line along the structures' local +X at yaw 0 (every
    // donor's flow axis; the tangent-plane projection of world +X, which the recon found to be the structures' +X near
    // slot-2's walker), in entry order, so each entry's +X output faces the next entry's input. Neighbours stand
    // GapMetres ([Dev] AutoBuildGap, 1.3 m by default) apart between their footprints (half footprints along X from their build definitions; the chamber to the
    // press is then 10 m centre to centre, a 2.95 m belt). The line is centred on the walker along +X and SideMetres to
    // its side nearest 270 deg from its facing (slot-2 has vanilla structures ahead of the spawn point and at 90 deg),
    // which keeps every site within the build reach (the game refuses 18 m: "Move closer to the build site."). One site
    // per entry: a refused site is reported, not moved.
    internal static class AutoLine
    {
        internal const float SideMetres = 6f, SideHeading = 270f;

        /// <summary>[Dev] AutoBuildGap: the gap after entry i (0-based) in metres; 1.3 when unset or unreadable.</summary>
        internal static float GapMetres(int i)
        {
            var gaps = new List<float>();
            foreach (var x in (DevTools.AutoBuildGap?.Value ?? "").Split(','))
                if (float.TryParse(x.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float g) && g >= 0f) gaps.Add(g);
            return gaps.Count == 0 ? 1.3f : gaps[Math.Min(i, gaps.Count - 1)];
        }
        static readonly List<float> along = new();

        internal static bool On => string.Equals(DevTools.AutoBuildLayout?.Value?.Trim(), "line", StringComparison.OrdinalIgnoreCase);

        /// <summary>Lays out the line for AutoBuild's entries (structure ids or vanilla kind numbers).</summary>
        internal static void Prepare(MoonWorld world, List<string> entries)
        {
            along.Clear();
            var half = entries.Select(e =>
            {
                var (who, yaw) = AutoBuilder.SplitYaw(e);
                int kind = int.TryParse(who, out int k) ? k : MoonStructures.Definition(who)?.Kind ?? -1;
                var def = kind < 0 ? null : world.BuildDefinition(kind);
                if (def == null) return 2f;
                // The footprint's extent along the line at the entry's yaw ('<id>@<yaw>').
                float r = yaw * Mathf.Deg2Rad;
                return Mathf.Abs(Mathf.Cos(r)) * def.halfFootprint.x + Mathf.Abs(Mathf.Sin(r)) * def.halfFootprint.y;
            }).ToList();
            float x = 0f;
            for (int i = 0; i < half.Count; i++)
            {
                if (i > 0) x += half[i - 1] + GapMetres(i - 1) + half[i];
                along.Add(x);
            }
            if (half.Count == 0) return;
            float mid = ((along[0] - half[0]) + (along[half.Count - 1] + half[half.Count - 1])) / 2f;
            for (int i = 0; i < along.Count; i++) along[i] -= mid;
            CoreLog.Info($"[dev] AutoBuild line: half footprints [{string.Join(", ", half.Select(h => h.ToString("F2")))}], gaps [{string.Join(", ", Enumerable.Range(0, Math.Max(0, half.Count - 1)).Select(i => GapMetres(i).ToString("F2")))}] m, centres along +X [{string.Join(", ", along.Select(a => a.ToString("F2")))}] m, {SideMetres:F0} m to the side");
        }

        internal static (float h, Vector3 at, string where) Site(Transform walker, int index)
        {
            var up = walker.position.normalized;
            var axis = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
            var side = Vector3.Cross(up, axis).normalized;
            if (Vector3.Dot(side, Quaternion.AngleAxis(SideHeading, up) * walker.forward) < 0f) side = -side;
            float a = index < along.Count ? along[index] : 0f;
            var at = walker.position + side * SideMetres + axis * a;
            return (-1000f - index, at, $"on the line (entry {index + 1} of {along.Count}, {a:F2} m along +X {AutoBelter.V(axis)}, {SideMetres:F0} m to the side {AutoBelter.V(side)}, {Vector3.Distance(at, walker.position):F1} m from the walker)");
        }
    }

    // The SandPower chain (config: AutoBuildLayout line; AutoBuild 95,96,ryanhartwig.coking-oven,
    // ryanhartwig.coalite-furnace; AutoFeed 96:Dust:12 only; AutoBelt 96:output->ryanhartwig.coking-oven:Sand in;
    // ryanhartwig.coking-oven:Coalite out->ryanhartwig.coalite-furnace:Coalite in; AutoSave; SunOverhead).
    // The vanilla chamber's sand rides into the oven, the oven's coalite rides into the generator:
    //  1 sandpower-chain: within ChainSeconds of the self-test the generator holds charge with no AutoFeed entry for
    //    either SandPower machine, with CargoEntered seen for both and CargoEmitted of coalite (cargo 3) from the
    //    oven. The first sand canister, oven cycle, coalite and charge are timed from the chamber's feed.
    //  2 sandpower-save-clean: a coalite canister put on the sand belt (the oven refuses it at its sand port, so it
    //    waits at the end), then a save: the write after it records that canister, both production states and every
    //    coalite canister in the world, with both belts still built and no refused save.
    //  Reload run (AutoBuild, AutoFeed, AutoBelt empty, the chain run's slot): sandpower-reload, both belts between the
    //    restored machines, every coalite canister the sidecar held back with cargo 3 (one riding the sand belt), and
    //    both production states as saved. The model checks (DevModels.cs) run in both runs.
    internal static partial class SelfTest
    {
        internal const float ChainSeconds = 180f;
        static int chainPhase, sandBelt = -1, briqBelt = -1, pinId = -1, chainSaveSeq = -1;
        static float chainUntil = -1f, chainGrace = -1f;
        static List<int> loadedBriq;
        static readonly List<float> enteredTimes = new();
        static float tOrigin = -1f, tSand = -1f, tSandIn = -1f, tCycle = -1f, tBriq = -1f, tBriqIn = -1f, tCharge = -1f;
        static int trackC = -1, trackG = -1, trackBelt = -1;

        internal static bool SandChainPending => chainPhase != 0;

        internal static bool BeltedInto(int depot) => AutoBelter.Results.Any(r => r.Ok && r.To == depot);

        static byte CoaliteValue => (byte)(MoonStructures.CargoDefinition(CoaliteId)?.Value ?? 3);

        // Every in-game frame (DevTools.TickBelts): once AutoBelt has built a belt into the oven, note the first time
        // each step of the chain is seen. Times are from the chamber's AutoFeed (else from when tracking started).
        internal static void TrackChain()
        {
            if (tCharge >= 0f || AutoBelter.Results.Count == 0) return;
            var world = CoreState.World ?? CoreState.FindWorld();
            if (world == null) return;
            float now = Time.unscaledTime;
            try
            {
                if (trackBelt < 0)
                {
                    var c = MoonStructures.Definition(OvenId);
                    if (c == null) return;
                    var r = AutoBelter.Results.FirstOrDefault(x => x.Ok && x.To >= 0 && world.depots.ContainsKey(x.To) && world.depots.get_Item(x.To).kind == c.Kind);
                    if (r == null) return;
                    trackBelt = r.Belt;
                    trackC = r.To;
                    var g = MoonStructures.Definition(GeneratorId);
                    trackG = g != null ? DepotOfKind(world, g.Kind) : -1;
                    tOrigin = now;
                }
                // AutoBelt runs 2 s before AutoFeed; once the chamber's feed exists, the times count from it.
                var feed = AutoFeeder.For("96");
                if (feed != null) tOrigin = feed.At;
                if (tSand < 0f)
                    foreach (var kv in world.riders)
                        if (kv.Value.belt == trackBelt && world.canisters.ContainsKey(kv.Key) && world.canisters.get_Item(kv.Key).cargo == MoonCargo.Sand) { tSand = now; break; }
                for (int i = 0; i < entered.Count && i < enteredTimes.Count && (tSandIn < 0f || tBriqIn < 0f); i++)
                {
                    if (tSandIn < 0f && entered[i].DepotId == trackC) tSandIn = enteredTimes[i];
                    if (tBriqIn < 0f && entered[i].DepotId == trackG) tBriqIn = enteredTimes[i];
                }
                if (tBriq < 0f) foreach (var x in emitted) if (x.e.DepotId == trackC && x.e.Cargo.Value == CoaliteValue) { tBriq = x.t; break; }
                var cs = world.ProductionOf(trackC);
                if (tCycle < 0f && cs != null && cs.Cycles >= 1) tCycle = now;
                var gs = trackG >= 0 ? world.ProductionOf(trackG) : null;
                if (tCharge < 0f && gs != null && gs.ChargeKwh > 0) tCharge = now;
            }
            catch (Exception) { }
        }

        static string T(float t) => t < 0f ? "not seen" : $"{t - tOrigin:F1} s";

        static string TimingText() =>
            $"from the chamber's feed: first sand canister on belt {trackBelt} {T(tSand)}, first sand into the oven {T(tSandIn)}, first oven cycle {T(tCycle)}, first coalite emitted {T(tBriq)}, first coalite into the generator {T(tBriqIn)}, first generator charge {T(tCharge)}";

        static void SandChainChecks(MoonWorld world)
        {
            SandModelChecks(world);
            var sr = AutoBelter.Results.FirstOrDefault(r => ovenDepot >= 0 && r.To == ovenDepot);
            var br = AutoBelter.Results.FirstOrDefault(r => ovenDepot >= 0 && r.From == ovenDepot);
            if (sr == null && br == null) { CoreLog.Info("[selftest] AutoBelt built no belt into or out of the coking oven: sandpower-chain not applicable"); return; }
            string why = sr == null ? "no AutoBelt entry into the oven" : !sr.Ok ? $"AutoBelt '{sr.Entry}' did not build: {sr.Reason}"
                : br == null ? "no AutoBelt entry out of the oven" : !br.Ok ? $"AutoBelt '{br.Entry}' did not build: {br.Reason}"
                : br.To != generatorDepot ? $"the oven's belt ends at depot {br.To}, not the generator ({generatorDepot})" : null;
            if (why != null) { Assert("sandpower-chain", false, why); Assert("sandpower-save-clean", false, why); return; }
            sandBelt = sr.Belt;
            briqBelt = br.Belt;
            chainPhase = 1;
            chainUntil = Time.unscaledTime + ChainSeconds;
            CoreLog.Info($"[selftest] sandpower-chain: sand belt {sandBelt} ({world.BeltPath(sandBelt)?.Length:F2} m) from depot {sr.From} into oven {ovenDepot}, coalite belt {briqBelt} ({world.BeltPath(briqBelt)?.Length:F2} m) into generator {generatorDepot}; waiting up to {ChainSeconds:F0} s");
        }

        internal static void TickSandChain()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            float now = Time.unscaledTime;
            try
            {
                switch (chainPhase)
                {
                    case 1: TickChainRuns(world, now); break;
                    case 2: TickPin(world, now); break;
                    case 3: TickChainSave(world, now); break;
                    case 4: TickSandReload(world, now); break;
                }
            }
            catch (Exception e)
            {
                string name = chainPhase == 1 ? "sandpower-chain" : chainPhase == 4 ? "sandpower-reload" : "sandpower-save-clean";
                Assert(name, false, "threw " + e.GetType().Name + ": " + e.Message);
                if (chainPhase == 1) Assert("sandpower-save-clean", false, "the chain check threw");
                chainPhase = 0;
            }
        }

        static void TickChainRuns(MoonWorld world, float now)
        {
            var c = world.ProductionOf(ovenDepot);
            var g = world.ProductionOf(generatorDepot);
            bool noFeed = AutoFeeder.For(OvenId) == null && AutoFeeder.For(GeneratorId) == null;
            var inC = entered.Where(e => e.DepotId == ovenDepot).ToList();
            var inG = entered.Where(e => e.DepotId == generatorDepot).ToList();
            var outC = emitted.Where(x => x.e.DepotId == ovenDepot && x.e.Cargo.Value == CoaliteValue).ToList();
            bool ok = g != null && g.ChargeKwh > 0 && noFeed && inC.Count > 0 && inG.Count > 0 && outC.Count > 0;
            if (!ok && now < chainUntil) return;
            CoreLog.Info($"[selftest] sandpower-chain: {TimingText()}");
            CoreLog.Info($"[selftest] sandpower-chain: CargoEntered oven {inC.Count}x ({inC.Sum(e => e.Kilograms)} kg {string.Join("/", inC.Select(e => e.Cargo.Id).Distinct())}), generator {inG.Count}x ({inG.Sum(e => e.Kilograms)} kg {string.Join("/", inG.Select(e => e.Cargo.Id).Distinct())}); CargoEmitted from the oven {outC.Count}x ({outC.Sum(x => x.e.Kilograms)} kg, belts [{string.Join(",", outC.Select(x => x.e.BeltId).Distinct())}]); riders on belt {sandBelt}: {RidersText(world, sandBelt)}; on belt {briqBelt}: {RidersText(world, briqBelt)}");
            CoreLog.Info($"[selftest] sandpower-chain: oven {StateText(c)} power {c?.Power:F2} grid {c?.Grid}; generator {PowerText(g)}");
            Assert("sandpower-chain", ok, $"generator charge {g?.ChargeKwh:F4} kWh, AutoFeed of a SandPower machine {!noFeed}, CargoEntered oven {inC.Count} generator {inG.Count}, CargoEmitted cargo {CoaliteValue} from the oven {outC.Count} (within {ChainSeconds:F0} s)");
            StartPin(world);
        }

        // A coalite canister put near the start of the sand belt: it rides to the end (or the queue there), and the
        // oven refuses it at its sand port, so it is still on the belt when the save is written.
        static void StartPin(MoonWorld world)
        {
            chainPhase = 2;
            var path = world.BeltPath(sandBelt);
            if (path == null) { Assert("sandpower-save-clean", false, $"BeltPath({sandBelt}) is null"); chainPhase = 0; return; }
            pinId = Spawn(world, path, Math.Min(0.5f, path.Length / 4f), (MoonCargo)CoaliteValue, out string why);
            if (pinId < 0) { Assert("sandpower-save-clean", false, "spawning a coalite canister: " + why); chainPhase = 0; return; }
            CoreLog.Info($"[selftest] sandpower-save-clean: coalite canister {pinId} put on the sand belt {sandBelt} ({path.Length:F2} m)");
            chainUntil = Time.unscaledTime + 30f;
        }

        static void TickPin(MoonWorld world, float now)
        {
            bool waiting = Rider(world, pinId, out int b, out float d, out bool moving) && b == sandBelt && !moving;
            if (!waiting && now < chainUntil) return;
            CoreLog.Info($"[selftest] sandpower-save-clean: riders on belt {sandBelt} before the save: {RidersText(world, sandBelt)}; on belt {briqBelt}: {RidersText(world, briqBelt)}; oven status '{SafeStatus(world.ProductionOf(ovenDepot))}'");
            if (!waiting) { Assert("sandpower-save-clean", false, $"coalite canister {pinId} did not settle on belt {sandBelt} within 30 s (rider belt {b} d {d:F2} moving {moving})"); chainPhase = 0; return; }
            DevTools.RequestSave();
            chainSaveSeq = DevTools.SaveSeqAtRequest;
            chainPhase = 3;
            chainGrace = -1f;
            chainUntil = now + 120f;
        }

        static void TickChainSave(MoonWorld world, float now)
        {
            if (chainGrace < 0f)
            {
                bool written = Sidecar.WriteSeq > chainSaveSeq;
                Il2CppSystem.Threading.Tasks.Task pending = null;
                try { pending = world?.PendingSave; } catch (Exception) { }
                if (written && (pending == null || pending.IsCompleted)) chainGrace = now + 3f;
                else if (now < chainUntil) return;
            }
            if (chainGrace >= 0f && now < chainGrace) return;
            chainPhase = 0;
            var w = Sidecar.LastWrite;
            bool belts = world.conveyors != null && world.conveyors.ContainsKey(sandBelt) && world.conveyors.ContainsKey(briqBelt);
            bool riding = Rider(world, pinId, out int pb, out float pd, out _) && pb == sandBelt;
            int pinCargo = world.canisters.ContainsKey(pinId) ? (byte)world.canisters.get_Item(pinId).cargo : -1;
            var live = new List<int>();
            foreach (var kv in world.canisters) if ((byte)kv.Value.cargo == CoaliteValue) live.Add(kv.Key);
            string path = SidecarPath();
            bool pinSaved = false, prodC = false, prodG = false;
            var missing = new List<int>();
            string recorded = "no sidecar";
            if (path != null && File.Exists(path))
            {
                var file = SidecarCodec.Deserialize(File.ReadAllText(path));
                var briq = file.Canisters.Where(x => x.CargoId == CoaliteId).Select(x => x.Id).ToHashSet();
                pinSaved = briq.Contains(pinId);
                missing = live.Where(id => !briq.Contains(id)).ToList();
                var pc = file.Production.FirstOrDefault(p => p.DepotId == ovenDepot);
                var pg = file.Production.FirstOrDefault(p => p.DepotId == generatorDepot);
                prodC = pc != null && pc.Id == OvenId;
                prodG = pg != null && pg.Id == GeneratorId;
                recorded = $"{file.Canisters.Count} canister record(s), {briq.Count} coalite [{string.Join(", ", briq)}]; production oven {(pc == null ? "none" : $"stores [{string.Join(" ", pc.Stores.Select(s => s.Sum(x => x.Pending)))}] kg cargo [{string.Join(", ", pc.SlotCargoIds)}]")}, generator {(pg == null ? "none" : $"stores [{string.Join(" ", pg.Stores.Select(s => s.Sum(x => x.Pending)))}] kg charge {pg.Charge:F3}")}";
            }
            int refused = Sidecar.VanillaFailures - vanillaFailuresAtWatch;
            CoreLog.Info($"[selftest] sandpower-save-clean: write #{w?.Seq} after the request (#{chainSaveSeq}); belts {sandBelt},{briqBelt} built {belts}; coalite canister {pinId} riding {riding} d {pd:F2} cargo {pinCargo}; coalite canisters in the world [{string.Join(", ", live)}]; sidecar {recorded}; 'Moon save failed' {saveFailures}x, vanilla writes that threw {refused}");
            bool ok = w != null && w.Seq > chainSaveSeq && belts && riding && pinCargo == CoaliteValue && pinSaved && missing.Count == 0 && prodC && prodG && saveFailures == 0 && refused == 0;
            Assert("sandpower-save-clean", ok, $"write after the request {(w != null && w.Seq > chainSaveSeq)}, belts built {belts}, canister {pinId} riding {riding} cargo {pinCargo} in the sidecar {pinSaved}, coalite canisters missing from the sidecar [{string.Join(", ", missing)}], production records oven {prodC} generator {prodG}, 'Moon save failed' {saveFailures}x, refused writes {refused}");
        }

        // Reload run: the chain run's machines, belts, coalite canisters and production states, restored.
        static void SandReloadChecks()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            StructureDefinition c = MoonStructures.Definition(OvenId), g = MoonStructures.Definition(GeneratorId);
            if (world == null || c == null || g == null || !Sidecar.LoadedIds().Contains(OvenId))
            {
                CoreLog.Info($"[selftest] '{OvenId}' was neither built by AutoBuild nor restored from the sidecar this run: SandPower checks not applicable");
                return;
            }
            ovenDepot = DepotOfKind(world, c.Kind);
            generatorDepot = DepotOfKind(world, g.Kind);
            SandModelChecks(world);
            loadedBriq = Sidecar.LoadedCanisters().Where(x => x.CargoId == CoaliteId).Select(x => x.Id).ToList();
            sandBelt = briqBelt = -1;
            foreach (var kv in world.conveyors)
            {
                if (kv.Value == null || ovenDepot < 0) continue;
                int from = world.MachineWithPort(kv.Value.start, true), to = world.MachineWithPort(kv.Value.end, false);
                if (to == ovenDepot) sandBelt = kv.Key;
                if (from == ovenDepot && to == generatorDepot) briqBelt = kv.Key;
            }
            CoreLog.Info($"[selftest] sandpower-reload: restored oven depot {ovenDepot}, generator depot {generatorDepot}; belt into the oven {sandBelt}, oven to generator {briqBelt} ({world.conveyors.Count} belts); sidecar coalite canisters [{string.Join(", ", loadedBriq)}]");
            chainPhase = 4;
            chainUntil = Time.unscaledTime + 20f;
        }

        static void TickSandReload(MoonWorld world, float now)
        {
            var back = loadedBriq.Where(id => world.canisters.ContainsKey(id) && (byte)world.canisters.get_Item(id).cargo == CoaliteValue).ToList();
            bool onSand = back.Any(id => Rider(world, id, out int b, out _, out _) && b == sandBelt);
            var rc = Sidecar.RestoredProduction.FirstOrDefault(x => x.StructureId == OvenId);
            var rg = Sidecar.RestoredProduction.FirstOrDefault(x => x.StructureId == GeneratorId);
            bool ok = ovenDepot >= 0 && generatorDepot >= 0 && sandBelt >= 0 && briqBelt >= 0 && loadedBriq.Count > 0 && back.Count == loadedBriq.Count && onSand && Same(rc) && Same(rg);
            if (!ok && now < chainUntil) return;
            chainPhase = 0;
            CoreLog.Info($"[selftest] sandpower-reload: riders on belt {sandBelt}: {(sandBelt >= 0 ? RidersText(world, sandBelt) : "no belt")}; on belt {briqBelt}: {(briqBelt >= 0 ? RidersText(world, briqBelt) : "no belt")}; oven {Prod(rc)}, now {StateText(world.ProductionOf(ovenDepot))}; generator {Prod(rg)}, now {PowerText(world.ProductionOf(generatorDepot))}");
            Assert("sandpower-reload", ok, $"depots oven {ovenDepot} generator {generatorDepot}; belts {sandBelt},{briqBelt}; coalite canisters back with cargo {CoaliteValue} {back.Count} of {loadedBriq.Count}, one on the sand belt {onSand}; oven {Prod(rc)}; generator {Prod(rg)}");

            static bool Same(Sidecar.ProductionRestore r) => r != null && r.Saved.Length == r.Loaded.Length && r.Saved.Zip(r.Loaded, (a, b) => Math.Abs(a - b) <= 0.01).All(x => x);
            static string Prod(Sidecar.ProductionRestore r) => r == null ? "not restored" : $"depot {r.Depot} saved kg [{string.Join(" ", r.Saved)}], at load [{string.Join(" ", r.Loaded)}]";
        }
    }

    // [Dev] RegisterBoxProbe: the example plugin registers ryanhartwig.example-box-probe, a router (a splitter: In on -X,
    // Forward on +X, Left on +Z, Right on -Z) cloned from the floodlight (kind 4) with a 1.4 x 1.4 m Footprint. 15 s
    // after load: the donor's and the probe's build definitions (halfFootprint, Routes(def, kind), port count), the
    // prefab components and colliders of both and of the probe's built view, MachineJoins for the probe's runtime ports,
    // and three dust canisters put on the AutoBelt belt into the probe, followed for 10 s (each hops onto a belt at
    // one of the probe's outputs that can take it, or waits: RouterHop, CargoRouted). Belt-boxes plan Tasks 1 and 3.
    internal static class BoxSpike
    {
        internal const string ProbeId = "ryanhartwig.example-box-probe";
        static readonly int[] DonorKinds = { 4 };

        static bool donorDone;
        static int dustId = -1, dustBelt = -1, dustDepot = -1;
        static float dustFrom = -1f, dustNext = -1f;

        internal static void Tick(float gameT)
        {
            try
            {
                var world = CoreState.World ?? CoreState.FindWorld();
                if (world == null) return;
                float now = Time.unscaledTime;
                if (DevTools.RegisterBoxProbe.Value && !donorDone && gameT > 15f) { donorDone = true; Donors(world); }
                if (dustId >= 0 && now >= dustNext) DustTick(world, now);
            }
            catch (Exception e) { CoreLog.Once("spike-tick", "[spike] tick threw: " + e, 3); }
        }

        static void Donors(MoonWorld world)
        {
            var probe = MoonStructures.Definition(ProbeId);
            foreach (int k in DonorKinds.Concat(probe != null ? new[] { probe.Kind } : Array.Empty<int>()))
            {
                try
                {
                    var def = world.BuildDefinition(k);
                    if (def == null) { CoreLog.Info($"[spike] donor {k}: no build definition"); continue; }
                    string routes;
                    try { routes = MoonWorld.Routes(def, k).ToString(); } catch (Exception e) { routes = "threw " + e.Message; }
                    CoreLog.Info($"[spike] donor {k} '{def.displayName}': halfFootprint ({def.halfFootprint.x:F2},{def.halfFootprint.y:F2}) price {def.priceDollars} ports {def.conveyorPorts?.Length ?? 0} Routes(def, {k}) {routes}");
                    Components($"donor {k} prefab", def.prefab);
                }
                catch (Exception e) { CoreLog.Warn($"[spike] donor {k} threw: {e.Message}"); }
            }
            if (probe == null) { CoreLog.Info($"[spike] '{ProbeId}' is not registered (RegisterBoxProbe read too late, or ExampleStructure missing)"); return; }
            int depot = DevTools.BuildResults.TryGetValue(ProbeId, out var b) && b.ok ? b.depot : SelfTest.DepotOfKind(world, probe.Kind);
            if (depot < 0) { CoreLog.Info("[spike] no probe depot (AutoBuild it)"); return; }
            var rec = world.depots.get_Item(depot);
            var view = UnityEngine.Object.FindFirstObjectByType<MoonDepotConstruction>()?.ViewOf(depot);
            Components($"probe depot {depot} view", view);
            var colliders = view == null ? null : view.GetComponentsInChildren<Collider>(true);
            string colliderText = colliders == null ? "no view" : string.Join(", ", colliders.Select(c => c.GetIl2CppType().Name + " on '" + PathOf(c.transform, view.transform) + "'"));
            CoreLog.Info($"[spike] probe depot {depot}: colliders on the view [{colliderText}]; ports {PortResolver.Describe(MoonStructures.PortsOf(probe)?.ToList())}");
            foreach (var p in AutoBelter.RuntimePorts(world, rec))
            {
                string joins;
                try { joins = world.MachineJoins(rec.kind, p).ToString(); } catch (Exception e) { joins = "threw " + e.Message; }
                CoreLog.Info($"[spike] probe depot {depot} runtime port '{p.label}' {(p.output ? "out" : "in")} at {AutoBelter.V(p.position)} dir {AutoBelter.V(p.direction)} belt {p.belt}: MachineJoins {joins}");
            }
            var r = AutoBelter.Results.FirstOrDefault(x => x.Ok && x.To >= 0 && world.depots.ContainsKey(x.To) && world.depots.get_Item(x.To).kind == probe.Kind);
            if (r == null) { CoreLog.Info("[spike] no AutoBelt belt into a probe: dust canister not placed"); return; }
            // Three dust canisters, as the belt's spacing allows: each hops to the next output that can take it, and
            // one that no output can take (an output belt full, the others not joined) waits at the input.
            var path = world.BeltPath(r.Belt);
            if (!routedHooked) { routedHooked = true; MoonStructures.CargoRouted += c => { if (c.DepotId == dustDepot) routed.Add(c); }; }
            routed.Clear();
            dustDepot = r.To;
            var ids = new List<int>();
            foreach (float d in new[] { path.Length - 0.4f, path.Length - 1.15f, path.Length - 1.9f })
            {
                if (d < 0.1f) break;
                int id = SelfTest.Spawn(world, path, d, MoonCargo.Dust, out string why);
                CoreLog.Info($"[spike] probe dust: canister {id} put at d {d:F2} of belt {r.Belt} (length {path.Length:F2}) into probe depot {r.To}{(id < 0 ? ": " + why : "")}");
                if (id >= 0) ids.Add(id);
            }
            if (ids.Count == 0) return;
            dustId = ids[0]; dustBelt = r.Belt;
            outBelt = AutoBelter.Results.FirstOrDefault(x => x.Ok && x.From == r.To)?.Belt ?? -1;
            dustFrom = Time.unscaledTime; dustNext = dustFrom + 1f;
        }

        static readonly List<CargoRouted> routed = new();
        static bool routedHooked;
        static int outBelt = -1;

        static void DustTick(MoonWorld world, float now)
        {
            dustNext = now + 2f;
            CoreLog.Info($"[spike] probe dust t+{now - dustFrom:F0} s: first {Canister(world, dustId)}; CargoRouted {routed.Count}x [{string.Join(", ", routed.Select(c => $"{c.Kilograms} kg {c.Cargo} belt {c.FromBeltId}->{c.ToBeltId}"))}]; riders on in belt {dustBelt}: {SelfTest.RidersText(world, dustBelt)}; WaitingAtEnd({dustBelt}) {Try(() => world.WaitingAtEnd(dustBelt).ToString())}; riders on out belt {outBelt}: {(outBelt >= 0 ? SelfTest.RidersText(world, outBelt) : "none")}");
            if (now > dustFrom + 10.5f) dustId = -1;
        }

        static void Components(string what, GameObject go)
        {
            if (go == null) { CoreLog.Info($"[spike] {what}: null"); return; }
            var comps = go.GetComponentsInChildren<Component>(true);
            var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var where = new List<string>();
            int colliders = 0;
            foreach (var c in comps)
            {
                if (c == null) continue;
                string n = c.GetIl2CppType().Name;
                count[n] = count.TryGetValue(n, out int k) ? k + 1 : 1;
                if (c.TryCast<Collider>() != null) colliders++;
                if (n.StartsWith("Moon", StringComparison.Ordinal)) where.Add($"{n} on '{PathOf(c.transform, go.transform)}'");
            }
            CoreLog.Info($"[spike] {what} '{go.name}': {comps.Length} components, {colliders} colliders; types: {string.Join(", ", count.Select(kv => kv.Value > 1 ? $"{kv.Key} x{kv.Value}" : kv.Key))}; game scripts: {(where.Count == 0 ? "none" : string.Join("; ", where))}");
        }

        static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var x = t; x != null && x != root; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return parts.Count == 0 ? "<root>" : string.Join("/", parts);
        }

        // One canister: present, kilograms / capacity, cargo, rider (belt, distance, moving).
        static string Canister(MoonWorld world, int id) => Try(() =>
        {
            if (world.canisters == null || !world.canisters.ContainsKey(id)) return $"canister {id} gone";
            var c = world.canisters.get_Item(id);
            string rider = "no rider";
            if (world.riders != null && world.riders.ContainsKey(id))
            {
                var r = world.riders.get_Item(id);
                rider = $"rider belt {r.belt} d {r.distance:F2} moving {r.moving}";
            }
            return $"canister {id} {c.kilograms:F2} kg / capacity {c.capacity:F2}, cargo {(byte)c.cargo}, at {AutoBelter.V(c.position)}, {rider}";
        });

        static string Try(Func<string> f)
        {
            try { return f(); } catch (Exception e) { return "threw " + e.GetType().Name + ": " + e.Message; }
        }
    }
}
#endif
