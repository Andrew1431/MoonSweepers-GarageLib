# StructureLib

The shared library for modded structures in Moon Sweepers: new buildings in the build menu, machines that turn one
cargo into another, new cargo kinds, and fuel generators. A modded structure borrows a vanilla structure's prefab and
rules, is kept out of the vanilla save (uninstalling a mod never breaks a save), and is checked between co-op players.
[SandPower](../../mods/SandPower) is the first mod built on it; [ExampleStructure](../../examples/ExampleStructure)
shows every registration pattern.

## Using it

Reference `src/StructureLib/StructureLib.csproj` (`Private="false"`), add `[BepInDependency(StructureLib.Plugin.Guid)]`,
and register everything from your plugin's `Load()`. Registering later is refused: the registry freezes when the first
world loads. Pass your plugin GUID as the owner; the co-op handshake reports it with your plugin's version.

```csharp
var fuel = MoonStructures.RegisterCargo(new CargoDefinition
{
    Id = "you.fuel", Value = 42,                 // claim the byte in docs/cargo-registry.md; never change either
    Name = "fuel pellet", ShortName = "pellet", UnitKilograms = 2, Price = 5m,
}, Guid);

MoonStructures.Register(new StructureDefinition
{
    Id = "you.press", Name = "Press", Category = "Production", Price = 800m,
    Note = "Presses 4 kg of sand into fuel every 8 s. Needs 12 kW.",
    BaseStructure = MoonWorld.StonePressKind,    // the vanilla kind it borrows from
    Processor = new ProcessorDefinition
    {
        RunningKw = 12, CycleSeconds = 8,
        Slots = { Slot.Input("Sand", Cargo.Sand, 16), Slot.Batch("Pressing", Cargo.Sand, 4), Slot.Output("Fuel", fuel, 10) },
    },
}, Guid);
```

- **Structures** (`MoonStructures.Register(StructureDefinition, ownerGuid)`): `Id` (stable, `author.structure`),
  `Name`, `Category` (a build-menu tab; a new name adds a tab), `Note` (the card line), `Price`, `BaseStructure` (a
  vanilla kind; foundations, decks, building parts, catwalk supports and the world grid are refused). Optional
  `CardImagePng` or `CardImageKey`; without them the card picture is rendered from the model.
- **Models:** `BuildModel(ModelContext)` returns a replacement root under `ctx.Parent`; it runs once per world and the
  game copies the result for every one built. `DecorateModel(GameObject, ModelContext)` runs on every copy (tint,
  extra parts). `ModelContext` carries the `Definition`, the `Parent` and the donor's vanilla build definition
  (`Donor`). Without either, the donor's model is used.
- **Cargo** (`MoonStructures.RegisterCargo(CargoDefinition, ownerGuid)`): `Id`, `Value` (a byte 3..255 claimed in
  [`docs/cargo-registry.md`](../../docs/cargo-registry.md)), `Name`, `ShortName`, `UnitKilograms`, `Price` (dollars
  per unit). `CanisterTint` is not applied yet. It returns the `Cargo` handle for slots, or `Cargo.None` when
  refused. Vanilla cargo handles are `Cargo.Dust`, `Cargo.Sand`, `Cargo.Stone`.
- **Processors** (`StructureDefinition.Processor`): `RunningKw`, `CycleSeconds`, up to 8 `Slots`: at least one
  `Slot.Input`, at least one `Slot.Batch`, and one `Slot.Output` unless `ProducesPower`. Capacities are kilograms. A
  cycle starts when every Batch slot can be filled to capacity from an Input slot of the same cargo and the Output
  slot has room; it moves the batch into the Output slot at the same mass (4 kg in, 4 kg out).
