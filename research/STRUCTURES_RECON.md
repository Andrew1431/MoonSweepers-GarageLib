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
| 96 | Pressure chamber | Production | `IsProcessor`, `IsPowerNode`, `Processor(k)` = `ChamberSpec` |
| 97 | Stone press | Production | `IsProcessor`, `IsPowerNode`, `Processor(k)` = `PressSpec` |
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
  `IsWorldSquare`, `IsWorldGrid`. All are Harmony-prefixable; none reads a table a mod could grow.
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
- A fuel-burning generator is a power node that supplies without sun: either a "panel" whose sun factor is replaced by
  a fuel state, or a processor whose cycle produces kW instead of cargo. Which is cleaner depends on `SolvePower`'s
  internals (world-time question).
- `MoonPowerLinks` draws the arcs; `ShowPreview(kind, position, rotation, joined)` during placement.

## Interop hazards found

- `MoonWorld.TryPanel(int, out PanelSpec)` hard-crashes the game when it returns true (interop `out` of a boxed
  struct). Read the static `PostPanel` / `TwinPanel` / `TrackerPanel` instead.
- `MoonWorld.BuiltDepots` (`IEnumerable<MoonDepotSnapshot>`) is not foreach-able from managed code; iterate `depots`.
- `MoonWorld.PowerLinks` is an `IReadOnlyList` interop interface; use the `powerLinks` list.

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

## Still open

- What `SolvePower` does per node (supply = `peakKw × SunFactor`, battery charge / discharge, `parent` links), to
  choose how a fuel-burning generator plugs in. IL2CPP, so there is no IL to read: needs a behaviour spike with a
  solar panel and a processor built, logging `MoonProductionState` per tick.
- Where processor input arrives in practice (hand-fed canister vs. belt from a catcher or garage outfeed).
- How the vanilla new-game flow starts from code (`MoonMainMenu.PlayChosen()` and `MoonSession.StartShift()` from a
  plugin both did nothing from the menu). Starting by hand works; dev automation can load an existing slot instead.
