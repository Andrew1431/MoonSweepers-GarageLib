# Splitters and mergers: recon notes

How vanilla Moon Sweepers splits and merges belt traffic, and what StructureLib would need for a compact
Satisfactory-style splitter (1 in, 3 out: left / forward / right) and merger (3 in, 1 out). Read with
`STRUCTURES_RECON.md` ("Belts and ports", the donor table in fact (e)). Recon done 2026-11-02 without launching the
game: the interop type shapes, the game's own data files and the M1 belt logs.

Sources:
- **Asset reads.** `MoonSweepers_Data/resources.assets` (Unity 6000.6.0f1, serialized file v23, no type trees),
  parsed by hand with scratch scripts (field order = the interop dump order). The parse is checked against the
  pressure chamber, whose values match the in-game probe exactly (half (4.11, 2.63), height 6.16, reach 22, ports
  `IN` (-2.95, 0.85, 0) / `OUT` (2.95, 0.85, 0) dir +X, prefab `pressure_chamber`).
- **Type dumps.** `research/dump_MoonWorld.txt` (line numbers cited as `MW:<line>`), plus new dumps of `MoonMachineBelt`,
  `MoonConveyorSave`, `MoonConveyorNetwork`, `MoonRaisedMachine` and `MoonDepotConstruction`, written to the session
  scratchpad (not to `research/`).
