using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MoonCleaners;
using UnityEngine;

namespace StructureLib.Patches
{
    // Belt input into modded machines. When a canister reaches the end of a belt joined to a machine's input port, the
    // game calls the instance MoonWorld.HandOff(belt, path, id) every frame until it answers true (the canister is then
    // gone); for any kind but its own machines it answers false by an internal kind check, and the canister waits at the
    // belt end with later ones queuing behind it (recon "Belts and ports", fact (a)). EnterMachine and FeedProcessor are
    // never called on that path. So for a belt whose end is a modded machine's input port this prefix decides instead
    // (BeltIntake): the kilograms go into the first Input slot of the canister's cargo under the canister's owner (as
    // CrewProcessorLoad's prefix does), the canister is removed with the game's own RemoveCanister (or its kilograms
    // rewritten through MoveCanisterRecord when only part fits), and MoonStructures.CargoEntered fires. A refusal answers
    // false (the canister keeps waiting, as vanilla leaves a refused one) and sets the machine's flow status in vanilla's
    // wording. Any belt whose end is not a modded machine runs vanilla untouched. A router's input port (a structure with
    // StructureDefinition.Router) goes to RouterHop instead: no slot, no cargo check, the canister moves onto a belt at one
    // of its output ports or waits.
    //
    // Belt -> (depot, port) is resolved once per belt and cached (HandOff runs every frame for a waiting canister): the
    // machine is MachineWithPort(end, false), the port is the resolved PortDefinition whose local position, placed by the
    // depot's view pose, lies within MoonWorld.PortTolerance of the belt's end (as the dev AutoBelt matches ports). The
    // cache is dropped when the world, the number of belts or the number of depots changes, and an entry whose belt end
    // moved is resolved again.
    //
    // Belt output needs no patch: the game's own EmitProduct puts a modded machine's output on the belt joined to its output
    // port (one 1 kg canister per EmitCargo call, recon fact (f)). EmitterOf only names the machine behind an outfeed belt,
    // so the EmitCargo postfix (ProductionPatches) can raise MoonStructures.CargoEmitted.
    [HarmonyPatch]
    internal static class BeltPatches
    {
        sealed class Joined
        {
            internal Vector3 End;
            internal int Belt = -1, Depot = -1, Kind = -1;
            internal StructureDefinition Definition;
            internal PortDefinition Port;
            internal string WaitingText;
            internal MoonCargo WaitingCargo = (MoonCargo)255;
        }

