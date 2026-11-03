# Build QoL

Optional BepInEx mod for Moon Sweepers with small build-tool tweaks. GUID `com.ryanhartwig.buildqol`. It does not need
GarageLib or StructureLib.

## Keep selection

In the base game a placement resets the build tool. A structure with no conveyor ports (a solar post, a lamp) leaves
you with no ghost, and a machine with ports (a pressure chamber, a stone press) switches the tool to belts so you can
draw its belts. With `KeepSelection` on, the same structure stays selected with a new ghost ready, and its rotation is
kept, so you can place the next copy straight away.

- Belts behave exactly as before (they never go through the reset).
- Foundation-kit pieces (platform foundation, hex pieces) already stay selected in the base game and are unchanged.
- If you cannot build another (`CanBuild` false, for example you are out of money), the base game's reset happens
  instead, so you are never left holding a ghost you cannot place.
- For a machine with ports, the "draw its belts, or RMB to put the belt away" hint is cleared, since you hold the
  machine again. To draw its belts, pick the belt as usual.
- A placement still plays the base game's "placement started" UI sound for the new ghost.

## Config

`BepInEx/config/com.ryanhartwig.buildqol.cfg`:

| Setting | Default | Meaning |
|---|---|---|
| `Tweaks.KeepSelection` | `true` | `false`: base-game behaviour; nothing is patched and the mod only logs that it loaded. |