- **Power** (`StructureDefinition.Power`, with a `ProducesPower` processor): `PeakKw`, `BatteryKwh`, `Range`,
  `SupplyMode.Fuel`. Each burned batch adds half the battery. See [Power](#power) for the rules and limits.
- **Events** (main thread): `MoonStructures.Built` (`StructureBuilt`: definition, depot id, depot record, the decorated
  view) when a modded structure is built and again for each one restored at load; `Removed` (depot id) after one is
  removed; `CycleFinished` (`CycleFinished`: definition, depot id, live production state) when a modded machine
  finishes a cycle. A handler that throws is logged and does not stop the game.
- **Lookups:** `Definition(kind)`, `Definition(id)`, `IsModded(kind)`, `IsModded(MoonCargo)`, `CargoDefinition(...)`,
  `All`, `AllCargo`, `Frozen`, `Disabled`.

## How it works

- **Hashed kinds.** A structure's kind number is FNV-1a of its id, mapped above vanilla's range
  (`1_000_000 + hash % (int.MaxValue - 1_000_000)`), so every install agrees on it without identical mod lists. A
  collision between two ids disables the library for the session (no modded structures or cargo) rather than
  renumbering. Cargo values are declared, not hashed: a byte has too little room.
- **Freeze.** The registry freezes when the first world loads: kinds are assigned, donors are checked, duplicate or
  out-of-range cargo is dropped, and so is any structure whose slots use dropped cargo. Every error names the id.
- **Cloned definitions.** Each world gets one copy of the donor's `MoonDepotBuildDefinition` per modded structure, with
  its name, price and (with `BuildModel`) its prefab. A `BuildDefinition` prefix answers the modded kind with it; other
  per-kind lookups (shape, grid, robots) answer as the donor. Placement, charging and refunds are the game's own.
- **Catalog.** Each structure is appended to the build menu's static catalog (a new tab if its category is new) with
  the image key `structurelib:<id>`; the card picture is rendered from the model once and swapped into open menus.
- **Sidecar.** The game re-reads every save it writes and refuses a kind it does not know, so modded depots, their
  machines' production states and canisters of modded cargo are cut out of the save while it is written, put back
  after, and written to `structurelib.json` beside the save. On load they go back into the load snapshot and restore
  like vanilla ones. Ownership rule: only a record the library confirmed in the world is replaced by its live state on
  the next save; every other record (a missing mod's, or one that failed to restore) is written back verbatim, so
  nothing is dropped or duplicated. A mod that is removed leaves its records waiting until it is installed again.
- **Machines.** A modded processor gets its own `ProcessorSpec`, and prefixes on the cycle methods run the generic
  cycle above, so the game's `StepProduction` drives it like a vanilla machine.
- **Power.** The game's power solve never sees a modded kind; a `SolvePower` postfix attaches modded machines and fuel
  generators to grids afterwards. Rules in [Power](#power).
- **Co-op handshake.** Each player publishes a manifest of its registered structures and cargo; a client refuses to
  join a host whose StructureLib mods differ. Rules in [Co-op handshake](#co-op-handshake).
- **Safety.** If game members the library relies on are missing (a game update), it logs them and disables itself. A
  sidecar failure is logged and never stops the vanilla save or load.

The recon behind all this, with the evidence, is in
[`research/STRUCTURES_RECON.md`](../../research/STRUCTURES_RECON.md).

## Status

| Area | Works (checked in game, single player) | Unsupported | Untested |
|---|---|---|---|
| Registration | Hashed kinds, freeze, donor and cargo checks, refusal messages | Foundations, decks, building parts and catwalk supports as donors | |
| Build menu | Catalog entry, note, rendered card art; placement through the menu's own path (`PlanStructure`, site check, `TryBuildStructure`, which records a free pose) saves and reloads cleanly with its pose (dev `AutoBuildViaPlan`) | | Placing a structure by hand (the real ghost and the remove tool) |
| Build and remove | Build, refund, removal, `Built` and `Removed` | | |
| Save | Sidecar for depots (pose included), production states and modded-cargo canisters; orphans kept; reload restores (see [Save audit](#save-audit)) | A modded robot dock's robots-off setting (resets on load); a modded garage holding vehicles | |
| Cargo | Names, unit mass, price, canister round trip (see [Cargo audit](#cargo-audit)) | Depositing modded cargo at the collector; co-op cargo events; `CanisterTint` (not applied) | Whether `EmitCargo` keeps the modded value on a belt |
| Processors | Cycle, batch moved at the same mass, `CycleFinished`, loading through `CrewProcessorLoad` | Walker loading (`LoadProcessor` refuses: "Not a production machine"); belt input (`FeedProcessor`, `EnterMachine`, `PortCargo`) and canister input into a machine: unsupported and untested, the vanilla port path refuses modded machines | Belt output (an outfeed belt, `EmitCargo`) |
| Power | Machines on a vanilla grid's surplus and batteries; fuel generators charge, supply modded machines and keep their charge across saves; a modded-only group gets a negative grid id | Fuel generators feeding vanilla machines; power links drawn to modded structures; `SupplyMode.Solar`; `Power` without a `Processor`; a generator's `RunningKw` (ignored) | |
| Co-op | Manifest built and compared with itself | Graceful degradation for a client missing a mod | Both two-player cases (identical mods; client missing a mod) |

## Power

A modded machine (a `ProcessorDefinition`) and a modded fuel generator (a `ProcessorDefinition` with `ProducesPower`
plus a `PowerDefinition` with `SupplyMode.Fuel`) are attached to the power grid after the game's own solve; the game's
solve never sees modded kinds. Details and evidence: `research/STRUCTURES_RECON.md`, "Processors (Task 10)" and
"Power sources (Task 11)".

- **Grid id** (`MoonProductionState.Grid`): the vanilla grid of the nearest vanilla power node whose range reaches the
  structure. Otherwise the id of its fuel network (every modded structure within a fuel generator's `Range`, chained
  through other generators): the lowest vanilla grid id in the network, else `-(lowest depot id in the network)`.
  Vanilla grid ids are positive, so a negative id is a StructureLib-owned grid. 0 means no grid.
- **Supply order** for a machine's `RunningKw` while running: the vanilla grid's sun surplus, then the fuel generators
  of its network, then the vanilla grid's batteries. `Power` is the share covered.
- **A fuel generator** burns one batch per cycle into its battery: `ChargeKwh` rises by `BatteryKwh / 2` per cycle,
  capped at `BatteryKwh`, and no cycle starts while the battery is full. It delivers at most `PeakKw`. Its `Power` is
  always 1, `SuppliedKw` is what it gave in the last solve, and it is listed in `world.powerNodes` as a panel node
  between solves. Its charge is saved with the production record.
- **Not supported:** a fuel generator feeding vanilla machines (the vanilla grid's totals cannot be written from
  managed code), power links drawn to modded structures, supply from `SupplyMode.Solar` definitions, and supply from a
  `Power` definition without a `Processor` (it has no production state to hold charge).
- **A generator's `RunningKw` is ignored:** a fuel generator always reads `Power` 1 and `DemandKw` 0 and never draws
  from the grid, so setting `RunningKw` on a `ProducesPower` processor has no effect. Leave it 0.
- **Do not patch `MoonWorld.TryPanel`:** a Harmony prefix on it killed the game natively after world load. See
  `research/STRUCTURES_RECON.md`, "Interop hazards found".
- The battery bank (kind 6) prefab has no production view; use `MoonWorld.SolarPostKind` (93) as a generator's
  `BaseStructure`, as the example burner does.

## Co-op handshake

On a join, the client refuses to enter a crew whose host has different StructureLib mods. Code: `Handshake.cs` (game
side) and `Pure/HandshakePayload.cs` (the registry-agnostic payload). Why this hook: `research/STRUCTURES_RECON.md`,
"Co-op session (Task 12)".

- **Channel, two levels:** key `structurelib.manifest`, value the base64 of `HandshakePayload.Encode` (magic `SLH`,
  then a deflated body: protocol, structures, cargo). Every StructureLib player in a lobby writes it as its lobby
  member data (`SetLobbyMemberData`); the host also writes it as lobby data (`SetLobbyData`), which a client can read
  before it is a member, as vanilla's listing version is. Every 2 s while `MoonSteamLobby.InLobby` each value is read
  back and written again if Steam does not hold it (a dropped write is retried). A manifest of 8192 characters or more
  is published as the marker `too-big` instead, so a client can tell "too big" from "absent". The game's own session
  messages and its `MoonCrewSession.Protocol` byte are untouched. Outside a lobby nothing runs.
- **Manifest:** one entry per registered structure (id, kind from the id hash, owning plugin GUID, that plugin's
  version from its BepInEx metadata) and per registered cargo (id, byte value, owner, version).
- **Client check:** a prefix on `MoonCrewSession.Offered` (the host's world offer, before any world byte is
  downloaded) reads the host's lobby data, then its member data, and compares that manifest with its own. On any problem
  it calls the session's own `EndJoin(join, line, leave: true)` and skips the offer.
- **Admit on uncertainty** (`HandshakePayload.ReadHost`): nothing published counts as a host without StructureLib
  (an empty manifest, so a client with mods is refused) only when the client is verifiably in the host's lobby
  (`MoonSteamLobby.InLobby && MoonSteamLobby.LobbyId == join.lobby`); Steam returns member data only to members.
  Outside the lobby, for the `too-big` marker, or for an unreadable value, the client logs a warning naming the cause
  and the join goes ahead unchecked. Any exception is logged and the join goes ahead unchecked too.
- **Host check:** the host compares each crewmate's manifest once per change, logs the problems and shows them with
  `MoonCrewSession.Notify`. It does not kick: the crewmate's game refuses itself on the same messages. A crewmate
  without StructureLib publishes nothing; the host logs that once and lets them in.
- **Messages** (`HandshakePayload.Compare(host, client)`, one per problem, all logged):
  - `missing mod <Owner> <Version> (structure <Id>)`, or `(cargo <Id>)`: the host has it, the client does not.
  - `version mismatch <Owner>: host <v> client <v>`
  - `cargo value conflict <Id>: host <n> client <n>`
  - `extra mod on client <Owner>`: the host would not render the client's structures.
  - `handshake protocol mismatch: host <n> client <n>` (alone; nothing else is compared).
- **Refusal line** shown by `EndJoin`: `Mods differ from the host's: ` and the first three messages joined by `; `,
  then `(+N more in BepInEx/LogOutput.log)` when there are more.
- **Untested:** both two-player cases (identical mods: the join succeeds and a host-built modded structure appears on
  the client; client without ExampleStructure: the join is refused with the `missing mod` message) need a second
  machine and have not been run. Whether `Offered` is reached on every join path (gathered lobby and listed shift) is
  read from the interop signatures, not traced. Only the single-player self-test `handshake-manifest` (live manifest,
  encode/decode, self-compare) has run.

## Save audit

The game writes the save through `MoonProgressStore.Serialize` (core sections, then one saver per section) and reads
it straight back; a section that names a depot or canister the save does not hold, or a kind it does not know, makes
it refuse the whole save. The modded depots therefore leave the save snapshot in the `MoonDepotSave.Write` prefix and
stay out until the `Serialize` finalizer, so every later saver sees only the depots the save holds. The build menu
records a free pose for what it places (`placedPose`), which `MoonStructurePoseSave` writes per depot; before this the
depots came back right after `MoonDepotSave.Write` and every save after a menu-placed modded structure was refused
("Structure pose ID"). The pose is part of the depot record, so the sidecar keeps it. Evidence and dumps:
`research/STRUCTURES_RECON.md`, "Per-depot savers audit".

| Saver | Names | Status |
|---|---|---|
| `Serialize` core (`canisters`, piles, inventories, vehicle storage, wallets, upgrades) | canister id | modded-cargo canisters stripped to the sidecar; the rest vanilla-safe |
| `MoonDepotSave` | depot id | stripped to the sidecar |
| `MoonStructurePoseSave` (derived from `depots`) | depot id | stripped with the depots; pose kept in the sidecar depot record |
| `MoonProductionSave` | depot id | stripped to the sidecar |
| (`cargo`) | canister id | stripped to the sidecar with the canisters |
| `MoonStructureAnchorSave` (`anchors`) | own id and kind | an anchor of a modded kind is left out of the vanilla save and put back after; not kept across a reload (none seen) |
| `MoonRobotSettingsSave` (`robotsOff`) | depot id | an entry naming a modded depot is left out and put back after; unsupported: a modded robot dock's robots-off setting resets on load |
| `MoonGarageSave` (garage depots) | depot id | sees no modded depot; a modded structure borrowing the garage is unsupported (untested) |
| `MoonOwnedVehicleSave` (`garage`) | depot id | unsupported: a vehicle kept in a modded garage would name a stripped depot (untested) |
| `MoonLoadoutSave`, `MoonVehicleStorageSave`, `MoonConveyorSave`, `MoonToolContainerSave`, `MoonUpgradeTreeSave`, `MoonPlayerRecordSave`, `MoonScoreSave`, `MoonRadioSave`, `MoonRegionTouchSave`, `MoonWaypointSave`, `MoonLandmarkSave` | nothing | vanilla-safe: no depot id, canister id or cargo member |
| (`premiums`, `station`) | unchecked | unknown; no modded structure is a station |

## Cargo audit

The game's `MoonCargo` is a byte enum (Dust 0, Sand 1, Stone 2), and vanilla code answers every value it does not
know as dust. A modded cargo (`MoonStructures.RegisterCargo`, values 3..255) is only as good as the members that see
it. This table lists every `MoonCargo`-typed member (and every member that carries a cargo byte) in the dumped game
types, and what a modded value gets there.

Status:
- **patched**: StructureLib answers for a registered modded value (guard: registry frozen, not disabled, value registered;
  anything else runs vanilla).
- **vanilla-safe**: vanilla stores or passes the value through unchanged, checked or argued below.
- **unsupported**: a modded value is not handled yet; what happens is noted.
- **unknown**: not checked.

| Member | Status | Notes |
|---|---|---|
| `MoonWorld.CargoName(MoonCargo)` (static) | patched | `CargoDefinition.Name`. Self-test `cargo-modded-name`; an unregistered value (251) still answers "moon dust" (`cargo-unregistered-is-vanilla`). |
| `MoonWorld.CargoShortName(MoonCargo)` (static) | patched | `CargoDefinition.ShortName`. Checked in `cargo-modded-name`. |
| `MoonWorld.UnitKilograms(MoonCargo)` (static) | patched | `CargoDefinition.UnitKilograms`. Vanilla answers 1 for an unknown value; example grit is 3 kg so the self-test `cargo-modded-kg` (expects 3) proves the prefix. |
| `MoonWorld.CargoValue(MoonCargo)` (instance, `Decimal`) | patched | `CargoDefinition.Price`, converted exactly (decimal bits). Self-test `cargo-modded-value`. |
| `MoonDroppedCanister.cargo` | vanilla-safe | Holds the byte as given: a canister built with 250 reads 250, and `MoveCanisterRecord` stores it in `world.canisters` unchanged. |
| `MoonDroppedCanister.IsDust` (getter) | vanilla-safe | Not patched: false for 250, true for Dust (self-test `cargo-isdust-false`). |
| `MoonDroppedCanister(..., MoonCargo cargo)` (constructor) | vanilla-safe | Stores the value. |
| `MoonLooseDustSnapshot.cargo` / `MoonCargoSnapshot.cargo` (byte, per non-dust canister) | patched | Filled by `SnapshotProduction`. Modded entries (and their canisters) are cut out of the vanilla save by the `MoonProgressStore.Serialize` prefix and kept in `structurelib.json` by cargo id; put back after the write. |
| `MoonCanisterSnapshot` | n/a | Has no cargo member; a modded-cargo canister is saved as a sidecar `CanisterRecord`. |
| Canister cargo on load (`RestoreCanisters`, `RestoreProduction`) | patched | Vanilla restores an out-of-range cargo as dust. StructureLib injects sidecar canisters in a `RestoreCanisters` prefix and sets their cargo in a `RestoreProduction` postfix through `MoveCanisterRecord`. |
| `MoonWorld/ProcessorSpec.slotCargo` (`MoonCargo[]`) | patched | A modded processor gets its own spec (`SpecBuilder`, answered by the `Processor(kind)` prefix); its `slotCargo` holds the slot cargo bytes, modded values included. The production sidecar records slot cargo by id (`SlotCargoIds`). Self-tests `processor-spec-slots`, `production-sidecar`. |
| `MoonWorld.InputSlot(int kind, MoonCargo)` (static) | patched | For a modded kind: the first Input slot holding the cargo, else -1 (a modded structure without a processor: -1). Vanilla kinds still answer as vanilla. Self-tests `inputslot-accepts-dust`, `inputslot-refuses-dust`. |
| `MoonWorld.PortCargo(ConveyorPort)` (static, returns `MoonCargo`) | unsupported | Cargo of a machine port; vanilla ports only. Belt input into modded machines is not supported yet. |
| `MoonWorld.CrewProcessorLoad(player, machine, MoonCargo, kg)` | patched | Vanilla does nothing for a modded machine (no `InputSlot` call; checked in game). A prefix loads the first Input slot that takes the cargo, up to its capacity, under the player's share (`MoonWorld.Put`). Self-test `feed-lands-in-slot0`. Like vanilla it caps at capacity and never touches the walker's cargo (the walker path, `LoadProcessor`, deducts itself and does not call it). `LoadProcessor` refuses a modded machine ('Not a production machine'); belts (`FeedProcessor`, `EnterMachine`) are not checked. |
| `MoonWorld.EmitCargo(outfeed, owner, MoonCargo, kg, out reason)` | unknown | Only called with a connected outfeed belt (fact (d)); never reached in the self-test (the grinder has no belt, so its grit stays in the output slot). A postfix counts and logs modded cargo it sees. Whether the canister it makes keeps the modded value is not checked. |
| `MoonWorld.CrewCargoDeposit(player, MoonCargo, kg)` | unsupported | Deposit at the collector; pays through `CargoValue` (patched) but the rest of the path is not checked. Do not rely on depositing modded cargo yet. |
| `MoonWorld.AddPremium(player, MoonCargo, kg)` / `premiums` / `MoonPremiumSnapshot` | unknown | `MoonPremiumSnapshot` has no cargo member (station, player, kilograms); how `AddPremium` weighs a modded value is not checked. |
| `MoonWorld.LocalProcessorLoad` (`Action<int, MoonCargo, double>`) | unsupported | Co-op wire event. A modded value is not sent or received by StructureLib; clients without the mod would see dust. |
| `MoonWorld.LocalCargoDeposit` (`Action<MoonCargo, double, int>`) | unsupported | Co-op wire event, as above. |
| `MoonWorld.LocalCanister` / `RaiseLocalCanister` / `RaiseMachineCanister` / `ApplyCrewCanister` / `TruthHash` (`MoonDroppedCanister`, cargo inside) | unsupported | Co-op canister sync. Whether the cargo byte survives the wire is unknown; co-op is not supported. |
| `MoonCrewSession` (co-op world stream, joins) | unknown | Not dumped. A joining client receives the host's serialized world, which (through `Serialize`) has no modded canisters. |
| `MoonCanisterStreaming/FarShown.cargo` | unknown | Far-LOD canister drawing. A grit canister in the world (end-to-end run) logged no exceptions; how it is tinted was not looked at. |
| `MoonProductionState` | n/a | No `MoonCargo` member (slot stores are per owner; slot cargo comes from `ProcessorSpec.slotCargo`). |
| Cargo carried by a walker or a vehicle (`MoonPlayerRecordSnapshot`, `MoonLoadoutSave`, `MoonOwnedVehicle` / `MoonOwnedVehicleSave`, `MoonVehicleStorageSnapshot`, `MoonInventorySnapshot`, `MoonToolContainerSnapshot`) | n/a (checked) | The dumps of these records have no `MoonCargo` or cargo-byte member (walkers and vehicle storage hold kilograms only), and there is no separate carried-canister save record. Whether a canister held in hand or riding a vehicle is saved through `state.canisters` + `state.cargo` (and so stripped like a dropped one) is not checked: see the by-hand list in the SandPower README. |
