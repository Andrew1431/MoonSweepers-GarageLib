# Structures, production, cargo and power: recon notes

How vanilla Moon Sweepers defines, lists, builds, runs, powers and saves structures. Source: the interop dumps in
this folder (`tools/dump-types.ps1`) and the runtime probe in `spike-structures/` (menu-time dump 2026-11-01, game
build with metadata v108, BepInEx be.791 + ModEngine 1.0.2). Read with `VEHICLE_CORE_PLAN.md`: the same patterns
apply, and most of the choke points are static per-kind switches on `MoonWorld`.

## Identity: a structure is an `int` kind

Everywhere (catalog, placement, built records, production, power, save), a structure type is an `int kind`.

| Kind | Name | Catalog category | Notes |
|---|---|---|---|
| 10 | Packager | Logistics | `PackagerKind` |
| 11 | Splitter | Logistics | |
| 12 | Merger | Logistics | |
| 19–72 | hex foundation pieces | Structures | `HexPieceKind(k)` true |
| 83, 84, 87, 88 | hex roof / upper floor / stairwell floor | Structures | hex pieces too |
| 85, 86, 113 | wall corner / three-way / four-way | Structures | |
| 91, 92 | Robot vacuum Mk I / Mk II | Services | `RobotDockKind`, `RobotDockMk2Kind` |
| 93, 94, 95 | Solar post (S) / twin (M) / tracker (L) | Power | `IsSolarPanel`, `IsPowerNode`, `PowerRange` 16 / 24 / 34 |
| 96 | Pressure chamber | Production | `IsProcessor` (cannot be prefixed, see Interop hazards), `IsPowerNode`, `Processor(k)` = `ChamberSpec` |
| 97 | Stone press | Production | `IsProcessor` (cannot be prefixed, see Interop hazards), `IsPowerNode`, `Processor(k)` = `PressSpec` |
| 98 | Garage | Services | `GarageKind` (the vehicle garage GarageLib hooks) |
| 99 | J.O.O.N.A.S. | Services | `JoonasKind` |
| 100 | Capsule catcher | Services | `CapsuleCatcherKind` |
| 101 | platform foundation | Structures | `PlatformFoundation` |
| 110, 111, 112 | square roof / upper floor / stairwell floor | Structures | |
| 1000 | Conveyor belt | Logistics | `MoonDepotBuilder.ConveyorKind`; belts are their own records, not depots |

- `MoonDepotSave.MaxStructureKind = 192`. Kinds 0–9, 13–18, 73–82, 89–90, 102–109 and 114–192 answer false to every
  per-kind query at menu time. Whether the save reader rejects a kind above 192 is a world-time question (see below).
- Per-kind answers are **static switches** on `MoonWorld`: `IsSolarPanel`, `IsProcessor`, `IsPowerNode`,
  `HexPieceKind`, `PowerRange`, `Processor`, `TryPanel`, `InputSlot`, `IsRobotDock`, `IsEdgeRail`, `IsWorldHex`,
  `IsWorldSquare`, `IsWorldGrid` (the dump shows `IsEdgeRail`, `IsWorldHex`, `IsWorldSquare`, `IsWorldGrid` as
  instance methods). None reads a table a mod could grow. All are Harmony-prefixable **except `IsProcessor`**: a prefix
  on it crashes the game natively at world load (Task 5; see Interop hazards).
- `MoonWorld.BuildDefinition(int kind)` (instance) returns the `MoonDepotBuildDefinition` for a kind. No
  `MoonDepotBuildDefinition` or `MoonConveyorKit` asset is loaded at menu time (`Resources.FindObjectsOfTypeAll`
  finds 0), so the definition table arrives with the world. `MoonWorld.depotDefinition` is the root asset and it has an
  `additionalStructures[]` array of further definitions, which is the likely kind table.

## Definition (`MoonDepotBuildDefinition`, a ScriptableObject)

`prefab`, `previewMaterial`, `priceDollars`, `scale`, `halfFootprint`, `floorOffset`, `height`, `reach`,
`groundTolerance`, `standsOnDust`, `raisedModel`, `raisedBottom`, `skinPrefabs[]`, `displayName`,
`additionalStructures[]`, `conveyorPorts[]` (`name`, `position`, `direction`, `output`), `module`
(`MoonFoundationModule`: footprint polygon, shape enum, ports, catwalk/deck/wall data for the foundation kit).

## Build menu (`MoonDepotBuilder`)

- `static CatalogEntry[] baseStructures` (14 entries: `kind`, `name`, `category`, `image`) is the catalog. The
  instance copies it into `structureEntries`; `structures` filters it. Static arrays grow through interop, as with the
  vehicle catalog.
