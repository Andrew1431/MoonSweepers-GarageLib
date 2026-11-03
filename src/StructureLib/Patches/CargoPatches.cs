using System;
using HarmonyLib;
using MoonCleaners;

namespace StructureLib.Patches
{
    // The cargo switches. Vanilla answers every value it does not know (3..255) as dust, so a registered modded value is
    // answered from its CargoDefinition before vanilla runs. Every prefix lets vanilla run untouched unless the registry is
    // frozen, working, and the value is a registered modded cargo; an unregistered value still gets vanilla's dust answer.
    //
    // MoonDroppedCanister.IsDust is NOT patched: a canister constructed with a modded value already reports false (the
    // getter compares with Dust; checked in game by the cargo-isdust-false self-test). See README.md for the audit of
    // every MoonCargo-typed member.
    [HarmonyPatch]
    internal static class CargoPatches
    {
        /// <summary>The one guard every cargo prefix uses: null unless the value is a registered modded cargo in a working, frozen registry.</summary>
        internal static CargoDefinition Live(MoonCargo cargo) =>
            MoonStructures.Frozen && !MoonStructures.Disabled && MoonStructures.IsModded(cargo) ? MoonStructures.CargoDefinition(cargo) : null;

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.CargoName)), HarmonyPrefix]
        static bool CargoName(MoonCargo cargo, ref string __result)
        {
            var d = Live(cargo);
            if (d == null) return true;
            __result = d.Name;
            return false;
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.CargoShortName)), HarmonyPrefix]
        static bool CargoShortName(MoonCargo cargo, ref string __result)
        {
            var d = Live(cargo);
            if (d == null) return true;
            __result = d.ShortName;
            return false;
        }

        // A 23-byte native leaf (2 kg for Stone, 1 for any other value): every value answers from the table recorded
        // before patching or from its CargoDefinition, never by the original (see LeafTables).
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.UnitKilograms)), HarmonyPrefix]
        static bool UnitKilograms(MoonCargo cargo, ref double __result)
        {
            var d = Live(cargo);
            __result = d != null
                ? LeafTables.Modded(LeafTables.UnitKilograms, (byte)cargo, d.UnitKilograms, d.Id)
                : LeafTables.Vanilla(LeafTables.UnitKilograms, (byte)cargo);
            return false;
        }

        // Dollars per unit. A conversion failure leaves the answer to vanilla (dust's price) rather than throwing into the game.
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.CargoValue)), HarmonyPrefix]
        static bool CargoValue(MoonCargo cargo, ref Il2CppSystem.Decimal __result)
        {
            var d = Live(cargo);
            if (d == null) return true;
            try { __result = ToIl2Cpp(d.Price); }
            catch (Exception e)
            {
                CoreLog.Once("cargovalue:" + (byte)cargo, $"CargoValue({(byte)cargo}) for '{d.Id}': converting ${d.Price} threw; answering as vanilla: {e.Message}", 2);
                return true;
            }
            return false;
        }

        /// <summary>Exact decimal -> Il2CppSystem.Decimal (through its bits, not a double).</summary>
        internal static Il2CppSystem.Decimal ToIl2Cpp(decimal value)
        {
            int[] bits = decimal.GetBits(value);
            bool negative = (bits[3] & unchecked((int)0x80000000)) != 0;
            byte scale = (byte)((bits[3] >> 16) & 0xFF);
            return new Il2CppSystem.Decimal(bits[0], bits[1], bits[2], negative, scale);
        }
    }
}
