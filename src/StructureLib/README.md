# StructureLib

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
| `MoonWorld.UnitKilograms(MoonCargo)` (static) | patched | `CargoDefinition.UnitKilograms`. Vanilla answers 1 for an unknown value, the same as example grit's 1 kg, so `cargo-modded-kg` would pass unpatched too; the patch matters for cargo that is not 1 kg. |
| `MoonWorld.CargoValue(MoonCargo)` (instance, `Decimal`) | patched | `CargoDefinition.Price`, converted exactly (decimal bits). Self-test `cargo-modded-value`. |
| `MoonDroppedCanister.cargo` | vanilla-safe | Holds the byte as given: a canister built with 250 reads 250, and `MoveCanisterRecord` stores it in `world.canisters` unchanged. |
| `MoonDroppedCanister.IsDust` (getter) | vanilla-safe | Not patched: false for 250, true for Dust (self-test `cargo-isdust-false`). |
| `MoonDroppedCanister(..., MoonCargo cargo)` (constructor) | vanilla-safe | Stores the value. |
| `MoonLooseDustSnapshot.cargo` / `MoonCargoSnapshot.cargo` (byte, per non-dust canister) | patched | Filled by `SnapshotProduction`. Modded entries (and their canisters) are cut out of the vanilla save by the `MoonProgressStore.Serialize` prefix and kept in `structurelib.json` by cargo id; put back after the write. |
| `MoonCanisterSnapshot` | n/a | Has no cargo member; a modded-cargo canister is saved as a sidecar `CanisterRecord`. |
| Canister cargo on load (`RestoreCanisters`, `RestoreProduction`) | patched | Vanilla restores an out-of-range cargo as dust. StructureLib injects sidecar canisters in a `RestoreCanisters` prefix and sets their cargo in a `RestoreProduction` postfix through `MoveCanisterRecord`. |
| `MoonWorld/ProcessorSpec.slotCargo` (`MoonCargo[]`) | unsupported | Vanilla processors only (chamber, press). Modded processors and their slot cargo are Task 10. |
| `MoonWorld.InputSlot(int kind, MoonCargo)` (static) | unsupported | Which slot of a machine takes a cargo. For vanilla machines a modded value is presumably refused (-1); not checked. Task 10. |
| `MoonWorld.PortCargo(ConveyorPort)` (static, returns `MoonCargo`) | unsupported | Cargo of a machine port; vanilla ports only. Task 10. |
| `MoonWorld.CrewProcessorLoad(player, machine, MoonCargo, kg)` | unsupported | Loads cargo into a machine slot via `InputSlot`. Task 10. |
| `MoonWorld.EmitCargo(outfeed, owner, MoonCargo, kg, out reason)` | unsupported | Puts a machine's product on a belt; whether a modded value reaches the new canister unchanged is Task 10. |
| `MoonWorld.CrewCargoDeposit(player, MoonCargo, kg)` | unsupported | Deposit at the collector; pays through `CargoValue` (patched) but the rest of the path is not checked. Until Task 10 proves it, do not rely on depositing modded cargo. |
| `MoonWorld.AddPremium(player, MoonCargo, kg)` / `premiums` / `MoonPremiumSnapshot` | unknown | `MoonPremiumSnapshot` has no cargo member (station, player, kilograms); how `AddPremium` weighs a modded value is not checked. Task 10. |
| `MoonWorld.LocalProcessorLoad` (`Action<int, MoonCargo, double>`) | unsupported | Co-op wire event. A modded value is not sent or received by StructureLib; clients without the mod would see dust. |
| `MoonWorld.LocalCargoDeposit` (`Action<MoonCargo, double, int>`) | unsupported | Co-op wire event, as above. |
| `MoonWorld.LocalCanister` / `RaiseLocalCanister` / `RaiseMachineCanister` / `ApplyCrewCanister` / `TruthHash` (`MoonDroppedCanister`, cargo inside) | unsupported | Co-op canister sync. Whether the cargo byte survives the wire is unknown; co-op is not supported. |
| `MoonCrewSession` (co-op world stream, joins) | unknown | Not dumped. A joining client receives the host's serialized world, which (through `Serialize`) has no modded canisters. |
| `MoonCanisterStreaming/FarShown.cargo` | unknown | Far-LOD canister drawing. A grit canister in the world (end-to-end run) logged no exceptions; how it is tinted was not looked at. |
| `MoonProductionState` | n/a | No `MoonCargo` member (slot stores are per owner; slot cargo comes from `ProcessorSpec.slotCargo`). |