- `static string[] catalogCategories` = Recent, Services, Logistics, Production, Power, Structures.
  `structureSections` = Foundations, Railings, Walls, Floors, Roofs, Catwalks (the Structures tab's fold-out sections).
- Cards: `BuildCard(entry)`, `CardArt(image)` looks sprites up by the entry's `image` string (`catalogArt` dictionary).
  `ShowDetails(kind)`, `ProductionNote(kind)`, `ShowPrice(..., kind, ...)`, `CanBuild(kind)`.
- Placement: `BeginPlacement` / `BeginPlacementFromShop(kind)` → `MakeGhost(def, kind)` → `MoonWorld.PlanStructure`
  / `SnapMachine` / `PlanBuildingPart` → `DepotPlacementCheck` (sliced coroutine) → `TryBuildDepot(builder, direction,
  yaw, out reason, check, kind)`.

## Built records and save

- A built structure is a `MoonDepotSnapshot`: `id`, `kind`, `owner`, `x y z`, `yaw`, `placedPose` + quaternion,
  `support` (the deck it stands on), `stored`, `vehicles`, `style`, `contributions`. Kept in `MoonWorld.depots`
  (`Dictionary<int, MoonDepotSnapshot>`), `nextDepotId`.
- Save: `MoonLooseDustSnapshot.depots` / `nextDepotId` via `MoonDepotSave.Write/Read(r, state, version)`;
  `MoonWorld.SnapshotDepots` / `RestoreDepots`. Conveyors: `conveyors` / `MoonConveyorSave` (`MaxBelts` 20000).
  Production: `production` list of `MoonProductionSnapshot` (`id`, `progress`, `running`, `charge`, `stores` as
  per-slot owner shares) via `MoonProductionSave` (`MaxSlots` 8, `MaxShares` 256). Anchors (`anchors`) are more
  `MoonDepotSnapshot`s. Foundation style is a world-level int.
- The same snapshot object GarageLib already strips owned vehicles from, so a structure sidecar can hook
  `MoonDepotSave.Write` / `MoonProductionSave.Write` prefixes and `RestoreDepots` / `RestoreProduction` prefixes the
  same way.
- **Canisters (Task 9, traced in game on slot-2, 2026-11-01).** There is no `MoonCanisterSave`. The save runs on a
  worker thread: `MoonProgressStore.Write` → `Serialize(states, next, dust)`, which writes the core sections itself,
  `canisters` / `nextCanisterId` among them (the stream is already past them when the first per-section saver starts),
  then calls the per-section savers in this order: `MoonDepotSave`, `MoonLoadoutSave`, `MoonVehicleStorageSave`,
  `MoonConveyorSave`, `MoonStructurePoseSave`, `MoonToolContainerSave`, `MoonUpgradeTreeSave`,
  `MoonStructureAnchorSave`, `MoonRobotSettingsSave`, `MoonPlayerRecordSave`, `MoonProductionSave`, `MoonScoreSave`,
  `MoonGarageSave`, `MoonRadioSave`, `MoonRegionTouchSave`, `MoonWaypointSave`, `MoonOwnedVehicleSave`,
  `MoonLandmarkSave` (each `Write(w, state)`). Then the same thread reads the bytes back with the matching `Read`s. On
  load, `canisters` is already filled before `MoonDepotSave.Read` starts, so it is read inline by the core reader too.
- `MoonCanisterSnapshot` has no cargo. A canister's cargo is in `MoonLooseDustSnapshot.cargo`
  (`List<MoonCargoSnapshot>`: `id` = canister id, `byte cargo`), one entry per non-dust canister, filled by
  **`SnapshotProduction`** (not `SnapshotCanisters`); a canister set to 250 got the entry `7:250`. Which saver writes
  that list was not isolated (no non-dust canister existed when the stream offsets were traced); it is written inside
  `Serialize` either way.
- On load, a canister injected into `snapshot.canisters` with a `cargo` entry of 250 came back from `RestoreCanisters`
  as dust, and stayed dust (vanilla never gives a restored canister a cargo above Stone). The library sets it afterwards.
- `world.canisters` is `Dictionary<int, MoonDroppedCanister>` of value types: `canisters.set_Item(id, c)` (and a
  setter on a copy) from managed code does **not** stick (read back unchanged). `MoveCanisterRecord(c, local, up,
  rotation)` stores the canister it is given (cargo, kilograms and pose read back as passed). Do not call
  `TryGetCanister(id, out c)` (an `out` of a boxed struct, the `TryPanel` crash pattern); use `ContainsKey` + `get_Item`.
- So StructureLib strips modded-cargo canisters and their `cargo` entries in a `MoonProgressStore.Serialize` prefix
  (before the `MoonDepotSave.Write` prefix writes the sidecar `.tmp`), puts them back in its finalizer, injects saved
  ones into the load snapshot in a `RestoreCanisters` prefix and gives them their cargo in a `RestoreProduction`
  postfix (`MoveCanisterRecord`). Checked end to end on slot-2: sidecar canister → world canister with cargo 250 →
  next save "moved 1 modded-cargo canisters (1 cargo entries, 1 recorded) ... 6 canisters stay" → sidecar record.
- `MoonDroppedCanister.IsDust` is false for a canister constructed with cargo 250 (and true for Dust), so it needs no
  patch.

## Production (processors)

- `MoonWorld.ProcessorSpec`: `name`, `product`, `runningKw`, `cycleSeconds`, `slotNames[]`, `slotCargo[]`,
  `slotCapacity[]`, `output` (index of the output slot). Static instances `ChamberSpec`, `PressSpec`;
  `Processor(kind)` picks by kind.
  - Pressure chamber (96): 10 kW, 12 s cycle, slots Dust(20) → Pressing(5) → Sand(10); `ChamberBatchKg` 5.
  - Stone press (97): 18 kW, 6 s cycle, slots Dust(12), Sand(12) → Kiln dust(1), Kiln sand(1) → Stone(10);
    1 kg dust + 1 kg sand per block.
  - Pattern: input slots, "batch" slots holding the material in the current cycle, one output slot.
- `MoonProductionState` per built machine (`production` dictionary keyed by depot id): `Id`, `Kind`, `stores[]`
  (one `Dictionary<string,double>` per slot: kg per owner), `Progress`, `Running`, `Power`, `DemandKw`, `UsedKw`,
  `Grid`, `ChargeKwh`, `SuppliedKw`, `BatteryKw`, `SunFactor`, `Daylight`, `SunAim`, `TrackerTilt`, `Cycles`,
  `Status`. `NewProduction(id, kind)` creates it; `StepProduction(dt)` → `CanStartCycle` / `TryStartCycle` /
  `FinishCycle` / `EmitProduct` → `EmitCargo(outfeed belt, owner, cargo, kg, out reason)`.
- Input arrives through `CrewProcessorLoad(player, machine, cargo, kg)` (walker or belt hand-off into a port;
  `InputSlot(kind, cargo)` says which slot takes which cargo, -1 = refused) and `EnterMachine(machine, input, id)`
  for canisters riding belts.
- Views: `MoonProductionView` (base: `Setup(world, id)`, `Animate(state, dt)`, hum, emission/fill helpers) with
  `MoonSolarPanelView`, `MoonStonePressView`, `MoonPressureChamberView` subclasses on the prefabs.
  `MoonProductionInteraction` is the look-at panel (`ShowPanel(world, record, power)`).

## Cargo

- `MoonCargo` enum: Dust 0 (1 kg/unit), Sand 1 (1 kg), Stone 2 (2 kg). Names from static `CargoName`,
  `CargoShortName`, `UnitKilograms`; money from instance `CargoValue(cargo)`.
- **An out-of-range cargo value falls through to dust** (`CargoName((MoonCargo)7)` = "moon dust"). So a new cargo
  kind needs prefixes on every cargo switch, and anything unpatched will silently treat it as dust.
- Cargo moves as `MoonDroppedCanister` (`cargo` field) on belts and in hands; `MoonCargoSnapshot` / premiums track
  deposits (`CrewCargoDeposit`, `AddPremium`).

## Power

- `IsPowerNode(kind)` = solar panels and processors. `PowerNode` (record, state, position, `panel`, `PanelSpec`,
  `parent`), `powerNodes`, `powerLinks` (pairs of ids within `PowerRange`), `powerGrids`
  (`MoonPowerGrid`: `panels`, `machines`, `supplyKw`, `demandKw`, `usedKw`, `peakKw`, `satisfaction`, `storedKwh`,
  `capacityKwh`). `SolvePower(dt)` runs the grids; `StallPower` 0.08 is the satisfaction below which machines stall.
- `PanelSpec`: `name`, `peakKw`, `batteryKwh`, `range`, `tiltDeg`, `maxTiltDeg`, `tracker`. Post 3 kW / 0 kWh /
  16 m; Twin 7.5 kW / 4 kWh / 24 m; Tracker 16 kW / 10 kWh / 34 m. Supply scales with `SunFactor` / `Daylight` from
  `MoonSolarSystem`.
- A fuel-burning generator is a power node that supplies without sun. Answered in Task 11: a processor whose cycle
  charges a battery, supplied to modded machines after the solve (see "Power sources (Task 11)" below).
- `MoonPowerLinks` draws the arcs; `ShowPreview(kind, position, rotation, joined)` during placement.

## Interop hazards found

- `MoonWorld.TryPanel(int, out PanelSpec)` hard-crashes the game when it returns true (interop `out` of a boxed
  struct). Read the static `PostPanel` / `TwinPanel` / `TrackerPanel` instead.
- `MoonWorld.BuiltDepots` (`IEnumerable<MoonDepotSnapshot>`) is not foreach-able from managed code; iterate `depots`.
- `MoonWorld.PowerLinks` is an `IReadOnlyList` interop interface; use the `powerLinks` list.
- `MoonWorld.DevAddMoney(player, dollars)` does nothing in this build (wallet unchanged; the `Il2CppSystem.Decimal`
  argument marshals fine, it prints `20000`). The wallet is team-wide (`wallets.Balance`); `SetTeamDollars(Decimal)`
  works. GarageLib's `DevTools.Buy` relies on `DevAddMoney`, so its "adding money" step is silently a no-op.
- Enumerating `DepotPlacementSteps(check)` with `MoveNext()` ended after one step with `check.Done` still false (run 1;
  seen on the console, the log file was later overwritten). Drive a check with `check.Advance(milliseconds)`: one
  `Advance(50)` completed it (`Done=True`, `Slices=1`) in runs 3 and 4.
- There is no static `MoonWalker.Local`; `FindObjectsByType<MoonWalker>` finds the one walker. The local player id is
  `MoonWorld.LocalPlayer` (a `local…` id in single player). `MoonWorld.solarSystem` was null in one session 20 s after
  load; `FindFirstObjectByType<MoonSolarSystem>()` finds it.
- `PowerNode.spec` (a non-blittable `PanelSpec`) reads fine through the property; only `TryPanel`'s `out` crashes.
- **A Harmony prefix on `MoonWorld.IsProcessor(int kind)` crashes the game natively at world load** (Task 5, 5 of 5
  runs with it enabled; 5 of 5 without it clean). Harmony reports the patch as applied without error; the process then
  vanishes during world load, before `RestoreDepots`, with no managed exception, and the game's crash report says it
  "stopped without its crash handler". A log line as the first statement of the prefix never appeared, so the crash is
  in the detour or trampoline, before managed code runs. Its native body is a 15-byte leaf
  (`83 F9 60 75 03 B0 01 C3 83 F9 61 0F 94 C0 C3 CC`); no other Assembly-CSharp method shares its native address (all
  8592 `NativeMethodInfoPtr_*` scanned), so it is not identical-code folding. Cause unconfirmed (a caller on a
  non-main thread is one guess). StructureLib deliberately leaves it unpatched (comment on `ProductionPatches`,
  `Patches/ProductionPatches.cs`); a modded kind gets vanilla's answer for kinds above 192, false.
  - The crash is specific to `IsProcessor`. In the same build, prefixes on the static switches `IsSolarPanel`,
    `IsPowerNode`, `PowerRange`, `HexPieceKind`, `IsRobotDock` and `MapKindOf` (and on the instance methods
    `IsEdgeRail`, `IsWorldHex`, `IsWorldSquare`, `IsWorldGrid`, `FarSpec`, `BuildDefinition`, `AcceptsText`,
    `MoonDepotBuilder.CanBuild`, `MoonDepotBuilder.ProductionNote`) patched and the world loaded cleanly with the
    self-test 9/9. `IsSolarPanel` has the same 15-byte native shape (`83 F9 5D 7D 03 32 C0 C3 83 F9 5F 0F 9E C0 C3
    CC`) and is fine. Only patching and load were proven: no modded kind reached those remaps in game, since nothing
    was built.
  - What Task 10 did instead: prefixes on the instance method `MoonWorld.IsProcessorId(int id)` and on
    `MoonWorld.Processor(int kind)` (both patch and run fine), plus prefixes on the specific callers that refuse a modded
    machine. See "Processors (Task 10)" below for which callers those are.
- **A Harmony prefix on `MoonWorld.TryPanel(int, out PanelSpec)` kills the game natively** (Task 11, green1): the world
  loaded, then the process vanished within seconds, before any modded structure was built, with no managed exception
  and the crash report's "stopped without its crash handler". Removing only that prefix, the next run was clean (and
  every run after it). One run each way. StructureLib leaves `TryPanel` unpatched; a modded kind is none of the three
  panel kinds, so vanilla presumably answers false for it (not traced).
