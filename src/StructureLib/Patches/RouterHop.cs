using System;
using System.Collections.Generic;
using System.Linq;
using MoonCleaners;
using UnityEngine;

namespace StructureLib.Patches
{
    // Routers (StructureDefinition.Router): the HandOff prefix (BeltPatches.Intake) hands a canister waiting at the end of a
    // belt joined to a router's input port to Hop, which answers true when it moved the canister onto the start of a belt
    // at one of the router's output ports (the arriving canister is then gone) and false when it waits.
    //
    // Splitter: per depot a transient nextOut index; from it, in port order, the first output port with a belt starting
    // there (BeltStartingAt of the port's world point) whose entrance is clear takes the canister; nextOut moves past it.
    // Merger: per depot a transient turn index; the output belt must exist with a clear entrance, and an input earlier in
    // turn order with a canister waiting at its belt's end (BeltEndingAt, WaitingAtEnd, and BeltPatches.OffersTo: the
    // belt resolves to that port and the canister has more than 0 kg) goes first; turn moves past the
    // input served. Both are RouterRules. The indexes live in memory: they go with a world change (and so on load) and
    // with their depot, not when a belt elsewhere is built or removed (Reset, from BeltPatches.Fresh).
    //
    // The move (belt-boxes spike, facts (b)/(c)): EmitCargo(output belt, owner, cargo, kilograms) makes a new canister at
    // the belt's start with the kilograms but capacity 1 (it answers false while the entrance is blocked); MoveCanisterRecord
    // rewrites it with the arriving canister's kilograms, capacity and owner; RemoveCanister drops the arriving one. A
    // failure between the steps undoes the emit and leaves the arriving canister waiting; when the emit cannot be undone
    // the hop completes instead, so the cargo exists once (a case where neither works is logged as an error).
    // PlaceRider is not used: from the prefix it moves the record but not the rider. The new canister is found by id from
    // MoonWorld.nextCanisterId before and after the emit (no walk of world.canisters), else as the rider at d ~ 0.
    internal static class RouterHop
    {
        sealed class Box
        {
            internal int Kind;
            internal Vector3 At;
            internal List<PortDefinition> Ins, Outs;
            internal Vector3[] InAt, OutAt;
        }

        static readonly Dictionary<int, int> nextOut = new(), turn = new();
        static readonly Dictionary<int, Box> boxes = new();
        static IntPtr world0;

        /// <summary>
        /// Called from BeltPatches.Fresh when the world, the belt count or the depot count changes. A different world
        /// clears everything; otherwise only the entries of depots that no longer exist go, so building or removing a belt
        /// elsewhere keeps every router's turn (the port cache is checked against the record's kind and position anyway).
        /// </summary>
        internal static void Reset(MoonWorld world)
        {
            IntPtr now = world == null ? IntPtr.Zero : world.Pointer;
            if (now != world0 || world?.depots == null)
            {
                world0 = now;
                nextOut.Clear();
                turn.Clear();
                boxes.Clear();
                return;
            }
            var depots = world.depots;
            foreach (var map in new IDictionary<int, int>[] { nextOut, turn })
                foreach (int k in map.Keys.ToList())
                    if (!depots.ContainsKey(k)) map.Remove(k);
            foreach (int k in boxes.Keys.ToList())
                if (!depots.ContainsKey(k)) boxes.Remove(k);
        }

