using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    /// patches anything, by calling the unpatched original once per key the game itself defines, and no other key. Kinds:
    /// 0..<c>MoonDepotSave.MaxStructureKind</c> plus <c>MoonDepotBuilder.ConveyorKind</c>. Cargo: the values of the
    /// <c>MoonCargo</c> enum. (A leaf that indexed a table could fault on a key it does not know, so nothing outside the
    /// game's own domain is ever passed to an original.) Its prefix then answers a modded kind or cargo from the
    /// definition (or as its donor, from the table) and every other key from the table, and always returns false. A key
    /// that was not recorded answers the conservative default (false / 0 / no slot / dust's 1 kg) and logs once.</para>
    /// <para>Each prefix logs one line per distinct (leaf, key) the first time it is asked, so the last such line in a
    /// crash log names the last new leaf call.</para>
    /// </summary>
    internal static class LeafTables
    {
        /// <summary>Sanity cap on the game's MaxStructureKind (192 in the 2026-11 build); above it the kind domain counts as unknown.</summary>
        internal const int MaxSaneKind = 4096;

        internal static LeafTable<bool> IsSolarPanel = Unrecorded<bool>(nameof(IsSolarPanel), 1, false);
        internal static LeafTable<bool> IsPowerNode = Unrecorded<bool>(nameof(IsPowerNode), 2, false);
        internal static LeafTable<bool> HexPieceKind = Unrecorded<bool>(nameof(HexPieceKind), 3, false);
        internal static LeafTable<bool> IsRobotDock = Unrecorded<bool>(nameof(IsRobotDock), 4, false);
        internal static LeafTable<MoonWorld.MapRobotKind> MapKindOf = Unrecorded<MoonWorld.MapRobotKind>(nameof(MapKindOf), 5, default);
        internal static LeafTable<double> UnitKilograms = Unrecorded<double>(nameof(UnitKilograms), 6, 1.0);
        /// <summary>Keyed by LeafTable.PairKey(kind, cargo).</summary>
        internal static LeafTable<int> InputSlot = Unrecorded<int>(nameof(InputSlot), 7, -1);

        static LeafTable<T> Unrecorded<T>(string name, int id, T fallback) => LeafTable<T>.Empty(name, id, fallback, "not recorded");

        /// <summary>Records every table. Call once from Plugin.Load before any Harmony patch is applied.</summary>
        internal static void Snapshot()
        {
            var sw = Stopwatch.StartNew();
            int maxKind, conveyor;
            byte[] cargo;
            try
            {
                maxKind = MoonDepotSave.MaxStructureKind;
                conveyor = MoonDepotBuilder.ConveyorKind;
                var values = (MoonCargo[])Enum.GetValues(typeof(MoonCargo));
                cargo = new byte[values.Length];
                for (int i = 0; i < values.Length; i++) cargo[i] = (byte)values[i];
            }
            catch (Exception e)
            {
                CoreLog.Error($"Leaf tables: reading the game's kind and cargo domain threw; nothing recorded, every vanilla leaf answers its default this session: {e}");
                return;
            }
            if (maxKind < 1 || maxKind > MaxSaneKind || cargo.Length == 0)
            {
                CoreLog.Error($"Leaf tables: the game's domain looks wrong (MaxStructureKind {maxKind}, {cargo.Length} cargo values); nothing recorded, every vanilla leaf answers its default this session.");
                return;
            }

            var kinds = LeafTable<bool>.KindKeys(maxKind, conveyor);
            var cargoKeys = new List<long>();
            foreach (byte c in cargo) cargoKeys.Add(c);
            var pairs = new List<long>();
            foreach (long k in kinds) foreach (byte c in cargo) pairs.Add(LeafTable<int>.PairKey((int)k, c));

            IsSolarPanel = Take(nameof(IsSolarPanel), 1, kinds, k => MoonWorld.IsSolarPanel((int)k), false);
            IsPowerNode = Take(nameof(IsPowerNode), 2, kinds, k => MoonWorld.IsPowerNode((int)k), false);
            HexPieceKind = Take(nameof(HexPieceKind), 3, kinds, k => MoonWorld.HexPieceKind((int)k), false);
            IsRobotDock = Take(nameof(IsRobotDock), 4, kinds, k => MoonWorld.IsRobotDock((int)k), false);
            MapKindOf = Take(nameof(MapKindOf), 5, kinds, k => MoonWorld.MapKindOf((int)k), default(MoonWorld.MapRobotKind));
            // Vanilla answers 2 kg for Stone and 1 kg for any other value (its native bytes); the default is dust's.
            UnitKilograms = Take(nameof(UnitKilograms), 6, cargoKeys, c => MoonWorld.UnitKilograms((MoonCargo)(byte)c), 1.0);
            InputSlot = Take(nameof(InputSlot), 7, pairs, p => MoonWorld.InputSlot((int)(p >> 8), (MoonCargo)(byte)(p & 0xFF)), -1);

            int calls = IsSolarPanel.Count + IsPowerNode.Count + HexPieceKind.Count + IsRobotDock.Count + MapKindOf.Count + UnitKilograms.Count + InputSlot.Count;
            CoreLog.Info($"Leaf tables recorded before patching: {calls} original calls in {sw.ElapsedMilliseconds} ms. "
                + $"Domain: kinds 0..{maxKind} + {conveyor} ({kinds.Count}), cargo {string.Join(",", cargo)}. "
                + $"IsSolarPanel {Describe(IsSolarPanel)}, IsPowerNode {Describe(IsPowerNode)}, HexPieceKind {Describe(HexPieceKind)}, "
                + $"IsRobotDock {Describe(IsRobotDock)}, MapKindOf {Describe(MapKindOf)}, UnitKilograms {Describe(UnitKilograms)}, "
                + $"InputSlot {Describe(InputSlot)} (kind x cargo). Solar kinds: {True(IsSolarPanel, kinds)}; power nodes: {True(IsPowerNode, kinds)}.");
        }

        static LeafTable<T> Take<T>(string name, int id, List<long> keys, Func<long, T> original, T fallback)
        {
            LeafTable<T> t;
            try { t = LeafTable<T>.Snapshot(name, id, keys, original, fallback); }
            catch (Exception e) { t = LeafTable<T>.Empty(name, id, fallback, e.Message); }
            if (t.Count == 0) CoreLog.Error($"Leaf table {name}: nothing recorded ({t.Stop}); every vanilla key answers the default {fallback}, so vanilla {name} answers are wrong this session.");
            else if (t.Stop != null) CoreLog.Warn($"Leaf table {name}: recorded {t.Range}, stopped early ({t.Stop}); the keys from there on answer the default {fallback}.");
            return t;
        }

        static string Describe<T>(LeafTable<T> t) => t.Stop == null ? t.Range : t.Range + " (stopped: " + t.Stop + ")";

        static string True(LeafTable<bool> t, List<long> keys)
        {
            var hits = new List<long>();
            foreach (long k in keys) if (t.Answer(k, out _)) hits.Add(k);
            return hits.Count == 0 ? "none" : string.Join(",", hits);
        }

        static readonly ConcurrentDictionary<long, byte> noted = new();
        static readonly object logLock = new();

        /// <summary>A vanilla key: the recorded answer, or the default for a key not recorded.</summary>
        internal static T Vanilla<T>(LeafTable<T> t, int key)
        {
            T v = t.Answer(key, out bool inTable);
            Note(t, key, key, v, inTable ? "table" : "not in table (" + t.Range + "): default");
            return v;
        }

        /// <summary>A kind that answers as its donor when modded: the donor's (or its own) recorded answer.</summary>
        internal static T AsBase<T>(LeafTable<T> t, int kind)
        {
            var d = Remap.Live(kind);
            if (d == null) return Vanilla(t, kind);
            T v = t.Answer(d.BaseStructure, out bool inTable);
            Note(t, kind, kind, v, $"'{d.Id}' as base {d.BaseStructure}" + (inTable ? "" : ", not in table (" + t.Range + "): default"));
            return v;
        }

        /// <summary>A modded kind or cargo answered from its definition.</summary>
        internal static T Modded<T>(LeafTable<T> t, int kind, T answer, string id)
        {
            Note(t, kind, kind, answer, "'" + id + "'");
            return answer;
        }

        /// <summary>InputSlot for a vanilla kind or unregistered cargo: the recorded slot, or -1 (no slot) for a pair not recorded.</summary>
        internal static int VanillaInputSlot(int kind, MoonCargo cargo)
        {
            long key = LeafTable<int>.PairKey(kind, (byte)cargo);
            int v = InputSlot.Answer(key, out bool inTable);
            Note(InputSlot, key, kind, v, (inTable ? "table" : "not in table (" + InputSlot.Range + "): no slot") + ", cargo " + (byte)cargo);
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