- The static cargo switches `CargoName`, `CargoShortName`, `UnitKilograms` and the instance `CargoValue` (returning
  an `Il2CppSystem.Decimal` through `ref __result`) all take Harmony prefixes without trouble (Task 9, 4 runs).
- `world.canisters` (`Dictionary<int, MoonDroppedCanister>`, value-type values): `set_Item` from managed code does not
  change the stored canister. Write through `MoveCanisterRecord` instead (see "Built records and save").

## Processors (Task 10, traced in game on slot-2, 2026-11-01)

Traced with a temporary Harmony probe (not committed) and the self-test. The example grinder is built on the pressure
chamber (96): Input dust 10 kg, Batch dust 2 kg, Output grit 10 kg, 1 kW, 4 s cycle. Logs are in
`.superpowers/sdd/2026-11-01-structurelib/task-10-logs/`.

- **`NewProduction(id, kind)` asks `Processor(kind)` and sizes `stores` from the spec** (not inlined). The game creates
  a built modded processor's state itself, with `NewProduction(27, kind)` right after the build. Without the
  `Processor` prefix the state had `stores == null`; with it, the grinder's 3 slots.
- **`RestoreProduction`** calls `NewProduction(id, depots[id].kind)` for every `snapshot.production` entry, then fills
  the stores from the snapshot. So a snapshot injected in a `RestoreProduction` prefix restores a modded machine like a
  vanilla one: saved `[0 0 6]` kg came back `[0 0 6]`.
  - It creates states only for snapshot entries. So when a saved record no longer fits the definition's slots, the
    library injects an *empty* snapshot for that machine; `NewProduction` then sizes it from the current definition.
    The old record is kept verbatim but marked `Superseded`, so it is never offered again, and it is dropped once its
    machine is no longer saved (`Pure/ProductionPlan`, fix round 1). This path is argued from the trace above and
    unit-tested; it has not been run in game.
