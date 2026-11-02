using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MoonCleaners;

namespace StructureLib
{
    // The game describes a machine by a MoonWorld.ProcessorSpec (vanilla has two, ChamberSpec and PressSpec, picked by
    // the static Processor(kind)). A modded processor gets one synthesized from its ProcessorDefinition, built once per
    // definition and kept for the session; the Processor(kind) prefix answers it. Slot answers for the InputSlot and
    // AcceptsText prefixes come from the same definition (ProcessorLayout holds the pure part).
    internal static class SpecBuilder
    {
        static readonly Dictionary<StructureDefinition, MoonWorld.ProcessorSpec> cache = new(ReferenceEqualityComparer.Instance);

        /// <summary>The definition's spec, built on first use; null for a structure without a processor.</summary>
        internal static MoonWorld.ProcessorSpec Build(StructureDefinition d)
        {
            if (d?.Processor == null) return null;
            lock (cache)
            {
                if (cache.TryGetValue(d, out var spec)) return spec;
                spec = Create(d);
                cache[d] = spec;
                return spec;
            }
        }

        static MoonWorld.ProcessorSpec Create(StructureDefinition d)
        {
            var p = d.Processor;
            var slots = p.Slots;
            MoonWorld.ProcessorSpec spec;
            try { spec = new MoonWorld.ProcessorSpec(); }
            catch (Exception e)
            {
                CoreLog.Warn($"'{d.Id}': new ProcessorSpec() threw ({e.GetType().Name}: {e.Message}); cloning the pressure chamber's spec instead");
                spec = CloneChamberSpec();
            }
            int n = slots.Count;
            int output = ProcessorLayout.OutputIndex(slots.Select(s => s.Role).ToList(), p.ProducesPower);
            var names = new Il2CppStringArray(n);
            var cargo = new Il2CppStructArray<MoonCargo>(n);
            var capacity = new Il2CppStructArray<double>(n);
            for (int i = 0; i < n; i++)
            {
                names[i] = slots[i].Name ?? "";
                cargo[i] = slots[i].Cargo.ToMoon();
                capacity[i] = slots[i].Capacity;
            }
            spec.name = d.Name;
            spec.product = output >= 0 ? MoonWorld.CargoName(slots[output].Cargo.ToMoon()) : "power";
            spec.runningKw = p.RunningKw;
            spec.cycleSeconds = p.CycleSeconds;
            spec.slotNames = names;
            spec.slotCargo = cargo;
            spec.slotCapacity = capacity;
            spec.output = output;
            CoreLog.Info($"Processor spec for '{d.Id}' (kind {d.Kind}): '{spec.name}' makes '{spec.product}', {spec.runningKw} kW, {spec.cycleSeconds} s cycle, " +
                         $"slots [{string.Join(", ", slots.Select(s => $"{s.Role} '{s.Name}' {s.Cargo} {s.Capacity} kg"))}], output {spec.output}");
            return spec;
        }

        // Fallback when the constructor cannot be called: a ProcessorSpec allocated without running it, given every field
        // of the chamber's spec (Create then overwrites them all).
        static MoonWorld.ProcessorSpec CloneChamberSpec()
        {
            var source = MoonWorld.ChamberSpec;
            var clone = new MoonWorld.ProcessorSpec(IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<MoonWorld.ProcessorSpec>.NativeClassPtr));
            clone.name = source.name;
            clone.product = source.product;
            clone.runningKw = source.runningKw;
            clone.cycleSeconds = source.cycleSeconds;
            clone.slotNames = source.slotNames;
            clone.slotCargo = source.slotCargo;
            clone.slotCapacity = source.slotCapacity;
            clone.output = source.output;
            return clone;
        }

        /// <summary>Index of the first Input slot that takes <paramref name="cargo"/>; -1 when none does (or no processor).</summary>
        internal static int InputSlot(StructureDefinition d, MoonCargo cargo) =>
            d?.Processor == null ? -1 : ProcessorLayout.InputSlot(d.Processor.Slots.Select(s => (s.Role, s.Cargo.Value)).ToList(), (byte)cargo);

        /// <summary>The Input slots' cargo short names joined by " or "; empty for a structure without a processor.</summary>
        internal static string AcceptsText(StructureDefinition d) =>
            d?.Processor == null ? "" : ProcessorLayout.AcceptsText(d.Processor.Slots.Select(s => (s.Role, MoonWorld.CargoShortName(s.Cargo.ToMoon()))).ToList());

        /// <summary>Role, cargo byte and capacity of every slot (what ProcessorCycle plans with).</summary>
        internal static List<(SlotRole Role, byte Cargo, double Capacity)> CycleSlots(StructureDefinition d) =>
            d.Processor.Slots.Select(s => (s.Role, s.Cargo.Value, s.Capacity)).ToList();

        /// <summary>Cargo id of every slot ("dust", "sand", "stone" or a registered cargo id), as the production sidecar records them.</summary>
        internal static List<string> SlotCargoIds(StructureDefinition d) => d.Processor.Slots.Select(s => s.Cargo.Id).ToList();
    }
}
