using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.Injection;
using MoonCleaners;

namespace GarageLib
{
    /// <summary>Public entry point for vehicle mods.</summary>
    public static class MoonVehicles
    {
        /// <summary>First index the core hands out. Vanilla uses 0..21.</summary>
        public const int FirstModdedIndex = 22;
        /// <summary>The garage mask is 32 bits; indices past this can't be flagged in it.</summary>
        public const int MaskLimit = 32;

        static readonly Dictionary<string, VehicleDefinition> byId = new();
        static readonly Dictionary<int, VehicleDefinition> byIndex = new();

        /// <summary>True once indices are assigned and the catalog has been extended. Registering after this fails.</summary>
        public static bool Frozen { get; private set; }

        public static IReadOnlyCollection<VehicleDefinition> All => byId.Values;

        /// <summary>Fired on the main thread after a modded vehicle is spawned and initialized.</summary>
        public static event Action<VehicleSpawn> Spawned;
        /// <summary>Fired when a modded vehicle is bought (definition, garage id, owner id).</summary>
        public static event Action<VehicleDefinition, int, string> Bought;

        /// <summary>
        /// Resolver for custom (non-vanilla) upgrade levels. Replace it to change how custom upgrades are stored or
        /// shared; vanilla kinds always go through the game's own MoonWorld methods so other mods can patch those.
        /// </summary>
        public static IUpgradeResolver Upgrades { get; set; } = new SidecarUpgradeResolver();

        /// <summary>Register a vehicle. Call from your plugin's Load().</summary>
        public static VehicleDefinition Register(VehicleDefinition def, string ownerGuid = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (string.IsNullOrWhiteSpace(def.Id)) throw new ArgumentException("VehicleDefinition.Id is required");
            if (Frozen)
            {
                CoreLog.Error($"Register('{def.Id}') called after the registry froze; register from your plugin's Load(). Ignored.");
                return def;
            }
            if (byId.ContainsKey(def.Id))
            {
                CoreLog.Error($"Register: duplicate id '{def.Id}' (first from {byId[def.Id].Owner}, now {ownerGuid}). Ignored.");
                return byId[def.Id];
            }
            if (def.Controller != null && !typeof(VehicleController).IsAssignableFrom(def.Controller))
            {
                CoreLog.Error($"Register('{def.Id}'): Controller {def.Controller.FullName} must derive from VehicleController. Controller dropped.");
                def.Controller = null;
            }
            def.Owner = ownerGuid ?? "unknown";
            def.Name ??= def.Id;
            def.Summary ??= "";
            byId[def.Id] = def;
            CoreLog.Info($"Registered vehicle '{def.Id}' ('{def.Name}', base {def.BaseVehicle}, ${def.Price}) from {def.Owner}" +
                         (def.Controller != null ? $", controller {def.Controller.Name}" : "") +
                         (def.CustomUpgrades.Count > 0 ? $", custom upgrades [{string.Join(",", def.CustomUpgrades.Select(u => u.Id))}]" : ""));
            return def;
        }

        public static bool IsModded(int index) => byIndex.ContainsKey(index);
        public static VehicleDefinition Definition(int index) => byIndex.TryGetValue(index, out var d) ? d : null;
        public static VehicleDefinition Definition(string id) => id != null && byId.TryGetValue(id, out var d) ? d : null;

        /// <summary>Vanilla level of a vanilla upgrade kind, asked from the game so upgrade-changing mods are respected.</summary>
        public static int VanillaLevel(string storage, MoonVehicleUpgrades.Kind kind)
        {
            var w = CoreState.World;
            return w == null ? 0 : w.VehicleUpgradeLevel(storage, kind);
        }

        /// <summary>Level of a custom upgrade (goes through <see cref="Upgrades"/>).</summary>
        public static int CustomLevel(string storage, string upgradeId) => Upgrades?.Level(storage, upgradeId) ?? 0;

        // Assign indices: sorted by id so the same mod set always gets the same indices. Saves never contain modded
        // indices (the sidecar stores string ids), so a shift between sessions is harmless for single player.
        internal static void Freeze(int vanillaCount)
        {
            if (Frozen) return;
            Frozen = true;
            int next = Math.Max(FirstModdedIndex, vanillaCount);
            foreach (var def in byId.Values.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                def.Index = next++;
                byIndex[def.Index] = def;
                if (def.Index >= MaskLimit)
                    CoreLog.Warn($"'{def.Id}' got index {def.Index} (>= {MaskLimit}); the garage mask can't flag it. Expect it to be refused by garages until mask checks are patched.");
                if (def.Controller != null && !ClassInjector.IsTypeRegisteredInIl2Cpp(def.Controller))
                {
                    try { ClassInjector.RegisterTypeInIl2Cpp(def.Controller); CoreLog.Info($"Registered controller type {def.Controller.FullName} in IL2CPP"); }
                    catch (Exception e) { CoreLog.Error($"Registering controller {def.Controller.FullName} failed, '{def.Id}' will use vanilla driving: {e}"); def.Controller = null; }
                }
            }
            CoreLog.Info($"Registry frozen: vanilla vehicles={vanillaCount}, modded={byIndex.Count} " +
                         $"[{string.Join(", ", byIndex.Values.Select(d => d.ToString()))}]");
        }

        internal static void RaiseSpawned(VehicleSpawn s)
        {
            try { Spawned?.Invoke(s); } catch (Exception e) { CoreLog.Error($"Spawned handler threw for {s.Definition}: {e}"); }
        }

        internal static void RaiseBought(VehicleDefinition d, int garage, string owner)
        {
            try { Bought?.Invoke(d, garage, owner); } catch (Exception e) { CoreLog.Error($"Bought handler threw for {d}: {e}"); }
        }
    }

    public sealed class VehicleSpawn
    {
        public VehicleDefinition Definition;
        public MoonPrototypeVehicle Vehicle;
        public string Storage;
        /// <summary>The controller instance, if the definition has one.</summary>
        public VehicleController Controller;
    }
}