- **The production save shape.** `MoonProductionSnapshot` holds `id, progress, running, charge` and `stores`, with no
  kind and no cargo. `stores` is a per-slot list of `MoonWalletSnapshot` owner shares. **The kilograms are in
  `pendingKilograms`; `dollars` is 0.** `MoonProductionSave.Write` runs inside the same `Serialize` as
  `MoonDepotSave.Write`, after it; by then the depot finalizer has put the modded depots back into `state.depots`.
- **`IsProcessor(kind)` callers that refuse a modded machine** (inlined, or calling the unpatchable static), and what
  StructureLib does about each:
  - `CrewProcessorLoad`: vanilla loads nothing into a modded machine, and no `InputSlot` call was seen.
    **Prefix:** for a modded machine, load the first Input slot that takes the cargo, up to its capacity, under the
    player's share through `MoonWorld.Put(store, player, kg)`.
    - Who deducts the walker's cargo (fix round 1, checked on the vanilla chamber with
      `DevSetCarriedDust(player, 30)`):
      - The walker path is `LoadProcessor(player, machine, out reason)`. It loaded the chamber's 10 kg of free room,
        took those 10 kg out of the walker's inventory itself (`InventoryKilograms` 54.54 → 44.54), and did **not**
        call `CrewProcessorLoad`.
      - Vanilla `CrewProcessorLoad(player, 2, Dust, 15)` on the then full chamber left the inventory untouched and the
        slot at its 20 kg capacity, dropping the excess.
      - So `CrewProcessorLoad` is an apply step whose caller has already deducted. The prefix matches vanilla: it caps
        at capacity and never touches the walker's cargo.
    - `LoadProcessor` on the grinder refuses with `'Not a production machine'` (another inlined kind check). Walker
      loading into modded machines is not supported yet.
  - `CanStartCycle`: answers false with batch 0 for a modded kind, even with 6 kg of dust in slot 0 and `Power` 1. The
    recipe is hard-coded per vanilla kind.
    **Prefixes** on `CanStartCycle` / `TryStartCycle` / `FinishCycle` run a generic cycle (`Pure/ProcessorCycle`):
    - Start when every Batch slot is empty, each can be filled to its capacity from the first Input slot with the same
      cargo, and the Output slot has room for the whole batch.
    - `TryStartCycle` moves the kilograms with `MoonWorld.Move` (owners kept) and sets `Running`.
    - `FinishCycle` moves every Batch slot into the Output slot at the same mass (as the chamber does, fact (c)), or
      burns it for a power producer, and clears `Running` and `Progress`.
    - `StepProduction` drives all of this for a modded state as for a vanilla one: it calls `CanStartCycle`,
      `TryStartCycle`, `FinishCycle` and `EmitProduct`, advances `Progress` by `Power` × dt / `cycleSeconds`, and
      counts `Cycles`.
    - Seen in game: 6 kg dust became 6 kg grit in three 4 s cycles; the status line reads
      `Connect a belt to the example grit output` (from `spec.product`).
  - **`SolvePower`'s node list**: it is rebuilt from scratch on every call, from an inlined kind check (the patched
    `IsPowerNode` is not asked). A `PowerNode` added to `powerNodes` in a `SolvePower` prefix was gone after the call.
    So a modded processor is never a node, and stays at `Grid 0` / `Power 0`.
  - `LoadProcessor` (walker) refuses a modded machine (above). Not checked: `FeedProcessor` / `EnterMachine` (belts)
    and `PortCargo`, which very likely refuse it the same way. Belt and walker input is outside Task 10.