        /// <summary>True when the canister went onto an output belt (the caller answers HandOff true); false when it waits.</summary>
        internal static bool Hop(MoonWorld world, StructureDefinition d, int depot, PortDefinition inPort, int inBelt, int id)
        {
            MoonConveyorSnapshot outBelt;
            int served;
            Box box;
            try
            {
                box = BoxOf(world, d, depot);
                if (box == null || box.Outs.Count == 0 || box.Ins.Count == 0) return false;
                if (d.Router.Mode == RouterMode.Splitter)
                {
                    var belts = new MoonConveyorSnapshot[box.OutAt.Length];
                    var canTake = new bool[belts.Length];
                    for (int i = 0; i < belts.Length; i++)
                    {
                        belts[i] = world.BeltStartingAt(box.OutAt[i]);
                        canTake[i] = belts[i] != null && world.EntranceClear(belts[i].id);
                    }
                    served = RouterRules.PickOutput(nextOut.TryGetValue(depot, out int n) ? n : 0, canTake);
                    if (served < 0) return false;
                    outBelt = belts[served];
                }
                else
                {
                    int input = box.Ins.IndexOf(inPort);
                    if (input < 0) input = box.Ins.FindIndex(p => string.Equals(p.Name, inPort?.Name, StringComparison.Ordinal));
                    if (input < 0) return false;
                    outBelt = world.BeltStartingAt(box.OutAt[0]);
                    bool clear = outBelt != null && world.EntranceClear(outBelt.id);
                    var waiting = new bool[box.Ins.Count];
                    if (clear)
                        for (int k = 0; k < waiting.Length; k++)
                        {
                            if (k == input) { waiting[k] = true; continue; }
                            var b = world.BeltEndingAt(box.InAt[k]);
                            // Waiting counts only for an input whose belt resolves to port k and offers a canister of
                            // more than 0 kg: one Intake refuses before the hop would make the others yield for ever.
                            waiting[k] = b != null && b.id != inBelt && world.WaitingAtEnd(b.id) && BeltPatches.OffersTo(world, b.id, depot, box.Ins[k]);
                        }
                    if (!RouterRules.MergerAccepts(turn.TryGetValue(depot, out int t) ? t : 0, input, waiting, clear)) return false;
                    served = input;
                }
            }
            catch (Exception e)
            {
                CoreLog.Once("router-pick:" + d.Id, $"Router '{d.Id}' depot {depot}: choosing a belt for canister {id} threw; it waits: {e}", 1);
                return false;
            }

            MoonCargo cargo;
            double kg;
            int fresh;
            string found;
            try
            {
                if (!Move(world, d, depot, id, outBelt, out cargo, out kg, out fresh, out found)) return false;
            }
            catch (Exception e)
            {
                CoreLog.Once("router-move:" + d.Id, $"Router '{d.Id}' depot {depot}: moving canister {id} onto belt {outBelt?.id} threw; it waits: {e}", 1);
                return false;
            }

            // The canister has moved: nothing below may turn that into a false answer.
            try
            {
                if (d.Router.Mode == RouterMode.Splitter) nextOut[depot] = RouterRules.Advance(served, box.Outs.Count);
                else turn[depot] = RouterRules.Advance(served, box.Ins.Count);
                string cargoId = BeltPatches.CargoId(cargo);
                string port = d.Router.Mode == RouterMode.Splitter ? box.Outs[served].Name : box.Ins[served].Name;
                CoreLog.Once("router-first:" + depot, $"Router '{d.Id}' depot {depot} ({d.Router.Mode}) routes: canister {id} ({cargoId}, {kg} kg) from belt {inBelt} onto belt {outBelt.id} at port '{port}' as canister {fresh}", 1);
                CoreLog.Debug($"CargoRouted '{d.Id}' depot {depot}: {kg} kg {cargoId} belt {inBelt} -> belt {outBelt.id} ('{port}'), canister {id} -> {fresh} ({found})");
                MoonStructures.RaiseCargoRouted(new CargoRouted
                {
                    Definition = d, DepotId = depot, Cargo = new Cargo((byte)cargo, cargoId), Kilograms = kg, FromBeltId = inBelt, ToBeltId = outBelt.id,
                });
            }
            catch (Exception e) { CoreLog.Once("router-after:" + d.Id, $"Router '{d.Id}' depot {depot}: bookkeeping after a hop threw (the canister moved): {e}", 1); }
            return true;
        }

