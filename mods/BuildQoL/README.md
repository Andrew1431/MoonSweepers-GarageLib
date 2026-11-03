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

## Scrap belt contents

In the base game, removing a belt drops every canister riding on it onto the ground along the belt. With
`ScrapBeltContents` on, hold `ScrapModifier` (default Left Ctrl) while you confirm a belt removal to destroy those
canisters instead. The belt is removed and refunded as usual. Removing a belt without the key works as before.

- While you aim at a belt in removal mode, a red line under the removal keys shows what would go:
  "Hold Ctrl to scrap contents (3)", and with the key held "Ctrl: scrap contents (3 canisters, 3.0 kg)". If other
  players' canisters ride on it, the line names them ("2 are Andrew's"). An empty belt shows no line.
- `ScrapOthersCargo = false` scraps only canisters you own. Other players' canisters drop as in the base game.
- Only canisters riding the belt are destroyed. Loose canisters lying near it are not touched.
- Destroyed cargo is gone for good. There is no undo, and the game's belt undo does not bring canisters back.
- Only the keyboard has a modifier. There is no gamepad binding yet.
- No base-game binding uses Ctrl. All `MoonInput` bindings were listed in game and none has a Ctrl key. Removal mode
  also does not read the builder's own Ctrl input.
- Multiplayer: in the base game, any player can delete a canister by telling every other player directly. So the
  scrapping player sends the same "canister removed" message a vanilla client sends when it feeds a canister to a
  machine, and only the player who scraps needs the mod. One gap: a canister that boarded the belt on the host in the
  last fraction of a second before a client removes it may still drop on the host as a loose canister. **Multiplayer
  is not tested yet.**

To verify with a second player (Ryan hosts slot 2, Andrew joins, dev builds unless stated). Set up a belt with 4 or
more riders, 2 owned by each player, one near the end:

1. Client scraps, both modded: Andrew holds Ctrl and removes the belt. Andrew's log says `as client` with N canisters.
   Ryan's `canisters.Count` drops by N within 1 s, and no canister falls on either screen.