        static readonly Dictionary<int, Joined> cache = new();
        static MoonWorld cacheWorld;
        static int cacheBelts = -1, cacheDepots = -1;

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.HandOff)), HarmonyPrefix]
        static bool HandOff(MoonWorld __instance, int belt, MoonConveyorPath path, int id, ref bool __result)
        {
            if (!MoonStructures.Frozen || MoonStructures.Disabled || __instance == null) return true;
            Joined j;
            try { j = Resolve(__instance, belt, path); }
            catch (Exception e)
            {
                CoreLog.Once("handoff-resolve", $"HandOff(belt {belt}, canister {id}): resolving the belt's machine threw; running vanilla: {e}", 1);
                return true;
            }
            if (j?.Definition == null || j.Port == null) return true;
            __result = Intake(__instance, j, id);
            return false;
        }

        // The modded machine and port at the belt's end; an entry with no Definition for any other belt.
        static Joined Resolve(MoonWorld world, int belt, MoonConveyorPath path)
        {
            Fresh(world);
            Vector3 end;
            if (path != null) end = path.end;
            else if (world.conveyors != null && world.conveyors.ContainsKey(belt)) end = world.conveyors.get_Item(belt).end;
            else return null;
            if (cache.TryGetValue(belt, out var j) && j.End == end)
            {
                // The machine at the end may have been replaced between two calls with the counts unchanged (one removed,
                // another placed): the cached depot must still exist with the same kind.
                int? liveKind = world.depots != null && world.depots.ContainsKey(j.Depot) ? world.depots.get_Item(j.Depot)?.kind : null;
                if (BeltIntake.CacheValid(j.Depot, j.Kind, liveKind)) return j;
                CoreLog.Info($"Belt {belt}: cached depot {j.Depot} (kind {j.Kind}) is now {(liveKind == null ? "gone" : "kind " + liveKind)}; resolving its machine again");
                cache.Remove(belt);
            }

            j = new Joined { End = end, Belt = belt };
            int depot = world.MachineWithPort(end, false);
            var rec = depot >= 0 && world.depots != null && world.depots.ContainsKey(depot) ? world.depots.get_Item(depot) : null;
            var def = rec == null ? null : Remap.Live(rec.kind);
            // No machine record at the end: nothing to cache (HandOff is only called with a machine input there).
            bool keep = rec != null;
            j.Depot = rec == null ? -1 : depot;
            j.Kind = rec?.kind ?? -1;
            if (def != null)
            {
                j.Definition = def;
                var inputs = (MoonStructures.PortsOf(def) ?? new List<PortDefinition>()).Where(p => p != null && !p.Output).ToList();
                var view = UnityEngine.Object.FindFirstObjectByType<MoonDepotConstruction>()?.ViewOf(depot);
                if (view != null)
                {
                    PortDefinition best = null;
                    float bestD = float.MaxValue;
                    foreach (var p in inputs)
                    {
                        float d = Vector3.Distance(view.transform.position + view.transform.rotation * p.Position, end);
                        if (d < bestD) { bestD = d; best = p; }
                    }
                    if (best != null && bestD <= MoonWorld.PortTolerance) j.Port = best;
                    else CoreLog.Once($"handoff-noport:{depot}:{belt}", $"Belt {belt} ends at modded depot {depot} ('{def.Id}') but no input port lies within {MoonWorld.PortTolerance:F2} m of its end (nearest {(best == null ? "none" : $"'{best.Name}' {bestD:F3} m")}); its canisters are left to the game", 1);
                }
                else
                {
                    // No view yet (a load in progress): one input port is unambiguous; otherwise try again next time.
                    if (inputs.Count == 1) j.Port = inputs[0];
                    else keep = false;
                }
                if (j.Port != null) CoreLog.Info($"Belt {belt} feeds '{def.Id}' depot {depot} through its port '{j.Port.Name}' ({j.Port.Cargo})");
            }
            if (keep) cache[belt] = j;
            return j;
        }

        // Both caches are dropped when the world, the number of belts or the number of depots changes.
        static void Fresh(MoonWorld world)
        {
            int belts = world.conveyors?.Count ?? -1, depots = world.depots?.Count ?? -1;
            if (ReferenceEquals(world, cacheWorld) && belts == cacheBelts && depots == cacheDepots) return;
            cache.Clear();
            emitters.Clear();
            RouterHop.Reset(world);
            cacheWorld = world;
            cacheBelts = belts;
            cacheDepots = depots;
        }

        // The machine whose output port an outfeed belt starts at (EmitCargo has no machine argument): MachineWithPort(start,
        // true), cached per belt like the intake side (the cached depot must still exist with the same kind, and the belt
        // start must not have moved). Null when no machine record is there. Definition is null for a vanilla machine.
        internal sealed class Emitter
        {
            internal Vector3 Start;
            internal int Depot = -1, Kind = -1;
            internal StructureDefinition Definition;
        }

        static readonly Dictionary<int, Emitter> emitters = new();

        internal static Emitter EmitterOf(MoonWorld world, MoonConveyorSnapshot outfeed)
        {
            if (world == null || outfeed == null) return null;
            Fresh(world);
            int belt = outfeed.id;
            Vector3 start = outfeed.start;
            if (emitters.TryGetValue(belt, out var e) && e.Start == start)
            {
                int? liveKind = world.depots != null && world.depots.ContainsKey(e.Depot) ? world.depots.get_Item(e.Depot)?.kind : null;
                if (BeltIntake.CacheValid(e.Depot, e.Kind, liveKind)) return e;
                emitters.Remove(belt);
            }
            int depot = world.MachineWithPort(start, true);
            var rec = depot >= 0 && world.depots != null && world.depots.ContainsKey(depot) ? world.depots.get_Item(depot) : null;
            if (rec == null) return null;
            e = new Emitter { Start = start, Depot = depot, Kind = rec.kind, Definition = Remap.Live(rec.kind) };
            emitters[belt] = e;
            if (e.Definition != null) CoreLog.Info($"Belt {belt} carries the output of '{e.Definition.Id}' depot {depot}");
            return e;
        }

        // True when the canister was taken whole (the game then forgets its rider); false when it waits.
        static bool Intake(MoonWorld world, Joined j, int id)
        {
            var d = j.Definition;
            try
            {
                var canisters = world.canisters;
                if (canisters == null || !canisters.ContainsKey(id)) return false;
                var c = canisters.get_Item(id);
                if (c.kilograms <= 0) return false;
                // A router passes any cargo: no slot, no cargo check (RouterHop).
                if (d.Router != null) return RouterHop.Hop(world, d, j.Depot, j.Port, j.Belt, id);
                string cargoId = CargoId(c.cargo);
                var s = world.ProductionOf(j.Depot);
                (int Slot, double Kilograms, string Refusal) r;
                if (!string.Equals(j.Port.Cargo.Id, cargoId, StringComparison.Ordinal)) r = (-1, 0, BeltIntake.NotHere(cargoId));
                else if (d.Processor == null || s?.stores == null || s.stores.Length != d.Processor.Slots.Count) r = (-1, 0, BeltIntake.NotHere(cargoId));
                else
                {
                    var slots = d.Processor.Slots.Select((x, i) => (x.Role, x.Cargo.Id, x.Capacity, s.Stored(i))).ToList();
                    r = BeltIntake.Decide(slots, cargoId, c.kilograms);
                }
                if (r.Refusal != null)
                {
                    Waiting(j, s, c.cargo);
                    CoreLog.Once($"handoff-refused:{j.Depot}:{cargoId}:{r.Refusal}", $"Belt canister {id} ({cargoId}, {c.kilograms} kg) waits at '{d.Id}' depot {j.Depot} port '{j.Port.Name}': {r.Refusal}", 1);
                    return false;
                }

                string owner = string.IsNullOrEmpty(c.owner) ? MoonWorld.LocalPlayer : c.owner;
                bool whole = r.Kilograms >= c.kilograms - 1e-9;
                // The canister goes (or shrinks) first, so a failure can never leave the kilograms both in the slot and on the belt.
                if (whole)
                {
                    world.RemoveCanister(id);
                    if (canisters.ContainsKey(id)) { CoreLog.Once("handoff-remove:" + d.Id, $"RemoveCanister({id}) left the canister in the world; '{d.Id}' did not take it", 1); return false; }
                }
                else
                {
                    var rotation = new Il2CppSystem.Nullable<Quaternion>(c.rotation);
                    var left = new MoonDroppedCanister(c.id, c.owner, c.kilograms - r.Kilograms, c.capacity, c.position, c.up, c.createdAt, rotation, c.cargo);
                    left.physicalPose = c.physicalPose;
                    world.MoveCanisterRecord(left, c.position, c.up, rotation);
                }
                MoonWorld.Put(s.stores[r.Slot], owner, r.Kilograms);
                CoreLog.Debug($"Belt canister {id}: {r.Kilograms} kg {cargoId} into '{d.Id}' depot {j.Depot} slot {r.Slot}{(whole ? "" : " (part; the rest waits)")}");
                MoonStructures.RaiseCargoEntered(new CargoEntered
                {
                    Definition = d, DepotId = j.Depot, Slot = r.Slot, Cargo = new Cargo((byte)c.cargo, cargoId), Kilograms = r.Kilograms, Owner = owner,
                });
                return whole;
            }
            catch (Exception e)
            {
                CoreLog.Once("handoff-intake:" + d.Id, $"Taking belt canister {id} into '{d.Id}' depot {j.Depot} threw; it waits: {e}", 1);
                return false;
            }
        }

        // Vanilla's wording for a refused canister ('A example grit capsule is waiting at the dust input'), through the
        // production state's own Flow(status) setter.
        static void Waiting(Joined j, MoonProductionState s, MoonCargo cargo)
        {
            if (s == null) return;
            try
            {
                if (j.WaitingText == null || j.WaitingCargo != cargo)
                {
                    j.WaitingCargo = cargo;
                    j.WaitingText = $"A {MoonWorld.CargoName(cargo).ToLowerInvariant()} capsule is waiting at the {MoonWorld.CargoShortName(j.Port.Cargo.ToMoon()).ToLowerInvariant()} input";
                }
                s.Flow(j.WaitingText);
            }
            catch (Exception e) { CoreLog.Once("handoff-status:" + j.Depot, $"Setting the waiting status of depot {j.Depot} threw: {e.Message}", 1); }
        }

        internal static string CargoId(MoonCargo cargo) => (byte)cargo switch
        {
            0 => Cargo.Dust.Id,
            1 => Cargo.Sand.Id,
            2 => Cargo.Stone.Id,
            _ => MoonStructures.CargoDefinition(cargo)?.Id ?? "cargo " + (byte)cargo,
        };
    }
}
