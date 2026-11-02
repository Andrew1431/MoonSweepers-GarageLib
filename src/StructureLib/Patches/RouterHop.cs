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
    // turn order with a canister waiting at its belt's end (BeltEndingAt, WaitingAtEnd) goes first; turn moves past the
    // input served. Both are RouterRules. The indexes reset with the belt caches (Reset, from BeltPatches.Fresh) and on load.
    //
    // The move (belt-boxes spike, facts (b)/(c)): EmitCargo(output belt, owner, cargo, kilograms) makes a new canister at
    // the belt's start with the kilograms but capacity 1 (it answers false while the entrance is blocked); MoveCanisterRecord
    // rewrites it with the arriving canister's kilograms, capacity and owner; RemoveCanister drops the arriving one. Every
    // failure between the steps undoes the emit and leaves the arriving canister waiting: nothing is lost or duplicated.
    // PlaceRider is not used: from the prefix it moves the record but not the rider.
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

        internal static void Reset()
        {
            nextOut.Clear();
            turn.Clear();
            boxes.Clear();
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
                            waiting[k] = b != null && b.id != inBelt && world.WaitingAtEnd(b.id);
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
            try
            {
                if (!Move(world, d, depot, id, outBelt, out cargo, out kg, out fresh)) return false;
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
                CoreLog.Debug($"CargoRouted '{d.Id}' depot {depot}: {kg} kg {cargoId} belt {inBelt} -> belt {outBelt.id} ('{port}'), canister {id} -> {fresh}");
                MoonStructures.RaiseCargoRouted(new CargoRouted
                {
                    Definition = d, DepotId = depot, Cargo = new Cargo((byte)cargo, cargoId), Kilograms = kg, FromBeltId = inBelt, ToBeltId = outBelt.id,
                });
            }
            catch (Exception e) { CoreLog.Once("router-after:" + d.Id, $"Router '{d.Id}' depot {depot}: bookkeeping after a hop threw (the canister moved): {e}", 1); }
            return true;
        }

        // EmitCargo, MoveCanisterRecord of the new canister, RemoveCanister of the arriving one. False (with the emit undone)
        // whenever the arriving canister must keep waiting.
        static bool Move(MoonWorld world, StructureDefinition d, int depot, int id, MoonConveyorSnapshot outBelt, out MoonCargo cargo, out double kg, out int fresh)
        {
            fresh = -1;
            var canisters = world.canisters;
            var c = canisters.get_Item(id);
            cargo = c.cargo;
            kg = c.kilograms;
            double capacity = c.capacity;
            string ownerRaw = c.owner;
            string owner = string.IsNullOrEmpty(ownerRaw) ? MoonWorld.LocalPlayer : ownerRaw;

            var before = new HashSet<int>();
            foreach (var kv in canisters) before.Add(kv.Key);
            if (!world.EmitCargo(outBelt, owner, cargo, kg, out string reason))
            {
                CoreLog.Once($"router-emit:{depot}:{reason}", $"Router '{d.Id}' depot {depot}: belt {outBelt.id} refused canister {id}: '{reason}'; it waits", 1);
                return false;
            }
            foreach (var kv in canisters)
                if (!before.Contains(kv.Key)) { fresh = kv.Key; break; }
            if (fresh < 0)
            {
                // The emit made a canister we cannot find to fix: it already carries the kilograms (spike fact (b)), so the
                // arriving one still goes (keeping both would duplicate the cargo); only its capacity may be off.
                CoreLog.Once("router-nofresh:" + d.Id, $"Router '{d.Id}' depot {depot}: EmitCargo onto belt {outBelt.id} answered true but no new canister was found; its capacity was not rewritten", 1);
            }
            else
            {
                try
                {
                    var n = canisters.get_Item(fresh);
                    var rotation = new Il2CppSystem.Nullable<Quaternion>(n.rotation);
                    var moved = new MoonDroppedCanister(n.id, ownerRaw, kg, capacity, n.position, n.up, n.createdAt, rotation, n.cargo);
                    moved.physicalPose = n.physicalPose;
                    world.MoveCanisterRecord(moved, n.position, n.up, rotation);
                }
                catch (Exception e)
                {
                    // The emitted canister has the kilograms with capacity 1: right for a canister of up to 1 kg, wrong
                    // for a heavier one, which then waits at the input instead.
                    if (kg > 1.0 + 1e-9)
                    {
                        Undo(world, fresh);
                        CoreLog.Once("router-rewrite:" + d.Id, $"Router '{d.Id}' depot {depot}: rewriting canister {fresh} to {kg} kg / capacity {capacity} threw; canister {id} waits: {e.Message}", 1);
                        return false;
                    }
                    CoreLog.Once("router-rewrite:" + d.Id, $"Router '{d.Id}' depot {depot}: rewriting canister {fresh} threw; it keeps EmitCargo's capacity 1 ({kg} kg): {e.Message}", 1);
                }
            }
            world.RemoveCanister(id);
            if (canisters.ContainsKey(id))
            {
                if (fresh >= 0) Undo(world, fresh);
                CoreLog.Once("router-remove:" + d.Id, $"Router '{d.Id}' depot {depot}: RemoveCanister({id}) left the canister in the world; {(fresh >= 0 ? "the emitted one was removed and it" : "it")} waits", 1);
                return false;
            }
            return true;
        }

        static void Undo(MoonWorld world, int fresh)
        {
            try { world.RemoveCanister(fresh); }
            catch (Exception e) { CoreLog.Once("router-undo", $"Removing the emitted canister {fresh} threw: {e.Message}", 1); }
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