2. Wait 10 s, two rounds of the host's state check: the canisters are still gone on both sides.
3. Host scraps, both modded: the canisters vanish on Andrew's screen with no drop.
4. Client modded, host vanilla (`ScrapBeltContents = false` on Ryan's side): deleted on both.
5. Host modded, client vanilla: Andrew removes a loaded belt without Ctrl: the base game's drop on both.
6. Busy belt (a splitter feed at full rate): Andrew scraps mid-flow. Count the loose canisters on Ryan's side.
7. Others' cargo: the line names Andrew's canisters on Ryan's side. `ScrapOthersCargo = false` leaves them.
8. Ryan saves and reloads: the count matches step 2. Restore slot 2 afterwards.

## Config

`BepInEx/config/com.ryanhartwig.buildqol.cfg`:

| Setting | Default | Meaning |
|---|---|---|
| `Tweaks.KeepSelection` | `true` | `false`: base-game behaviour; nothing is patched. The load line lists every tweak as `on`, `off`, `missing-members` or `patch-failed`. |
| `Tweaks.ScrapBeltContents` | `true` | `false`: base-game belt removal; nothing is patched. |
| `Tweaks.ScrapModifier` | `LeftCtrl` | Unity Input System key held while confirming a belt removal to scrap its canisters. `None`: never scrap. |
| `Tweaks.ScrapOthersCargo` | `true` | `false`: only canisters you own are scrapped; others drop as in the base game. |

Dev builds also add `[Dev] AutoLoadSlot` (load `slot-<n>` from the main menu; slot 1 is always refused), `[Dev] Probe`
and `[Dev] ProbeKinds` (see Hazards: the probe logs the builder's state and places each listed kind itself through the
game's `TestInput` hook). `[Dev] ScrapProbe` and `[Dev] ScrapProbeHold` drive the scrap probe (see Hazards).

If a game update renames any member a tweak uses, the mod logs which ones and turns that tweak off.

## Code shape

Each tweak is one file holding its Harmony patch class(es) and a `Tweak` subclass (`Tweak.cs`: name, toggle text, the
game members it needs, its patch classes, optional `Update`/`LateUpdate` and dev hooks), plus one entry in
`Plugin.All`. Game-independent logic lives in `Pure/` (BCL only) and is covered by `tests/BuildQoL.Tests`
(`dotnet test tests/BuildQoL.Tests`).

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

### Scrap belt contents

Measured the same way, with `scan.ps1` on copies of the 2026-11 binaries; the copy matches the installed
`GameAssembly.dll`. On the removal path, `UpdateRemoval` asks `CanRemoveConveyor` about the aimed belt every frame. On
the confirm click it calls `TryRemoveConveyor` (its only native caller) in the same `Update`. `TryRemoveConveyor`
touches no canister. The drop happens frames later in `StepConveyors`, which lowers every rider whose belt is gone
(`LowerToGround`). The tweak deletes the riders in a prefix, before the original removes the belt and saves.

| Method | RVA | Size (B) | Prologue | Use |
|---|---|---|---|---|
| `MoonWorld.TryRemoveConveyor(MoonWalker, int, out string)` | 0x761170 | 576 | normal (metadata-init `cmp` at +0xD, first branch `jne` at +0x20) | **Prefix (ScrapBeltContents), always returns true**, so the original always runs. With the modifier held and `CanRemoveConveyor` true, it deletes the belt's riders first. |
| `MoonWorld.CanRemoveConveyor(MoonWalker, int, out string)` | 0x6B0F60 | 1168 | normal (metadata-init `cmp` at +0x10) | **Postfix (ScrapBeltContents)**: records `(frame, belt, result)` for the prompt line. The prefix also calls it. |
| `MoonWorld.RemoveCanister(int)` | 0x726D30 | 608 | normal | Called. Removes the id from `canisters`, `carriedCanisters`, `flyingCanisters`, `riders` and its sector. Raises nothing. |
| `MoonWorld.RaiseMachineCanister(CanisterOp, MoonDroppedCanister)` | 0x71CA10 | 208 | normal, short | Called on the host and solo (`Removed`). Raises `LocalCanister` only when not `Joined`. **Never patch.** |
| `MoonWorld.RaiseLocalCanister(CanisterOp, string, MoonDroppedCanister)` | 0x71C670 | 384 | normal | Called on a joined client (`Removed`, `MoonCrew.LocalPlayerId`). No `Joined` gate. This is the vanilla client path (`FeedCarriedCapsule`). |
| `MoonCrew.get_LocalPlayerId`, `MoonCrew.get_Joined` | 0x7C6990, 0x7C6910 | 160, 128 | metadata-init check, branch at +0xB | Called. **Never patch.** |
| `MoonSteamLobby.PlayerName(ulong)` | 0x59D500 | 224 | | Called only as a fallback when looking up a crew member's name. |
| `MoonDepotBuilder.get_IsRemoving` | 0x4545A0 | 16 | leaf (`movzx`, `ret`) | Read. **Never patch.** |
| `MoonDepotBuilder.UpdateRemoval(BuildInput)` | 0x450930 | 4144 | normal | Not patched. Gated on the walker (`IsFirstPerson`, `UIBlocked`, `Mounted`). Reads `back`, `remove`, `move`, `slot` and `place`, never `ctrl`. |
| `Notice`, `BeltPathOf`, `PlaceRider(3 args)`, `RiderOn`, `SetPrompt` | | 64, 16, 48, 112, 272 | short / thunks | **Never patch.** |

`CanisterOp`: `Upsert = 1`, `Taken = 2`, `Released = 3`, `Removed = 4`. In removal mode the game hides `prompt` (its
GameObject is off) and shows the `removeKeys` row ("LMB REMOVE CONVEYOR BELT +$2.00", "BACK") under "Placement
instructions". The tweak's line is therefore its own label: a copy of `prompt`, parented beside `removeKeys` and
placed under it. No game UI object is changed.

In-game probe (`[Dev] ScrapProbe = 1`), on slot 2 (a copy), solo. Three free-standing 4 m belts were built from code
(`ConveyorRouteValid`, then `TryBuildConveyor`). 1 kg dust canisters were put on them (`PackDustCanister`, then
`MoveCanisterRecord` onto the belt) before the build tool was taken out, because `PackDustCanister` refuses while the
tool is in hand. Each belt was then removed through the builder's own removal mode: the build tool from
`BeginPlacementFromShop`, then `CancelPlacement`, `BeginRemoval`, a `TestInput` aim ray at the belt, and `place` for one
frame. A dev flag that `Scrap.Held` honours forced the modifier on.

- **Scrap**: `riders on belt 1: [8 d 2.50] [9 d 3.25] [10 d 4.00]`. Aimed: `aimBelt=1 aimOk=True` each frame, line
  "Hold Ctrl to scrap contents (3)", then with the key held "Ctrl: scrap contents (3 canisters, 3.0 kg)". Click:
  `scrapped 3 canisters (3.0 kg) from belt 1 as host`, `TryRemoveConveyor(1) -> True 'Belt removed.'`,
  `spawned 8:-- 9:-- 10:--` (gone from `canisters` and `riders`). At +1, +5 and +30 frames: riding -3, canisters -3,
  nothing dropped.
- **Base game** (no modifier, belt 2, 3 riders): `TryRemoveConveyor(2) -> True 'Belt removed.'`,
  `spawned 11:c- 12:c- 13:c-` (still canisters, no longer riders), riding -3, canisters +0. They dropped.
- **Undo**: belt 3 with 2 riders and an undo step for it in the builder's `undo` list was scrapped (2 canisters,
  2.0 kg). Then `UndoBelts` ran with no exception, used up the step and brought no belt back.
- Save and reload: `RequestSave` with 9 canisters. After a restart and `[Dev] AutoLoadSlot = 2`: `canisters=9`.
- No `StepConveyors` or other exception, and no Build QoL error, in any run. `DroppedCanisterCount` counts every
  canister record, riding or not, so a drop shows as riding down and canisters unchanged.
- Not tested in game:
  - A real key press. The probe forces the modifier.
  - How the label looks on screen. It is active, red, 42 characters, one row below `removeKeys` in the same parent,
    but no screenshot was taken.
  - `ScrapOthersCargo` and the name lookup. In solo every canister owner is `local`.
  - A rider at distance 0.
  - The gamepad and multiplayer.

## Changelog

- 0.1.0: First release. `KeepSelection`: keep the placed structure selected with a new ghost, with rotation kept.
- Unreleased: `ScrapBeltContents`: hold Ctrl while removing a belt to destroy the canisters riding on it.
