# StructureLib: modded structures, machines, cargo and power for Moon Sweepers

Design for the second shared library in this monorepo and its first consumer. Written 2026-11-01 from the recon in
`research/STRUCTURES_RECON.md`. Decisions marked **(call)** are the author's; everything else came from Ryan.

## Intent

Let mods add new buildings and machines to Moon Sweepers the way GarageLib lets them add vehicles: register a
definition, and the library sells it in the build menu, places it, runs it, powers it, saves it safely and keeps
the vanilla save clean. The first consumer is a two-machine power chain: a compactor that presses moon sand into a
solid fuel, and a generator that burns that fuel to power the vanilla grid. Both are belt-fed like the vanilla
machines.

Why now: GarageLib proved the pattern (grow the game's static tables, remap per-index lookups to a vanilla donor,
sidecar the save). The repo has just been widened to "end-game enhancement mods", and buildings are the next family.
Ryan's framing: no shortcuts, the infrastructure must be scalable and maintainable, and it lives in this monorepo.

## Goals

1. A mod can register a structure in a few lines and get: a card in the build menu under a category, placement with
   the vanilla ghost, snapping and validation, a model, a price, removal with refund, and a save that survives the mod
   being removed.
2. A mod can register a **processor** (belt-fed machine with input slots, batch slots, an output slot, a cycle time
   and a kW demand) and a **power source** (a node that supplies kW to the vanilla grid), with their state driven by
   the vanilla production and power loops, so vanilla machines and modded machines interoperate on the same belts
   and grids.
3. A mod can register a new **cargo** kind (name, short name, unit kg, sale value) that rides belts, sits in canisters,
   enters machine ports and sells at the collector, without being mistaken for dust anywhere.
4. The vanilla save never contains a modded kind or cargo. Modded structures, their production state and modded cargo
   live in a sidecar beside the save. Removing a mod orphans its records in the sidecar; reinstalling restores them.
5. Tooling: one scaffold script for every package kind, and one dev loop script. Everything that can be checked
   without a human in the game is checked by a dev-build self-test.

## Non-goals (this milestone)

- Co-op synchronisation of modded kinds. Same deferral GarageLib made. The design must not block it: ids are strings,
  kinds are assigned deterministically, and the host-authoritative hooks are noted.
- A custom build-menu UI. Modded cards use the vanilla catalog and card art pipeline.
- New placement rules (new footprint shapes, new snapping). Modded structures borrow a vanilla `BaseStructure`'s
  `MoonFoundationModule` and footprint. New shapes are a later milestone.
- **Modded foundations, decks, walls, rails and catwalks (anything with a `MoonFoundationModule` that other pieces can
  stand on or join).** **(call)** A vanilla piece standing on a modded support would leave a dangling `support` id in
  the vanilla save when the mod is removed, breaking the save-safety anti-goal. Registration rejects a `BaseStructure`
  whose definition has a `module` with `Deck`, `BuildingPart`, `CatwalkSupport` or `WorldGrid` set, with a clear log
  line. Machines, services and power nodes only, this milestone. The dev example structure is a floodlight (kind 4)
  clone, not a platform.
- Modded vehicles interacting with modded structures (for example a vehicle unloading into a modded hopper).
- Balancing. Prices, rates and kW are first guesses, tuned in play.

## Anti-goals

- **Corrupting a vanilla save.** If a player removes StructureLib or a structure mod, their save must load in vanilla
  with no trace of the mod. A design that writes kind 193 or a cargo value 3 into the vanilla save is a failure even if
  it works while the mod is installed.
- **Silent dust.** The game treats any unknown `MoonCargo` as dust. A modded cargo that sells as dust, is labelled dust
  or enters a dust slot somewhere is a bug, not a quirk.
- **Owning state the game owns.** Vanilla production and power state stays in `MoonWorld.production`, `powerNodes`
  and `powerGrids`. The library adds entries and answers per-kind questions; it never mirrors the loops.
- **Hidden coupling to kind numbers.** Nothing in a mod, a save or a config refers to a runtime kind; only string ids.

