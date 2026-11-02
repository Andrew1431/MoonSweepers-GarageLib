# StructureLib

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
