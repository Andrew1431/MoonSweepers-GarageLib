using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StructureLib
{
    /// <summary>
    /// Port validation (run when the registry freezes) and the name a modded port carries in the cloned definition.
    /// Cargo ids are the <see cref="Cargo"/> handles' ids ("dust", "sand", "stone" or a registered modded id).
    /// </summary>
    public static class PortRules
    {
        /// <summary>Prefix of every port name StructureLib writes into a cloned definition: <c>sl:&lt;cargo byte&gt;:&lt;Name&gt;</c>.</summary>
        public const string NamePrefix = "sl:";

        /// <summary>
        /// Errors for a structure's ports against its processor slots: a duplicate port name; an input port whose cargo no
        /// Input slot holds; an output port whose cargo is not the Output slot's; more than one output port.
        /// </summary>
        public static IReadOnlyList<string> CheckPorts(string id, IReadOnlyList<(string Name, bool Output, string CargoId)> ports, IReadOnlyList<(SlotRole Role, string CargoId)> slots)
        {
            var errors = new List<string>();
            if (ports == null || ports.Count == 0) return errors;
            slots ??= Array.Empty<(SlotRole, string)>();

            foreach (var group in ports.GroupBy(p => p.Name ?? "", StringComparer.Ordinal).Where(g => g.Count() > 1))
                errors.Add($"'{id}': port '{group.Key}' is declared {group.Count()} times; port names must be unique.");

            var inputs = new HashSet<string>(slots.Where(s => s.Role == SlotRole.Input).Select(s => s.CargoId), StringComparer.Ordinal);
            string outputCargo = slots.Where(s => s.Role == SlotRole.Output).Select(s => s.CargoId).FirstOrDefault();
            foreach (var p in ports)
            {
                if (!p.Output)
                {
                    if (!inputs.Contains(p.CargoId))
                        errors.Add($"'{id}': port '{p.Name}' takes {p.CargoId} but no input slot does.");
                }
                else if (outputCargo == null)
                    errors.Add($"'{id}': port '{p.Name}' emits {p.CargoId} but the structure has no output slot.");
                else if (!string.Equals(p.CargoId, outputCargo, StringComparison.Ordinal))
                    errors.Add($"'{id}': port '{p.Name}' emits {p.CargoId} but the output slot holds {outputCargo}.");
            }

            int outputs = ports.Count(p => p.Output);
            if (outputs > 1)
                errors.Add($"'{id}': {outputs} output ports; a structure has at most one.");
            return errors;
        }

        /// <summary>
        /// A warning when a port's structure-local position lies outside the footprint (<paramref name="half"/> = half
        /// width along X and Z) grown by <paramref name="reach"/> on every side; null when it is inside.
        /// </summary>
        public static string WarnOutsideFootprint(string id, string port, (float x, float y, float z) pos, (float x, float y) half, float reach)
        {
            float limitX = Math.Abs(half.x) + reach, limitZ = Math.Abs(half.y) + reach;
            if (Math.Abs(pos.x) <= limitX && Math.Abs(pos.z) <= limitZ) return null;
            return string.Format(CultureInfo.InvariantCulture,
                "'{0}': port '{1}' at ({2:F2}, {3:F2}, {4:F2}) lies outside the footprint (half {5:F2} x {6:F2}) plus {7:F2} m; a belt may not reach it.",
                id, port, pos.x, pos.y, pos.z, half.x, half.y, reach);
        }

        /// <summary>The name a modded port gets in the cloned definition: <c>sl:&lt;cargo&gt;:&lt;name&gt;</c>.</summary>
        public static string EncodeName(byte cargo, string name) => NamePrefix + cargo.ToString(CultureInfo.InvariantCulture) + ":" + (name ?? "");

        /// <summary>The cargo byte of a name <see cref="EncodeName"/> wrote; false for any other name (a vanilla port).</summary>
        public static bool TryDecodeCargo(string name, out byte cargo)
        {
            cargo = 0;
            if (name == null || !name.StartsWith(NamePrefix, StringComparison.Ordinal)) return false;
            int end = name.IndexOf(':', NamePrefix.Length);
            if (end <= NamePrefix.Length) return false;
            return byte.TryParse(name.Substring(NamePrefix.Length, end - NamePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out cargo);
        }

        /// <summary>The cargo vanilla's PortCargo answers for a donor port name: IN_SAND is sand, every other port dust.</summary>
        public static string VanillaCargoId(string donorPortName) => donorPortName == "IN_SAND" ? "sand" : "dust";
    }
}