## Constraints (from recon, all verified 2026-11-01)

- Game: Unity 6000.6, IL2CPP (no IL to read), BepInEx 6 be.791 + MoonSweepers.ModEngine 1.0.2. All gameplay types in
  `MoonCleaners`. Harmony prefixes/postfixes on `MoonWorld` statics and instance methods work (GarageLib relies on
  them). Static array setters work through interop.
- A structure is an `int kind`. `MoonWorld.BuildDefinition(kind)` returns
  `DepotDefinition.additionalStructures[kind - 1]` for 1..192 and `null` otherwise. The array has 192 entries and all
  are used (several are unreleased: 5 solar_array, 6 battery_bank, 1 equipment_kiosk...). `MoonDepotSave.MaxStructureKind`
  = 192. Conveyor belts are kind 1000 but are separate records (`conveyors`), not depots.
- Per-kind rules are static switches on `MoonWorld`: `IsSolarPanel`, `IsProcessor`, `IsPowerNode`, `HexPieceKind`,
  `PowerRange`, `Processor(kind) -> ProcessorSpec`, `TryPanel(kind, out PanelSpec)`, `InputSlot(kind, cargo)`,
  `AcceptsText(kind)`, `IsRobotDock`, `IsEdgeRail`, `IsWorldHex/Square/Grid`, `MapKindOf`, `FarSpec`. Also
  `MoonDepotBuilder.ProductionNote(kind)` (card note) and `CanBuild(kind)` (always true in vanilla).
- Catalog: `MoonDepotBuilder.baseStructures` (static `CatalogEntry[]`: kind, name, category, image) and
  `catalogCategories` (static string[]: Recent, Services, Logistics, Production, Power, Structures). The instance
  builds `structureEntries` from them in `BuildCatalog`. Card art is looked up by the `image` string (`CardArt`,
  `catalogArt`).
- Built records: `MoonDepotSnapshot` (id, kind, owner, pose, support, stored, vehicles, style, contributions) in
  `MoonWorld.depots`. Save via `MoonDepotSave.Write/Read` on `MoonLooseDustSnapshot.depots`; production via
  `MoonProductionSave.Write/Read` on `.production` (`MoonProductionSnapshot`: id, progress, running, charge, per-slot
  owner shares; `MaxSlots` 8). Restore via `MoonWorld.RestoreDepots` / `RestoreProduction` (prefixable, same pattern
  as `RestoreOwnedVehicles`).
- Production: `ProcessorSpec` (name, product, runningKw, cycleSeconds, slotNames[], slotCargo[], slotCapacity[],
  output). `MoonProductionState` per machine id (stores[] per slot keyed by owner, Progress, Running, Power, DemandKw,
  UsedKw, Grid, ChargeKwh, SuppliedKw, BatteryKw, SunFactor, Daylight, Cycles). `NewProduction(id, kind)`,
  `StepProduction(dt)`, `CanStartCycle`, `TryStartCycle`, `FinishCycle`, `EmitProduct`, `EmitCargo(outfeed, owner,
  cargo, kg, out reason)`. Input via `CrewProcessorLoad(player, machine, cargo, kg)` and `EnterMachine`.
  Views: `MoonProductionView` subclasses on prefabs (`Setup`, `Animate(state, dt)`).
- Cargo: `MoonCargo` enum Dust 0 / Sand 1 / Stone 2. `CargoName`, `CargoShortName`, `UnitKilograms` (static),
  `CargoValue` (instance: 1 / 3 / 7). Out-of-range values fall through to dust. Cargo rides as
  `MoonDroppedCanister.cargo`; `MoonCargoSnapshot`/premiums record deposits.
- Power: `IsPowerNode` = solar or processor. `PowerNode` (record, state, position, panel, PanelSpec, parent),
  `powerLinks` by `PowerRange`, `powerGrids` (`MoonPowerGrid`: panels, machines, supplyKw, demandKw, usedKw, peakKw,
  satisfaction, storedKwh, capacityKwh). `SolvePower(dt)`. `PanelSpec` (name, peakKw, batteryKwh, range, tilt, tracker).
  `StallPower` 0.08.