Dev builds also add `[Dev] AutoLoadSlot` (load `slot-<n>` from the main menu; slot 1 is always refused), `[Dev] Probe`
and `[Dev] ProbeKinds` (see Hazards: the probe logs the builder's state and places each listed kind itself through the
game's `TestInput` hook).

If a game update renames any member the tweak uses, the mod logs which ones and turns the tweak off.

## Hazards

Measured on the 2026-11 game build. Addresses come from `[Address(RVA, Offset)]` in a copy of the Cpp2IL dummy
`BepInEx/dummy/Assembly-CSharp.dll`, and bytes and disassembly from a copy of `GameAssembly.dll` (`cdb -z`). Size is the
gap to the next method start over all dummy assemblies, `CC` padding included. "Normal prologue" means register saves
and `sub rsp` plus the IL2CPP metadata-init `cmp byte [rip+x],0`, with no branch or `ret` in the first 16 bytes. The
direct calls are `E8 rel32` targets that land on a known method start, plus `jmp` tail calls read from the disassembly.
In this table, 1000 is `MoonDepotBuilder.ConveyorKind`.

| Method (`MoonDepotBuilder`) | RVA | Size (B) | Prologue | What it does on the placement path |
|---|---|---|---|---|
| `UpdatePlacing(BuildInput)` | 0x44EDE0 | 6992 | normal | `TryBuildDepot` (+0x179E), then `NoteBuilt(selectedBuildId)` (+0x17FD), then `MachineBuilt(def)` (+0x180C), then the epilogue. It calls `UpdateFoundationPlacement` (+0x1A98) for foundation pieces and `CancelPlacement` (+0x1AD1) on the cancel input. No 0x3E8 immediates. |
| `ShowPlan(...)` | 0x4479C0 | 5392 | normal | `TryBuildStructure` (+0x120F), then `NoteBuilt` (+0x126F). If `def.module != null` (a foundation piece) it clears the plan and **stays in placement**. Otherwise it calls `MachineBuilt` (+0x138C), then the epilogue. |
| `MachineBuilt(def)` | 0x43E900 | 576 | normal, first branch `jne` at +0x17 | **The reset.** If `def.conveyorPorts.Length != 0` it calls `BeginConveyor` (+0xB3) and posts the "built · draw its belts" notice. Otherwise it tail-jumps (`jmp`) to `CancelPlacement`. **Postfix (KeepSelection).** |
| `NoteBuilt(int kind)` | 0x43FCE0 | 288 | normal, first branch at +0x1B | Moves `kind` to the front of `Recent` (`PlayerPrefs.SetString`). It is also called by `UpdateConveyor` with 1000. **Postfix (KeepSelection): records the built kind.** |
| `CancelPlacement()` | 0x436C20 | 592 | normal | Calls `EndRemoval`, `EndMove`, `PreviewReshapedBelt` and `EndConveyor`, destroys the ghost, and sets `placing = valid = false`, `suppressedUntil = frame + 2` and walker `Building = true`. Does **not** touch `selectedStructure` (0x68), `selectedBuildId` (0x418) or `ghostKind` (0x438). Postfix only in the dev probe. |
| `BeginConveyor()` | 0x42D6E0 | 1648 | normal | `CancelPlacement` if placing, then `selectedBuildId = 1000` (`mov dword [rdi+418h],3E8h`), then `placing`, `conveyorMode`. Postfix only in the dev probe. |
| `BeginPlacement()` | 0x42E2A0 | 1264 | normal | If placing and `selectedBuildId == selectedStructure`: toggles the menu (`SetOpen`). If `selectedStructure == 1000`: tail-jumps to `BeginConveyor`. Otherwise: `CancelPlacement` if placing, then `selectedBuildId = selectedStructure`, `MakeGhost`, `yaw = 180`, `suppressedUntil = frame + 2`, and UI sound 0x1A. Called by KeepSelection; postfix only in the dev probe. |
| `BeginPlacementFromShop(int)` | 0x42E060 | 576 | normal | Refuses while the walker is `Building` (set by `CancelPlacement` and `BeginConveyor`), so it **cannot** be used from the `MachineBuilt` postfix. Otherwise it selects the tool slot, then `SelectStructure(kind)` and `BeginPlacement()`. Returns `placing && selectedBuildId == kind`. Not patched. |
| `SelectStructure(int)` | 0x4459E0 | 256 | normal | Sets `selectedStructure` and calls `ShowDetails` only if `kind` is in `structures`. Otherwise it does nothing. Not patched. |
| `CanBuild(int)` | 0x436AC0 | 352 | normal | Checks `PendingSave`, the belt kit for 1000, and the definition/price. Called, not patched. |
| `UpdateConveyor(BuildInput)` | 0x44A760 | 9632 | normal | Belts: `TryBuildConveyorRoute`, `NoteBuilt(1000)` (+0x1957). Never calls `MachineBuilt`. |
| `Update()` | 0x451960 | 3472 | normal | Its `BeginPlacement` call (+0x924) is the menu/back key path. There is no automatic ghost after a reset. |
| `MakeGhost(def, int)` | 0x43EB40 | 2672 | normal | Its 0x3E8 hits are `[rbp+3E8h]` stack offsets, not ConveyorKind. |
| `get_IsPlacing`, `get_ConveyorMode`, `get_TestInput`, `RefreshMenu` | | 16 each | leaf / `jmp` thunk | **Never patch.** |
| `ToggleBuildMode()` | 0x449DF0 | 144 | `jne` at +0x10 | **Never prefix.** |
| `EndMachineSnap()` | 0x439860 | 112 | `call` at +0xE | Not patched. |

Both callers of `MachineBuilt` return immediately after it, so starting a new placement inside its postfix leaves no
caller code running on stale state. The postfix calls `SelectStructure` (when needed), then `BeginPlacement`, then puts
`yaw` back. It is guarded against re-entry. `BeginPlacement` never reaches `MachineBuilt` today.

In-game probe, on slot 2 (a copy) with DevMoney. Placements were driven through `TestInput`: an aim ray from the camera
to a ground point 4 to 18 m out, then `place = true` for one frame. `ReadInput` honours `TestInput`. The state is
logged after each patched call and 1, 5 and 30 frames later:

- **Base game** (`KeepSelection = false`, "patched=False"):
  - Solar post 93: `after MachineBuilt: ... IsPlacing=False ConveyorMode=False ghost=False`.
  - Pressure chamber 96: `after BeginConveyor: selectedStructure=96 selectedBuildId=1000 IsPlacing=True ConveyorMode=True ghost=False`
    with notice 'Pressure chamber built · draw its belts, or RMB to put the belt away'.
  - Platform foundation 101: `NoteBuilt(101)` only, no `MachineBuilt`, still `IsPlacing=True ghost=True`.
- **KeepSelection on**:
  - 93: `NoteBuilt(93)`, then `CancelPlacement`, then `BeginPlacement`, then `after MachineBuilt: selectedStructure=93 selectedBuildId=93 IsPlacing=True ConveyorMode=False ghost=True`.
    A second copy was placed from the kept ghost (depots 3 to 4) with no re-selection. A yaw of 135 set before the
    first placement was still 135 on the kept ghost.
  - 96: `after BeginConveyor: selectedBuildId=1000 ConveyorMode=True`, then `after MachineBuilt: selectedBuildId=96 IsPlacing=True ConveyorMode=False ghost=True`,
    with the notice gone. A second copy was placed from the kept ghost (depots 3 to 4).
  - 101 and the belt (two clicks, `NoteBuilt(1000)`, still `ConveyorMode=True`): identical to the base game.
- No errors or exceptions from Build QoL. It loads next to GarageLib, StructureLib, SandPower, BeltBoxes and DevMoney.
- Not tested in game: the `CanBuild` false fallback (DevMoney keeps the wallet full), multiplayer, and the gamepad.

## Changelog

- 0.1.0: First release. `KeepSelection`: keep the placed structure selected with a new ghost, with rotation kept.
