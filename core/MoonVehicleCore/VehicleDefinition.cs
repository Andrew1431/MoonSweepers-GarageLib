using System;
using System.Collections.Generic;
using MoonCleaners;
using UnityEngine;

namespace MoonVehicleCore
{
    /// <summary>
    /// Everything the core needs to sell, spawn and save a modded vehicle. A modded vehicle borrows a vanilla
    /// <see cref="BaseVehicle"/> for anything it doesn't override (model, seats, card picture, upgrade rules).
    /// </summary>
    public sealed class VehicleDefinition
    {
        /// <summary>Stable string id, saved in the sidecar. Never change it after release. Convention: "author.vehicle".</summary>
        public string Id;
        public string Name;
        /// <summary>Garage card summary line.</summary>
        public string Summary;
        /// <summary>Control hint shown by the game for the vehicle.</summary>
        public string Hint = "";
        public decimal Price = 1000m;

        /// <summary>Vanilla vehicle index to borrow from: 0 push sweeper, 1 ride-on, 4 crew van, 20 lunar sweeper, 21 mega vacuum rover.</summary>
        public int BaseVehicle = 1;

        // Catalog stats. NaN / -1 means "copy from BaseVehicle".
        public MoonPrototypeCatalog.Work? Work;
        public float Width = float.NaN, Depth = float.NaN, Reach = float.NaN, Speed = float.NaN, Turn = float.NaN;
        /// <summary>Cargo capsules (catalog "capacity").</summary>
        public int Capacity = -1;
        public double StorageKg = double.NaN;
        public float Heft = float.NaN;

        /// <summary>Optional seat layout. Null borrows the base vehicle's seats.</summary>
        public SeatSpec[] Seats;

        /// <summary>
        /// Optional full model builder. Return the vehicle's root GameObject parented under <c>ctx.Parent</c>.
        /// Null spawns the base vehicle's model and then calls <see cref="DecorateModel"/>.
        /// </summary>
        public Func<ModelContext, GameObject> BuildModel;
        /// <summary>Optional tweak applied after the model is built (tint, extra parts).</summary>
        public Action<GameObject, ModelContext> DecorateModel;

        /// <summary>Optional garage card picture. Null uses the base vehicle's picture.</summary>
        public Func<Sprite> CardIcon;

        /// <summary>
        /// Optional controller. Must derive from <see cref="VehicleController"/>; the core registers it with IL2CPP and
        /// adds it to every spawned instance. Its Suppress* properties decide which vanilla driving code still runs.
        /// </summary>
        public Type Controller;

        /// <summary>Vanilla upgrade kinds this vehicle accepts. Null follows the base vehicle's rules.</summary>
        public MoonVehicleUpgrades.Kind[] UpgradeKinds;
        /// <summary>Multiplier on vanilla upgrade prices. NaN follows the base vehicle.</summary>
        public double UpgradePriceScale = double.NaN;
        /// <summary>Upgrades that only exist for this vehicle. Levels live in the core sidecar, behind <see cref="MoonVehicles.Upgrades"/>.</summary>
        public List<CustomUpgrade> CustomUpgrades = new();

        /// <summary>Runtime index assigned by the core (-1 until the registry is frozen).</summary>
        public int Index { get; internal set; } = -1;
        /// <summary>BepInEx GUID of the registering plugin, for logs and the co-op registry.</summary>
        public string Owner { get; internal set; }

        public override string ToString() => $"{Id}#{Index}";
    }

    public struct SeatSpec
    {
        /// <summary>Foot position in vehicle-local space.</summary>
        public Vector3 Foot;
        /// <summary>Facing, degrees around local up.</summary>
        public float Turn;
        public SeatSpec(Vector3 foot, float turn) { Foot = foot; Turn = turn; }
    }

    public sealed class CustomUpgrade
    {
        /// <summary>Stable id, unique within the vehicle. Saved in the sidecar.</summary>
        public string Id;
        public string Title;
        public string Summary;
        /// <summary>Price to reach level i+1 from level i. Length is the max level.</summary>
        public decimal[] Prices = Array.Empty<decimal>();
        public int MaxLevel => Prices?.Length ?? 0;
        /// <summary>Optional effect description per level ("+20% lift").</summary>
        public Func<int, string> Effect;
    }

    public sealed class ModelContext
    {
        public VehicleDefinition Definition { get; internal set; }
        public Transform Parent { get; internal set; }
        /// <summary>The game's primitive builder; use its Box/Cylinder/Rod helpers and shell/dark/orange materials for vanilla style.</summary>
        public MoonPrototypeModels Models { get; internal set; }
    }
}
