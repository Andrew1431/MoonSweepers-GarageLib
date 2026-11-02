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

### Per-depot savers audit (final fix wave, 2026-11-02)

**The bug.** A modded structure placed from the build menu made every later vanilla save fail with
`InvalidDataException: Structure pose ID` (`MoonStructurePoseSave.Read`, the game's read-back). The build menu places
a structure through `PlanStructure` → `BeginStructureCheck` → `TryBuildStructure`, which sets the depot's
`placedPose = true` and its quaternion (`TryBuildDepot`, the dev AutoBuild path, leaves `placedPose` false). There is no
pose list in `MoonLooseDustSnapshot`: `MoonStructurePoseSave.Write` derives one record per posed depot from
`state.depots`, and its `Read` refuses a record whose depot id is not among the depots read. StructureLib used to put
the stripped depots back in the `MoonDepotSave.Write` finalizer, so the pose saver (5th in the order above) saw them
and wrote records for depots the depot section did not hold.

**Reproduced** with `[Dev] AutoBuildViaPlan` (AutoBuild through that same plan path) on the pre-fix library: depot 26
came back `placedPose=True q=(0,0,0,1)` and both saves of the run were refused with "Structure pose ID" (run `red1`,
`.superpowers/sdd/2026-11-01-structurelib/final-fix-logs/`). The old `vanilla-save-clean` assertion passed regardless
(the refusal is an exception out of `MoonProgressStore.Write`, not a Unity "Moon save failed" line).

**Fix.** Inside a `Serialize` the stripped depots stay out of `state.depots` until the `Serialize` finalizer, so every
saver after `MoonDepotSave` sees exactly the depots the vanilla save holds. The pose needs no record of its own: it is
part of the depot record, which the sidecar already keeps (`PlacedPose`, `Qx..Qw`) and re-injects. The production
strip matches against the depots the depot prefix took. Checked: run `green1` (same plan-path lamp, fixed library) saved
cleanly with the lamp's pose in `structurelib.json`; see the final-fix report for the reload.

| Saver (write order) | Snapshot data | Names a depot / canister? | Status |
|---|---|---|---|
| `Serialize` core sections | `canisters` / `nextCanisterId`, piles, inventories, vehicle storage, wallets, upgrades | canister id (canisters) | canisters of modded cargo **stripped** (Task 9); the rest vanilla-safe (no depot id, no cargo) |
| `MoonDepotSave` | `depots` / `nextDepotId` | depot id | **stripped** (Task 8), now out of the snapshot for the whole `Serialize` |
| `MoonLoadoutSave` | loadout slots (strings) | no | vanilla-safe |
| `MoonVehicleStorageSave` | `vehicles` (`MoonVehicleStorageSnapshot`: string id, kg) | no | vanilla-safe |
| `MoonConveyorSave` | `conveyors` (`MoonConveyorSnapshot`: id, owner, endpoints) | no depot member | vanilla-safe; a belt joined to a modded machine port is unsupported (belt input is) |
| `MoonStructurePoseSave` | derived from `state.depots` (`placedPose`, quaternion) | depot id | **stripped** with the depots (the C1 fix); the pose is kept in the sidecar depot record |
| `MoonToolContainerSave` | `toolContainers` (player, tool, tier, kg) | no | vanilla-safe |
| `MoonUpgradeTreeSave` | `upgradeTree` (player, node ids) | no | vanilla-safe |
| `MoonStructureAnchorSave` | `anchors` (`MoonDepotSnapshot`s; `ValidateSupports`) | own id + kind | an anchor of a **modded kind** is **stripped** for the write and put back (not kept across a reload; none seen in game); an anchor whose id equals a stripped depot id is only logged (whether anchor ids share the depot id space is unknown) |
| `MoonRobotSettingsSave` | `robotsOff` (depot ids) | depot id | an entry naming a stripped depot is **stripped** for the write and put back; not kept across a reload, so a modded robot dock's robots-off setting is **unsupported** (resets on load) |
| `MoonPlayerRecordSave` | `players` (pose, signature) | no | vanilla-safe; no cargo member |
| `MoonProductionSave` | `production` | depot id | **stripped** (Task 10), matched against the stripped depots |
| `MoonScoreSave` | `scores` | no | vanilla-safe |
| `MoonGarageSave` | filters `state.depots` by the garage kind | depot id | sees no modded depot (stripped); a modded structure borrowing the garage is **unsupported** (untested) |
| `MoonRadioSave`, `MoonRegionTouchSave`, `MoonWaypointSave`, `MoonLandmarkSave` | radio, regions, waypoints, landmarks | no | vanilla-safe |
| `MoonOwnedVehicleSave` | `ownedVehicles` (`garage` = a garage depot id) | depot id | **unsupported**: a vehicle kept in a modded garage would name a stripped depot (untested; garages are not refused as donors) |
| (`cargo`, written by one of the savers) | `MoonCargoSnapshot` (canister id, byte) | canister id | **stripped** (Task 9) |
| (`premiums`) | `MoonPremiumSnapshot` (station, player, kg) | `station` (meaning unchecked) | unknown; no modded structure is a station |

Vanilla depots standing on a stripped depot (`support`) are counted and logged at each save; none is expected, since
decks and foundations are refused as donors.

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
- **A Harmony prefix on the static `MoonWorld.PortCargo(port)` is safe** (M1 spike, 2026-11-02): a log-only prefix that
  returns true patched, ran (20 logged calls per run) and the world loaded cleanly in two runs, self-test 23/23 each.
  Unlike `IsProcessor`, nothing went wrong. Note its argument is the *definition's*
  `MoonDepotBuildDefinition/ConveyorPort { name, position, direction, output }`, not the runtime `MoonWorld/ConveyorPort`
  (see "Belts and ports" fact (d)).
- `CheckConveyor(builder, plan, out reason, out path, out price)` has an `out Il2CppSystem.Decimal` (a struct), so it was
  not called. `ConveyorRouteValid(plan, kit, builderPosition, out path, out reason, out routable)` (a class and two
  primitives out) is the route check the dev `AutoBelt` uses, and `TryBuildConveyor(builder, plan, out reason, out id)`
  builds; both ran without trouble.

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
  `MoonDepotSave.Write`, after it. (Since the final fix wave the modded depots stay out of `state.depots` until the
  `Serialize` finalizer, so the production strip matches against the depots the depot prefix took; see "Per-depot
  savers audit".)
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

## Co-op session (Task 12, from the interop surface, 2026-11-02)

Source: `research/dump_MoonCrewSession.txt`, `dump_MoonSteamLobby.txt`, `dump_MoonCrew.txt`, `dump_MoonSteam.txt`
(`research/tools/dump-types.ps1`), plus `Steamworks.SteamMatchmaking` from `com.rlabrecque.steamworks.net.dll`. The
interop assemblies carry signatures only, no method bodies, so the flow below is read from names, parameter lists and
the `Kind` enum. Nothing here was traced in a two-player session (no second machine).

The join flow, client side first:
- Entry points: `MoonCrewSession.JoinShift()` (a gathered lobby member joins the shift the host started) and
  `JoinListedShift(ulong lobby, string crewName)` (joining a running shift from the lobby browser). Both lead to
  `BeginJoin(CrewJoin j)` and the coroutine `Join(CrewJoin j)` (`_Join_d__265`), ticked by `UpdateJoining()`.
  `CrewJoin` holds `lobby`, `host`, `crewName`, `idBlock`, `offered`, `ready`, `failed` and a `Caption` the join UI
  shows.
- The client asks for the world (`Kind.WorldRequest`); the host answers `WorldOffer` (assigning an id block with
  `AssignIdBlock(world, crewmate)`) or `WorldUnavailable`. On the client the offer lands in
  `Offered(CrewJoin j, bool signed, int idBlock)`. The host then streams the save (`SendWorld(to)` ->
  `StreamWorld(...)`, `Kind.WorldPiece` / `ChunkPiece`, files tracked as `IncomingFile`, re-asks via `WantedFiles`)
  on `TransferChannel`; the client unpacks it (`ReceivePiece`, `TickDownload`, `UnpackWorld(blob, host)`), and
  `ShowContract` / `DeclineCrew` follow.
- `EndJoin(CrewJoin j, string why, bool leave)` is the session's own way to end a join with a reason, leaving the
  lobby when `leave` is true. This is the refusal path StructureLib uses.
- Wire: `Message(Kind)` starts a `MemoryStream` (protocol byte, then the kind); `SendTo(to, message, channel)`,
  `SendToCrew`, `SendBytes(to, data, length, channel, reliable)`; `Receive()` dispatches to `Handle(sender, size)`,
  `ReceiveTransfer` and `HandleWorld`. `Kind` is one byte-sized enum, dense from 10 (`Stroke`) to 85 (`DustPageUp`),
  all under the single `Protocol` byte. Channel numbers are statics (`WorldChannel`, `TransferChannel`) whose values
  the dump does not show.
- `MoonSteamLobby` wraps `SteamMatchmaking`: `InLobby`, `LobbyId`, `IsHost`, `HostId`, the static `members`
  (`List<Member>`: `id`, `name`, `host`, `local`), `Kick` / `Ban` / `PutOut` / `CloseSession`, `RemovalMessage`.
  Vanilla already publishes a version in lobby metadata (`Listing.version`, `Listing.compatible`), so lobby
  metadata is a channel the game itself reads for compatibility.
- Host-side display: `MoonCrewSession.Notify(string)` (an on-screen notice, `notice` / `noticeUntil`, drawn in
  `OnGUI`).

Hook chosen (Task 12): **Steam lobby data at two levels** under the key `structurelib.manifest`, plus a prefix on
`MoonCrewSession.Offered`.
- Every StructureLib player in a lobby publishes its manifest (`HandshakePayload.Encode`, base64) with
  `SteamMatchmaking.SetLobbyMemberData`; the host also publishes it with `SetLobbyData`. Steam returns member data
  only to lobby members, and nothing shows that a `JoinListedShift` client is a member before the offer; lobby data is
  readable from a listing too (vanilla's `Listing.version` comes from there). Each value is read back every 2 s and
  written again if missing (a dropped write is retried). A manifest over Steam's 8192-character value limit is
  published as the marker `too-big`.
- The client compares in a prefix on `Offered`, the first point where the join is known to be talking to this host
  and before any world byte is downloaded. It reads lobby data first, member data second. On any problem it calls
  `EndJoin(j, why, true)` with a line built from `Compare`'s messages and skips `Offered`.
- Admit on uncertainty (`HandshakePayload.ReadHost`): an empty value means "host without StructureLib" (empty
  manifest) only when the client is verifiably in that lobby (`MoonSteamLobby.InLobby && LobbyId == j.lobby`).
  Otherwise, and for the `too-big` marker or an unreadable value, the client logs why and lets the join through.
- The host reads each crewmate's member data and logs (and `Notify`s) problems; it does not kick. The comparison is
  symmetric, so the client already refuses on the same messages.
- Why not the alternatives:
  - A StructureLib `Kind` on the session's own messages: nothing in the interop surface shows that `Handle` ignores an
    unknown kind (no bodies), the enum is dense and owned by the game (a future update could take the value), and it
    would ride under the vanilla `Protocol` byte, which spec S4 rules out.
  - A private Steam networking channel: sending is easy (`SendBytes` takes primitives and an array), but receiving
    needs `SteamNetworkingMessages.ReceiveMessagesOnChannel`, the native `SteamNetworkingMessage_t` layout and its
    release, and the session-management calls take `SteamNetworkingIdentity` by `ref`: the struct-by-reference
    interop class that crashed `TryPanel`.
  - The world-file stream (`IncomingFile`, `wantedFiles`): host to client only, runs after the join is accepted, and
    every file in it is written into the client's world folder.
- Calls used take and return strings, primitives and `CSteamID` by value (a one-`ulong` struct; no `out`/`ref`).

## Belts and ports (M1, 2026-11-02)

Traced in game on slot-2 with the dev `AutoBuild` (ring layout), `AutoBelt`, `AutoFeed` and temporary trace postfixes
(20 lines per key, removed afterwards) on `EnterMachine`, `FeedProcessor`, `PortCargo`, `MachineJoins`,
`MachineOutfeed`, `EmitProduct`, `EmitCargo`, `BeltsAtPorts`, `MachinePortSnaps`, `HandOff`, `Enter`, `MachineInputAt`
and `FindMachineInput`, plus direct probe calls. Logs: `.superpowers/sdd/2026-11-02-structurelib-belts-and-models/
task-1-logs/` (run1 = chamber → grinder, run2 = grinder → chamber, run3/run4 = `PortCargo` prefix and donor facts). Ids
in the excerpts are depot / belt / canister numbers; the only player id is the single-player `local`.

Ports and belts, as the game reports them:
- A built depot's runtime ports come from `ConveyorPorts(near, radius, list)` (filter `structure == depot id`); their
  `label` is `PortLabel(def, port)` ("Pressure chamber input"), and `PortName(def, port)` is "input" / "output". The
  definition's own names are `IN`, `OUT`, `IN_DUST`, `IN_SAND`, `OUT_A`, …. `PortPoints(record)` returns
  `List<Vector3>` (positions only, no directions).
- **A port's `direction` is the belt's flow direction at that port, for inputs and outputs alike** (the chamber's `IN`
  and `OUT` both point local +X). The plan that built is `ConveyorPlan { start = output port position, startDirection =
  its direction, end = input port position, endDirection = its direction, startUp/endUp = position.normalized,
  startStructure/endStructure = the depot ids, startBelt = endBelt = replaces = -1 }`:
  `[dev] AutoBelt '96:output->ryanhartwig.example-grinder:input': route +start,+end (unset ids -1) valid=True routable=True length=15.37`
  then `[dev] belt 96:output -> ryanhartwig.example-grinder:input id=1 ok=True reason='Belt built.'` The belt's
  snapshot end lands exactly on the port (`end-to-port 0.000 m`). Refusals seen: `'Turn too tight: give it more room.'`
  (an output facing away from the input) and `'Blocked by a structure.'` (reversed directions).
- **Layout rule for scripted belts: each output port must face its input port.** Every donor seen has its ports along
  local +X (inputs at −X, outputs at +X, flow +X), and `AutoBuild` builds at yaw 0, so on slot-2 the downstream machine
  must stand on the +X side of the upstream one. The dev ring layout places entry i at 10 m from the walker at 0°, 90°,
  180°, 270° (then 45°, 135°, …) from its facing; on slot-2 90° is always blocked, so the entry whose heading it is
  falls through to the next free heading, and 0° → 180° runs along +X. Working orders on slot-2:
  - **`96,ryanhartwig.example-grinder,94`**: chamber at 0°, grinder at 180° (90° blocked), twin at 270°; belt
    `96:output -> grinder:input` builds (15.37 m). Likewise `grinder,96,94` for `grinder:output -> 96:input`, and
    `96,97,94` for `96:OUT -> 97:IN_SAND` (15.38 m).
  - **The brief's `94,96,ryanhartwig.example-grinder` does not work**: twin at 0°, chamber at 180° (90° blocked),
    grinder at 270°, so the chamber's +X output faces away from the grinder's input and every route is refused
    (`'Turn too tight: give it more room.'` / `'Blocked by a structure.'`). Another layout needs a yaw for AutoBuild.
- `PortName` and `PortLabel` are not unique per structure: the stone press's `IN_DUST` and `IN_SAND` are both `input` /
  `'Stone press input'`, and their runtime ports both carry that label. A port is identified by its definition `name`
  (or index) and found at runtime by position: the definition's local position placed by the depot's view pose lands
  exactly on the runtime port (`port 'IN_SAND' local (-4.10,0.85,1.25) -> expected (27.47,1992.83,-24.59), matched
  runtime 'Stone press input' at (27.47,1992.83,-24.59) (0.000 m)`; the belt then ends there, `end
  (27.47,1992.83,-24.59) (0.000 m from depot 27 …)`, the press centre being at z −25.84).
- Belt numbers: `MoonConveyorKit` `minLength 0.75 maxLength 30.00 minRadius 0.49 maxSlope 45.00 minHeight 0.35
  maxHeight 6.00 pricePerMetre 0.50 deckHeight 0.85`; `PortTolerance 0.05`, `PortReach 1.20`, `ConveyorReach 40.00`,
  `BeltSpeed 1.10` m/s, `BeltSpacing 0.75` m, `BeltRideHeight 0.03`, `ProductEmitSeconds 0.35`.

Facts:
- **(a) Belt input goes through `HandOff(belt, path, id)`; `EnterMachine` and `FeedProcessor` were never called** (0
  trace lines in all seven runs, including canisters the chamber accepted). When a canister reaches the end of a belt
  joined to an input port, the game asks `FindMachineInput(point)` / `MachineInputAt(point)` (both True for the
  chamber's and the grinder's input point) and then calls `HandOff` every frame:
  - Vanilla chamber, dust: `HandOff(belt 1, id 10) -> True; after: canister 10 gone`, and the chamber's slot 0 went
    0 → 1 kg (run2). `PortCargo` was not called on this path (its only trace lines were the probe's own calls).
  - Vanilla chamber, wrong cargo (grit): `HandOff(belt 1, id 9) -> False` every frame; the canister stays a rider at
    the belt end (`d 15.37` of `15.37`, `moving False`, `WaitingAtEnd True`), and the chamber's status reads
    `'A example grit capsule is waiting at the dust input'`.
  - **Example grinder (modded, clone of the chamber's ports), dust: refused.** `MachineInputAt((28.62,1992.83,-25.84))
    -> True`, then `HandOff(belt 1, id 8) -> False; after: canister 8 cargo 0 … riding True … rider belt 1 d 15.37
    moving False` every frame (run1). The canister is not dropped: it sits on the belt at the end, and every later
    canister queues behind it at `BeltSpacing` (`[9 d 14.62 …] [10 d 13.87 …] …`). The grinder's status stays `''`
    (no "waiting" text). Since the cargo was right and `PortCargo` was not consulted, the refusal is inside `HandOff`,
    by kind (native, not observable further). The belt-input bridge of spec call 1 is needed, and its hook point is
    `HandOff` (instance method; a canister refused there stays riding at the end, so "nearest rider at the input port
    point with `WaitingAtEnd`" finds it).
- **(b) Vanilla emits 1 kg per `EmitCargo` call, one canister per call, about every 0.7 s.** `EmitProduct` runs every
  `ProductEmitSeconds` (0.35 s) and calls `EmitCargo(belt, owner, cargo, 1 kg)`, which alternates True / False while
  the belt entrance is not clear (`BeltSpacing` 0.75 m at 1.1 m/s): `EmitCargo(belt 1, owner local, cargo 1, kg 1) ->
  True at t=41.1`, `-> False at t=41.5`, `-> True at t=41.8`, … Each sand canister holds `1.0 kg` (capacity 1). The
  chamber's two 5 kg cycles became ten 1 kg canisters (run1). With the belt blocked at its far end the canisters fill it
  (11 riders on a 15.37 m belt); a full belt makes `EmitCargo` return False and the output slot then fills (S0 fact (d)).
  The example grinder emits the same way: `EmitCargo(belt 1, owner local, cargo 250, kg 1) -> True` (run2): 1 kg per
  canister even though grit's `UnitKilograms` is 3.
- **(c) A modded cargo (grit, 250) canister rides a belt end to end and keeps its cargo.** Created with
  `DevSetCarriedDust(player, kg)` + `PackDustCanister(player, point, up, out id)` (a 1 kg / capacity 1 dust canister),
  cargo set by rewriting the record through `MoveCanisterRecord(new MoonDroppedCanister(… cargo 250), …)` (as the
  sidecar does), and put on the belt by `MoveCanisterRecord` to a point on the belt (`BeltPath(belt).Point(d)` + up ×
  `BeltRideHeight`): the game boarded it within 2 s (`riding True rider belt 1`). Setting the cargo while it rode also
  held (`set cargo 250 while riding -> ok; … rider belt 1 d 6.18 moving True`). Both rode 15.37 m to the chamber's
  input end and stopped there with `cargo 250`. `CarryDustCanister(player, id, walkerPosition)` returned False (walker
  ~10 m away), so `PlaceCarriedOnBelt` was never reached: **not tested**. The grinder's own emitted grit canisters
  (fact b) rode the same way.
- **(d) `PortCargo(MoonDepotBuildDefinition/ConveyorPort port)` takes the definition's port, not the runtime one**
  (signature checked against the interop assembly; the spec assumed `MoonWorld/ConveyorPort`). So a prefix sees only
  `{ name, position, direction, output }`: no depot id, no label. Its answer looks name-based: `IN_SAND` → Sand, every
  other port (`IN`, `IN_DUST`, `IN_A`, `IN_B`, and all outputs `OUT`, `OUT_A`, `OUT_B`) → Dust; seen for the chamber
  (`IN` Dust, `OUT` Dust), the stone press (`IN_DUST` Dust, `IN_SAND` Sand, `OUT` Dust), packager 10, splitter 11,
  merger 12, robot vacuums 91/92, garage 98 and capsule catcher 100. Vanilla code never called it in these runs (only
  the probe did), so what reads it (the belt tool's port labels or snapping, most likely) is not seen. A log-only
  prefix is safe: two clean world loads (`[spike] PortCargo PREFIX ran: port 'IN_SAND' output False`, self-test 23/23
  both runs); see "Interop hazards found".
- **(e) Donor facts** (from `BuildDefinition(kind)`; renderer bounds from the prefab instantiated under an inactive
  holder: every renderer's mesh bounds taken into root space; all renderers are enabled and active, so "all" and
  "enabled" agree; prefab root scale 1 in all three):

  | | Pressure chamber (96) | Stone press (97) | Solar post (93) |
  |---|---|---|---|
  | `halfFootprint` | (4.11, 2.63) | (4.63, 3.22) | (0.91, 0.64) |
  | `height` | 6.16 | 6.14 | 1.44 |
  | `floorOffset` | 0.02 | 0.02 | 0.02 |
  | `scale` | 1.00 | 1.00 | 1.00 |
  | `reach` | 22.00 | 22.00 | 16.00 |
  | prefab | `pressure_chamber` (29 renderers) | `stone_press` (56) | `solar_post` (10) |
  | renderer bounds min | (-4.08, -0.00, -2.60) | (-4.60, -0.49, -3.19) | (-0.89, -0.00, -0.61) |
  | renderer bounds max | (4.08, 6.17, 2.60) | (4.60, 6.16, 3.19) | (0.89, 1.46, 0.61) |
  | `conveyorPorts` | `IN` input (-2.95, 0.85, 0.00) dir (1, 0, 0); `OUT` output (2.95, 0.85, 0.00) dir (1, 0, 0) | `IN_DUST` input (-4.10, 0.85, -1.25) dir (1, 0, 0); `IN_SAND` input (-4.10, 0.85, 1.25) dir (1, 0, 0); `OUT` output (3.90, 0.85, 0.00) dir (1, 0, 0) | none |

  Port positions are structure-local, as the definition stores them (y 0.85 = the kit's `deckHeight`). The production
  views sit on the prefab root (`MoonPressureChamberView`, `MoonStonePressView`, `MoonSolarPanelView`).
  **The solar post rotates nothing.** Its `MoonSolarPanelView` is on the root, and its `head`, `array`,
  `actuatorBarrel` and `actuatorRod` are all null, both on the prefab and on a built post (`[spike] post 26 view:
  MoonSolarPanelView on '<root>' head 'null' array 'null' barrel 'null' rod 'null' yaw 0.00 tilt 35.00 … rested True`).
  Over 2 s and 30 s, `0 of 15 transforms rotated` (runs 3 and 4). The post's hierarchy is flat: ten material meshes
  (`MC _ Cyan frosted diffuser`, `Solar _ PV module glass`, …) and `Colliders/Plinth` directly under the root, all at
  identity; the 35° panel tilt is in the mesh. So a holder parented to the template root is under no animated transform.
  The grinder's clone carries the chamber's two ports unchanged (`'Example grinder input'` / `'Example grinder
  output'`); the burner's and the lamp's clones have none. Card pictures of every catalog entry (101, vanilla and
  modded) were written by `[Dev] DumpCards` to `<game>\BepInEx\cache\structurelib\cards\<kind>-<name>.png` and copied
  to `task-1-logs/cards/` (`96-pressure-chamber.png` 338×373, `97-stone-press.png` 370×357, `93-solar-post--s.png`
  378×382, cut from 512×512 atlases).
- **(f) The definition-driven belt path reads the clone's ports; only input is refused, inside `HandOff`.**
  - Belt building: `TryBuildConveyor` built belts both to the grinder's input (run1) and from its output (run2), with
    the grinder as `startStructure` / `endStructure`. `MachineWithPort(point, output)` → 27 (the grinder),
    `MachinePortAt` → True, `PortTaken` / `FreeMachinePort` flip once a belt is joined, `BeltEndingAt(input port)` → 1.
  - `MachineOutfeed`: called only by the probe (vanilla's emit path did not go through the patched method; it is
    inlined or cached). Probe: `MachineOutfeed(26/kind 529738499) -> belt 1` with a belt on the grinder's output (run2),
    `-> null` without one; the chamber the same (`MachineOutfeed(26/kind 96) -> belt 1`).
  - `EmitProduct`: runs for the grinder (vanilla body; StructureLib's prefix only skips power producers) and emits:
    `EmitProduct(depot 26 kind 529738499) after: stores [8.0 2.0 0.0]` with `EmitCargo(belt 1, owner local, cargo 250,
    kg 1) -> True` — **belt output from a modded machine works with no library code**; 10 kg dust became ten 1 kg grit
    canisters on the belt.
  - `BeltsAtPorts(def, position, rotation)`: called only by the probe; for the grinder's clone at its view pose it
    returned `1 [1]` once the belt existed (the chamber's: `1 [1]`), 0 before.
  - `MachineJoins(kind, runtime port)`: called only by the probe. Raw answers: with no belts, chamber `IN`/`OUT` False
    and grinder `IN`/`OUT` True (run0); with the belt on the grinder's input, grinder input False and output True,
    chamber input False and output False (run1). Its meaning is unclear (not "accepts a belt": the vanilla chamber
    answers False for free ports that do take belts).
  - `MachinePortSnaps(def, kind, aim, MachineSnapReach, held)`: called only by the probe, aiming 3 m out from each
    port. Chamber: 0 snaps for both ports. Grinder: input 0, output `1 snaps [belt 0 port 0 machine 27 machinePort 1
    beltEnd False score 0.05]`. Not a belt-tool session, so this is a weak signal only.
  - None of `MachineJoins`, `MachinePortSnaps`, `BeltsAtPorts` was called by `TryBuildDepot` or `TryBuildConveyor`;
    they belong to interactive placement, which the spike did not drive.
  - No refusal by kind before the ports are read, except the input transfer in `HandOff`.

Side observations:
- `world.machineInputAt` was empty when read 1 s after the belt was built (filled lazily, or keyed elsewhere), yet
  `MachineInputAt` answered True.
- In a world whose chamber had sand in its output and no belt (slot-2's own depot 2), `EmitProduct` keeps setting
  `'Connect a belt to the sand output'` (S0 fact (d) again).

Belt input into modded machines (Task 3, 2026-11-02; logs `task-3-logs/`, run2 = 31 passed, 0 failed):
- **`HandOff(int belt, MoonConveyorPath path, int id)`**: `belt` is the belt id (key of `world.conveyors`), `path` is
  that belt's path (its `end` is the belt's end, which is the joined input port's point to 0.000 m), and `id` is the
  canister id (key of `world.canisters`, which still holds the canister while it rides; `world.riders[id]` gives its
  `belt`, `distance`, `moving`). StructureLib's prefix resolves the machine with `MachineWithPort(path.end, false)`
  (the depot id) and the port as the resolved `PortDefinition` whose local position, placed by the depot's view pose,
  is within `PortTolerance` of `path.end` (`Belt 1 feeds 'ryanhartwig.example-grinder' depot 27 through its port 'Dust
  in' (dust)`), and caches that per belt. Answering true after the game's own `RemoveCanister(id)` is clean: the rider
  is dropped and the queue advances (three 1 kg dust canisters taken one after the other, no exception; the canister
  after the released one moved up to `d 15.37`). Answering false leaves the canister riding at the end with
  `WaitingAtEnd(belt)` True and later canisters queuing at `BeltSpacing` (five sand canisters at 15.37, 14.62, …,
  12.37, then a grit canister behind them at 11.62), exactly as vanilla leaves a refused one. A partial take
  (`MoveCanisterRecord` with fewer kilograms) is implemented but not exercised in game (canisters are 1 kg).
- **Status text:** `MoonProductionState.Flow(string)` is vanilla's setter for the flow status, and `Status` reads it
  back. The prefix sets vanilla's wording on a refusal: `'A moon dust capsule is waiting at the dust input'` (slot
  full; `CargoName(Dust)` is "Moon dust", lowered) and the same form for sand at the dust port. Without a refusal the
  grinder shows its other statuses (`'Connect a belt to the example grit output'` from `EmitProduct` when its output
  holds grit and no belt is joined).
- **`PortName` / `PortLabel` (Task 2):** both derive from the port's output flag only: `PortName` is "input" /
  "output", `PortLabel` is "<display name> input" / "<display name> output" (`'Example grinder input'`). The raw
  `sl:<cargo>:<Name>` port name never shows in the game's UI.
- **A structure without a processor cannot declare input ports:** `PortRules.CheckPorts` needs an Input slot of each
  input port's cargo, so declared input ports on a processor-less structure drop it at freeze (a plain structure that
  leaves `Ports` null keeps its donor's ports unchecked, and belts into them are refused, as before).
- **The lunar clock runs in real time,** so a restored slot-2 is often at night and the solar twin then supplies
  nothing: the chamber made no sand in run1 (`grid 26 power 0.00`). Dev `[Dev] SunOverhead = true` sets the lunar time
  whose sun is overhead at the walker 5 s after load (`LunarTime 0.612 -> 0.822 (sun dot up 0.689), Daylight 1.00`);
  the belt checks need it.

Belt output and save with belts (Task 4, 2026-11-02; logs `task-4-logs/`, run1 = 45 passed, 0 failed; run2 (reload)
= 33 passed, 0 failed):
- **Layout.** `AutoBuild = ryanhartwig.example-grinder,ryanhartwig.example-burner,ryanhartwig.example-lamp`: grinder at
  0°, burner at 180° (90° blocked for the burner), lamp at 90° (the smaller lamp fits there). The grinder's
  `Grit out` faces the burner's `Grit in` along +X; `AutoBelt` built belt 1 on the first route tried (`+start,+end`,
  17.07 m). Adding the solar twin (`...,94,...`) puts the grid on solar power, and the Task 11 check
  `power-grinder-runs-on-burner` then fails for config reasons (the burner supplies 0 kW while the twin covers the
  grinder; `task-4-logs/run1a-with-twin.log`). The burner alone (fed 4 kg of grit by `AutoFeed`) powers the grinder.
- **Emit cadence (fact (b), again for a modded machine):** `CargoEmitted` 1 kg grit per `EmitCargo` call, gap 0.70 s
  between two emits (run1a); the canister is 1 kg / capacity 1 although grit's `UnitKilograms` is 3. The first emitted
  canister is a rider at `d 0.00` (moving) right after the emit.
- **The outfeed path has no machine argument.** `EmitCargo` is an instance method `bool EmitCargo(MoonConveyorSnapshot
  outfeed, string owner, MoonCargo cargo, double kg, out string reason)`, and `MoonConveyorSnapshot` holds only `id`,
  `owner`, `start`, `startDirection`, `end`, `endDirection` (no structure ids; checked by reflection on the interop
  assembly). `MachineWithPort(outfeed.start, true)` names the emitting machine (`Belt 1 carries the output of
  'ryanhartwig.example-grinder' depot 26`).
- **Chain.** The grinder's grit rode the 17.07 m belt into the burner's `Grit in` and was taken (`CargoEntered` slot 0
  grit 1 kg). A vanilla sand canister put at the belt end is refused by the burner (`'A moon sand capsule is waiting at
  the grit input'`) and holds every grit canister behind it on the belt.
- **Save with canisters on a belt.** Write #7 (requested at t≈47 s; `SoonSaveSeconds` 60, yet the write followed within
  a few seconds) recorded 6 grit canisters (ids 9–13, 15) in the sidecar while they rode belt 1; no `Moon save failed`,
  no vanilla write threw, `vanilla-save-clean` PASS. The belt is a vanilla record (endpoints only) and needs nothing.
- **Reload.** The sidecar restored the 6 grit canisters under their own ids (`restoring a 'ryanhartwig.example-grit'
  canister as canister 9 (1/1 kg)`, …, `6 of 6 restored canisters hold their modded cargo`), and they were riders on
  belt 1 again by the time the self-test ran (20 s after load), queued behind the sand at 17.07, 16.42, 15.92, 15.17,
  14.42, 13.67, 12.92 m, all `moving False`, cargo 250. The vanilla sand canister came back on the belt the same way.
  `MoonConveyorSave` restored belt 1 between the restored grinder (26) and burner (27); the burner's production state
  came back as saved (`stores [0:1 1:0]`).
- **Models (Task 5, slot-2, 2026-11-02).** The burner with `Model = "example_burner.bin"` on the solar post: the swap
  hid the post's 10 renderers (mesh bounds in template space: donor bottom 0.000), model bottom 0.000, holder y 0.000,
  scale 1.00; the built view keeps the prefab's 3 colliders, all 10 donor renderers stay disabled, the 3 model
  renderers are enabled, and `StructureLibModel` is a direct child of the view root. **No player equipment exists when a
  world's clones are built** (`MoonPrototypeModels` not found at world load), so the model's materials start as tinted
  stand-ins copied from the donor's first material; the first run used them for the card (all cyan). They are now
  turned into the game's prototype materials in place once the equipment exists (`StructureMaterials.Upgrade`, within
  seconds of load), and the card waits for that. A missing file logs one error and keeps the post's model and the
  declared port. Fix round 1: the model's lowest vertex now goes on template-local y = 0, where the chamber's and the
  post's meshes start, and not on the donor's lowest bound. The press's bound is at −0.49 with its foundation under
  ground. `floorOffset` (0.02) is not added, because the game applies it to the placed root. `Port_*` nodes are
  composed through their parent chain and moved by the holder's scale and offset, so the ports stay on the geometry.
- **The SandPower chain (Task 6, slot-2, 2026-11-02; logs `task-6-logs/`, run1 = 34 passed, 0 failed; run2 (reload)
  = 28 passed, 0 failed).**
  - **Layout.** The ring cannot hold a three-machine chain: only 0° and 180° lie on the +X axis through the walker, so
    a third machine downstream of the second has no site. Dev `[Dev] AutoBuildLayout = line` puts the entries on one
    line along the structures' +X (world +X projected on the tangent plane; the built depots' rotation is identity
    there), 1.3 m between neighbouring footprints (centres from each definition's `halfFootprint.x`), centred on the
    walker and 6 m to its side. A first try at 10 m spacing and 10 m to the side put the end entries 18 m away and the
    game refused them: `'Move closer to the build site.'` (the tracker 95, `reach` not read, and the generator on the post, `reach`
    16). With `AutoBuild = 95,96,ryanhartwig.sand-compactor,ryanhartwig.briquette-generator` the centres were
    −11.60, −4.24, 5.80, 12.64 m (half footprints 1.96, 4.11, 4.63, 0.91), 7.3–14.0 m from the walker, all built.
  - **Belts.** `96:output -> compactor:Sand in` 2.98 m and `compactor:Briquettes out -> generator:Briquettes in`
    2.04 m, both valid on the first route (`+start,+end`); each belt end lands on its port to 0.000 m.
  - **Power.** The vanilla solar tracker (95) with `[Dev] SunOverhead`: the chamber, the compactor and the generator
    all on its grid (26); the compactor ran at power 1.00. The twin (94) was not tried: its 7.5 kW peak cannot cover the
    compactor's 12 kW on top of the chamber.
  - **Timings** from the chamber's `AutoFeed` of 12 kg dust: first sand canister on the belt 12.0 s, first sand into the
    compactor 14.7 s, first compactor cycle 24.8 s, first briquette emitted 25.1 s, first briquette into the generator
    27.0 s, first generator charge (3 kWh) 47.7 s. The whole 12 kg became 12 sand canisters of 1 kg (`CargoEntered`
    12x, 12 kg) and, by the check, 8 briquette canisters of 1 kg (`CargoEmitted` 8x, all into the generator,
    `CargoEntered` 8x); briquettes leave as 1 kg canisters although their `UnitKilograms` is 2.
  - **Save and reload.** A briquette canister put on the sand belt waits at its end (`'A sand briquette capsule is
    waiting at the sand input'`); the save after it recorded it in the sidecar with both production records (generator
    stores [8 2] kg, charge 3.000), no `Moon save failed`. The reload restored it on the sand belt at `d 2.98` with
    cargo 3, both belts between the restored machines, and both production states as saved (the generator's batch then
    finished: charge 6.0).
  - **Native crash at load (the load-time card render is the suspect).** One launch of the same build died natively
    about 12 s after the world loaded, while rendering the compactor's card picture (the last log line `Card art for
    'ryanhartwig.sand-compactor': bounds (8.50, 5.95, 4.70), ortho 5.03`; `task-6-logs/run1a-crash-at-load-LogOutput.log`).
    The launches before and after rendered the same card. Evidence found by the Task 6 review:
    - Crash dump `%LOCALAPPDATA%\CrashDumps\MoonSweepers.exe.874884.dmp` (04:52:24) and the game's own `Crash
      reports\crash 2026-11-02 04-52-11.txt` ("no sign of life written").
    - Exception `0xC0000005`, a null read at `UnityPlayer.dll+0x10a652f`.
    - `MoonSweepers.exe.492412.dmp` (2026-11-01 20:57) has the **identical** signature. It was the first plan's Task 7
      silent load death, when no `Model`, no `.bin`, no stand-in materials and no large mesh existed.
    - In both, the BepInEx log ended inside `IconRenderer`'s first or second icon-camera render (after the `bounds …
      ortho` line or after the reframe line), and the game's log ended just after "Unloading … unused Assets".
    - Both followed a relaunch 5–9 s after a forced kill.
    - The other dumps on the machine do not match: the known `IsProcessor` crash (`GameAssembly.dll+0x6ef5d4`) and two
      `coreclr.dll+0x1d1fdd` faults.
    Conclusion: the load-time card render (first-plan code, `IconRenderer`) is the suspect, not the SandPower models; 2
    occurrences in roughly 40 loads. To reproduce, vary one thing per arm, about 20 launches each: the relaunch delay
    after a kill (under 10 s vs 30 s or more); card rendering on vs off, or delayed a few frames after "Session in
    game"; the icon camera's MSAA (8x vs 1x); SandPower installed vs the example only. Read the native stack with
    WinDbg and Unity's symbol server.
  - **Mitigation shipped, unproven (final fix wave).** `IconRenderer` now starts no render until 5 ticks after the
    model materials change (the stand-in shader swap) or `QueueAll` runs, and creates the icon camera disabled,
    enabling it a frame later, so no camera is created, first draws, or follows a shader swap in the same frame. The
    crash has not been reproduced, so whether this helps is unknown; the reproduction arms above still stand.

## Still open

- `PlaceCarriedOnBelt` with a carried canister (needs a walker in reach). What vanilla code calls `PortCargo`,
  `MachineJoins`, `MachinePortSnaps` and `BeltsAtPorts` (the interactive belt tool and machine placement).
- `SolvePower` with satisfaction between 0 and 1 (battery drained under load). Whether the press, other batch sizes
  and several owners follow the chamber's pattern. A build through a completed (`Valid=True`) placement check.