- Interop hazards: `TryPanel` with its `out` struct hard-crashes the game from managed code; `BuiltDepots` is not
  foreach-able; `PowerLinks` interface `Count` is a method. See the recon note.
- Repo rules: ids and GUIDs never change after release; no Steam ids in commits; hot paths on the board are
  `src/GarageLib/**`, `shared/**`, `Directory.Build.*`, `GarageLib.slnx`, `package.ps1`, `modpack.json`.
- Toolchain on Ryan's machine: .NET SDK 8 (the `.slnx` needs 9.0.200+, so build per project or upgrade); Steam
  launching does nothing, `MoonSweepers.exe` launches directly with `steam_appid.txt`.

## The how, as decided

### Packages

| Package | Folder | What |
|---|---|---|
| StructureLib **(call: name)** | `src/StructureLib/` | The library. Depends on nothing in the repo (not on GarageLib). |
| SandPower **(call: name)** | `mods/SandPower/` | The first consumer: sand compactor + fuel generator + the fuel cargo. Depends on StructureLib. |
| ExampleStructure | `examples/ExampleStructure/` | Dev-only consumer with a trivial structure (a floodlight clone) and a trivial processor, for self-tests. Not released. |

Both released packages get a `Plugin.cs` with `const string Version`, a README with a changelog, and an entry in
`modpack.json`. `package.ps1` already picks up `mods/`.

### StructureLib public API **(call: shape; mirrors GarageLib so contributors learn one pattern)**

```csharp
[BepInDependency(StructureLib.Plugin.Guid)]
public override void Load()
{
    var fuel = MoonStructures.RegisterCargo(new CargoDefinition
    {
        Id = "ryanhartwig.sand-briquette",     // saved; never change after release
        Value = 3,                              // byte 3..255, claimed in docs/cargo-registry.md; never change after release
        Name = "sand briquette", ShortName = "briquette", UnitKilograms = 2, Price = 7m,
        // optional: canister tint / icon
    }, Guid);

    MoonStructures.Register(new StructureDefinition
    {
        Id = "ryanhartwig.sand-compactor",
        Name = "Sand compactor", Category = "Production", Note = "Presses 4 kg of moon sand into a 2 kg briquette every 8 s. Needs 12 kW.",
        Price = 800m,
        BaseStructure = MoonWorld.StonePressKind,   // donor: prefab, footprint, ports, module, placement rules
        Processor = new ProcessorDefinition
        {
            RunningKw = 12, CycleSeconds = 8,
            Slots = { Slot.Input("Sand", Cargo.Sand, 16), Slot.Batch("Pressing", Cargo.Sand, 4), Slot.Output("Briquettes", fuel, 10) },
        },
        DecorateModel = (go, ctx) => ...,            // optional tint / parts; BuildModel to replace the model
    }, Guid);

    MoonStructures.Register(new StructureDefinition
    {
        Id = "ryanhartwig.briquette-generator",
        Name = "Briquette generator", Category = "Power", Price = 1500m,
        BaseStructure = 6 /* battery_bank, unreleased vanilla definition */,
        Power = new PowerDefinition { PeakKw = 10, BatteryKwh = 6, Range = 24 },
        Processor = new ProcessorDefinition           // a generator is a processor whose cycle makes kW, not cargo
        {
            RunningKw = 0, CycleSeconds = 20,
            Slots = { Slot.Input("Briquettes", fuel, 10), Slot.Batch("Burning", fuel, 2) },
            ProducesPower = true,
        },
    }, Guid);
}
```

