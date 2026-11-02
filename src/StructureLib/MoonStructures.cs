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

        /// <summary>Fired on the main thread after a modded structure is built.</summary>
        public static event Action<StructureBuilt> Built;
        /// <summary>Fired when a modded structure is removed (its kind).</summary>
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
                var errors = Rules.CheckProcessor(def.Id, slots, def.Processor.ProducesPower);
                if (errors.Count > 0)
                {
                    foreach (var e in errors) CoreLog.Error(e);
                    CoreLog.Error($"Register('{def.Id}'): processor dropped.");
                    def.Processor = null;
                }
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
        /// Register a cargo kind. Returns its handle for use in slots, or <c>default</c> if it was rejected.
        /// Value clashes between different cargo are only detected when the registry freezes.
        /// </summary>
        public static Cargo RegisterCargo(CargoDefinition def, string ownerGuid = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (string.IsNullOrWhiteSpace(def.Id)) throw new ArgumentException("CargoDefinition.Id is required");
            if (Frozen)
            {
                CoreLog.Error($"RegisterCargo('{def.Id}') called after the registry froze; register from your plugin's Load(). Ignored.");
                return default;
            }
            if (cargoById.TryGetValue(def.Id, out var first))
            {
                CoreLog.Error($"RegisterCargo: duplicate id '{def.Id}' (first from {cargoOwner[def.Id]}, now {ownerGuid}). Ignored.");
                return new Cargo(first.Value, first.Id);
            }
            if (def.Value < Rules.FirstModdedCargoValue)
            {
                CoreLog.Error($"RegisterCargo: cargo '{def.Id}' uses value {def.Value}; modded cargo values must be in the range 3..255. Ignored.");
                return default;
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

        // Assigns kinds and validates everything. donorLookup reads the vanilla build definition for a base kind.
        internal static void Freeze(Func<int, DonorTraits> donorLookup)
        {
            if (Frozen) return;
            Frozen = true;

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
                return;
            }

            int dropped = 0;
            foreach (var def in byId.Values.OrderBy(d => d.Id, StringComparer.Ordinal).ToList())
            {
                var error = Rules.CheckDonor(def.Id, def.BaseStructure, donorLookup(def.BaseStructure));
                if (error != null)
                {
                    CoreLog.Error(error + " Structure dropped.");
                    byId.Remove(def.Id);
                    dropped++;
                    continue;
                }
                def.Kind = kinds[def.Id];
                byIndex[def.Kind] = def;
            }

            var cargoErrors = Rules.CheckCargo(cargoById.Values.Select(c => (c.Id, c.Value)));
            foreach (var e in cargoErrors) CoreLog.Error(e);
            // The rules put every offending id in single quotes, so a cargo is dropped if any error names it.
            foreach (var c in cargoById.Values.ToList())
            {
                string quoted = "'" + c.Id + "'";
                if (cargoErrors.Any(e => e.Contains(quoted)))
                {
                    CoreLog.Error("Cargo " + quoted + " dropped.");
                    cargoById.Remove(c.Id);
                }
                else byCargoValue[c.Value] = c;
            }

            CoreLog.Info($"Registry frozen: structures={byIndex.Count}" + (dropped > 0 ? $" ({dropped} dropped)" : "") +
                         $", cargo={byCargoValue.Count}" +
                         $" [{string.Join(", ", byIndex.Values.Select(d => d.ToString()))}]");
        }

        internal static void RaiseBuilt(StructureBuilt b)
        {
            try { Built?.Invoke(b); } catch (Exception e) { CoreLog.Error($"Built handler threw for {b.Definition}: {e}"); }
        }

        internal static void RaiseRemoved(int kind)
        {
            try { Removed?.Invoke(kind); } catch (Exception e) { CoreLog.Error($"Removed handler threw for kind {kind}: {e}"); }
        }

        internal static void RaiseCycleFinished(CycleFinished c)
        {
            try { CycleFinished?.Invoke(c); } catch (Exception e) { CoreLog.Error($"CycleFinished handler threw for {c.Definition}: {e}"); }
        }
    }
}