        // EmitCargo, MoveCanisterRecord of the new canister, RemoveCanister of the arriving one. True when the arriving
        // canister is gone and its cargo rides the output belt; false (with the emit undone) when it must keep waiting.
        // Once EmitCargo has answered true nothing here throws: every later step is guarded, and the outcome is decided by
        // whether the arriving canister is still in world.canisters, so a hop can never leave both canisters behind
        // silently (and HandOff, called again next frame, emit another copy).
        static bool Move(MoonWorld world, StructureDefinition d, int depot, int id, MoonConveyorSnapshot outBelt, out MoonCargo cargo, out double kg, out int fresh, out string found)
        {
            fresh = -1;
            found = "not found";
            var canisters = world.canisters;
            var c = canisters.get_Item(id);
            cargo = c.cargo;
            kg = c.kilograms;
            double capacity = c.capacity;
            string ownerRaw = c.owner;
            string owner = string.IsNullOrEmpty(ownerRaw) ? MoonWorld.LocalPlayer : ownerRaw;

            // The emitted canister is found by id, without walking world.canisters: MoonWorld.nextCanisterId read before
            // and after the emit (RouterRules.EmittedId), else the rider at d ~ 0 on the output belt (its entrance was
            // clear, so only the new canister can sit there).
            int next0 = world.nextCanisterId;
            bool had0 = canisters.ContainsKey(next0), had1 = canisters.ContainsKey(next0 + 1);
            // A throw up to here (or from EmitCargo itself) leaves nothing emitted: the caller logs it and the canister waits.
            if (!world.EmitCargo(outBelt, owner, cargo, kg, out string reason))
            {
                CoreLog.Once($"router-emit:{depot}:{reason}", $"Router '{d.Id}' depot {depot}: belt {outBelt.id} refused canister {id}: '{reason}'; it waits", 1);
                return false;
            }

            try
            {
                int next1 = world.nextCanisterId;
                fresh = RouterRules.EmittedId(next0, next1, k => k == next0 ? had0 : k == next0 + 1 && had1, k => Present(world, k));
                if (fresh >= 0) found = $"nextCanisterId {next0} -> {next1}";
                else
                {
                    fresh = RiderAtStart(world, outBelt.id);
                    found = fresh >= 0 ? $"rider at d 0 (nextCanisterId {next0} -> {next1})" : $"not found (nextCanisterId {next0} -> {next1})";
                    CoreLog.Once("router-find-fallback:" + d.Id, $"Router '{d.Id}' depot {depot}: nextCanisterId went {next0} -> {next1} over EmitCargo onto belt {outBelt.id}, which does not name the new canister; the rider at its start: {(fresh >= 0 ? "canister " + fresh : "none")}", 1);
                }
            }
            catch (Exception e) { CoreLog.Once("router-find:" + d.Id, $"Router '{d.Id}' depot {depot}: finding the canister EmitCargo put on belt {outBelt.id} threw: {e.Message}", 1); }

            if (fresh < 0)
            {
                // The emit made a canister we cannot find to fix: it already carries the kilograms (spike fact (b)), so the
                // arriving one still goes (keeping both would duplicate the cargo); only its capacity may be off.
                CoreLog.Once("router-nofresh:" + d.Id, $"Router '{d.Id}' depot {depot}: EmitCargo onto belt {outBelt.id} answered true but no new canister was found; its capacity was not rewritten", 1);
            }
            else if (!Rewrite(world, fresh, ownerRaw, kg, capacity, out string why))
            {
                // The emitted canister has the kilograms with capacity 1: right for a canister of up to 1 kg, wrong for a
                // heavier one, which then waits at the input instead, unless the emit cannot be undone: completing the hop
                // (capacity 1) beats leaving a duplicate on the belt.
                if (kg > 1.0 + 1e-9 && Undo(world, d, depot, fresh, outBelt.id))
                {
                    CoreLog.Once("router-rewrite:" + d.Id, $"Router '{d.Id}' depot {depot}: rewriting canister {fresh} to {kg} kg / capacity {capacity} threw; canister {id} waits: {why}", 1);
                    return false;
                }
                CoreLog.Once("router-rewrite:" + d.Id, $"Router '{d.Id}' depot {depot}: rewriting canister {fresh} threw; it keeps EmitCargo's capacity 1 ({kg} kg) and the hop completes: {why}", 1);
            }

            try { world.RemoveCanister(id); }
            catch (Exception e) { CoreLog.Once("router-remove-threw:" + d.Id, $"Router '{d.Id}' depot {depot}: RemoveCanister({id}) threw: {e.Message}", 1); }
            if (!Present(world, id)) return true;

            // The arriving canister is still there: take the emitted one back so the cargo exists once, and wait.
            if (fresh >= 0)
            {
                bool undone = Undo(world, d, depot, fresh, outBelt.id);
                CoreLog.Once("router-remove:" + d.Id, $"Router '{d.Id}' depot {depot}: RemoveCanister({id}) left the canister in the world; the emitted canister {fresh} {(undone ? "was taken back" : "could not be taken back (logged as an error)")} and it waits", 1);
            }
            else Loud($"router-dup:{depot}:{outBelt.id}", $"Router '{d.Id}' depot {depot}: RemoveCanister({id}) left the canister in the world and the canister EmitCargo put on belt {outBelt.id} could not be found: {kg} kg of cargo {(byte)cargo} now exists twice (belt {outBelt.id} and canister {id}, which waits)");
            return false;
        }