- `StructureDefinition`: `Id`, `Name`, `Category` (existing or new; new categories are appended to
  `catalogCategories`), `Note`, `Price`, `BaseStructure` (vanilla kind to borrow from), `CardImage` (vanilla image key
  or PNG bytes; default: rendered like GarageLib's `IconRenderer`), `BuildModel` / `DecorateModel`, `Processor`,
  `Power`, `Seats`-style extras are out of scope. `Index`/`Kind` assigned by the library, `Owner` = plugin GUID.
- `CargoDefinition`: `Id`, `Value` (byte, declared, see allocation), `Name`, `ShortName`, `UnitKilograms`, `Price`
  (decimal per unit), optional `CanisterTint`. Returns a `Cargo` handle wrapping the runtime `MoonCargo` value.
- `ProcessorDefinition`: `RunningKw`, `CycleSeconds`, ordered `Slots` (`Input` / `Batch` / `Output` with cargo and
  capacity), `ProducesPower`. Slot count ≤ `MoonProductionSave.MaxSlots` (8) is enforced at registration.
- `PowerDefinition`: `PeakKw`, `BatteryKwh`, `Range`, and `SupplyMode` (`Solar` = vanilla sun maths, `Fuel` = driven by
  the processor's burning slot). **(call)** Solar mode exists so a "better solar panel" mod is trivial.
- Events: `Built(StructureBuilt)`, `Removed`, `CycleFinished`. Queries: `IsModded(kind)`, `Definition(kind)`,
  `Definition(id)`, `IsModded(MoonCargo)`, `CargoDefinition(cargo)`.
- Registration must happen in `Load()`. The registry freezes and injects on the first world load (prefix on
  `MoonWorld.RestoreDepots`, with `Injection.Ensure()` re-run every few seconds in game as GarageLib does), **not** on
  the first frame: the definition table does not exist at menu time. Static catalog entries and per-kind prefixes work
  from `Load()` onward because they key on kinds assigned deterministically at freeze; the builder guards every prefix
  with "frozen and modded" checks so menu-time calls fall through to vanilla.
- Plugin GUIDs: StructureLib `com.ryanhartwig.structurelib`, SandPower `com.ryanhartwig.sandpower`, example
  `com.ryanhartwig.structurelib.examplestructure` (confirmed by Ryan 2026-11-01; permanent after release).

### Kind and cargo allocation **(call)**

- **Structure kinds are hashed from the id (Ryan and Andrew, 2026-11-01: "more robust").** `kind = 1_000_000 +
  (FNV1a32(Id) % (int.MaxValue - 1_000_000))`. Deterministic across every install, independent of which other mods
  are present, so co-op players don't need identical mod *lists* to agree on numbers (they still need the mod to see
  the structure). The range avoids vanilla 0..192 and the conveyor's 1000. At freeze the library checks for collisions
  between registered ids and with vanilla; a collision is a hard error naming both ids (never silently renumber). The
  hash function and range are frozen constants: changing them is a save-visible break for co-op only (saves hold ids),
  but still a major-version change.
- **No growth of `additionalStructures`.** Sparse kinds cannot live in a 192-slot array, so the `BuildDefinition`
  prefix is the only definition path, and the per-kind prefixes are the only rule path. The game already runs one
  sparse kind (conveyor, 1000), which is the precedent. S1 must prove every vanilla path tolerates a kind with no array
  slot: placement, build, remove, refund, save/restore (sidecar makes the save moot), production, power, crew relay.
  `MoonDepotSave.MaxStructureKind` only matters to the vanilla save reader, which never sees a modded kind.
- **Cargo values are declared, not hashed.** `MoonCargo` is a **byte**, so there are 253 free values and hashing into
  them would collide across the ecosystem. Each `CargoDefinition` carries a required `Value` byte in 3..255; the repo
  keeps `docs/cargo-registry.md` as the ledger of claimed values (SandPower's briquette = 3), and a new cargo claims
  its value by pull request. At freeze the library refuses duplicate values (hard error) and values below 3. Values
  are never written to the vanilla save; see "Save".
- Kind 1000 (conveyor) and the `MoonDepotBuilder.ConveyorKind` path are never touched.

### Injection (what the library patches) **(call: the list; each line maps to a recon fact)**

Definitions and catalog:
- Per modded kind, create a **cloned** definition once per world: `ScriptableObject.Instantiate(base)` then override
  `displayName`, `priceDollars`, and (if the mod supplies them) `prefab`, `conveyorPorts`. Prefix
  `MoonWorld.BuildDefinition` to return it for modded kinds. The array is not touched (see allocation); if S1 finds an
  inlined `additionalStructures[kind - 1]` read somewhere, that call site gets its own prefix and the recon note
  records it.
- Grow `MoonDepotBuilder.baseStructures` with a `CatalogEntry(kind, Name, Category, imageKey)` per modded structure
  and `catalogCategories` with any new category. Postfix `MoonDepotBuilder.CardArt(image)` to return the modded
  sprite for a modded image key. Prefix `ProductionNote(kind)` and `MoonWorld.AcceptsText(kind)`.
- Prefix the per-kind statics for modded kinds: `IsProcessor`, `IsPowerNode`, `IsSolarPanel` (true only for `Solar`
  supply mode), `PowerRange`, `Processor` (returns a `ProcessorSpec` built from the definition), `InputSlot`,
  `HexPieceKind`/`IsEdgeRail`/`IsWorldHex`/`IsWorldSquare`/`IsWorldGrid`/`IsRobotDock`/`MapKindOf`/`FarSpec`
  (remap to `BaseStructure`), and `MoonDepotBuilder.CanBuild`.
- `TryPanel` is **not** called from managed code. For `Power` definitions, prefix `TryPanel` to return false and
  instead fill the `PowerNode.spec` field in a postfix on wherever vanilla creates the node (to be found by the
  builder: `PowerCandidates` / node pool refill in `SolvePower`; the dev self-test asserts a modded panel joins a grid).
  If no safe hook exists, fall back to postfixing `SolvePower` to add supply for modded nodes directly.

Model: prefix on the view setup for modded kinds (the builder finds where the prefab is instantiated on build and
restore; `MoonWorld.RevealBuilt(view, belt)` and `MoonBuildReveal` are the leads). Apply `BuildModel`/`DecorateModel`
and attach `MoonProductionView` only when the base prefab already has one.

Production and power:
- `NewProduction(id, kind)` is static and builds `stores[]` from `Processor(kind)`; with `Processor` prefixed it
  should just work. The self-test asserts a modded processor gets the right slot count.
- `EmitProduct` → `EmitCargo(outfeed, owner, cargo, kg, ...)` takes a `MoonCargo`; our spec's `slotCargo` carries the
  modded value, so canisters come out with it. The self-test asserts an emitted canister has the modded cargo.
- Fuel power: a `ProducesPower` processor's cycle consumes the batch slot and, in a postfix on `FinishCycle`, credits
  `ChargeKwh` on its own `MoonProductionState` up to `BatteryKwh`; the node's supply is driven from that charge (via the
  spec hook above or the `SolvePower` postfix). `SunFactor` is forced to 1 for fuel nodes if vanilla multiplies by it.
  The behaviour spike (below) settles which.

Cargo:
- Prefix `CargoName`, `CargoShortName`, `UnitKilograms`, `CargoValue`, `PortCargo`-adjacent readers for modded
  values. Audit every `MoonCargo` consumer in the dumps (`research/dump_MoonWorld.txt`, canister views, collector
  deposit, premiums, crew sync) and list each in the library README as patched / unreachable / unsupported.
- `MoonDroppedCanister.IsDust` must be false for modded cargo; if it is a computed property on the enum value the
  prefix on the getter covers it.

Save (the most important part):
- Prefix `MoonDepotSave.Write` to pull modded-kind `MoonDepotSnapshot`s out of `state.depots`; finalizer puts them
  back. Same for `MoonProductionSave.Write` on `state.production` (by depot id), `MoonConveyorSave.Write` is untouched
  (belts are vanilla). Canisters: prefix the canister save to swap modded cargo canisters out (record them in the
  sidecar with their stable cargo id) and back.
- Prefix `RestoreDepots` / `RestoreProduction` / canister restore to inject sidecar records (ids → current kinds and
  cargo values) before vanilla restores. Keep `nextDepotId` consistent: modded depots keep their vanilla-assigned ids,
  and the sidecar stores them; on inject, if an id collides with a vanilla record, reassign and log.
- Sidecar file `structurelib.json` in the save slot folder (`MoonProgressStore.Root`, as GarageLib), versioned, with
  orphan handling and `.bak` / `.tmp` writes copied from GarageLib's `Sidecar.cs`. **(call)** StructureLib does not
  share GarageLib's file or code; shared sidecar helpers are a candidate for `shared/` later, once both exist.
- Removal refund: `StructureRefund(id)` reads the definition price; with `BuildDefinition` prefixed it works.

Dev tools (DEVBUILD only, as GarageLib): `[Dev]` config with `AutoLoadSlot` (refuses slot-1 unless `AllowSlot1` is
also set, because Ryan's only save is slot-1 **(call)**), `AutoBuild` (place a list of modded kinds by id near the
player via `TryBuildDepot` with a fixed offset), `AutoFeed` (call `CrewProcessorLoad` on the built machine with N kg of
a cargo), `SelfTest` (runs the assertions below and logs PASS/FAIL lines), `DumpCatalog`.

### SandPower (the first consumer) **(call: names and numbers, Ryan: mechanics)**

- Cargo **"sand briquette"**: compacted moon sand, 2 kg per unit, sells for $5 (between sand $3 and stone $7, so
  compacting is worth a little on its own but burning is the point). Lore line for the README: regolith sand pressed
  into dense bricks that burn hot in the generator's oxygen-fed kiln.
- **Sand compactor**: base = stone press (97). Belt-fed. Sand in (16 kg store), 4 kg per cycle, 8 s cycle, 12 kW,
  briquettes out onto the outfeed. Price $800. Card note states the numbers like vanilla notes do.
  **(call)** Yield follows whatever mass rule vanilla's `FinishCycle` applies to the batch slot (S0 finds it); the
  library adds no yield field in this milestone. If vanilla conserves mass, 4 kg of sand becomes two 2 kg briquettes,
  and the briquette price is set so that compacting alone is a small gain over selling sand (sand $3/kg in, so
  briquettes ≈ $7 per 2 kg unit) and burning is the real reason to build it. Final numbers are tuned after S0.
- **Briquette generator**: base = battery_bank (6, unreleased vanilla model; fallback stone press if the prefab lacks
  a production view). Belt-fed. Briquettes in (10 units store), burns 1 briquette per 20 s cycle, supplies 10 kW while
  burning, 6 kWh battery, 24 m range. Price $1,500. Category Power.
- Both appear in the build menu with rendered card icons; both save to the sidecar; removing SandPower leaves vanilla
  clean and the records waiting.

### Tooling **(Andrew's ask; call on shape)**

- `new-package.ps1 -Kind vehicle|mod|lib -Name X -Author y` replaces `new-vehicle.ps1` (kept as a thin wrapper for
  one release). Templates under `templates/Vehicle`, `templates/Mod`, `templates/Lib`.
- `dev.ps1 [-Only Name] [-Launch] [-Tail]`: closes the game if running, builds the chosen projects (per project, so
  SDK 8 works), installs, launches `MoonSweepers.exe` directly, and tails `BepInEx/LogOutput.log` filtered to the
  repo's log sources. Honors `MOONSWEEPERS_DIR`.
- `research/tools/dump-types.ps1` (already added) regenerates the type dumps.
- `global.json` is **not** added (it would force an SDK). CONTRIBUTING gets a line: `.slnx` needs SDK 9.0.200+;
  otherwise build per project or use `dev.ps1`.

### Milestones

- **S0 Behaviour spike** (research only): copy slot-1 to slot-2 **(call)**, then from a dev-build plugin top up the
  wallet and build a solar twin, a pressure chamber and the belt between them via `TryBuildDepot` and the conveyor
  plan path, near the spawn. Log `MoonProductionState` and `MoonPowerGrid` per second for a day/night cycle, feed
  dust with `CrewProcessorLoad`, and trace `SolvePower`, `PowerCandidates`, node creation, `FinishCycle` (what mass
  rule moves batch kg to the output slot), `EmitCargo`, canister save/restore. Ryan building the same by hand in
  slot-2 is an optional shortcut, not a dependency. Output: answers to the "still open" questions in the recon note,
  written into it; the belt automation becomes the `AutoBuild` dev tool. Gate for S2/S3 design details.
- **S1 Library core**: registry, kind allocation, definition cloning + `BuildDefinition` prefix, catalog + card art +
  note, per-kind remaps, model hook, sidecar for depots. Example structure buildable, saved, orphaned, restored.
- **S2 Cargo + processors**: cargo registry and patches, `ProcessorSpec` synthesis, production sidecar, canister
  sidecar. Example processor turns sand into the example cargo on a belt.
- **S3 Power**: `PowerDefinition`, fuel supply, grid join. Example generator powers the pressure chamber.
- **S4 Co-op handshake (cheap version)**: on join, the host sends its registry (structure ids → kinds, cargo ids →
  values, mod GUID + version each) over a StructureLib-owned channel kept separate from `MoonCrewSession.Protocol`
  (hook point: the same place GarageLib's plan M5 names). With hashed kinds and declared cargo values the numbers
  agree by construction; the client checks for mods it lacks and for version or value conflicts, and refuses the join
  with a message naming them. Then a two-player test with identical mods confirms vanilla relays
  modded kinds and cargo unchanged (`crewDepots`, `CrewProcessorLoad`, `WriteCrewProduction`). Graceful degradation
  (placeholder models for a client missing a mod, late-join state) stays deferred. **(call)** The handshake is written
  so GarageLib can adopt it later (registry-agnostic payload); lifting it into `shared/` is a follow-up board task.
- **S5 SandPower + tooling + docs**: the real mod, `new-package.ps1`, `dev.ps1`, READMEs, modpack entry, CONTRIBUTING
  update, GarageLib README cross-link.
- **S6 Release** (when Ryan or Andrew asks): `package.ps1 -Release`.

### Testing bar

Dev-build `SelfTest` runs in-game and logs one PASS/FAIL line per assertion; a run with any FAIL is not done:
registry froze with expected kinds; catalog contains modded entries with art; `BuildDefinition(modded)` non-null and
distinct from base; placement check for a modded kind completes valid on open ground; building it creates a depot and
(for processors) a production state with the right slot count; feeding sand produces a briquette canister with the
modded cargo value; the generator joins a grid and reports supply > 0 while burning; save → sidecar has the records and
the vanilla snapshot lists none of them; reload → same depots and production state; with SandPower absent the records
are orphaned and the vanilla save loads. Manual checks (noted as untested in the hand-off if skipped): building through
the real menu by hand, belt hand-off between a vanilla press and the compactor, selling briquettes at the collector.

## Left to the builder

- Exact hook points for the model instantiation on build/restore and for `PowerNode` creation (S0 answers them).
- Whether `ProcessorSpec` / `PanelSpec` instances can be constructed from managed code with their arrays set, or must
  be cloned from `ChamberSpec` / `PostPanel` and mutated.
- The card icon pipeline: reuse `GarageLib.IconRenderer` by copy (StructureLib must not depend on GarageLib) or lift it
  into `shared/` (a `shared/Icons` package would then be a hot path on the board; prefer copy for S1, dedupe later).
- JSON sidecar schema details, beyond: version, written, structures (id, depot id, owner, pose, support, stored,
  style, contributions), production (depot id, progress, running, charge, per-slot shares with cargo ids), canisters
  (id, owner, cargo id, kg, capacity, pose), orphans kept verbatim.
- Log source names and verbosity; follow `GarageLib.CoreLog`.
- Where `MoonCargo` is marshalled over the crew wire (`WriteCrewProduction`, canister truth hashes): document as
  unsupported in co-op for now, don't patch.
