using System;
using System.Collections.Generic;
using System.Linq;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    /// <summary>
    /// A belt port of a modded structure. Positions are structure-local in Unity axes (Y up, metres); the vanilla donors
    /// put theirs at deck height (y 0.85). <see cref="Direction"/> is the belt's flow direction at the port, for inputs
    /// and outputs alike (every donor's ports point local +X: inputs on the -X side, outputs on the +X side).
    /// </summary>
    public sealed class PortDefinition
    {
        /// <summary>Unique per structure, e.g. "Sand in". The game shows "input"/"output", never this name.</summary>
        public string Name;
        public Vector3 Position;
        public Vector3 Direction = Vector3.right;
        public bool Output;
        /// <summary>The cargo this port carries: an Input slot's cargo for an input, the Output slot's for an output.</summary>
        public Cargo Cargo;

        public static PortDefinition In(string name, Cargo cargo, Vector3 position, Vector3 direction) =>
            new PortDefinition { Name = name, Cargo = cargo, Position = position, Direction = direction, Output = false };
        public static PortDefinition Out(string name, Cargo cargo, Vector3 position, Vector3 direction) =>
            new PortDefinition { Name = name, Cargo = cargo, Position = position, Direction = direction, Output = true };

        public override string ToString() =>
            $"'{Name}' {(Output ? "out" : "in")} {Cargo} at ({Position.x:F2},{Position.y:F2},{Position.z:F2}) dir ({Direction.x:F2},{Direction.y:F2},{Direction.z:F2})";
    }

    /// <summary>A donor build definition's port and footprint data, read from the game when the registry freezes.</summary>
    internal sealed class DonorShape
    {
        internal Vector2 HalfFootprint;
        internal float Scale = 1f;
        internal List<(string Name, Vector3 Position, Vector3 Direction, bool Output)> Ports = new();
    }

    // Which ports a modded structure gets (resolved once, when the registry freezes): the model's Port_* nodes
    // (MoonStructures.ModelPortsProvider), else the declared Ports, else the donor's ports with a cargo each. When the
    // donor's build definition could not be read (null or a throw) and neither the model nor the definition gives ports,
    // Ports is null: the clone's conveyorPorts stay the donor's own instead of being emptied (PortRules.ChooseSource).
    internal static class PortResolver
    {
        internal const string UntouchedRule = "no rule (base ports unreadable, clone left untouched)";

        internal static (List<PortDefinition> Ports, string Rule) Resolve(StructureDefinition d, DonorShape donor, Func<StructureDefinition, DonorShape, List<PortDefinition>> modelPorts)
        {
            List<PortDefinition> fromModel = null;
            if (modelPorts != null)
            {
                try { fromModel = modelPorts(d, donor); }
                catch (Exception e) { CoreLog.Error($"'{d.Id}': reading ports from the model threw; using the declared or donor ports: {e.Message}"); }
            }
            switch (PortRules.ChooseSource(fromModel != null && fromModel.Count > 0, d.Ports != null, donor != null))
            {
                case PortSource.Model: return (Copy(fromModel), "model Port_* nodes");
                case PortSource.Declared: return (Copy(d.Ports), "declared Ports");
                case PortSource.Donor: return (FromDonor(d, donor), $"donor {d.BaseStructure} ports by name");
                default:
                    CoreLog.Warn($"'{d.Id}': the ports of base kind {d.BaseStructure} could not be read and the structure declares none; its clone keeps the base's ports unchanged");
                    return (null, UntouchedRule);
            }
        }

        static List<PortDefinition> Copy(List<PortDefinition> ports) =>
            ports.Where(p => p != null).Select(p => new PortDefinition { Name = p.Name, Position = p.Position, Direction = p.Direction, Output = p.Output, Cargo = p.Cargo }).ToList();

        // Vanilla's PortCargo rule (IN_SAND is sand, any other port dust) matched against the processor's slots: an input
        // keeps that cargo when an Input slot holds it, else takes the first Input slot's; an output carries the Output
        // slot's cargo. A port the processor has no slot for (an output on a generator) is left out. Without a processor
        // the donor's ports keep vanilla's cargo.
        static List<PortDefinition> FromDonor(StructureDefinition d, DonorShape donor)
        {
            var result = new List<PortDefinition>();
            var slots = d.Processor?.Slots;
            foreach (var p in donor.Ports)
            {
                var vanilla = PortRules.VanillaCargoId(p.Name) == "sand" ? Cargo.Sand : Cargo.Dust;
                Cargo cargo = vanilla;
                if (slots != null)
                {
                    var role = p.Output ? SlotRole.Output : SlotRole.Input;
                    var matching = slots.Where(s => s.Role == role).ToList();
                    if (matching.Count == 0)
                    {
                        CoreLog.Info($"'{d.Id}': donor port '{p.Name}' left out: the processor has no {role} slot");
                        continue;
                    }
                    cargo = p.Output ? matching[0].Cargo : matching.Any(s => s.Cargo.Id == vanilla.Id) ? vanilla : matching[0].Cargo;
                }
                result.Add(new PortDefinition { Name = p.Name, Position = p.Position, Direction = p.Direction, Output = p.Output, Cargo = cargo });
            }
            return result;
        }

        internal static DonorShape ReadDonor(MoonDepotBuildDefinition def)
        {
            if (def == null) return null;
            var shape = new DonorShape { HalfFootprint = def.halfFootprint, Scale = def.scale };
            var ports = def.conveyorPorts;
            for (int i = 0; i < (ports?.Length ?? 0); i++)
            {
                var p = ports[i];
                if (p != null) shape.Ports.Add((p.name, p.position, p.direction, p.output));
            }
            return shape;
        }

        internal static string Describe(List<PortDefinition> ports) =>
            ports == null || ports.Count == 0 ? "none" : string.Join("; ", ports.Select(p => p.ToString()));
    }
}