- **String literals.** Read from `il2cpp_data/Metadata/global-metadata.dat` (metadata v108).
- **Logs.** `.superpowers/sdd/2026-11-02-structurelib-belts-and-models/task-1-logs/run0-names-LogOutput.log`
  (lines 462–468, the probe's port list for kinds 11 and 12).

No method body was disassembled: there is no Cpp2IL or Ghidra on the machine, and the interop assemblies carry no
xref attributes. **Everything said here about vanilla behaviour comes from member names and data, and is marked
(inferred)** where no trace backs it.

## 1. The vanilla definitions

- **Where they live.** They are `MoonDepotBuildDefinition` ScriptableObject assets in `resources.assets`: `splitter`
  (pathID 13985) and `merger` (pathID 13975). The game reaches them through `MoonWorld.depotDefinition
  .additionalStructures[kind - 1]`, served by `MoonWorld.BuildDefinition(kind)` (MW:838). They do not come from
  `MoonPrototypeCatalog`. The build-menu entry is `MoonDepotBuilder.baseStructures` (kind 11 "Splitter", kind 12
  "Merger", category Logistics; card images `splitter` / `merger`, 369×300, `task-1-logs/cards/`).
- **How StructureLib reads one.** `Injection.DefinitionFor` calls `world.BuildDefinition(d.BaseStructure)` and
  `Object.Instantiate`s it (`src/StructureLib/Injection.cs:89-95`). `PortResolver.ReadDonor` copies `halfFootprint`,
  `scale` and every `conveyorPorts[i]` (`name`, `position`, `direction`, `output`) into a `DonorShape`
  (`src/StructureLib/PortDefinition.cs:99-110`). `ApplyPorts` then rewrites the clone's `conveyorPorts`
  (`Injection.cs:151-180`). Kinds 11 and 12 pass `Rules.CheckDonor`, which refuses only foundations, decks, building
  parts, catwalk supports and the world grid (`src/StructureLib/Pure/Rules.cs:104-114`).

Donor rows, in the format of `STRUCTURES_RECON.md` fact (e). These values were read from the asset (not from a
running game), so the "renderer bounds" row is the mesh AABB in prefab space:

| | Splitter (11) | Merger (12) |
|---|---|---|
| `halfFootprint` | (1.23, 1.67) → 2.46 × 3.34 m | (1.23, 1.67) |
| `height` | 1.21 | 1.21 |
| `floorOffset` | 0.02 | 0.02 |
| `scale` | 1.00 | 1.00 |
| `reach` | 16.00 | 16.00 |
| `groundTolerance` | 0.12 | 0.12 |
| `standsOnDust` | **true** (chamber, packager: false) | true |
| `priceDollars` | **5** (packager 100, chamber 500) | 5 |
| `raisedModel` / `raisedBottom` | mesh `splitter_raised` (1.77 MB) / 0.64 | mesh `merger_raised` / 0.64 |
| `skinPrefabs` / `additionalStructures` | none / none | none / none |
| `previewMaterial` | `DepotBuildPreview` (shared by all) | same |
| prefab | `splitter` | `merger` |
| mesh AABB | `SM_splitter.001` (-1.20, 0.00, -1.64)..(1.20, 1.23, 1.64), **189,015 verts**, 6 submeshes | `SM_merger.001`, same AABB, 179,371 verts |
| `conveyorPorts` | `IN` input (0.00, 0.85, -1.60) dir (0, 0, 1); `OUT_A` output (-0.70, 0.85, 1.60) dir (0, 0, 1); `OUT_B` output (0.70, 0.85, 1.60) dir (0, 0, 1) | `IN_A` input (-0.70, 0.85, -1.60) dir (0, 0, 1); `IN_B` input (0.70, 0.85, -1.60) dir (0, 0, 1); `OUT` output (0.00, 0.85, 1.60) dir (0, 0, 1) |

- **Ports.** The port positions match the in-game probe (`[trace] PortCargo(port 'OUT_A' out at (-0.70,0.85,1.60))`,
  run0 line 462). Unlike every production donor, the splitter and merger **flow along local +Z**, not +X. Both
  splitter outputs point forward, side by side: it is a Y-splitter with no side outputs. `PortCargo` answers Dust for
  every one of these ports (by name).
- **Power.** Neither kind is a power node. The recon's kind table gives 11 and 12 no power, processor or solar flag,
  and the objects hold no production view (inferred: `StepProduction` never sees them).
- **Prefab hierarchy** (both prefabs):
  - The root carries 10 `BoxCollider`s and nothing else.
  - `Model` holds `SM_splitter.001`, its MeshRenderer and **`MoonMachineBelt`** (MonoScript pathID 245 in
    `globalgamemanagers.assets`).
  - Under that are the children `Belt slats` (mesh AABB (-1.06, 0.83, -1.60)..(1.06, 0.87, 1.60): an internal belt
    2.12 m wide) and, on the splitter only, `Selector` (local position (0, 0.85, 0.47); mesh (-0.05, 0.02,
    -1.39)..(0.35, 0.17, 0.05): a 1.4 m flap that pivots near the outputs and points back at the input).
- **`MoonMachineBelt` serialized values.**
  - Splitter: `pitch` 0.139, `end` 1.606, `selectorTip` (0.327, -1.373), `selectorSwing` 26.8°.
  - Merger: the same `pitch` and `end`, with `selector` null and swing 0.
  - Its members are `Setup(world, machine)`, `Step(dt)`, `Steer()`, `SelectorAngle`, `target`, `velocity`, a static
    `crossings` list and `CapsuleRadius` (scratch dump). So the selector is animated toward the output the next
    canister takes (inferred).

## 2. How the game routes cargo at a splitter

A splitter or merger is **not a processor**: it has no production state and no slots, and it does not use
`EmitCargo`. It is a **routing machine**. Canisters leave the input belt and ride short internal paths ("routes")
from an input port to an output port, then board the output belt. The members that show this, all on `MoonWorld`:

| Member | Reading (inferred from the name and signature) |
|---|---|
| `static Int32 MachineRoute(machine, input, output)`, `RouteMachine(route)`, `RouteInput(route)`, `RouteOutput(route)`, `static RouteNumberBits` (MW:15, 738-741) | A route id packs depot id, input-port index and output-port index into one int. |
| `MoonConveyorPath MachinePath(route)` (MW:742), `machinePaths : Dictionary<int,(MoonDepotSnapshot, MoonConveyorPath)>` (MW:13) | Each route is a real `MoonConveyorPath` (input port → output port), cached per route. |
| `Rider.belt` (MW:2247), `riders`, `beltQueues`, `RouteFull(route)` (MW:768) | Riders ride routes the way they ride belts; the `belt` field likely holds a route id while inside. |
| `Boolean EnterMachine(MoonDepotSnapshot machine, Int32 input, Int32 id)` (MW:760) | Moves a canister from a belt end onto a route. **Never called in the M1 traces**, which had no splitter (recon fact (a)). |
| `Boolean HandOff(belt, path, id)` (MW:759) | Called every frame for a canister at a belt end that joins a machine input (traced). For a router it most likely calls `EnterMachine` (inferred). |
| `Boolean RouteEntranceClear(machine, input, output)` (MW:773), `EntranceClear(belt)`, `ConnectedOutput(machine, point)` (MW:763) | The skip rule: an output with no belt, or with a blocked entrance, is skipped. |
| `machineTurns : Dictionary<int,int>` (MW:10) | **The per-depot "next output" index** (the splitter's round-robin). |
| `lastInput : Dictionary<int,int>` (MW:25) | The merger's last input served (its alternation). |
| `Boolean GivesWay(machine, def, pose, rotation, input)` (MW:761) | Merger priority: whether this input yields to another one. |
| `Single MergeLimit(route, front, length)` (MW:774) | How far a canister may advance on a merging route, keeping `BeltSpacing` where routes join. |
| `MachineCrossings(id, list)`, `crossingByMachine`, `crossingsFrame/Step` (MW:26-28, 765) | The canisters inside a machine this frame. `MoonMachineBelt` draws and steers from these. |
| `Boolean Enter(belt, id)` (MW:764), `enteredBelts` (MW:30) | Boarding the output belt at a route's end. |
| `static Boolean Routes(MoonDepotBuildDefinition def, Int32 kind)` (MW:1387) | "This structure is a router": the gate for everything above and for cut-in (below). |
| `CutIn(def, kind, aim, choice, out options, out label, out joint)`, `CutBelts`, `CutBelt(plan)` (MW:1396, 1401-1402) | Placing a router onto an existing belt cuts it in two. Literals: "Cuts into the belt: {0} → {1}", "It can't cut in here: {0}", "The belt it cuts into is gone". |
| `StepConveyors(dt)`, `BoardCanisters()` (MW:747-748) | The belt step that drives all of it. |

- **The game's own description** of the feature (metadata literal): "Belts can split and merge to sort the traffic."
  No literal names a splitter status ("blocked", "alternating", ...). Splitters show no status text (inferred).
- **`EmitCargo` picks no outfeed.** `EmitCargo(MoonConveyorSnapshot outfeed, owner, cargo, kg, out reason)`
  (MW:1542) is handed one belt by its caller. For a processor, the caller `EmitProduct(s, spec)` (MW:1541) gets that
  belt from `MoonConveyorSnapshot MachineOutfeed(MoonDepotSnapshot d)` (MW:1487), which returns **one** belt (traced:
  `MachineOutfeed(26/kind 96) -> belt 1`).
- **The garage is vanilla's only multi-output emitter.** It has two outputs (`OUT_A` (-3.19, 0.85, 3.70), `OUT_B`
  (-1.91, 0.85, 3.70)), a separate `GarageOutfeeds(d, List belts)` (MW:1285) and a per-garage
  `garageSiloTurn : Dictionary<int,int>` (MW:354). So vanilla's own round-robin over output belts exists, but for the
  garage only (inferred).

## 3. Several ports on a StructureLib structure today

> Settled by the spike: `Routes(def, kind)` reads the definition, so a clone of 11 or 12 answers True (see "Spike
> facts (Task 1)" (a)); the last bullet's "probably false" below is wrong.

- **Outputs: refused at freeze.** `PortRules.CheckPorts` adds "N output ports; a structure has at most one"
  (`src/StructureLib/Pure/PortRules.cs:45-47`). It also needs every output's cargo to be the single Output slot's
  (lines 39-42), so a processor-less structure cannot declare an output at all.
- **Inputs on a processor-less structure: refused.** An input port needs an Input slot of its cargo (lines 34-37).
  `README.md` and the recon say the same: "A structure without a processor cannot declare input ports".
  - **A clone that keeps its donor's ports bypasses the check.** With `Ports` null, the ports come from
    `PortRules.ChooseSource` → `Donor`, and `FromDonor` with no processor keeps them all
    (`PortDefinition.cs:75-97`). Those ports are never checked.
  - But `BeltPatches.HandOff` intercepts **every** belt that ends at a modded depot: `Resolve` finds a port, then
    `Intake` refuses because `d.Processor == null` (`src/StructureLib/Patches/BeltPatches.cs:47-61, 177`). So a plain
    clone of kind 11 refuses every canister, and its belts stall.
- **What vanilla would do with several clone outputs** (inferred). `EmitProduct` only ever emits to `MachineOutfeed`'s
  single belt. A processor with several outputs would feed one of them (most likely the first output port with a belt
  joined) and never the others. `ApplyPorts` writes them all (`Injection.cs:162-173`), so the belt tool would still
  join a belt to each.
- **Several inputs: work today.** `BeltPatches.Resolve` matches the belt end to the **nearest declared input port**
  within `PortTolerance` 0.05 m, cached per belt (`BeltPatches.cs:92-104`). Each input belt gets its own `HandOff`
  calls. The stone press's two vanilla inputs show the game joins one belt per input port.
- **The vanilla router path never runs for a modded kind** (inferred). `Routes(def, kind)` is not in the `Remap`
  patches (`src/StructureLib/Patches/DefinitionPatches.cs:44-67` remaps only HexPieceKind, IsEdgeRail, IsWorld*,
  IsRobotDock, MapKindOf and FarSpec). So a clone of 11 or 12 gets vanilla's answer for its hashed kind, probably
  false.

## 4. Hooks

- **A canister at a belt end joined to a structure port.**
  - `MoonWorld.HandOff(int belt, MoonConveyorPath path, int id)` runs every frame until it answers true. This is
    traced, and it is our prefix (`BeltPatches.cs:47`). Answering false leaves the canister waiting
    (`WaitingAtEnd(belt)` true) and queues later ones at `BeltSpacing`.
  - `HandOff` is also called for a vanilla splitter's input (inferred: the input is a machine input that
    `FindMachineInput` / `MachineInputAt` resolve).
  - The prefix leaves vanilla kinds alone (`Remap.Live` guard via `Resolve` → `Definition`).
- **Putting a canister on a chosen belt.**
  - `EmitCargo(outfeed, owner, cargo, kg, out reason)`: proven for modded cargo, makes a **new** canister at the
    belt's start. It answers false while `EntranceClear` fails (fact (b)).
  - `PlaceRider(id, path, distance)` and `MoveCanisterRecord` (MW:753, 757) could move the **same** canister onto
    another belt. Not tested: `MoveCanisterRecord` onto a belt point boarded within 2 s in fact (c).
- **Finding the belt at a port.** `BeltStartingAt(point)` / `BeltEndingAt(point)` (MW:776-777, traced for inputs),
  `MachineWithPort(point, output)`. The port's world point is the view pose × the local port position
  (`BeltPatches.cs:100`).
- **Vanilla splitter state.** Only the transient dictionaries `machineTurns` (next output), `lastInput` and
  `crossingByMachine`. **None is saved**: no saver or snapshot type has such a member (`MoonDepotSnapshot`,
  `MoonConveyorSnapshot`, `MoonProductionSnapshot`), so the round-robin restarts after a load (inferred).
- **No both-direction port, and no pass-through port.**
  - Both port types carry a single `bool output`: `MoonDepotBuildDefinition/ConveyorPort`, and `MoonWorld/ConveyorPort`
    { position, direction, output, structure, belt, occupied, label }.
  - The builder filters ports through `MoonDepotBuilder.Compatible(ConveyorPort)`: a belt start needs an output, a
    belt end an input (inferred).
  - The splitter and merger are vanilla's pass-through structures, with separate in and out ports. Their extra trick
    is **cut-in**: drop one on a belt and that belt becomes two (`CutIn` / `CutBelts`).

## 5. Size

- **Why the vanilla splitter is large.**
  - Its footprint is 2.46 × 3.34 m (`halfFootprint` (1.23, 1.67)).
  - Its ports are **3.2 m apart along the flow** (z ±1.6), and its two outputs are **1.4 m apart side by side**,
    both facing forward.
  - The internal belt is 2.12 m wide (two lanes) and needs room for the 1.4 m selector flap.
  - Because both outputs run parallel to the input, each output belt has to curve away before it can go anywhere,
    which adds metres beyond the box.
  - The mesh is heavy (189k vertices, plus a 1.77 MB `splitter_raised` variant).
- **Belt numbers.** The kit asset `ConveyorKit` (pathID 13794) gives these pieces:
  - Tread `Conveyor tread`: 0.70 m wide (x ±0.35).
  - Coupler and endcap: 0.98 m wide (x ±0.49). Saddle: 0.99 m. Feet: 0.92 m.
  - **A belt is about 1.0 m wide overall.**
  - Kit values: `treadPitch` 0.14, `jointSpacing` 2.15, `deckHeight` 0.85, `minLength` 0.75, `minRadius` 0.49,
    `maxSlope` 45.
  - World statics: `BeltSpacing` 0.75 m, `BeltSpeed` 1.1 m/s (at most ≈1.47 canisters/s per belt), `PortTolerance`
    0.05, `PortReach` 1.2.
- **Minimum plausible box for 1 in / 3 out** (left / forward / right), one belt wide.
  - Ports at the face centres of a square of side s: `In` (0, 0.85, -s/2) dir +Z; `Fwd` (0, 0.85, s/2) dir +Z;
    `Left` (-s/2, 0.85, 0) dir -X; `Right` (s/2, 0.85, 0) dir +X.
  - Each side belt starts at a face and sweeps ±0.5 m about the port. So s ≥ 1.0 keeps the three outgoing belts' frames
    from overlapping one another and the input belt. Same rule with `minLength` 0.75 and `minRadius` 0.49 (a side belt
    can turn right away).
  - **Recommended: s = 1.2–1.5 m** (`halfFootprint` ≈ (0.6–0.75, 0.6–0.75)).
  - Below that the ports are within `PortReach` 1.2 of each other. The belt tool then picks by nearest
    `MachineSnapOption` score, which is untested on ports this close.
  - Nothing found sets a minimum port spacing (only `PortTolerance` for matching and `MachineSnapReach` for snapping).
    The vanilla spacing (1.4 m) is the tightest one seen.
  - Ports must keep y 0.85 (`deckHeight`).

## 6. Save and load

- **`MoonConveyorSave`** (`MaxBelts`, `Write`/`Read`, scratch dump) writes belts as endpoints only. A belt between a
  router and a machine saves like any other. StructureLib already restores belts between modded depots (recon Task 4).
- **`MoonDepotSave`** writes the depot record. `MoonProductionSave` has nothing for 11 or 12 (they have no production
  state).
- **Canisters inside a vanilla splitter** ride routes. They are ordinary `canisters` records with positions inside the
  box. Riders are not saved: `BoardCanisters` re-boards loose canisters near belts after a load. Whether it puts them
  back on a *route* is unknown (inferred: they land on the internal slats or drop to `CapsuleRest`).
- **What our sidecar would need:**
  - Design B (below) keeps no canister inside: nothing beyond today's depot record. Round-robin indices may stay
    transient, as vanilla's are.
  - Design A (clone of 11 or 12) would have canisters on the routes of a depot that the sidecar strips from the vanilla
    save and re-injects after `RestoreDepots`. Those canisters' positions would then point inside a depot the vanilla
    read never saw: the same class of problem as the pose-save bug (recon "Per-depot savers audit").

## 7. Three cheapest viable designs

**B. Library "router" structures on the `HandOff` prefix (recommended).**
- **How it works.**
  - A new `StructureDefinition.Router` (Splitter or Merger) lets `PortRules` accept several outputs and cargo-agnostic
    inputs when no processor is declared.
  - `BeltPatches.Intake` branches for routers. Splitter: starting at the depot's `nextOut`, find the first output port
    whose belt exists and has `EntranceClear(belt)`. If there is one, `EmitCargo(that belt, owner, cargo, kilograms)`
    then `RemoveCanister(id)`, advance `nextOut`, and answer true. Otherwise answer false (the canister waits).
  - Merger: per depot, a turn index over its inputs. An input's `HandOff` is accepted only when it is that input's turn
    among the inputs whose belts have `WaitingAtEnd` true, and the output's entrance is clear.
  - Donor: any small vanilla kind without production (the solar post 93, as the burner does, or the packager 10),
    with a custom `.bin` box and the four ports.
- **Inherits for free.**
  - Belt joining, snapping, labels (`PortLabel` "… input/output").
  - Canister queuing and waiting at belt ends, and spacing on the output belt (`EmitCargo` refuses until the entrance
    is clear).
  - Modded cargo (proven), the save of belts and of riders on them, and the existing sidecar.
- **Loses.**
  - Cut-in placement.
  - Canisters do not visibly cross the box: they vanish at the input end and appear at the output start. In a 1.2 m box
    that is hardly visible.
  - The canister id changes (a new id per hop), which the sidecar already handles for modded cargo.
  - `EmitCargo` takes `kg` and makes capacity-1 canisters (fact (b)): a canister over 1 kg (a partial or packager
    capsule) needs a check.
- **Risk: low.** Only hooks already proven in game: the `HandOff` prefix, `EmitCargo`, `RemoveCanister`,
  `EntranceClear` (untested call, public). Throughput is bounded by `HandOff` per frame and `BeltSpacing`, about the
  belt's 1.47/s.

**C. As B, but move the same canister** with `PlaceRider(id, outPath, 0)` or `MoveCanisterRecord` to the output
belt's start instead of remove-and-emit.
- **Inherits:** all of B, plus the canister keeps its id, owner and kilograms (no capacity-1 rewrite, no sidecar
  churn).
- **Risk: medium.** `HandOff` answering true after our own `PlaceRider` may make the game's rider bookkeeping
  (`riders`, `beltQueues`, `leftBelts`) drop or double the rider. The game's own removal path is the only one proven
  (Task 3).

**A. Clone kind 11 or 12** with a smaller `halfFootprint`, a custom model and three outputs, and let vanilla route.
- **What it needs.**
  - `Routes` (and whatever else checks kind 11/12 natively: `EnterMachine`, `MachineRoute`, `CutIn`) remapped to the
    donor.
  - `BeltPatches` told to leave router clones to vanilla.
  - A model that keeps `MoonMachineBelt` with `Belt slats` and `Selector` children (or a mesh with none).
- **Would inherit:** cut-in onto belts, the selector animation, vanilla's round-robin with blocked-output skipping,
  merge spacing (`MergeLimit`, `GivesWay`), and visible canisters crossing.
- **Risk: high.**
  - Every native kind check is invisible (`HandOff` refused a modded processor "by kind", fact (a)).
  - `Routes(def, kind)` is a small static leaf like `IsProcessor`, whose prefix crashes the game at world load
    (recon "Interop hazards found"). `PortCargo`, also static, was safe, so only a trial can tell.
  - Whether routing supports **three** outputs, side-facing ports, or a 1.2 m path is unknown: `selectorSwing`
    26.8° and `MachinePath` were built for two forward lanes 3.2 m long.
  - Routes hold canisters inside a depot the sidecar strips (section 6).

**Rejected: a processor with an instant cycle and an `EmitCargo` prefix that rotates `outfeed`.** Processor slots
are per cargo, so a splitter would need a slot for every cargo, and `FinishCycle` moves into one output slot of one
cargo. `EmitProduct` runs every 0.35 s and emits about one canister per 0.7 s, and `CheckPorts` forbids the outputs
anyway. It is no cheaper than B and worse in every way.

## Open questions

1. Does vanilla `HandOff` call `EnterMachine` for a splitter's input, and does `Routes(def, kind)` read the kind or
   the definition? (Settled for `Routes` by the spike: it reads the definition, "Spike facts (Task 1)" (a).) Settle it with a trace of `HandOff` / `EnterMachine` / `RouteEntranceClear` on a built vanilla
   splitter, plus `Routes` answers for 11, 12 and a clone. This decides whether A is possible at all.
2. Vanilla splitter rule: strict alternation through `machineTurns`, or skipping blocked or unconnected outputs?
   (Trace `machineTurns` with one output belt full.) The merger: what `GivesWay` prefers.
3. Is `EntranceClear(belt)` safe to call from a `HandOff` prefix, and does `EmitCargo` accept `kg` > 1 for a packed
   capsule?
4. Does the interactive belt tool snap reliably to ports 0.6–0.75 m from each other on three faces? (Only `AutoBelt`
   has built belts to modded ports so far, README "Ports": untested by hand.)
5. Where do canisters inside a vanilla splitter go after save and reload (`BoardCanisters` onto routes, or drop)?
6. Can a modded router support cut-in (prefix `CutIn` / `Routes` for our kinds) without design A's native
   dependencies?

## Spike facts (Task 1)

Traced in game on slot-2 (restored from `.scratch/slot-2-backup-task11` before every run), 2026-11-02, with the dev
switches `[Dev] RegisterBoxProbe`, `HopProbe`, `PrefixProbe` (the last two removed in Task 3, when the router hop replaced them; `src/StructureLib/DevBelts.cs` `BoxSpike`; the probe
structure `ryanhartwig.example-box-probe` is a clone of kind 11 with no processor and `Ports` null, registered by
`examples/ExampleStructure/Plugin.cs` behind the flag). Logs: `.superpowers/sdd/2026-11-02-belt-boxes/task-1-logs/`
(`run1-donors`, `run2-sidebelts`, `run3-probe-dust`, `run4a`/`run4b`/`run4c` emit, `run5-place`). AutoBuild gained two
dev extensions for this: `<id>#<n>` in AutoBelt endpoints (the n-th AutoBuild entry of an id named more than once; a
repeated entry now gets its own depot) and `<entry>@<yaw>` in AutoBuild (TryBuildDepot's yaw, degrees). No native
crash in any run; no exception from any probe call.

**(a) Donor: the floodlight, kind 4.** Prefab components (distinct types; every component under the prefab asset,
inactive included) and `MoonWorld.Routes(def, kind)` (called, never patched):

| Kind | `halfFootprint` | Components | Colliders | Game scripts | `Routes` |
|---|---|---|---|---|---|
| 4 Floodlight | (0.73, 0.73) | BoxCollider, MeshFilter ×6, MeshRenderer ×6, Transform ×7 | 1 | none | False |
| 10 Packager | (1.41, 1.55) | BoxCollider ×2, MeshFilter ×23, MeshRenderer ×23, MoonPackagerView, Transform ×24 | 2 | `MoonPackagerView` on the root | False |
| 11 Splitter | (1.23, 1.67) | BoxCollider ×10, MeshFilter ×3, MeshRenderer ×3, MoonMachineBelt, Transform ×5 | 10 | `MoonMachineBelt` on `Model/SM_splitter.001` | True |
| 12 Merger | (1.23, 1.67) | BoxCollider ×10, MeshFilter ×2, MeshRenderer ×2, MoonMachineBelt, Transform ×3 | 10 | `MoonMachineBelt` on `Model` | True |
| 93 Solar post | (0.91, 0.64) | BoxCollider ×3, MeshFilter ×10, MeshRenderer ×10, MoonSolarPanelView, Transform ×15 | 3 | `MoonSolarPanelView` on the root | False |
| probe (clone of 11, kind 1441920781) | (1.23, 1.67) | as 11 | 10 | `MoonMachineBelt` on `Model/SM_splitter.001`, enabled and active on the built view | **True** |

    [spike] donor 1441920781 'Box probe': halfFootprint (1.23,1.67) price 5 ports 3 Routes(def, 1441920781) True
    [spike] donor 4 prefab 'floodlight': 20 components, 1 colliders; types: BoxCollider, MeshFilter x6, MeshRenderer x6, Transform x7; game scripts: none
    [spike] probe depot 27: MoonMachineBelt present x1 on 'SM_splitter.001' (enabled True, active True); ports 'IN' in dust at (-0.00,0.85,-1.60) ...

- **`Routes` reads the definition, not the kind**: it answers True for a hashed kind it has never seen. So a clone of 11
  or 12 *is* a vanilla router as far as `Routes` goes, and cut-in and the routing path stay gated on for it (open
  question 1). `CutIn` itself was not called, because it has an `out Vector3` (the interop hazard rule). That rules 11
  out by the spec's own test ("no vanilla routing"). It also carries `MoonMachineBelt` and ten root colliders.
- The packager carries a `MoonPackagerView` (a stray view) and the solar post a `MoonSolarPanelView`. **The floodlight
  is the only candidate with no game script, one collider and `Routes` False.** Its footprint is 1.46 m square, close to
  the box's 1.4 m, and the example lamp already clones it.
- The probe's runtime ports keep the splitter's names and +Z flow (`'Box probe input'`, `'Box probe output A'`,
  `'Box probe output B'`). `MachineJoins(kind, port)` answers True for each free port, and False for the input once a
  belt has joined it.
- **A canister at the probe's input waits, and nothing routes it.** In run3 the probe stood at yaw 90 behind a pressure
  chamber (`96,ryanhartwig.example-box-probe@90`, belt 1, 2.53 m). A dust canister put at the belt's end was refused by
  our prefix (`Belt canister 8 (dust, 1 kg) waits at 'ryanhartwig.example-box-probe' depot 27 port 'IN': dust does not
  go in here`) and stayed put for 11 s: `rider belt 1 d 2.53 moving False`, `WaitingAtEnd(1) True`, 1.82 m from the
  depot centre at every 2 s sample. `machineTurns`, `lastInput` and `crossingByMachine` never held the depot. There was
  no `EnterMachine`-like move (the prefix answers first, so vanilla's router branch never ran).

**(b) `EmitCargo(outfeed, owner, cargo, 3.0)` keeps the kilograms but sets capacity 1. `MoveCanisterRecord` fixes the
capacity with the rider intact.** Run4c used `96,grinder,burner`: belt A 1 = chamber → grinder `Dust in`, belt B 2 =
grinder `Grit out` → burner `Grit in`, with belt B emptied first (see below). Inside the `HandOff` prefix, for a 3 kg /
capacity 3 grit canister waiting at A's end:

    [spike] hop emit: EmitCargo(belt 2, owner local, cargo 250, 3.0) -> True reason ''; new canister 19: canister 19 3.00 kg / capacity 1.00, cargo 250, at (28.52,1992.86,-35.75), rider belt 2 d 0.00 moving False
    [spike] hop emit: after MoveCanisterRecord(3 kg, capacity 3): canister 19 3.00 kg / capacity 3.00, cargo 250, at (28.52,1992.86,-35.75), rider belt 2 d 0.00 moving False
    [spike] hop emit: RemoveCanister(18) -> present False; riders A 1: none; riders B 2: [19 cargo 250 d 0.00 moving False]; queue A 1 entry; queue B 0 entries; leftBelts []; answering true
    [spike] hop t+0.5 s: canister 18 gone; new canister 19 3.00 kg / capacity 3.00, cargo 250, at (29.07,1992.86,-35.75), rider belt 2 d 0.55 moving True; riders A 1: none; ... queue A 0 entries; queue B 1 entry; leftBelts []
    [v] Belt canister 19: 3 kg ryanhartwig.example-grit into 'ryanhartwig.example-burner' depot 28 slot 0

- The new canister has a rider on B at d 0 at once and rides at belt speed. The burner took all 3 kg at B's end. A's
  queue drops its entry by the next step: no stale rider or queue entry is left on A.
- **A full output belt refuses.** With B holding five canisters (the self-test's `belt-save-clean` pins one at its
  end), `EmitCargo` answered `False reason 'The grit output belt is full'` 300 times in a row, and `EntranceClear(2)`
  was False (run4b). The canister kept waiting and nothing was lost. So the router must check `EntranceClear` (or
  `EmitCargo`'s answer) before `RemoveCanister`, as the spec says.
- Interop note: `beltQueues` entries are `ValueTuple<float,int>`. Enumerated from managed code they read back as
  garbage (`-803349120@-10598219776.00`), so only their count was logged. The counts track the riders.

**(c) `PlaceRider(id, BeltPath(B), 0f)` from the prefix does not move the rider: the canister snaps back to A.** Run5
used the same chain. The signature is `Void PlaceRider(Int32 id, MoonConveyorPath path, Single distance)`, as dumped.

    [spike] hop place: PlaceRider(18, BeltPath(2), 0f) returned; canister 18 3.00 kg / capacity 3.00, cargo 250, at (28.52,1992.86,-35.75), rider belt 1 d 3.61 moving False; riders A 1: [18 cargo 250 d 3.61 moving False]; riders B 2: none; queue A 1 entry; queue B 0 entries; leftBelts []; answering true
    [spike] hop: HandOff called again for canister 18 on belt 1 after the hop (phase 2); canister 18 3.00 kg / capacity 3.00, cargo 250, at (28.52,1992.86,-35.75), rider belt 1 d 3.61 moving False
    [spike] hop t+0.5 s: canister 18 3.00 kg / capacity 3.00, cargo 250, at (22.62,1992.86,-35.75), rider belt 1 d 3.61 moving False; riders A 1: [18 cargo 250 d 3.61 moving False]; riders B 2: [19 cargo 250 d 0.48 moving True]; queue A 1 entry; queue B 1 entry; leftBelts []
    [spike] hop t+10.0 s: canister 18 3.00 kg / capacity 3.00, cargo 250, at (22.62,1992.86,-35.75), rider belt 1 d 3.61 moving False; riders A 1: [18 cargo 250 d 3.61 moving False]; riders B 2: none; queue A 1 entry; queue B 0 entries; leftBelts []

- `PlaceRider` moved only the canister record's position to B's start. The rider stayed `belt 1 d 3.61`.
- Answering true did not drop the rider. `HandOff` was called again the next frame for the same canister on A, the
  canister went back to A's end (22.62), and it waited there (the grinder refuses grit) for the whole 10 s.
- B never had a rider for it (canister 19 on B is the grinder's own output). There was one rider record throughout and
  no loss, but no hop either.
- **Mechanism for Tasks 3–6: spec option (2).** The hop is:
  1. `EmitCargo(output belt, owner, cargo, kg)`;
  2. `MoveCanisterRecord` of the new canister with the arriving one's kilograms and capacity (and owner);
  3. `RemoveCanister(arriving)`;
  4. answer true.

  The canister id changes on every hop.

**(d) The four calls are safe from the `HandOff` prefix.** With `PrefixProbe` on, these calls ran on every prefix call
over four world loads (run3, run4a, run4b, run4c), with no throw and no native crash (`PrefixProbe: 51024 prefix calls
so far, 0 threw, 2 belt(s) seen` is an intermediate tick of run4a; its last was 57,296 calls, 0 threw):
- `EntranceClear(belt)`, `WaitingAtEnd(belt)`, `BeltStartingAt(start)` and `BeltEndingAt(end)`;
- per output port of the machine, `BeltStartingAt(port point)` and `EntranceClear` of that belt.

The answers:

    [spike] PrefixProbe belt 1 -> 'ryanhartwig.example-box-probe' depot 27, canister 8: EntranceClear True WaitingAtEnd True BeltStartingAt(start) 1 BeltEndingAt(end) 1; output ports ['OUT_A' BeltStartingAt null; 'OUT_B' BeltStartingAt null]
    [spike] PrefixProbe belt 1 -> 'ryanhartwig.example-grinder' depot 27, canister 8: EntranceClear True WaitingAtEnd True BeltStartingAt(start) 1 BeltEndingAt(end) 1; output ports ['Grit out' BeltStartingAt 2 EntranceClear True]
    [spike] PrefixProbe belt 2 -> 'ryanhartwig.example-burner' depot 28, canister 12: EntranceClear False WaitingAtEnd True BeltStartingAt(start) 2 BeltEndingAt(end) 2; output ports []

- `WaitingAtEnd(belt)` is already True on the first `HandOff` call for a canister.
- `BeltStartingAt(port world point)` finds the belt on an output port (the port's local position under the view's
  pose), and answers null on a free port.
- `EntranceClear` of the incoming belt itself can be False (belt 2, a 2.47 m belt full behind the waiting canister).

**(e) A U-shaped belt between +Z side ports of neighbouring structures does not build at the line spacing.** Run2 used
`AutoBuild = probe,probe` on the line: centres 3.76 m apart along +X, 1.3 m between footprints, both at yaw 0, so each
port flows +Z.

    [dev] AutoBelt 'ryanhartwig.example-box-probe#1:OUT_A->ryanhartwig.example-box-probe#2:IN': route +start,+end (unset ids -1) valid=False routable=True length=6.86 reason='Turn too tight: give it more room.'
    [dev] AutoBelt 'ryanhartwig.example-box-probe#1:OUT_B->ryanhartwig.example-box-probe#2:IN': route +start,+end (unset ids -1) valid=False routable=True length=5.56 reason='Turn too tight: give it more room.'

- `routable` was True for every direction pairing, but `valid` never was:
  - as declared (+start, +end): 'Turn too tight', length 6.86 for OUT_A (4.46 m across) and 5.56 for OUT_B (3.06 m
    across);
  - the reversed pairings: for OUT_A 'Blocked by a structure' (lengths 6.04–7.03); for OUT_B 'Turn too tight' with
    one direction reversed (5.60) and 'Blocked by a structure' with both reversed (4.74).
- Run1 failed the same way (`96,probe` at yaw 0, the chamber's +X output into the probe's +Z input; length 4.70,
  'Turn too tight').
- The same probe at yaw 90 behind the chamber (run3) built a straight 2.53 m belt on the first route
  (`valid=True routable=True length=2.53`).
- So the belt tool needs room for a 180° turn. In the self-test, place a side output's target off the line (or add
  spacing), or rotate the target so the belt runs straight.

## Self-test facts (Task 6)

From the BeltBoxes self-test runs (`.superpowers/sdd/2026-11-02-belt-boxes/task-6-logs/`, `src/StructureLib/DevBoxes.cs`):
- Side belts: with 5 m between the two boxes' footprints (`[Dev] AutoBuildGap = 1.3,1.3,5,1.3`), both side-to-side
  U-belts (splitter Left to merger Left, Right to Right) built on the first route, 9.81 m each; the straight Forward to
  Back belt was 5.00 m. A single 5 m gap for every neighbour would put the line's ends past the build reach.
- `TryRemoveConveyor(walker, id, out reason)` removes a belt ("Belt removed."). Its riders become loose canisters lying
  in its path, and a new belt along that path is refused ("Blocked by an object.") until they are gone. Removing them
  (`RemoveCanister`) lets the rebuild through.
- `RemoveStructureNow` on a router keeps every belt joined to it. The canister waiting at the end of a belt into the
  removed router stays there (rider, not moving). The game wrote a save within 3 s of the removal without being asked.
  The belts then save and reload ending (or starting) at nothing, with their canisters.
- After that reload, a test of `MachineWithPort(end, false) >= 0` took every open end for joined (the first reload
  attempt released nothing); checked against `world.depots` instead, all three were open (the raw answer was not
  logged). Taking the canister off the end of each open belt out of the splitter let the queue move up and the
  splitter hop within 0.2 s.
- A 3 kg grit canister hopped whole: `CargoRouted` 3 kg, and the new canister held 3 kg at capacity 3 (the rewrite
  after `EmitCargo` worked).
- A sand canister at the end of the merger's output belt holds the merger (the burner refuses sand). Every input belt
  then fills, and once the sand is removed the next six hops came Right, Back, Left, Right, Back, Left (turn order
  from wherever the turn stood).
