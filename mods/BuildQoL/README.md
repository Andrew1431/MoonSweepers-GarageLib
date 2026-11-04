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

## Foundation run

Hold **Shift** and turn the mouse wheel while you hold a platform-foundation ghost (any world-square foundation piece,
not in free mode) to set a run length from 1 to `RunMax`. The run goes from the ghost's square in the direction the
camera faces (the nearest of the square's four edges, so it does not flicker between two directions near 45°). The
squares after the ghost get outlines: green is buildable, red is blocked. The prompt shows `Run 6 · $12`, or
`Run 6 · 1 blocked`.

- Click as usual. The base game builds the ghost's square, then the mod builds the rest of the run, one piece per frame,
  with the game's own planner and build call. Each piece is charged as usual.
- A run with a red square, or one you cannot pay for in full, is refused: nothing is built, and the prompt says why.
  If a piece fails anyway while the run is being built (something moved into it), it is skipped and the run carries
  on. When the run ends, a notice reports what was built, for example `Built 9 of 10 (1 blocked)`.
- The run may go past your build reach (`RunIgnoresReach`). The ghost's square is always within reach, because the base
  game placed it, and the run is never longer than `RunMax`. The reach is raised only for the mod's own build call and
  restored right after it, so your normal placements keep the base game's reach.
- Plain wheel still rotates the ghost. The length stays set until you change it back to 1, pick another structure, or
  stop placing. A length of 1 is the base game.
- Runs stack. With the ghost on top of existing foundations, each square of the run is planned on the foundation at
  the ghost's level, the way the base game stacks a single piece on the one under the cursor. A run stays on one level:
  where it would leave the foundations under it, those squares are blocked (red) and the run is refused.
- Not covered yet: gamepad, multiplayer, hex foundation pieces, and free-placed pieces.

## Config

`BepInEx/config/com.ryanhartwig.buildqol.cfg`:

| Setting | Default | Meaning |
|---|---|---|
| `Tweaks.KeepSelection` | `true` | `false`: base-game behaviour; nothing is patched. The load line lists every tweak as `on`, `off`, `missing-members` or `patch-failed`. |
| `Tweaks.FoundationRun` | `true` | Shift + wheel run length for world-square foundation pieces. `false`: base-game behaviour; nothing is patched. |
| `Tweaks.RunMax` | `10` | Longest run in pieces, clamped to 1..32. |
| `Tweaks.RunModifier` | `LeftShift` | Key held with the wheel (Unity Input System key name; `LeftShift` or `RightShift` accept either Shift). Other modifiers already have a base-game use with the wheel. |
| `Tweaks.RunIgnoresReach` | `true` | Build the whole run even past your build reach. `false`: the run stops at the first piece out of reach (shown grey in the preview). |

Dev builds also add `[Dev] AutoLoadSlot` (load `slot-<n>` from the main menu; slot 1 is always refused), `[Dev] Probe`
and `[Dev] ProbeKinds` (see Hazards: the probe logs the builder's state and places each listed kind itself through the
game's `TestInput` hook).

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

### Foundation run

Measured on the 2026-11 game build with the same tools (spike reports A and A2). `TryBuildStructure` refuses a piece
farther than `def.reach + 2 m` from the walker ("Move closer to the build site"); `def` is the shared
`MoonDepotBuildDefinition` asset (`BuildDefinition(kind)` returns the same object every call; never saved), and its
`reach` (+0x40) is 30 for the square foundations. The site check (`BeginStructureCheck`/`Advance`,
`StructureObstaclesClear`) has no distance test, so the mod enforces its own limit: at most `RunMax` (≤ 32) cells from
the ghost's square, each step 7.5 to 8.5 m from the last or the run is cancelled.

| Method | RVA | Size (B) | Prologue | What it does on the run path |
|---|---|---|---|---|
| `MoonDepotBuilder.ReadInput()` | 0x442AB0 | 2208 | normal, first branch `jne` at +0x17 | Fills `BuildInput`: `wheel` (+0x28) = mouse `scroll.y` (or `TestInput.wheel`), `place` (+0x18), `fine` (+0x1E) = Shift. Only `UpdatePlacing` reads `wheel`: ±90° on a snapped grid piece, also with Shift. **Postfix (FoundationRun)**: on a Shift + wheel tick with a world-square ghost it takes the tick for the run length and sets `wheel = 0`; it sets `place = false` for a refused run and while a run is being built. |
| `MoonDepotBuilder.NoteBuilt(int)` | 0x43FCE0 | 288 | normal, first branch at +0x1B | Called by `ShowPlan` after vanilla built the ghost's square. **Postfix (FoundationRun)**, beside KeepSelection's: queues the rest of an armed run. |
| `MoonDepotBuilder.ShowPlan(...)` | 0x4479C0 | 5392 | normal | Writes the prompt each frame, so FoundationRun calls `SetPrompt` from `LateUpdate`. Not patched. |
| `MoonDepotBuilder.UpdatePlacing(BuildInput)` | 0x44EDE0 | 6992 | normal | The only `wheel` consumer. Not patched. |
| `MoonDepotBuilder.SetPrompt(string, bool, bool)` | 0x446320 | 272 | no metadata-init | Called only. |
| `MoonDepotBuilder.Notice(string)` | 0x43FE00 | 64 | short leaf | Called only. **Never patch.** |
| `MoonWorld.PlanStructure(...)` | 0x70F300 | 1088 | normal | Called per run cell (preview and build), with `target` = the id of the foundation the piece sits on: the base game passes `StructureAt(hit.collider)`, and `PlanWorldHex` (0x70F740) stacks only with a target (`StackedOnFree` / `PlanStacked`); with none it plans on the ground grid. Not patched. |
| `MoonWorld.WorldGridCell(MoonDepotSnapshot)` | 0x76D6A0 | 160 | short | Plan-encoded square id of a built foundation. The run indexes the world-square depots (`MoonWorld.depots`) within range by square once per frame to find each cell's stacking target. Called only. **Never patch.** |
| `MoonWorld.BeginStructureCheck(plan)` | 0x6A8B60 | 464 | normal | Called; `DepotPlacementCheck.Advance(5 ms)` until `Done`. Not patched. |
| `MoonWorld.StructurePlacementSteps` | 0x754A90 | 144 | normal, short | **Never patch.** |
| `MoonWorld.TryBuildStructure(...)` | 0x75BEB0 | 2976 | normal | Checks plan/check match and age (≤ 1.1 s), the reach (+0x4BD), obstacles; spawns, `ChargeBuild`, `SaveProgress`. Called once per piece with `def.reach` raised in `try`/`finally`. Not patched. |
| `MoonWorld.StructureObstaclesClear(plan, out reason)` | 0x751CB0 | 11744 | normal | Called for the preview colours. Not patched. |
| `MoonWorld.BuildDefinition(int)` | 0x6AF090 | 336 | normal | Called. Not patched. |
| `MoonWorld.GridCenter` / `GridCorners` / `GridFrame` | 0x6E5950 / 0x6E5A00 / 0x6E5A60 | 176 / 96 / 720 | short | Take the plan-encoded cell `-(id + 1)` (a raw id throws in `MoonSquareGrid.Point`) and return unit-sphere points, scaled by the ghost's radius. Called only. **Never patch.** |
| `MoonWorld.IsWorldSquare` / `IsWorldGrid` | 0x6EFCE0 / 0x6EFC30 | 80 / 96 | leaf, `je` at +0xF | Called only. **Never patch.** |
| `MoonSquareGrid.Find` | 0x591110 | 800 | normal | Called (next cell from a world point). Not patched. |
| `MoonSquareGrid.Center` / `Split` | 0x590E80 / 0x591700 | 128 / 80 | leaf | Not used by the mod. **Never patch.** |

The `ReadInput` postfix binds `ref BuildInput __result` on the IL2CPP struct return and its writes reach the game, so
no yaw-restore fallback is needed. In-game probe (`[Dev] RunProbe`, dev build, slot 2 copy with DevMoney, 2026-11-03),
driven through `TestInput` with Shift stood in by a dev flag and the facing by a dev direction:

- Plain wheel tick: `yaw 180.0 -> 270.0 N=1`. Five Shift + wheel ticks: `yaw 180.0 -> 180.0 N=6 wheel ticks cancelled 5`.
- Run of 6 from a ghost 20 m out: `queued 5 pieces`, each `TryBuildStructure ok=True`, steps 7.961 to 7.962 m, reach
  `30 -> 38.62 -> 30` up to `30 -> 54.17 -> 30` for the pieces past 32 m; depots +6, wallet −$12, notice `Built 6 of 6`.
- A dust-canister copy in run cell 3: preview `GGRGG`, prompt `Run 6 · 1 blocked`, the click refused (`place` cleared),
  depots +0, wallet unchanged.
- `RunIgnoresReach = false`: preview `GGOOO`; `out of reach at 34.3 m ... run stops`, `Built 3 of 6 (stopped: out of reach)`.
- Reach restored: every run in the final pass (pass 6) logged `reach now 30 (was 30)`; that is its only evidence of
  the restore. The refusal itself comes from pass 2: after a reach-raised run, an unraised `TryBuildStructure` 40.7 m
  out gave `Move closer to the build site` with reach 30. In passes 4 to 6 that control aimed at an already-built tile.
- The probe runs above predate fix round 1 (built-cell check in `NoteBuilt`, no run preview while the ghost is invalid,
  a 20-slice site check, numeric wallet read); those changes are not yet re-run in game.
- No errors from Build QoL. Not tested in game: Shift + wheel by hand (the spike's pointer checks show `wheel` is
  `Mouse.scroll.y`), a run across a cube-face seam, other square kinds than 101, the money check with a short wallet,
  gamepad, multiplayer.

## Changelog

- 0.1.0: First release. `KeepSelection`: keep the placed structure selected with a new ghost, with rotation kept.
- Unreleased: `FoundationRun`: Shift + mouse wheel sets a run length for platform foundations; one click builds the run.
  Runs started on top of existing foundations stack on them (first play-test found them refused as blocked).
