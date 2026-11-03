using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    /// <summary>Public entry point for structure mods.</summary>
    public static class MoonStructures
    {
        static readonly Dictionary<string, StructureDefinition> byId = new(StringComparer.Ordinal);
        static readonly Dictionary<int, StructureDefinition> byIndex = new();
        static readonly Dictionary<string, CargoDefinition> cargoById = new(StringComparer.Ordinal);
        static readonly Dictionary<string, (string Guid, string Version)> cargoOwner = new(StringComparer.Ordinal);
        static readonly Dictionary<byte, CargoDefinition> byCargoValue = new();

        /// <summary>True once kinds are assigned and the registry is validated. Registering after this fails.</summary>
        public static bool Frozen { get; private set; }

        /// <summary>True if two structure ids mapped to the same kind. The library registers nothing in that case.</summary>
        public static bool Disabled { get; internal set; }

        /// <summary>True once frozen with at least one modded machine (a Processor that does not ProducesPower).</summary>
        internal static bool HasMachines { get; private set; }

        public static IReadOnlyCollection<StructureDefinition> All => byId.Values;
        public static IReadOnlyCollection<CargoDefinition> AllCargo => cargoById.Values;

        /// <summary>
        /// Fired on the main thread when a modded structure's view is spawned: right after it is built, and again for each
        /// saved one restored when a world loads. The view is already decorated.
        /// </summary>
        public static event Action<StructureBuilt> Built;
        /// <summary>Fired on the main thread after a modded structure is removed (its depot id).</summary>
        public static event Action<int> Removed;
        /// <summary>Fired on the main thread when a modded processor finishes a cycle (its batch is in the output slot).</summary>
        public static event Action<CycleFinished> CycleFinished;
        /// <summary>
        /// Fired on the main thread when a canister at the end of a belt joined to a modded machine's input port is taken
        /// into one of its Input slots (the canister is removed, or reduced when only part of it fit).
        /// </summary>
        public static event Action<CargoEntered> CargoEntered;
        /// <summary>
        /// Fired on the main thread when a modded machine puts a canister of its output on the belt joined to its output
        /// port. The game emits by itself (EmitProduct, every 0.35 s while the output slot holds cargo and the belt's
        /// entrance is clear): one 1 kg canister per emit.
        /// </summary>
        public static event Action<CargoEmitted> CargoEmitted;
        /// <summary>
        /// Fired on the main thread when a router (<see cref="StructureDefinition.Router"/>) moves a canister from the end
        /// of a belt at one of its input ports onto the start of a belt at one of its output ports. The canister on the
        /// output belt is a new one with the same cargo, kilograms, capacity and owner; <c>CargoEntered</c> and
        /// <c>CargoEmitted</c> are not raised for routers.
        /// </summary>
        public static event Action<CargoRouted> CargoRouted;

        /// <summary>Register a structure. Call from your plugin's Load().</summary>
        [MethodImpl(MethodImplOptions.NoInlining)] // GetCallingAssembly must see the mod, not an inlined caller
        public static StructureDefinition Register(StructureDefinition def, string ownerGuid = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (string.IsNullOrWhiteSpace(def.Id)) throw new ArgumentException("StructureDefinition.Id is required");
            if (Frozen)
            {
                CoreLog.Error($"Register('{def.Id}') called after the registry froze; register from your plugin's Load(). Ignored.");
                return def;
            }
            if (byId.TryGetValue(def.Id, out var first))
            {
                CoreLog.Error($"Register: duplicate id '{def.Id}' (first from {first.Owner}, now {ownerGuid}). Ignored.");
                return first;
            }
            if (def.Processor != null)
            {
                var slots = (def.Processor.Slots ?? new List<Slot>()).Select(s => (s.Role, s.Capacity)).ToList();
                var errors = Rules.CheckProcessor(def.Id, slots, def.Processor.ProducesPower, MoonProductionSave.MaxSlots);
                if (errors.Count > 0)
                {
                    foreach (var e in errors) CoreLog.Error(e);
                    CoreLog.Error($"Register('{def.Id}'): processor dropped.");
                    def.Processor = null;
                }
            }
            // A "structurelib:" key would send CardArt back into our own prefix (endless recursion); treat it as unset.
            if (def.CardImageKey != null && def.CardImageKey.StartsWith("structurelib:", StringComparison.Ordinal))
            {
                CoreLog.Error($"Register('{def.Id}'): CardImageKey '{def.CardImageKey}' uses the reserved 'structurelib:' prefix; ignored.");
                def.CardImageKey = null;
            }
            if (!string.IsNullOrWhiteSpace(def.Model)) def.ModelPath = ResolveModel(def, System.Reflection.Assembly.GetCallingAssembly());
            def.Owner = ownerGuid ?? "unknown";
            def.OwnerVersion = PluginVersion(ownerGuid);
            def.Name ??= def.Id;
            def.Category ??= "";
            def.Note ??= "";
            byId[def.Id] = def;
            CoreLog.Info($"Registered structure '{def.Id}' ('{def.Name}', base {def.BaseStructure}, ${def.Price}) from {def.Owner} {def.OwnerVersion}" +
                         (def.Processor != null ? $", processor with {def.Processor.Slots.Count} slots" : "") +
                         (def.Power != null ? ", power source" : "") +
                         (def.ModelPath != null ? $", model {def.ModelPath}" : ""));
            return def;
        }

        // Model is a file name next to the registering mod's DLL. The file is read (and a missing one reported) when the
        // registry freezes and when the model is built.
        static string ResolveModel(StructureDefinition def, System.Reflection.Assembly owner)
        {
            string dir = null;
            try { dir = Path.GetDirectoryName(owner?.Location); } catch (Exception e) { CoreLog.Warn($"Register('{def.Id}'): the registering assembly has no location ({e.Message})"); }
            if (string.IsNullOrEmpty(dir)) dir = BepInEx.Paths.PluginPath;
            string path = Path.Combine(dir, def.Model);
#if DEVBUILD
            if (DevTools.BreakModelPath != null && DevTools.BreakModelPath.Value)
            {
                path = Path.Combine(dir, "missing-" + Path.GetFileName(def.Model));
                CoreLog.Info($"[dev] BreakModelPath: '{def.Id}' model points at {path}");
            }
#endif
            return path;
        }

        /// <summary>
        /// Register a cargo kind. Returns its handle for use in slots, or <see cref="Cargo.None"/> if it was rejected.
        /// Value clashes between different cargo are only detected when the registry freezes.
        /// </summary>
        public static Cargo RegisterCargo(CargoDefinition def, string ownerGuid = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (string.IsNullOrWhiteSpace(def.Id)) throw new ArgumentException("CargoDefinition.Id is required");
            if (Frozen)
            {
                CoreLog.Error($"RegisterCargo('{def.Id}') called after the registry froze; register from your plugin's Load(). Ignored.");
                return Cargo.None;
            }
            if (cargoById.TryGetValue(def.Id, out var first))
            {
                CoreLog.Error($"RegisterCargo: duplicate id '{def.Id}' (first from {cargoOwner[def.Id].Guid}, now {ownerGuid}). Ignored.");
                return new Cargo(first.Value, first.Id);
            }
            if (def.Value < Rules.FirstModdedCargoValue)
            {
                CoreLog.Error($"RegisterCargo: cargo '{def.Id}' uses value {def.Value}; modded cargo values must be in the range 3..255. Ignored.");
                return Cargo.None;
            }
            def.Name ??= def.Id;
            def.ShortName ??= def.Name;
            cargoById[def.Id] = def;
            cargoOwner[def.Id] = (ownerGuid ?? "unknown", PluginVersion(ownerGuid));
            CoreLog.Info($"Registered cargo '{def.Id}' ('{def.Name}', value {def.Value}) from {cargoOwner[def.Id].Guid} {cargoOwner[def.Id].Version}");
            return new Cargo(def.Value, def.Id);
        }

        /// <summary>Plugin GUID and version that registered a cargo id; null if the id is not registered.</summary>
        internal static (string Guid, string Version)? CargoOwner(string id) => id != null && cargoOwner.TryGetValue(id, out var o) ? o : null;

        // The registering plugin's version from its BepInEx metadata. Plugins are listed before their Load() runs.
        static string PluginVersion(string guid)
        {
            try
            {
                var plugins = BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance?.Plugins;
                if (guid != null && plugins != null && plugins.TryGetValue(guid, out var info)) return info.Metadata.Version.ToString();
            }
            catch (Exception e) { CoreLog.Warn($"Reading the version of plugin {guid} failed: {e.Message}"); }
            return "unknown";
        }

        public static bool IsModded(int kind) => byIndex.ContainsKey(kind);
        public static StructureDefinition Definition(int kind) => byIndex.TryGetValue(kind, out var d) ? d : null;
        public static StructureDefinition Definition(string id) => id != null && byId.TryGetValue(id, out var d) ? d : null;

        /// <summary>True only for cargo values a mod registered (never for the game's own 0..2).</summary>
        public static bool IsModded(MoonCargo cargo) => byCargoValue.ContainsKey((byte)cargo);
        public static CargoDefinition CargoDefinition(MoonCargo cargo) => byCargoValue.TryGetValue((byte)cargo, out var d) ? d : null;
        /// <summary>A registered cargo by id (null if unknown, or dropped when the registry froze).</summary>
        public static CargoDefinition CargoDefinition(string id) => id != null && cargoById.TryGetValue(id, out var d) ? d : null;

        /// <summary>
        /// The ports a structure's cloned definition carries, resolved when the registry froze (model Port_* nodes, else
        /// <see cref="StructureDefinition.Ports"/>, else the base structure's ports). Null before the freeze, for a
        /// dropped structure, and when the base structure's ports could not be read and none were given (the clone then
        /// keeps the base's ports unchanged).
        /// </summary>
        public static IReadOnlyList<PortDefinition> PortsOf(StructureDefinition def) => def?.ResolvedPorts;

        // Ports from the structure's model file (Port_* nodes); a null or empty answer (no Model, no readable file, no
        // Port_* node) falls through to the declared ports.
        internal static Func<StructureDefinition, DonorShape, List<PortDefinition>> ModelPortsProvider = ModelSwap.Ports;

        // The belt-side allowance for a port outside the footprint (MoonWorld.PortReach, 1.20 m in this build).
        static float PortReach
        {
            get { try { return MoonWorld.PortReach; } catch (Exception) { return 1.2f; } }
        }

        // Assigns kinds and validates everything. donorLookup reads the vanilla build definition for a base kind.
        // Validation works on copies; the live tables change only once everything is decided, and Frozen is set last.
        // If anything throws, the library disables itself for the session (Frozen stays false) instead of half-working.
        internal static void Freeze(Func<int, DonorTraits> donorLookup, Func<int, DonorShape> shapeLookup = null)
        {
            if (Frozen || Disabled) return;
            try
            {
                IReadOnlyDictionary<string, int> kinds;
                try
                {
                    kinds = KindAllocator.Assign(byId.Keys);
                }
                catch (KindCollisionException e)
                {
                    CoreLog.Error($"{e.Message} StructureLib is disabled: no modded structures or cargo will be added.");
                    Disabled = true;
                    byId.Clear();
                    cargoById.Clear();
                    Frozen = true;
                    return;
                }

                var structures = new Dictionary<string, StructureDefinition>(byId, StringComparer.Ordinal);
                var cargo = new Dictionary<string, CargoDefinition>(cargoById, StringComparer.Ordinal);

                int dropped = 0;
                foreach (var def in structures.Values.OrderBy(d => d.Id, StringComparer.Ordinal).ToList())
                {
                    var error = Rules.CheckDonor(def.Id, def.BaseStructure, donorLookup(def.BaseStructure));
                    if (error != null)
                    {
                        CoreLog.Error(error + " Structure dropped.");
                        structures.Remove(def.Id);
                        dropped++;
                    }
                }

                // Routers have no processor and no power; a Footprint must be positive.
                foreach (var def in structures.Values.OrderBy(d => d.Id, StringComparer.Ordinal).ToList())
                {
                    var fp = def.Footprint.HasValue ? (def.Footprint.Value.x, def.Footprint.Value.y) : ((float, float)?)null;
                    var defErrors = RouterRules.CheckDefinition(def.Id, def.Router != null, def.Processor != null, def.Power != null, fp);
                    if (defErrors.Count == 0) continue;
                    foreach (var e in defErrors) CoreLog.Error(e);
                    CoreLog.Error($"Structure '{def.Id}' dropped.");
                    structures.Remove(def.Id);
                    dropped++;
                }

                var cargoPairs = cargo.Values.Select(c => (c.Id, c.Value)).ToList();
                foreach (var e in Rules.CheckCargo(cargoPairs)) CoreLog.Error(e);
                var offenders = Rules.CargoOffenders(cargoPairs);
                var cargoByValue = new Dictionary<byte, CargoDefinition>();
                foreach (var c in cargo.Values.ToList())
                {
                    if (offenders.Contains(c.Id))
                    {
                        CoreLog.Error($"Cargo '{c.Id}' dropped.");
                        cargo.Remove(c.Id);
                    }
                    else cargoByValue[c.Value] = c;
                }

                // Slots must point at cargo that survived (or is vanilla); otherwise the structure would run on the wrong cargo.
                foreach (var def in structures.Values.OrderBy(d => d.Id, StringComparer.Ordinal).ToList())
                {
                    if (def.Processor == null) continue;
                    var slotErrors = Rules.CheckSlotCargo(def.Id,
                        def.Processor.Slots.Select(s => (s.Name, s.Cargo.Id, s.Cargo.IsValid)),
                        cargo.ContainsKey);
                    if (slotErrors.Count == 0) continue;
                    foreach (var e in slotErrors) CoreLog.Error(e);
                    CoreLog.Error($"Structure '{def.Id}' dropped.");
                    structures.Remove(def.Id);
                    dropped++;
                }

                // Ports: resolve (model, declared, donor), then each port's cargo must be a slot's; outside the footprint
                // plus the belt reach is only a warning.
                var resolved = new Dictionary<string, (List<PortDefinition> Ports, string Rule)>(StringComparer.Ordinal);
                foreach (var def in structures.Values.OrderBy(d => d.Id, StringComparer.Ordinal).ToList())
                {
                    var shape = shapeLookup?.Invoke(def.BaseStructure);
                    var r = PortResolver.Resolve(def, shape, ModelPortsProvider);
                    var slots = (def.Processor?.Slots ?? new List<Slot>()).Select(s => (s.Role, s.Cargo.Id)).ToList();
                    // A router's ports (cargo already the dust byte, PortResolver) are checked by the router rules, whatever
                    // their source; unresolved ports count as none. A plain structure keeps its donor's ports and vanilla
                    // cargo; null ports leave the clone's untouched.
                    var portErrors = def.Router != null
                        ? RouterRules.CheckPorts(def.Id, def.Router.Mode, (r.Ports ?? new List<PortDefinition>()).Select(p => (p.Name, p.Output)).ToList())
                        : r.Ports == null || (def.Processor == null && def.Ports == null && r.Rule.StartsWith("donor", StringComparison.Ordinal))
                        ? new List<string>()
                        : PortRules.CheckPorts(def.Id, r.Ports.Select(p => (p.Name, p.Output, p.Cargo.Id)).ToList(), slots);
                    if (portErrors.Count > 0)
                    {
                        foreach (var e in portErrors) CoreLog.Error(e);
                        CoreLog.Error($"Structure '{def.Id}' dropped.");
                        structures.Remove(def.Id);
                        dropped++;
                        continue;
                    }
                    // The footprint the clone will have: the definition's override when set, else the donor's.
                    Vector2? half = def.Footprint.HasValue ? def.Footprint.Value / 2f : shape?.HalfFootprint;
                    if (half.HasValue && r.Ports != null)
                        foreach (var p in r.Ports)
                        {
                            string warn = PortRules.WarnOutsideFootprint(def.Id, p.Name, (p.Position.x, p.Position.y, p.Position.z), (half.Value.x, half.Value.y), PortReach);
                            if (warn != null) CoreLog.Warn(warn);
                        }
                    resolved[def.Id] = r;
                }

                // Commit.
                byId.Clear();
                foreach (var kv in structures) byId[kv.Key] = kv.Value;
                cargoById.Clear();
                foreach (var kv in cargo) cargoById[kv.Key] = kv.Value;
                foreach (var kv in cargoByValue) byCargoValue[kv.Key] = kv.Value;
                foreach (var def in byId.Values)
                {
                    def.Kind = kinds[def.Id];
                    byIndex[def.Kind] = def;
                    if (resolved.TryGetValue(def.Id, out var r)) { def.ResolvedPorts = r.Ports; def.PortRule = r.Rule; }
                }
                HasMachines = byIndex.Values.Any(d => d.Processor != null && !d.Processor.ProducesPower);

                CoreLog.Info($"Registry frozen: structures={byIndex.Count}" + (dropped > 0 ? $" ({dropped} dropped)" : "") +
                             $", cargo={byCargoValue.Count}" +
                             $" [{string.Join(", ", byIndex.Values.Select(d => d.ToString()))}]");
                Frozen = true;
            }
            catch (Exception e)
            {
                Disabled = true;
                byIndex.Clear();
                byCargoValue.Clear();
                foreach (var def in byId.Values) def.Kind = -1;
                CoreLog.Error($"Freezing the structure registry failed; StructureLib is disabled for this session (no modded structures or cargo): {e}");
            }
        }

        internal static void RaiseBuilt(StructureBuilt b)
        {
            try { Built?.Invoke(b); } catch (Exception e) { CoreLog.Error($"Built handler threw for {b.Definition} depot {b.DepotId}: {e}"); }
        }

        internal static void RaiseRemoved(int depotId)
        {
            try { Removed?.Invoke(depotId); } catch (Exception e) { CoreLog.Error($"Removed handler threw for depot {depotId}: {e}"); }
        }

        internal static void RaiseCargoEntered(CargoEntered c)
        {
            try { CargoEntered?.Invoke(c); } catch (Exception e) { CoreLog.Error($"CargoEntered handler threw for {c.Definition} depot {c.DepotId}: {e}"); }
        }

        internal static void RaiseCargoRouted(CargoRouted c)
        {
            try { CargoRouted?.Invoke(c); } catch (Exception e) { CoreLog.Error($"CargoRouted handler threw for {c.Definition} depot {c.DepotId}: {e}"); }
        }

        internal static void RaiseCargoEmitted(CargoEmitted c)
        {
            try { CargoEmitted?.Invoke(c); } catch (Exception e) { CoreLog.Error($"CargoEmitted handler threw for {c.Definition} depot {c.DepotId}: {e}"); }
        }

        internal static void RaiseCycleFinished(CycleFinished c)
        {
            try { CycleFinished?.Invoke(c); } catch (Exception e) { CoreLog.Error($"CycleFinished handler threw for {c.Definition}: {e}"); }
        }
    }
}
