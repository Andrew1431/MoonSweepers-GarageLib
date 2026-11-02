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
  was dust, so it was not observed, but a non-dust canister presumably comes back as dust after a save.

## Still open

- How belt input reaches a processor (catcher / garage outfeed belt into a port, `EnterMachine`, `FeedProcessor`).
- What `EmitProduct` → `EmitCargo` does with a belt attached.
- `SolvePower` with satisfaction between 0 and 1 (battery drained under load). Whether the press, other batch sizes
  and several owners follow the chamber's pattern. A build through a completed (`Valid=True`) placement check.
