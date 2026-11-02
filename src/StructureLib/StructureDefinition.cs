using System;
using System.Collections.Generic;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    /// <summary>
    /// Everything the core needs to sell, build and run a modded structure. A modded structure borrows a vanilla
    /// <see cref="BaseStructure"/> kind for anything it doesn't override (footprint, build rules, default model).
    /// </summary>
    public sealed class StructureDefinition
    {
        /// <summary>Stable string id, mapped to a kind number. Never change it after release. Convention: "author.structure".</summary>
        public string Id;
        public string Name;
        public string Category;
        /// <summary>Short description shown in the build menu.</summary>
        public string Note;
        public decimal Price = 1000m;

        /// <summary>Vanilla structure kind to borrow from. Foundation-like kinds (decks, building parts, catwalk supports, world grid) are refused.</summary>
        public int BaseStructure;

        /// <summary>Optional PNG bytes for the build card (square, transparent background).</summary>
        public byte[] CardImagePng;
        /// <summary>Optional key of a card image the mod supplies some other way.</summary>
        public string CardImageKey;

        /// <summary>
        /// Optional full model builder. Return the structure's root GameObject parented under <c>ctx.Parent</c>.
        /// Called once per world; the result is the template the game copies for every one built (it replaces the base
        /// structure's prefab). Null builds the base structure's model. <see cref="DecorateModel"/> runs on each copy either way.
        /// </summary>
        public Func<ModelContext, GameObject> BuildModel;
        /// <summary>Optional tweak applied after the model is built (tint, extra parts).</summary>
        public Action<GameObject, ModelContext> DecorateModel;

        /// <summary>Makes the structure a machine that turns inputs into an output (or into power). Null for a plain structure.</summary>
        public ProcessorDefinition Processor;
        /// <summary>Makes the structure a power source. Null if it supplies none.</summary>
        public PowerDefinition Power;

        /// <summary>Kind number assigned at freeze. -1 until then.</summary>
        public int Kind { get; internal set; } = -1;
        /// <summary>Plugin GUID that registered the structure.</summary>
        public string Owner { get; internal set; }

        public override string ToString() => $"{Id} (kind {Kind})";
    }

    /// <summary>A machine that runs cycles: it consumes its Input and Batch slots and fills its Output slot (or produces power).</summary>
    public sealed class ProcessorDefinition
    {
        /// <summary>Power draw while running.</summary>
        public float RunningKw;
        /// <summary>Seconds one cycle takes.</summary>
        public float CycleSeconds;
        /// <summary>At most 8 slots: at least one Input, at least one Batch, and one Output unless <see cref="ProducesPower"/>.</summary>
        public List<Slot> Slots = new();
        /// <summary>True for a generator: it has no Output slot and feeds the power grid instead.</summary>
        public bool ProducesPower;
    }

    public struct Slot
    {
        public SlotRole Role;
        public string Name;
        public Cargo Cargo;
        public double Capacity;

        public static Slot Input(string name, Cargo cargo, double capacity) => new Slot { Role = SlotRole.Input, Name = name, Cargo = cargo, Capacity = capacity };
        public static Slot Batch(string name, Cargo cargo, double capacity) => new Slot { Role = SlotRole.Batch, Name = name, Cargo = cargo, Capacity = capacity };
        public static Slot Output(string name, Cargo cargo, double capacity) => new Slot { Role = SlotRole.Output, Name = name, Cargo = cargo, Capacity = capacity };
    }

    public enum SupplyMode { Solar, Fuel }

    /// <summary>Makes a structure a power source.</summary>
    public sealed class PowerDefinition
    {
        public float PeakKw;
        public float BatteryKwh;
        public float Range;
        public SupplyMode SupplyMode = SupplyMode.Fuel;
    }

    /// <summary>A new kind of cargo. Values 0..2 belong to the game; mods use 3..255.</summary>
    public sealed class CargoDefinition
    {
        /// <summary>Stable string id. Convention: "author.cargo".</summary>
        public string Id;
        /// <summary>Byte value stored in saves (3..255), unique across all loaded mods.</summary>
        public byte Value;
        public string Name;
        public string ShortName;
        public double UnitKilograms;
        public decimal Price;
        /// <summary>Tint of the cargo canister. Null keeps the default.</summary>
        public Color? CanisterTint;
    }

    /// <summary>Handle to a cargo kind, vanilla or modded, used in <see cref="Slot"/>s.</summary>
    public readonly struct Cargo
    {
        public byte Value { get; }
        public string Id { get; }

        public Cargo(byte value, string id)
        {
            Value = value;
            Id = id;
        }

        public static Cargo Dust => new Cargo(0, "dust");
        public static Cargo Sand => new Cargo(1, "sand");
        public static Cargo Stone => new Cargo(2, "stone");

        /// <summary>
        /// The "no cargo" handle <see cref="MoonStructures.RegisterCargo"/> returns when it rejects a registration.
        /// It is never Dust and must not be used: a slot holding it makes the whole structure get dropped at freeze.
        /// </summary>
        public static readonly Cargo None = new Cargo(0, null);

        /// <summary>False for <see cref="None"/> and for an unset (default) handle.</summary>
        public bool IsValid => Id != null;

        public MoonCargo ToMoon() => (MoonCargo)Value;

        public override string ToString() => Id ?? "none";
    }

    /// <summary>Passed to <see cref="StructureDefinition.BuildModel"/> and <see cref="StructureDefinition.DecorateModel"/>.</summary>
    public sealed class ModelContext
    {
        public StructureDefinition Definition;
        public Transform Parent;
        /// <summary>The vanilla build definition of <see cref="StructureDefinition.BaseStructure"/>.</summary>
        public MoonDepotBuildDefinition Donor;
    }

    /// <summary>Payload of <see cref="MoonStructures.Built"/>.</summary>
    public sealed class StructureBuilt
    {
        public StructureDefinition Definition;
        /// <summary>The game's depot id (key of <c>MoonWorld.depots</c>).</summary>
        public int DepotId;
        /// <summary>The depot record: kind, owner, position, yaw.</summary>
        public MoonDepotSnapshot Record;
        /// <summary>The live view root, already decorated and named <c>"ModStructure_" + Definition.Id</c>.</summary>
        public GameObject View;
    }

    /// <summary>Payload of <see cref="MoonStructures.CycleFinished"/>.</summary>
    public sealed class CycleFinished
    {
        public StructureDefinition Definition;
        /// <summary>The game's depot id of the machine (key of <c>MoonWorld.production</c>).</summary>
        public int DepotId;
        /// <summary>The machine's live production state, after the cycle moved its batch into the output slot.</summary>
        public MoonProductionState State;
    }
}