        // MoveCanisterRecord of the emitted canister with the arriving one's kilograms, capacity and owner.
        static bool Rewrite(MoonWorld world, int fresh, string owner, double kg, double capacity, out string why)
        {
            why = null;
            try
            {
                var n = world.canisters.get_Item(fresh);
                var rotation = new Il2CppSystem.Nullable<Quaternion>(n.rotation);
                var moved = new MoonDroppedCanister(n.id, owner, kg, capacity, n.position, n.up, n.createdAt, rotation, n.cargo);
                moved.physicalPose = n.physicalPose;
                world.MoveCanisterRecord(moved, n.position, n.up, rotation);
                return true;
            }
            catch (Exception e) { why = e.Message; return false; }
        }

        // Removes the emitted canister; true only when it has left world.canisters. A failure is an error, once per belt.
        static bool Undo(MoonWorld world, StructureDefinition d, int depot, int fresh, int belt)
        {
            string why = null;
            try { world.RemoveCanister(fresh); }
            catch (Exception e) { why = e.Message; }
            if (!Present(world, fresh)) return true;
            Loud($"router-undo:{depot}:{belt}", $"Router '{d.Id}' depot {depot}: taking back the emitted canister {fresh} on belt {belt} failed{(why == null ? " (RemoveCanister left it in the world)" : ": " + why)}; it stays on the belt");
            return false;
        }

        // The one rider at d ~ 0 on the belt (the fallback for finding the emitted canister), else -1. Walks world.riders.
        static int RiderAtStart(MoonWorld world, int belt)
        {
            int id = -1;
            foreach (var kv in world.riders)
            {
                if (kv.Value.belt != belt || kv.Value.distance > 0.05f) continue;
                if (id >= 0) return -1;
                id = kv.Key;
            }
            return id >= 0 && Present(world, id) ? id : -1;
        }

        // Present in world.canisters; a lookup that throws counts as present (the safe answer: the canister waits).
        static bool Present(MoonWorld world, int id)
        {
            try { return world.canisters.ContainsKey(id); }
            catch (Exception) { return true; }
        }

        static readonly HashSet<string> loud = new(StringComparer.Ordinal);

        static void Loud(string key, string m)
        {
            if (loud.Add(key)) CoreLog.Error(m);
        }

        // The router's ports in world space, from its view's pose; null while the view does not exist (a load in progress).
        // Cached per depot, checked against the live record's kind and position.
        static Box BoxOf(MoonWorld world, StructureDefinition d, int depot)
        {
            var rec = world.depots != null && world.depots.ContainsKey(depot) ? world.depots.get_Item(depot) : null;
            if (rec == null) return null;
            var at = new Vector3(rec.x, rec.y, rec.z);
            if (boxes.TryGetValue(depot, out var box) && box.Kind == rec.kind && box.At == at) return box;
            var view = UnityEngine.Object.FindFirstObjectByType<MoonDepotConstruction>()?.ViewOf(depot);
            if (view == null) return null;
            var ports = MoonStructures.PortsOf(d) ?? new List<PortDefinition>();
            box = new Box
            {
                Kind = rec.kind,
                At = at,
                Ins = ports.Where(p => p != null && !p.Output).ToList(),
                Outs = ports.Where(p => p != null && p.Output).ToList(),
            };
            var t = view.transform;
            box.InAt = box.Ins.Select(p => t.position + t.rotation * p.Position).ToArray();
            box.OutAt = box.Outs.Select(p => t.position + t.rotation * p.Position).ToArray();
            boxes[depot] = box;
            return box;
        }
    }
}