- **Grid membership rule (a `SolvePower` postfix).** After the vanilla solve, each modded processor that is not a node
  attaches to the grid of the nearest vanilla node, provided the node's own `PowerRange(node.record.kind)` reaches the
  machine:
  - Distance is measured with `FootprintDistance(machine record position, node.position, node.rotation, node.half)`.
  - Demand is `RunningKw` while `Running`, else 0.
  - The machine is supplied (`Pure/GridShare`) first from the grid's sun surplus, `supplyKw - usedKw` (shared between
    modded machines in the same solve), then from its batteries when `storedKwh > 0` (S0 saw batteries discharge at any
    rate).
  - Energy accounting (corrected in fix round 1):
    - While charging, vanilla has already stored the sun surplus in the batteries (fact (b): `BatteryKw` = surplus ×
      ~0.92).
    - So the part of the modded machine's sun share that was being stored is taken back: `takenBack =
      min(fromSun × 0.92, charging)`. `charging` is the sum of the grid's panels' positive `BatteryKw`, shared between
      modded machines in the same solve (`GridShare.ChargeTakenKw`).
    - `(fromBattery + takenBack) × dt / 3600` kWh then comes out of the `ChargeKwh` of the grid's panel states, in node
      order.
    - When the batteries are full or discharging, nothing was stored, so nothing is taken back from the sun share.
  - Satisfaction is the share of demand covered. An idle machine reads 1 when the grid has any surplus or charge, else
    0.
  - The state then gets `Grid` = that grid, `Power` = satisfaction, `DemandKw`, `UsedKw = DemandKw × satisfaction`, and
    `SuppliedKw = 0`.
  - With no node in range: grid 0, power 0.
  - **Limits.** `powerGrids` holds `MoonPowerGrid` *structs*, which managed code cannot write back (the same hazard as
    `world.canisters`). So the grid's reported `demandKw` / `usedKw` / `satisfaction` leave out the modded demand, and
    the vanilla machines on that grid do not see it.
    - The energy itself is conserved through the panels' `ChargeKwh`: the battery share, plus the sun share that was
      being stored.
    - Two things are not mirrored. A sun share used while the batteries are full was surplus vanilla wasted, so no
      energy is created there. A vanilla machine's own satisfaction is not reduced when a modded machine takes the same
      surplus first in the next solve.
  - No power link is drawn to the modded machine.
  - In game, the grinder attached to the new solar twin's grid 26 (5.8 m from the twin's footprint, range 24 m; supply
    3.38 kW) and ran its cycles.
- `[Dev] ForcePower` (default off) forces `Power = 1` on modded processors for isolating problems; with it on,
  `cycle-produces-grit` fails on purpose.

## Power sources (Task 11, traced in game on slot-2, 2026-11-02)

The example burner is a fuel generator: `Power = { PeakKw 5, BatteryKwh 2, Range 20, SupplyMode Fuel }` and a processor
with `ProducesPower`, `RunningKw 0`, 5 s cycle, Input grit 10 kg → Batch "Burning" grit 1 kg. Logs are in
`.superpowers/sdd/2026-11-01-structurelib/task-11-logs/` (red1, green1–3, reload1).

- **Donor: the solar post (93), not the battery bank (6).** A burner built on the battery bank had no
  `MoonProductionView` anywhere under its view (green2: `MoonProductionView none`), so per the pre-made ruling the example
  burner uses `MoonWorld.SolarPostKind`; its view then carries a `MoonSolarPanelView` (green3). SandPower's generator
  (Task 13) follows. `world.powerAnchors` had no entry for the burner with either donor: the game fills it only for
  structures it powers itself, and nothing in StructureLib reads it.
- **`SolvePower` ignores the burner's kind entirely.** Its node list comes from an inlined kind check (Task 10), so the
  modded kind is never asked about: not through `IsPowerNode`, not through `IsSolarPanel`. The burner's production
  state exists (the game's `NewProduction` sizes it from the `Processor(kind)` spec, output -1) and `StepProduction`
  runs its cycle like the grinder's.
- **Supply goes through the `SolvePower` postfix (`Patches/PowerPatches.cs`, pure rules in `Pure/FuelGrid.cs` and
  `Pure/GridShare.cs`).** After the vanilla solve:
  - every modded processor finds the nearest vanilla node in range (Task 10's rule);
  - `FuelGrid.Assign` links every modded member within a fuel generator's `Range` (centre to centre) into one network;
  - grid ids: a member a vanilla node reaches keeps that vanilla grid id. A member no vanilla node reaches takes its
    network's id: the lowest vanilla grid id in the network, else, when the network holds a fuel generator,
    **`-(lowest depot id in the network)`**. Vanilla grid ids are depot ids (grid 26 was the twin depot 26's), so
    positive; a negative id cannot collide. 0 still means no grid. In game: burner depot 26 and grinder 27 both
    reported grid -26;
  - a machine draws its demand from, in order: the vanilla grid's sun surplus (with the charging take-back of Task 10),
    the fuel generators of its network (each at most `PeakKw`, and at most what its `ChargeKwh` lasts over the solve:
    `FuelGrid.AvailableKw`, split in network order by `FuelGrid.Split`), then the vanilla grid's batteries. Stored
    charge per grid is now tracked across machines in one solve (it was read pre-drain for each machine before);
  - a fuel generator gets `Grid`, `Power` 1 (it runs on its own fuel; with `RunningKw` 0 the Task 10 rule would have
    given it 0 at an empty battery and it could never start), `SuppliedKw` = what its network drew, `BatteryKw` = minus
    that, and the drawn kWh out of its `ChargeKwh`;
  - it is then appended to `world.powerNodes` as a panel node (`panel = true`, `spec = SpecBuilder.PanelSpecFor(def)`, a
    point footprint at its depot position, `parent` = its own index). The next solve rebuilds the list and drops it, so
    it is re-added each solve, as a new object. `nodePool` stayed at 2 (the vanilla nodes) with it in place, so the
    pool does not grow; no exception in any run.
- **Charging.** The `FinishCycle` prefix burns the batch and sets `ChargeKwh = min(ChargeKwh + BatteryKwh / 2,
  BatteryKwh)` (`ProcessorCycle.ChargeAfterBurn`: two cycles fill it). `CanStartCycle` / `TryStartCycle` refuse to
  start a cycle while `ChargeKwh >= BatteryKwh` (`ProcessorCycle.CanBurn`). Done in the `FinishCycle` prefix rather
  than a postfix so a charge is only added when the burn itself ran. In game: 4 kg grit gave 4 cycles while the grinder
  drew (1 kW off 1.000 kWh after the first), the battery ended at 2.0000 kWh, and 2 kg more grit loaded with the battery
  full started no cycle in 10 s.
- **`EmitProduct` throws for a power producer.** `StepProduction` calls it on a timer even before a cycle, and vanilla
  indexes `stores[spec.output]` with output -1: `IndexOutOfRangeException`, caught by `MoonWorld.Fault` (red1). A
  prefix skips it for modded `ProducesPower` kinds.
- **Charge persists.** The production snapshot's `charge` carries `ChargeKwh`: the sidecar recorded the burner with
  `Charge 2`, and on reload the restored burner read 2.0000 kWh and joined grid -26 again (reload1).
- **Limits.**
  - A vanilla machine is never fed by a fuel generator: `powerGrids` holds structs that managed code cannot write back
    (Task 10), so a vanilla grid's totals never see modded supply, and vanilla machines get their satisfaction from
    vanilla's own solve. A possible later route: a vanilla machine's `MoonProductionState` fields are writable, so a
    postfix could raise its `Power` from fuel after the solve. Not built.
  - No power link is drawn to or from a modded structure (`powerLinks` is not touched).
  - A machine reached by a vanilla grid only through the fuel generator's network still reports and draws from that
    vanilla grid (the network's id), since it is on the same grid.
  - Only `SupplyMode.Fuel` generators with a `ProducesPower` processor supply. A `SupplyMode.Solar` definition answers
    `IsSolarPanel` true but supplies nothing yet; a `Power` definition without a processor has no production state to
    carry charge.
  - A generator's `RunningKw` is unsupported: `SetSupply` gives a fuel generator `Power` 1 and `DemandKw` 0, so it never
    draws from the grid whatever its `RunningKw` says (a mod author setting it gets nothing, silently).
  - "Daylight" plays no part for a fuel generator: `SolvePower` recomputes `SunFactor` for its own panels only, and the
    postfix never reads it.

## World-time findings (new game in slot-1, 2026-11-01)

- **The kind table is `MoonWorld.DepotDefinition.additionalStructures`, 192 entries, and it is full.**
  `BuildDefinition(kind)` returns `additionalStructures[kind - 1]` for 1..192, `null` for 0 and for anything above
  192 (no exception: it bounds-checks). `MaxStructureKind` = 192 = the array length. So there are **no free kinds
  below 193**; a modded structure must take a kind ≥ 193 and the library has to answer `BuildDefinition` for it
  (prefix) and, to be safe against inlined array reads, also grow the array.
- The 192 slots include unreleased or internal definitions that the catalog never lists: 1 equipment_kiosk,
  2 canister_rack, 3 cargo_locker, 4 floodlight, 5 solar_array, 6 battery_bank, 7 landing_pad, 8 walkway,
  9 safety_barrier, 13–16 conveyor pieces, 17–18 packager / depot foundations, 84 foundation_hex_22m. Each has a
  prefab, so they are usable as `BaseStructure` donors (a battery bank and a solar array already exist as models).
- `MoonDepotBuilder.BuildCatalog` turns the 14 static `baseStructures` plus the foundation kit into 98 instance
  `structureEntries`. `CanBuild(kind)` is true for every kind 1..192: nothing is progression-gated. `ProductionNote`
  is a hard-coded per-kind string (the long card descriptions), so modded kinds need a prefix for their note.
- `AcceptsText(kind)` is non-empty only for processors ('dust', 'dust or sand').
- `CargoValue`: dust $1, sand $3, stone $7 per unit.
- Save: `MoonDepotSave.Read(version=35)`; a fresh world writes `depots=0`, `production=0`. What `Read` does with a
  kind above 192 is still untested; the sidecar design makes it moot for our own saves.
- Pressure chamber costs $500, stone press $1,000, solar post / twin / tracker prices are in the definitions.

## Answered by S0 (behaviour spike, 2026-11-01)

Source: `spike-structures/` `WorldProbe` + traces, six in-game runs on `slot-2` (a `tools/copy-slot.ps1` clone of
slot-1). Each run builds a solar twin (94) 12 m ahead of the walker and a pressure chamber (96) 10 m beyond it, logs
production and power every second for 120 s, calls `CrewProcessorLoad` with 10 kg of dust (20 kg in run 6) at +30 s,
and saves at the end. Runs 3 and 4 loaded a save holding the earlier rig, which covers restore. The sun was below the
horizon at the walker on load (`SunFactor` 0, status 'Night: no sunlight, and its batteries are empty'). From run 3
on, the probe sets `SetLunarTime` to the time whose `SunDirectionAt` is closest to the walker's up (0.8225 here, ~42°
elevation, `SunFactor` ~0.74–0.81). The redacted log excerpts behind each claim were kept locally with the task
report; they are not in the repo.

- **(a) Where the built view is instantiated: `MoonDepotConstruction.Spawn(MoonDepotSnapshot data)`, on build and
  on restore.** A postfix sees the view with `__instance.ViewOf(data.id)`: an unparented root named
  `'<display name> <id>'` (`'Solar twin 1'`, `'Pressure chamber 2'`). The `[trace]` lines show
  `MoonProductionView.Setup(world, id)` (`MoonSolarPanelView` / `MoonPressureChamberView`) and then `Keep(id, root)`
  firing before `Spawn` returns. Order:
  - Build, all in the same frame and within the `TryBuildDepot` call: `Setup` → `Keep` → `Spawn` returns true →
    `MoonBuildReveal.Begin` / `MoonBuildReveal.Play(root)` → `MoonWorld.RevealBuilt(view, belt=0)` →
    `SnapshotCanisters`, `SnapshotDepots` (a save snapshot) → `TryBuildDepot` returns true. `MoonDepotSave.Write`
    comes after.
  - Restore, one frame: `MoonDepotSave.Read` → `RestoreCanisters` → `RestoreDepots` → `RestoreProduction`
    (`NewProduction` per machine) → `MoonDepotConstruction.Awake` → `Spawn` per depot (`Setup`, `Keep`). On restore
    there was no `RevealBuilt` and no `MoonBuildReveal`, and `RestoreExistingViews` never fired, so neither is a
    restore hook. `MoonBuildReveal` has no `Awake`/`Start` in this build; `Play` is its static entry point.
  - This is a clean hook, so no fallback is needed: prefix/postfix `MoonDepotConstruction.Spawn` for modded kinds.
    Which prefab it instantiates was not traced. A candidate is `SkinOf(def, style)`; if it reads the definition, a
    cloned definition with its own `prefab` covers `BuildModel`, with `DecorateModel` in the `Spawn` postfix.
- **(b) Power nodes appear during a `SolvePower` call, and `SolvePower` works out each panel's `SunFactor` itself.**
  - Nodes: in run 2, the first `SolvePower` after the build went from `nodes=0` (pre) to `nodes=2` (post, `pool=2`,
    `firstNodeSameObject=False`). Later calls reuse the same objects (`firstNodeSameObject=True`). After a second rig
    was built in run 3, the next sampled solve already had 4 nodes. Each node holds the depot `record` and its
    `MoonProductionState` `state`. For the twin, `panel=True` and `spec` = 'Solar twin' 7.5 kW / 4 kWh. `parent=0` on
    every node. Links: 1 for 2 nodes, 5 for 4. Sampled `dt` was 0.251–0.267 s, so `SolvePower` runs about four times a
    second, not every frame. `PowerCandidates(kind, position, rotation, result)` returned `[1]` (the twin) when the
    probe called it for a chamber 8 m away. It was never traced during play, so it looks like a placement query rather
    than node creation.
  - Per panel, each solve:
    - `SunFactor` is recomputed: planting `SunFactor = 0.25` just before a solve came out as 0.744.
    - `UsedKw = spec.peakKw × SunFactor` (7.5 × 0.736 = 5.52).
    - `SuppliedKw` is the panel's share of machine demand. Two panels and one 10 kW chamber: 5.00 each. One panel:
      all of its 5.70.
    - `BatteryKw` is the surplus × ~0.92 while charging (5.52 − 5.00 → 0.48; 5.84 idle → 5.38), or minus the deficit
      while discharging (−4.30 = 10 − 5.70, no loss).
    - `ChargeKwh` integrates `BatteryKw` over real seconds (0.0416 → 0.0404 kWh in 1 s at −4.30 kW).
  - Grid (`MoonPowerGrid`): `supplyKw` = Σ panel `UsedKw`, `peakKw` = Σ `spec.peakKw`, `capacityKwh` = Σ
    `batteryKwh`, `storedKwh` = Σ `ChargeKwh`, `demandKw` = Σ running machines' `runningKw`. `satisfaction` was 1
    whenever sun plus battery covered demand, and 0 at night with empty batteries (run 2). Machine node: `DemandKw` =
    10 only while `Running`, `UsedKw = DemandKw × satisfaction`, `Power` = satisfaction (it reads 1.0 while idle). A
    satisfaction between 0 and 1 was not observed.
  - No clean per-node hook: nodes appear during a `SolvePower` call, and no other traced method creates them. A
    `SunFactor` written before the solve is overwritten. The spec's fallback applies: **postfix `SolvePower` to add
    supply for modded nodes directly**. "Force `SunFactor` to 1 for fuel nodes" cannot be done by pre-setting the
    field.
- **(c) Pressure chamber cycle (the only recipe run, 5 kg batches, one owner): 5 kg dust in, 5 kg sand out.**
  - `TryStartCycle` moves `ChamberBatchKg` (5 kg) from slot 0 (Dust) to slot 1 (Pressing).
  - After 12 s at `Power` 1 (progress +0.083 per second), `FinishCycle` empties slot 1 into the output slot 2 at the
    same mass: pre `[0:5 1:5 2:0]` → post `[0:5 1:0 2:5]`. The second cycle ended at `[0:0 1:0 2:10]`.
  - `TryStartCycle` is called again in the same frame as `FinishCycle`. The batch move first shows in the next logged
    state (2 frames later in run 3, 37 frames later in run 5).
  - The single owner's share stays on the moved kilograms (owner count 1 → 1). More than one owner was not tested.
  - `Cycles` goes up after `FinishCycle` returns: the post line still shows the old value.
  - Across both cycles, 10 kg dust became 10 kg sand.
  - The stone press and other batch sizes were not run, so this is not a general rule.
- **(d) `EmitCargo` needs a connected outfeed belt; without one it is never called, and a full output slot stops the
  machine.** With sand in the output slot and no belt, `EmitProduct` runs on a short timer, leaves the output untouched
  and sets status `'Connect a belt to the sand output'`. `EmitCargo` was patched and traced zero times across three
  full runs. With 20 kg of dust loaded (run 6), two cycles brought the output to 10 kg. After that, `TryStartCycle`
  kept being called but moved nothing, and the chamber sat at `running=False [0:10 1:0 2:10]` from t=60 s to t=119 s
  with 10 kg of dust still waiting. The 10 kg output slot (the chamber spec's sand capacity) stops the cycle.
- **(e) `TryBuildDepot(builder, direction, yaw, out reason, null, kind)` accepts a null check** and validates inline.
  - On a clear site: `ok=True 'Solar twin built!'` / `'Pressure chamber built!'` (every successful build used
    `route=null-check`).
  - Refusals with a null check read `'Not enough money.'`, `'Too close to another structure.'` and `'Blocked by a
    player, object, or structure.'`.
  - `BeginDepotCheck` + one `check.Advance(50)` completes a check (`Done=True`, `Slices=1`, with `Valid` / `Reason`).
    Only refused sites (`Valid=False`) were seen on this route; no build through a completed check was observed.
  - Building at 22 m from the walker was not refused for reach.
  - The price comes off the team wallet: 20000.95 after the top-up in run 2, 19350.95 when run 3 loaded that save. So
    twin + chamber cost $650 (chamber $500, so twin $150).
  - A new machine's `MoonProductionState` is not created inside `TryBuildDepot`: `production` has no entry right after
    it returns, and `NewProduction` is traced later in the same frame.

Also answered:
- Dev automation can load an existing slot from the menu: set `MoonSaveSlots.Active` to the index whose
  `SlotFolder` ends in `/slot-<n>` (slot-2 is index 2), then `MoonSession.StartShift()` (spike `AutoLoad`).
- `CrewProcessorLoad(player, machine, Dust, kg)` puts the kilograms straight into input slot 0 under the player's
  owner share (10 → `[0:10]`, 20 → `[0:20]`); it does not wait for a canister.
- `MoonCanisterSnapshot` has no `cargo` field (id, owner, kilograms, capacity, pose only). Every canister in this save
  was dust, so it was not observed, but a non-dust canister presumably comes back as dust after a save. (Task 9: the
  cargo is in `MoonLooseDustSnapshot.cargo`, see "Built records and save".)

## Still open

- How belt input reaches a processor (catcher / garage outfeed belt into a port, `EnterMachine`, `FeedProcessor`).
- What `EmitProduct` → `EmitCargo` does with a belt attached.
- `SolvePower` with satisfaction between 0 and 1 (battery drained under load). Whether the press, other batch sizes
  and several owners follow the chamber's pattern. A build through a completed (`Valid=True`) placement check.
