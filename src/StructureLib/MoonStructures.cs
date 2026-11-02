using System;
using System.Collections.Generic;
using System.Linq;
using MoonCleaners;

namespace StructureLib
{
    /// <summary>Public entry point for structure mods.</summary>
    public static class MoonStructures
    {
        static readonly Dictionary<string, StructureDefinition> byId = new(StringComparer.Ordinal);
        static readonly Dictionary<int, StructureDefinition> byIndex = new();
        static readonly Dictionary<string, CargoDefinition> cargoById = new(StringComparer.Ordinal);
        static readonly Dictionary<string, string> cargoOwner = new(StringComparer.Ordinal);
        static readonly Dictionary<byte, CargoDefinition> byCargoValue = new();

        /// <summary>True once kinds are assigned and the registry is validated. Registering after this fails.</summary>
        public static bool Frozen { get; private set; }

        /// <summary>True if two structure ids mapped to the same kind. The library registers nothing in that case.</summary>
        public static bool Disabled { get; internal set; }

        public static IReadOnlyCollection<StructureDefinition> All => byId.Values;
        public static IReadOnlyCollection<CargoDefinition> AllCargo => cargoById.Values;

        /// <summary>
        /// Fired on the main thread when a modded structure's view is spawned: right after it is built, and again for each
        /// saved one restored when a world loads. The view is already decorated.
        /// </summary>
        public static event Action<StructureBuilt> Built;
        /// <summary>Fired on the main thread after a modded structure is removed (its depot id).</summary>
        public static event Action<int> Removed;
        /// <summary>Fired when a modded processor finishes a cycle.</summary>
        public static event Action<CycleFinished> CycleFinished;

        /// <summary>Register a structure. Call from your plugin's Load().</summary>
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
            def.Owner = ownerGuid ?? "unknown";
            def.Name ??= def.Id;
            def.Category ??= "";
            def.Note ??= "";
            byId[def.Id] = def;
            CoreLog.Info($"Registered structure '{def.Id}' ('{def.Name}', base {def.BaseStructure}, ${def.Price}) from {def.Owner}" +
                         (def.Processor != null ? $", processor with {def.Processor.Slots.Count} slots" : "") +
                         (def.Power != null ? ", power source" : ""));
            return def;
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
                CoreLog.Error($"RegisterCargo: duplicate id '{def.Id}' (first from {cargoOwner[def.Id]}, now {ownerGuid}). Ignored.");
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
            cargoOwner[def.Id] = ownerGuid ?? "unknown";
            CoreLog.Info($"Registered cargo '{def.Id}' ('{def.Name}', value {def.Value}) from {cargoOwner[def.Id]}");
            return new Cargo(def.Value, def.Id);
        }

        public static bool IsModded(int kind) => byIndex.ContainsKey(kind);
        public static StructureDefinition Definition(int kind) => byIndex.TryGetValue(kind, out var d) ? d : null;
        public static StructureDefinition Definition(string id) => id != null && byId.TryGetValue(id, out var d) ? d : null;

        /// <summary>True only for cargo values a mod registered (never for the game's own 0..2).</summary>
        public static bool IsModded(MoonCargo cargo) => byCargoValue.ContainsKey((byte)cargo);
        public static CargoDefinition CargoDefinition(MoonCargo cargo) => byCargoValue.TryGetValue((byte)cargo, out var d) ? d : null;
        /// <summary>A registered cargo by id (null if unknown, or dropped when the registry froze).</summary>
        public static CargoDefinition CargoDefinition(string id) => id != null && cargoById.TryGetValue(id, out var d) ? d : null;

        // Assigns kinds and validates everything. donorLookup reads the vanilla build definition for a base kind.
        // Validation works on copies; the live tables change only once everything is decided, and Frozen is set last.
        // If anything throws, the library disables itself for the session (Frozen stays false) instead of half-working.
        internal static void Freeze(Func<int, DonorTraits> donorLookup)
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
                }

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

        internal static void RaiseCycleFinished(CycleFinished c)
        {
            try { CycleFinished?.Invoke(c); } catch (Exception e) { CoreLog.Error($"CycleFinished handler threw for {c.Definition}: {e}"); }
        }
    }
}
