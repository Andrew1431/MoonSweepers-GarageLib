using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using MoonCleaners;

namespace StructureLib.Patches
{
    /// <summary>
    /// <para><b>Rule: a Harmony prefix on a short native leaf must never fall through to the original</b> (never return
    /// true). A short leaf here is a static switch whose whole native body, or a branch, a branch target or a <c>ret</c>,
    /// lies inside the first ~16 bytes the detour overwrites. Calling such an original through Il2CppInterop's relocated
    /// copy runs off the end of the function.</para>
    /// <para>Evidence (2026-11-02, .scratch/crash-solar-2026-11-02/diagnosis.md): clicking a vanilla build card for kind
    /// 93..97 crashed with an access violation at GameAssembly+0x6EF7B5. Vanilla called <c>MoonWorld.IsSolarPanel</c>, a
    /// 15-byte leaf at +0x6EF730 (<c>cmp ecx,5Dh; jge +3; xor al,al; ret; cmp ecx,5Fh; setle al; ret</c>). The prefix
    /// returned true for the vanilla kind, and the original, called through <c>il2cpp_runtime_invoke</c>, ran into the next
    /// function at +0x6EF740 with the kind in rcx as <c>this</c>. The older <c>IsProcessor</c> crash (+0x6EF5D4, 0x14 past
    /// a 15-byte leaf at +0x6EF5C0) is the same mechanism. Every patched method's native size is in
    /// research/STRUCTURES_RECON.md, "Interop hazards found".</para>
    /// <para>So every pure kind -> value leaf StructureLib prefixes is recorded here, at plugin load and BEFORE Harmony
    /// patches anything, by calling the unpatched original for every key 0..bound-1. Its prefix then answers a modded kind
    /// from the definition (or as its donor, from the table) and a vanilla kind from the table, and always returns false.
    /// A key outside the table answers the conservative default (false / 0 / no slot / dust's 1 kg) and logs once.</para>
    /// <para>Each prefix logs one line per distinct (leaf, key) the first time it is asked, so the last such line in a
    /// crash log names the last new leaf call.</para>
    /// </summary>
    internal static class LeafTables
    {
        /// <summary>Kinds recorded: vanilla kinds are 1..192 (MoonDepotSave.MaxStructureKind) plus belts (1000). Modded kinds are 1,000,000 and up and never reach a table.</summary>
        internal const int KindBound = 1024;
        /// <summary>Kinds recorded for InputSlot (machine kinds; the press is 97). Each kind records all 256 cargo values.</summary>
        internal const int MachineKindBound = 256;
        internal const int CargoBound = 256;

        internal static LeafTable<bool> IsSolarPanel = Unrecorded<bool>(nameof(IsSolarPanel), 1, false);
        internal static LeafTable<bool> IsPowerNode = Unrecorded<bool>(nameof(IsPowerNode), 2, false);
        internal static LeafTable<bool> HexPieceKind = Unrecorded<bool>(nameof(HexPieceKind), 3, false);
        internal static LeafTable<bool> IsRobotDock = Unrecorded<bool>(nameof(IsRobotDock), 4, false);
        internal static LeafTable<MoonWorld.MapRobotKind> MapKindOf = Unrecorded<MoonWorld.MapRobotKind>(nameof(MapKindOf), 5, default);
        internal static LeafTable<double> UnitKilograms = Unrecorded<double>(nameof(UnitKilograms), 6, 1.0);
        internal static LeafTable<int[]> InputSlot = Unrecorded<int[]>(nameof(InputSlot), 7, null);

        static LeafTable<T> Unrecorded<T>(string name, int id, T fallback) => LeafTable<T>.Empty(name, id, fallback, "not recorded");

        /// <summary>Records every table. Call once from Plugin.Load before any Harmony patch is applied.</summary>
        internal static void Snapshot()
        {
            var sw = Stopwatch.StartNew();
            IsSolarPanel = Take(nameof(IsSolarPanel), 1, KindBound, k => MoonWorld.IsSolarPanel(k), false);
            IsPowerNode = Take(nameof(IsPowerNode), 2, KindBound, k => MoonWorld.IsPowerNode(k), false);
            HexPieceKind = Take(nameof(HexPieceKind), 3, KindBound, k => MoonWorld.HexPieceKind(k), false);
            IsRobotDock = Take(nameof(IsRobotDock), 4, KindBound, k => MoonWorld.IsRobotDock(k), false);
            MapKindOf = Take(nameof(MapKindOf), 5, KindBound, k => MoonWorld.MapKindOf(k), default(MoonWorld.MapRobotKind));
            // Vanilla answers 2 kg for Stone and 1 kg for every other value (its native bytes); the default is dust's.
            UnitKilograms = Take(nameof(UnitKilograms), 6, CargoBound, c => MoonWorld.UnitKilograms((MoonCargo)(byte)c), 1.0);
            InputSlot = Take(nameof(InputSlot), 7, MachineKindBound, k =>
            {
                var slots = new int[CargoBound];
                for (int c = 0; c < CargoBound; c++) slots[c] = MoonWorld.InputSlot(k, (MoonCargo)(byte)c);
                return slots;
            }, null);
            CoreLog.Info($"Leaf tables recorded before patching in {sw.ElapsedMilliseconds} ms: "
                + $"IsSolarPanel {Describe(IsSolarPanel)}, IsPowerNode {Describe(IsPowerNode)}, HexPieceKind {Describe(HexPieceKind)}, "
                + $"IsRobotDock {Describe(IsRobotDock)}, MapKindOf {Describe(MapKindOf)}, UnitKilograms {Describe(UnitKilograms)}, "
                + $"InputSlot {Describe(InputSlot)} (x {CargoBound} cargo). Solar kinds: {Keys(IsSolarPanel)}; power nodes: {Keys(IsPowerNode)}.");
        }

        static LeafTable<T> Take<T>(string name, int id, int bound, Func<int, T> original, T fallback)
        {
            LeafTable<T> t;
            try { t = LeafTable<T>.Snapshot(name, id, bound, original, fallback); }
            catch (Exception e) { t = LeafTable<T>.Empty(name, id, fallback, e.Message); }
            if (t.Count == 0) CoreLog.Error($"Leaf table {name}: nothing recorded ({t.Stop}); every vanilla kind answers the default {fallback}, so vanilla {name} answers are wrong this session.");
            else if (t.Stop != null) CoreLog.Warn($"Leaf table {name}: recorded {t.Range}, stopped early ({t.Stop}); keys above answer the default {fallback}.");
            return t;
        }

        static string Describe<T>(LeafTable<T> t) => t.Stop == null ? t.Range : t.Range + " (stopped: " + t.Stop + ")";

        static string Keys(LeafTable<bool> t)
        {
            var keys = new System.Collections.Generic.List<int>();
            for (int k = 0; k < t.Count; k++) if (t.Answer(k, out _)) keys.Add(k);
            return keys.Count == 0 ? "none" : string.Join(",", keys);
        }

        static readonly ConcurrentDictionary<long, byte> noted = new();
        static readonly object logLock = new();

        /// <summary>A vanilla key: the recorded answer, or the default outside the table.</summary>
        internal static T Vanilla<T>(LeafTable<T> t, int key)
        {
            T v = t.Answer(key, out bool inTable);
            Note(t, key, key, v, inTable ? "table" : "outside table " + t.Range + ": default");
            return v;
        }

        /// <summary>A kind that answers as its donor when modded: the donor's (or its own) recorded answer.</summary>
        internal static T AsBase<T>(LeafTable<T> t, int kind)
        {
            var d = Remap.Live(kind);
            if (d == null) return Vanilla(t, kind);
            T v = t.Answer(d.BaseStructure, out bool inTable);
            Note(t, kind, kind, v, $"'{d.Id}' as base {d.BaseStructure}" + (inTable ? "" : ", outside table " + t.Range + ": default"));
            return v;
        }

        /// <summary>A modded kind answered from its definition.</summary>
        internal static T Modded<T>(LeafTable<T> t, int kind, T answer, string id)
        {
            Note(t, kind, kind, answer, "'" + id + "'");
            return answer;
        }

        /// <summary>InputSlot for a vanilla kind: the recorded slot, or -1 (no slot) outside the table.</summary>
        internal static int VanillaInputSlot(int kind, MoonCargo cargo)
        {
            int[] slots = InputSlot.Answer(kind, out bool inTable);
            int v = slots != null ? slots[(byte)cargo] : -1;
            Note(InputSlot, ((long)kind << 8) | (byte)cargo, kind, v, (inTable ? "table" : "outside table " + InputSlot.Range + ": no slot") + ", cargo " + (byte)cargo);
            return v;
        }

        /// <summary>One log line per distinct (leaf, key), the first time it is asked; a dictionary lookup and no allocation after that.</summary>
        internal static void Note<TTable, TAnswer>(LeafTable<TTable> t, long key, int kind, TAnswer answer, string source)
        {
            long k = ((long)t.Id << 56) ^ key;
            if (noted.ContainsKey(k) || !noted.TryAdd(k, 0)) return;
            lock (logLock) CoreLog.Once("leaf:" + t.Name + ":" + key, $"leaf {t.Name}({kind}) = {answer} ({source})", 1);
        }
    }
}
