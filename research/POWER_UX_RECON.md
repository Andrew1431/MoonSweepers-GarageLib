# Power UX recon: look-at panel, fuel power to vanilla machines, arcs, modded canister look

Traced in game on slot-2, 2026-11-02, with the dev-only probe `src/StructureLib/DevPowerUx.cs` (`[Dev] PowerUxProbe`,
`PowerUxTest`, `PowerUxHome`; `[powerux]` log lines). The installed mods were StructureLib (Debug), SandPower, BeltBoxes
and DevMoney (all Release). Seven game runs:

| Run | What it did |
|---|---|
| 1 | Built the test bed and traced the solve and canister views (the panel was hidden: the walker was carrying a canister) |
| 2 | Tour without the build tool: machine panels only |
| 3 | Tour with and without the build tool, then the routes `fuel,link,anchor,arc,tint,far` |
| 4 | Lost: the walker spawned where run 3 left it, so the probe found no generator nearby |
| 5 | Routes again: `link`, the PowerAnchor prefix, `fuel`, `tint`, `far` |
| 6 | Our own arc and canister close-ups |
| 7 | Canister close-ups with the pitch fixed |

**The test bed** (it stays in slot-2 for the implementation):
- Sand compactors 26 and 27, briquette generator 28 and pressure chamber 29, in a line at z = -35.75.
- Belt 1 runs from the output of 27 to the input of 28. A dust canister still waits at its end.
- The only vanilla power is the solar twin, depot 1, about 50 m away, with the vanilla chamber 2 on its grid 1. So
  chamber 29 is 8.0 m from the generator and outside every solar range.
- The walker's position is saved. `[Dev] PowerUxHome = 22.39,1993.00,-29.77` puts it back at the bed.

**Native sizes** come from the hazard-table method (STRUCTURES_RECON.md, "Interop hazards found"). The RVA and file
offset come from `[Address]` in `BepInEx/dummy/Assembly-CSharp.dll`. The size is the gap to the next Assembly-CSharp
method. The bytes come from `GameAssembly.dll`. A script listed the direct `E8` call targets in each body that land on
a method start, which is how the call orders below were read. Every method the probe patched is a normal prologue of
192 B or more, with no branch or `ret` in its first 16 bytes:

| Method | Size (B) |
|---|---|
| `MoonProductionInteraction.ShowPanel` | 6912 |
| `MoonLookPanel.Show(5)` (first branch at +0x1C) | 400 |
| `MoonLookPanel.SetRow` | 464 |
| `MoonLookPanel.Rows` | 416 |
| `MoonWorld.StepProduction` | 912 |
| `MoonWorld.SolvePower` | 9440 |
| `MoonWorld.PowerAnchor` | 752 |
| `MoonPowerLinks.ShowGrid` | 2464 |
| `MoonPowerLinks.Link` | 464 |
| `MoonPowerLinks.Arc` | 1792 |
| `MoonPowerLinks.ShowAmbient` | 1088 |
| `MoonPowerLinks.ShowPreview` | 1088 |
| `MoonCanisterStreaming.Place` | 624 |
| `MoonDustCanisterView.Show` | 1104 |
| `MoonDustCanisterView.Bind` | 560 |
| `MoonDustCanisterView.ShowCargo` | 1152 |
| `MoonDepotBuilder.get_HoldingBuildTool` | 192 |

`MoonProductionInteraction.SetRow` (80 B, `je` at +0xB) is a short leaf, so it was not patched. `TryPanel` was not
patched.

New member dumps in this commit: `research/dump_MoonLookPanel.txt`, `dump_MoonDustCanisterView.txt`,
`dump_MoonDroppedCanister.txt`, `dump_MoonActionPromptCard.txt`, `dump_MoonDustCanisterInteraction.txt`,
`dump_MoonCrewCanisterFlight.txt` and `dump_MoonDustCanisterFlight.txt`. Trace excerpts are kept locally (gitignored) in
`.superpowers/sdd/2026-11-02-power-ux/logs/`.

## A. The look-at panel

### Mechanism

- **`TryPanel(kind, out spec)` writes the tracker spec for every kind except 93 and 94, and returns true only for
  93..95.** This was decoded from its 272-byte native body (file offset 0x75ECD0):
  - `cmp ebx,5Dh / jne` → for 93, copy PostPanel.
  - `cmp ebx,5Eh / jne` → for 94, copy TwinPanel.
  - Otherwise copy TrackerPanel (the statics at +0x130/+0x140) into `*out`.
  - Then `cmp ebx,5Dh / jl → return false`, else `return kind <= 95`.

  So a modded kind gets `spec = TrackerPanel` and false.
- **ShowPanel calls IsProcessor and then TryPanel.** `ShowPanel(world, record, power)` calls `ProductionOf`,
  `BuildDefinition`, `PowerGridOf` and `IsProcessor` (+0x482), then `TryPanel` (+0x49A), then `MoonLookPanel.Show`.
  IsProcessor is unpatchable, and it answers false for a modded kind. ShowPanel evidently ignores TryPanel's false and
  uses the spec. That gives "Solar tracker (L)", 34 m, 16 kW and 10 kWh.
- **The panel is drawn only through three MoonLookPanel calls:** `MoonLookPanel.Show(what, name, state, stateColor,
  opacity)` (kicker, title, status), `SetRow(i, label, fill, value, color)` and `Rows(n)`. The panel fields are `kicker`,
  `title` and `status` (TMP_Text), plus `rows[i].label`, `rows[i].value` and `rows[i].fill` (Image). The TMP_Text walk
  under `look.canvas` found exactly those texts.
- **Who gets a panel.** `MoonProductionInteraction.Update` (2688 B) calls `MoonDepotBuilder.get_HoldingBuildTool`
  (+0x500), `Aimed` (+0x532), `IsProcessor` (+0x599), `MoonPowerLinks.ShowGrid` (+0x5C7) and `ShowPanel` (+0x5E2).
  `Aimed` (720 B) calls `MoonDepotConstruction.StructureAt(collider)`, `TryGetStructure` and `IsPowerNode` (+0x299).
  Observed (run 3, each target with the build tool off and then forced on through a `get_HoldingBuildTool` prefix):

  | Target | Build tool off | Build tool on |
  |---|---|---|
  | Vanilla panel (twin 1) | no panel (canvas inactive) | `ShowPanel(1, 94, power True)` |
  | Vanilla machine (chamber 2, 29) | `ShowPanel(id, 96, power False)`: production panel | `power True`: production panel plus a POWER row |
  | Briquette generator 28 | no panel | `ShowPanel(28, 103144923, power True)`: tracker panel |
  | Sand compactor 27 | no panel | `ShowPanel(27, 430315676, power True)`: **tracker panel** |
  | Box splitter 30 | `Aimed` 0, no panel | `Aimed` 0, no panel |

  - The player's wrong panels are the generator and the compactor, and only with the hammer in hand.
  - The modded machine gets the *power-source* layout because IsProcessor is false. Without the tool it gets nothing,
    while a vanilla machine gets its production panel.
  - The router gets nothing. `Aimed` returned 0 for it in runs 2 and 3, with and without the tool and with both pitch
    signs. Aimed's only kind query is IsPowerNode, which answers false for a router. That it filters is argued from the
    call list; the aim itself was not proven independently.

### Exact texts (run 3)

The `·` separator has two spaces on each side in the kicker, and one in the row values.

**Vanilla solar twin, depot 1 (night, grid 1):**
```
Show(what 'WIRELESS POWER  ·  24 M RANGE', name 'Solar twin (M)', state 'Night: its battery powers the grid', colour rgba(0.56,0.60,0.65,1.00))
SetRow(0, 'OUTPUT', fill 0.00, '0.0 / 7.5 kW', rgba(0.27,0.88,0.94,1.00))
SetRow(1, 'BATTERY', fill 0.26, '1.0 / 4 kWh', rgba(0.50,0.88,0.63,1.00))
SetRow(2, 'GRID', fill 0.00, '1 machine · 1 panel · 0.0 kW made', rgba(0.27,0.88,0.94,1.00))
SetRow(3, 'DEMAND', fill 0.00, 'no machine running', rgba(0.50,0.88,0.63,1.00))
Rows(4)
```

**Briquette generator, depot 28 (state Grid -26, ChargeKwh 6.0000; `grid NOT in powerGrids`):**
```
Show(what 'WIRELESS POWER  ·  34 M RANGE', name 'Solar tracker (L)', state 'Night: its battery powers the grid', colour rgba(0.56,0.60,0.65,1.00))
SetRow(0, 'OUTPUT', fill 0.00, '0.0 / 16 kW', ...)   SetRow(1, 'BATTERY', fill 0.60, '6.0 / 10 kWh', ...)
SetRow(2, 'GRID', fill 0.00, '0 machines · 0 panels · 0.0 kW made', ...)   SetRow(3, 'DEMAND', fill 0.00, 'no machine running', rgba(0.95,0.42,0.11,1.00))
```
- BATTERY is our `ChargeKwh` over the tracker's 10 kWh.
- GRID is empty because grid -26 has no `powerGrids` entry. In the play-test, where the generator sat on a solar grid,
  it read "2 machines · 2 panels".
- DEMAND turns amber when the grid lookup fails.

**Sand compactor, depot 27:** the same layout with `state 'Night: no sunlight'` and `BATTERY '0.0 / 10 kWh'`.

**Vanilla chamber 29, no panel in reach.** Without the tool:
```
Show(what 'PRODUCTION  ·  DUST INTO SAND', name 'Pressure chamber', state 'No power: build solar panels within reach', colour rgba(0.95,0.42,0.11,1.00))
SetRow(0, 'DUST', fill 1.25, '25 / 20 kg', rgba(0.72,0.74,0.78,1.00))   SetRow(1, 'SAND', fill 0.00, '0 / 10 kg', rgba(0.86,0.66,0.38,1.00))
SetRow(2, 'CYCLE', fill 0.02, '2% · 0 done', rgba(0.27,0.88,0.94,1.00))   Rows(3)
```
With the tool, row 0 becomes `SetRow(0, 'POWER', fill 0.00, 'no panels in reach', rgba(1.00,0.48,0.42,1.00))`, the slot
rows move down one, and `Rows(4)`. On chamber 2 (grid 1) the POWER row reads `'idle · 0.0 kW made · 26% stored'` in
cyan. An idle CYCLE row reads `'0 done'`. The status is `MoonProductionState.Status`, also seen as `'Connect a belt to
the sand output'`.

### Recommended fix

- **(b) A prefix on `MoonProductionInteraction.ShowPanel(MoonWorld world, MoonDepotSnapshot record, bool power)`**
  (6912 B, normal prologue). For a modded kind it returns false and draws the panel itself:
  - `__instance.look.Show(what, name, state, colour, opacity)`, `look.SetRow(i, label, fill, value, colour)` for each
    row, and `look.Rows(n)`, in the vanilla formats above.
  - The colours are `MoonProductionInteraction.Cyan` / `Amber` / `Coral` / `Mint` / `Muted` (statics).
  - The opacity: vanilla reads `MoonPlayerStats.HudOpacity` (+0x410 in ShowPanel; `board` field). Pass the same.
  - Sources: the generator uses its definition (name, PeakKw, BatteryKwh, Range), its state's `ChargeKwh` and
    `SuppliedKw`, and the fuel-network members. A modded machine uses the production layout (kicker from the
    definition, title, `state.Status`, slot rows from its stores, CYCLE), plus a POWER row when `power` is true.
  - Vanilla kinds fall through.
  - This is cheaper and safer than (a), a postfix that rewrites every row, because TryPanel's tracker numbers would leak
    into fills and colours.
- **Untested.** No managed call to `look.Show` / `SetRow` / `Rows` was made. MoonLookPanel is an Il2CppSystem.Object
  whose methods take strings and a Color; that should marshal, but it is not proven.
- **A modded machine also needs a panel without the tool.** Vanilla never asks ShowPanel for it, because IsProcessor
  is false.
  - Option: a postfix on `MoonProductionInteraction.Update` (2688 B, normal prologue) that, when `Aimed(world)` is a
    modded processor and `builder.HoldingBuildTool` is false, calls `ShowPanel(world, record, false)` (which our prefix
    then draws).
  - Not tested. Watch that Update's own Hide for the not-a-processor case may run in the same frame; a LateUpdate-time
    call from our driver may be needed instead.
- **Routers show nothing** (not a wrong panel). Leave them, or make a separate feature.

## B. Fuel power to vanilla machines

### Order

`StepProduction(dt)` (912 B) calls `SolvePower` itself (+0xD0), on a timer: SolvePower's dt was 0.252 / 0.267 s while
StepProduction's frame dt was 0.003–0.034 s. It then steps the machines (`Processor`, `TryStartCycle` +0x204,
`FinishCycle` +0x23B, `EmitProduct` +0x2B5). Run 1, one solve frame:
```
f6904 StepProduction pre dt 0.003: chamber Grid 29 Power 0.000 DemandKw 10.00 UsedKw 0.00 ... Running True Progress 0.018 ... Status 'No power: build solar panels within reach'
f6904 SolvePower pre dt 0.252: chamber Grid 29 Power 0.000 DemandKw 10.00 ...
f6904 SolvePower post (after StructureLib): chamber Grid 29 Power 0.000 DemandKw 10.00 UsedKw 0.00 ...
f6904 StepProduction post: chamber Grid 29 Power 0.000 ...
f6905 StepProduction pre dt 0.003 ... / f6905 StepProduction post ...   (no SolvePower in frames 6905..6908)
```

### The chamber out of range

- **Its state after the vanilla solve:** `Grid 29` (its own grid: `grid 29: panels 0 machines 1 supply 0.00 ...`),
  `Power 0`, `DemandKw 10`, `UsedKw 0`, `Running True`, `Status 'No power: build solar panels within reach'`, and
  Progress stuck at 0.018.
- **Who sets the demand:** the solve. `DemandKw` is already 10 before our postfix; SolvePower calls `CanStartCycle`
  (+0x13FF) for it.
- **Why it was Running:** right after the build, `NewProduction` left `Power 1.000`, so the first StepProduction started
  a cycle before any solve ran (t12: Power 1, not running; t14: Running True, Progress 0.018, Power 0).

### The fuel route (runs 3 and 5)

A `SolvePower` postfix with `HarmonyPriority(Priority.Last)`, after StructureLib's, sets `_Power_k__BackingField = 1`,
`_UsedKw_k__BackingField = DemandKw` and `_Grid_k__BackingField = <generator's grid, -26>`:
```
f2405 fuel: chamber before Grid 29 Power 0.000 DemandKw 10.00 UsedKw 0.00 ... Progress 0.018 -> after Grid -26 Power 1.000 DemandKw 10.00 UsedKw 10.00 ...
f2413 SolvePower pre: chamber Grid -26 Power 1.000 ... Progress 0.040
f2413 SolvePower post (after StructureLib): chamber Grid 29 Power 0.000 ... Progress 0.040   <- vanilla resets it every solve
t 69 .. t 81: Progress 0.018, 0.182, 0.324, 0.490, 0.657, 0.824, 0.963
f2740 FinishCycle(chamber): Grid -26 Power 1.000 ... Running True Progress 1.002 Cycles 0 ... stores [20.00 5.00 0.00]
t 83 chamber 29: ... Progress 0.103 Cycles 1 ... stores [15.00 5.00 5.00]      <- 5 kg sand made, the next cycle started
```

- **Vanilla does not overwrite the fields before StepProduction reads them.** Progress advanced about 0.022 per
  0.267 s solve interval, which is the 12 s cycle at Power 1.
- **But every solve resets them** (Power 0, Grid 29). So the raise must be reapplied in every SolvePower postfix. It is
  harmless that it is undone, because the next StepProduction only reads after the next postfix.
- **The status text stays "No power: …":** the solve writes it. `MoonProductionState` has writable `powerStatus` and
  `flowStatus` string fields; that `Status` derives from them is unconfirmed.
- **Stall behaviour (`StallPower` 0.08) was not measured with partial power.** The route only used 0 → 1. Not
  established: whether Power between 0 and 0.08 stalls progress or only slows it, and whether an idle chamber with
  `Power < StallPower` refuses to start a cycle (`TryStartCycle` / `CanStartCycle`).
- **Energy.** The generator's `ChargeKwh` stayed 6.0000 throughout: the probe route created energy.
  - The real implementation, in StructureLib's own postfix, must treat each vanilla machine whose `Power < StallPower`
    (or below what its vanilla grid gives) and that lies within a fuel generator's Range as a member drawing
    `DemandKw`.
  - It should take that from `FuelGrid.AvailableKw` and drain `ChargeKwh` by `drawn × dt / 3600`.
  - It sets `Power = drawn / DemandKw` and `UsedKw = drawn`, and `Grid` to the network id. Taking `Grid` from the
    network keeps ShowPanel's grid lookup failing ("0 machines · 0 panels"), which only matters for the vanilla panel
    text.

## C. Arcs

- **`MoonPowerLinks.ShowGrid(id)` runs only from `Update` while the build tool is held and a power node is aimed.**
  Run 3, aiming the twin:
  ```
  ShowGrid(1)
  Link(1, 2, strength 1.00) in ShowGrid(1)
  Arc((-24.09,1993.09,-16.46) -> (-34.02,1995.80,-17.60), width 0.120, rgba(1.00,1.00,1.00,0.35)) in ShowGrid(1)
  ShowGrid(1) done: used 1 lines 1 gridPanels [1] linkedPanels [1] gridMachines [2] ring (-24.09,1991.98,-16.46) r 24.0
  ```
  - The arc ends are `PowerAnchor(id)` points: `powerAnchors` held 1, 2 and 29 (for example
    `29=(35.56,1995.80,-35.75)`).
  - `Link` (464 B) calls `IsSolarPanelId` (both ends), `PowerAnchor` (both), `ProductionOf` (both) and then `Arc`.
  - The ring is the panel's `PowerRange`, drawn by `Ring(centre, radius, colour)`.
- **For our structures `ShowGrid` finds nothing:** `ShowGrid(28)`, `(29)` and `(27)` ended with `used 0`, empty
  `gridPanels` and `gridMachines`, and no `Link`. That held even with the pair (28, 29) in `world.powerLinks`
  (`f2405 link: added (28,29) to powerLinks, now 2`, runs 3 and 5), with `powerAnchors[28]` set (run 3), and with a
  `PowerAnchor` prefix answering 28 (run 5). So ShowGrid takes its members from grid membership, not from `powerLinks`.
  - Grid membership is evidently `powerGrids` / the node `parent` roots; its call list holds no `PowerGridOf`.
  - Grid -26 has no `powerGrids` entry, and `powerGrids` holds structs managed code cannot write back.
- **The solve rebuilds `powerLinks`.** SolvePower pre showed 2 links and post showed 1, so an added pair lives only
  until the next solve.
- **`PowerAnchor(28)` is false** in Layout (run 1), and still false after `powerAnchors[28]` was set and read back
  (run 3). PowerAnchor (752 B) computes the anchor from `BuildDefinition` / `DepotPose` / `Anchor`, behind a check the
  modded kind fails; the dictionary is not what it answers from.
- **Our own `MoonPowerLinks` draws.** `new MoonPowerLinks(world, interaction.transform)`, with `Begin()`,
  `Arc(from, to, width, colour)` and `End()` called every frame from our driver. The material is `'Power link'`.
  ```
  arc: drawing (27.57,1994.00,-35.79) (anchor False) -> (35.56,1995.80,-35.75) (anchor True) width 0.060 rgba(0.27,0.88,0.94,1.00); used 1 lines 1
  ```
  Run 6's `powerux-2.png` (side view of the pair) shows a cyan arc from the chamber's top towards the generator.
  - Vanilla's own call is width 0.12 with `rgba(1,1,1,0.35)`. The run 3 arc used those values; how that colour renders
    is not established.
- **Recommended fix:** StructureLib owns one `MoonPowerLinks` and draws its fuel-network arcs itself.
  - Each frame it calls `Begin`, one `Arc` per (generator, member) pair, then `End`.
  - Anchors: `PowerAnchor(id)` for vanilla ends; for modded ends, the depot position plus 2 m up (or a definition
    anchor).
  - Gate it like vanilla: build tool held and a network member aimed (`MoonDepotBuilder.HoldingBuildTool`,
    `MoonProductionInteraction.Aimed`). Optionally add `Ring(generatorPos, Range, colour)`.
  - No game method needs patching for this. A vanilla panel cannot be the source of a modded arc, since vanilla never
    links into a modded grid.

## D. The canister look

- **The view.** Canisters use `MoonDustCanisterView`, the same class for belt, ground and hand. The hand uses its own
  instance, `'Carried dust capsule'`; run 1 saw `ShowCargo(view 38045 'Carried dust capsule' Id 14, cargo 3)`.
  `MoonCanisterStreaming.Place(view, c)` → `MoonDustCanisterView.Show(c, point, rotation)` → `RefreshFill(c)` (960 B) →
  `ShowCargo(MoonCargo cargo)` (1152 B, no calls into Assembly-CSharp). `Bind(world, id)` runs when a pooled view is
  given a canister.
- **What ShowCargo does** (run 1). `dustRenderer` 'Dust', material 'Dust', shader 'Moon Cleaners/Station finish'
  (it has `_BaseColor`):
  - dust (0): the Dust renderer's property block gets `_BaseMap 'CanisterDust'` (`dustTexture`).
  - sand (1): `_BaseMap 'moon_sand_base_color'` (`sandTexture`).
  - stone (2): `stoneBlock` is set active and the other parts inactive; the stone block's own block gets
    `_EmissionColor rgba(3.95,0.58,0.04,0.94)` (`stoneGlow`).
  - briquette (3): `_BaseMap 'CanisterDust'`. **Unknown cargo takes the dust branch.**
- **Tint test** (`tint`; runs 3, 5, 6 and 7). A ShowCargo postfix set `_BaseColor` on the Dust renderer's block *and*
  on the view's own `properties` block, which RefreshFill re-applies. It used `CargoDefinition.CanisterTint`, else a
  test charcoal `(0.25,0.20,0.18)`, and white for cargo 0..2.
  - Read back seconds later, the blocks keep it alongside the texture:
    - briquette: `dust block: ... _BaseMap 'CanisterDust' _BaseColor rgba(0.25,0.20,0.18,1.00)`;
    - dust: `_BaseColor rgba(1.00,1.00,1.00,1.00)`;
    - sand: `_BaseMap 'moon_sand_base_color' _BaseColor rgba(1,1,1,1)`.
  - Pooled views re-bound to dust, sand or stone got white back (run 3: every cargo 0/1/2 view logged
    `_BaseColor (1,1,1,1)`).
  - In run 7's close-ups (`run7/powerux-5.png`, `powerux-7.png`), some canisters have a dark fill next to grey dust and
    beige sand. That the dark ones are the briquettes is a judgement by eye; no renderer-id-to-pixel check was made.
- **Far path.** Beyond the near views, `LateUpdate` (3744 B) draws canisters instanced: `WorkOut(FarShown, c, fill)` and
  then `Draw(FarPart, ...)`.
  - `BuildParts` makes 8 parts: Rubber, Graphite, Orange, Ivory, Glass, `'Dust (far, instanced)'` twice (flags
    `dust True sand False fill True`, and `dust True sand True fill True`) and the stone block.
  - At 95 m (run 5) the ground briquette was `farShown cargo 3 fill 1.00 valid True`, so it is drawn with one of the
    two dust fill parts. Which one was not established; neither is tinted.
  - Belt canisters at 102 m were in neither path (`FarDistance` 100).
  - A far tint would need a third fill `FarPart` with a tinted material, plus a cargo-to-part choice inside
    LateUpdate/WorkOut. That is not cheap. Recommend accepting the dust look at a distance for now.
- **Recommended fix:** a postfix on `MoonDustCanisterView.ShowCargo(MoonCargo cargo)` (1152 B, normal prologue):
  - Modded cargo with a `CanisterTint`: `v.properties.SetColor("_BaseColor", tint)` and the same on
    `v.dustRenderer`'s block (`GetPropertyBlock` / `SetColor` / `SetPropertyBlock`).
  - Anything else: set `_BaseColor` white, since views are pooled.
  - Optionally swap `_BaseMap` for a modded texture the same way.
  - This covers belt, ground and hand.

## E. Sun factor

Status, 2026-11-08: **the formula below is read from the machine code. It has not been measured in game.** The game
could not be run for this task. The sample table is still to be measured; the procedure is at the end of this
section. Until the table is filled in, treat the formula as a hypothesis with strong static support.

**Method.** Each method's RVA comes from `[Address]` in `BepInEx/dummy/Assembly-CSharp.dll`. The bodies were
disassembled from `GameAssembly.dll` with MSVC `dumpbin /disasm` (image base `0x180000000`). Field offsets come from
`[FieldOffset]` in the same dummy assembly. The float constants were read from the image. The three CRT helpers the
code calls were told apart by mapping a copy of `GameAssembly.dll` without running it (`LoadLibraryEx`,
`DONT_RESOLVE_DLL_REFERENCES`) and calling each one on 0.5 and 0.1. `0x1802BCD64` is `asin`, `0x1802BBD40` is `cos`
and `0x1802BC5A0` is `sin`. The tools were scratch programs and are not in the repo.

Offsets used:
- `PowerNode`: `up` +0x2C, `rotation` +0x38, `panel` +0x50, `spec` +0x58.
- `PanelSpec`, inline in the node: `peakKw` +0x60, `tiltDeg` +0x6C, `maxTiltDeg` +0x70, `tracker` +0x74.
- `MoonProductionState`: `DemandKw` +0x2C, `UsedKw` +0x30, `SunFactor` +0x60, `Daylight` +0x64, `SunAim` +0x68,
  `TrackerTilt` +0x74.

**What `SolvePower` does per panel** (`MoonWorld.SolvePower`, RVA `0x744AC0`, panel loop at +0xDA0..+0x124E):
- The sun direction is `s = world.SunDirection`. It is read once per solve (call at +0xD03). `get_SunDirection`
  calls `MoonSolarSystem.get_LunarTime` and `SunDirectionAt`, so it is `SunDirectionAt(LunarTime)` unless
  `SunOverride` is set.
- `h = dot(n.up, s)`, using the node's own up, not the walker's.
- `d = smoothstep(clamp01((h + 0.0096) / 0.0192))`, where `smoothstep(t) = 3t² − 2t³`. This is a soft horizon about
  ±0.55° wide: `d` is 0 below the horizon and 1 just above it.
- `Daylight = d > 0.5`, which is the same as `h > 0`.
- **The panel normal `nrm`:**
  - Fixed post and twin: `nrm = n.rotation * (0, cos(tiltDeg°), −sin(tiltDeg°))`. That is the panel's local up,
    tipped by `tiltDeg` toward its local back. `SunAim` and `TrackerTilt` are not written for a fixed panel.
  - Tracker:
    - `p` is `s` projected onto the plane normal to `n.up`, then normalised. If that is about 0, the code uses
      `n.rotation * Vector3.back` instead.
    - `zenith = 90 − asin(clamp(h, −1, 1))·57.29578` (degrees).
    - `tilt = tiltDeg > zenith ? tiltDeg : min(maxTiltDeg, zenith)`, so the zenith clamped to
      `[tiltDeg, maxTiltDeg]`.
    - `nrm = n.up·cos(tilt) + p·sin(tilt)`.
    - It writes `SunAim = p` and `TrackerTilt = tilt`. While `d ≤ 0` it writes `rotation * back` and `tiltDeg`
      instead.
- **`SunFactor = max(0, dot(s, nrm)) · d`.** Then `UsedKw = SunFactor · spec.peakKw` and `DemandKw = 0`.

So:
- **Fixed panels (post, twin):** `SunFactor = max(0, dot(s, nrm)) · d`. With `tiltDeg = 0` this is just
  `max(0, dot(s, up))` above the horizon.
- **Tracker:** it does **not** read `max(0, elevation)`. While the zenith angle is inside `[tiltDeg, maxTiltDeg]`,
  the normal points straight at the sun, so `SunFactor = d ≈ 1` whatever the elevation. Outside that range it is
  `cos(zenith − clampedTilt) · d`. Its azimuth is free, since `p` follows the sun, so it depends on `up` only through
  `h`.
- **For the solar array (Task 5):** `Sun.Factor` should copy this. It takes `s` from
  `MoonSolarSystem.SunDirectionAt(solar.LunarTime)` (or `world.SunDirection`), `up` from the site's surface normal,
  and a normal from the array's own tilt rule. `MoonSolarSystem.Daylight` is a world-wide value with no per-site
  geometry. It is not the fallback unless the measurement disagrees with this formula.
- The vanilla `tiltDeg` / `maxTiltDeg` values are built in `MoonWorld..cctor` from data, not from immediates, so they
  were not read statically. `LogSun` prints them.
- One weak check against earlier data: STRUCTURES_RECON.md (b) saw a twin at `SunFactor` 0.744 with the clock set
  to the time whose sun was closest to the walker's up (best dot up about 0.74). That fits a twin with a small
  `tiltDeg`. It is not a test of the formula.

**Sample table: to be measured.**

| Hour | Kind | `SunFactor` | predicted | err | `Daylight` | `SunAim` | `TrackerTilt` | solar `Daylight` | dot(sun, up) | LunarTime |
|---|---|---|---|---|---|---|---|---|---|---|
| 0, 6, 9, 12, 15, 18 | twin / post / tracker | to be measured | | | | | | | | |

Worst error: to be measured. The pass mark is under 3 % at every sample for the twin and the post.

**Procedure** (for the controller, game closed first, `game.lock` held):
1. `dotnet build src/StructureLib -c Release -p:InstallToGame=true` (a DevBuild).
2. Back up `BepInEx/config/com.ryanhartwig.structurelib.cfg`, then set `[Dev] AutoLoadSlot = 2` and
   `LogSun = true`. Never use slot 1. If slot 2 has no panels, add `AutoBuild = 94` (93 is the post, 95 the tracker)
   and restore slot 2 afterwards.
3. Launch `MoonSweepers.exe` directly. Once the `[dev]` lines appear, step DevMoney's `[Time] TimeOfDay`
   (`com.ryanhartwig.devmoney.cfg`, re-read on change) through 0, 6, 9, 12, 15 and 18, about 8 s each, then set
   it back to -1.
4. Collect the lines from `BepInEx/LogOutput.log`. The `[sun] session:` line gives `InLobby`, `IsHost` and
   `MoonCrew.Joined`. Each `[sun] ... depot <id> <kind>:` line gives `SunFactor`, the `predicted` value from the
   formula above (`SunLog.Predict` in `src/StructureLib/DevBelts.cs`), `err`, `Daylight`, `SunAim`, `TrackerTilt`,
   `solar.Daylight`, `LunarTime`, dot(`SunDirectionAt`, up), dot(`world.SunDirection`, up) and the spec's tilt.
5. Fill in the table. If `err` stays under 0.03 at every sample, the formula is confirmed. If it does not, check
   first whether `world.SunDirection` and `SunDirectionAt(LunarTime)` disagree (`SunOverride` set).
6. Close the game, restore the cfg and slot 2, and delete the lock.

## F. Co-op state flow

Status, 2026-11-08: this verdict comes from the dumps and from static reading of the machine code, the same way as
section E. **No two-player session was run** (no second player was available within the task). Single-player
logging was not possible either, since the game could not be run. `LogSun`'s `[sun] session:` line will record
`MoonSteamLobby.IsHost`, `InLobby` and `MoonCrew.Joined` on the next run.

**Verdict: (S) every peer solves its own world.** Power state is not replicated. Only machine cycle state is.

The evidence, from `dump_MoonWorld.txt`, `dump_MoonCrewSession.txt`, `dump_MoonCrew.txt`, `dump_MoonSteamLobby.txt`
and the disassembly:

1. **`SolvePower` runs on every peer.** The call chain is
   `MoonWorld.Update` (+0x8A9) → `StepConveyors` (+0x2AB) → `StepProduction` (+0xD0) → `SolvePower`.
   - `Update` calls `StepConveyors` with nothing around it but the profiler marker. `StepConveyors` calls
     `StepProduction` without a condition.
   - `StepProduction` does three things before the call: it checks that `depots` is non-empty, adds `dt` to
     `productionClock` (+0x920), and calls `SolvePower` once that reaches 0.25 s. It makes no crew, lobby or host
     check.
   - `SolvePower` (9440 B) calls neither `MoonCrew.get_Joined` nor any `MoonSteamLobby` getter, and it does not touch
     their class statics (`0x18236CE80`, `0x182370580`). Its only Assembly-CSharp callees are `TryPanel`,
     `ProductionOf`, `Processor`, `NodeDistance`, `CanStartCycle`, `BuildDefinition`, `StructurePose`, `Anchor`,
     `Root`, `get_SunDirection` and `MoonLoc.T`.
   - This is the only caller of `SolvePower`. No other method has a direct `call` to it.
2. **Machine cycles are host-only.** Inside `StepProduction`'s per-machine loop, `MoonCrew.get_Joined()` (+0x1E2)
   skips `TryStartCycle`, the progress step, `FinishCycle` and `EmitProduct` when it is true.
   - `Joined` is a non-zero test on a `MoonCrew` static. `MoonCrew.Join(root, host)` sets those statics, so it is a
     client's flag.
   - A client's `Running`, `Progress` and cycle count therefore come from the host.
3. **The host streams cycle state, not power state.**
   - `MoonCrewSession.UpdateWorld` calls `SendProduction()` only when `!MoonCrew.Joined` and
     `Time.time ≥ nextProduction` (+0xAEE..+0xB0C). That is `Kind.Production = 63` on the crew channel.
   - `SendProduction` → `MoonWorld.WriteCrewProduction(w, progress)` writes, per processor state only (`Processor(kind)`
     filter): `Id`, `Running`, `Cycles`, the `stores` (owner and kg per slot), and `Progress` into the second writer.
   - The client's `MoonCrewSession.HandleWorld` (+0x274F) → `ApplyCrewProduction` writes only `Running`,
     `Progress`, `lastCycle`, `hostCycles` and the stores.
   - **No power field travels:** `Power`, `DemandKw`, `UsedKw`, `Grid`, `ChargeKwh`, `SuppliedKw`, `BatteryKw`,
     `SunFactor`, `Daylight`, `SunAim` and `TrackerTilt` are absent. Solar panels are not processors, so they are not
     in the message at all.
4. **The clock is shared, so the sun agrees.** `UpdateWorld` sends `Kind.Sky = 77` (`MoonSolarSystem.WriteCrewClock`).
   The client applies it in `HandleWorld` (+0x2D21) → `MoonSolarSystem.ApplyCrewClock`. Each peer's `SolvePower`
   then sees the same `LunarTime`, and so computes the same `SunFactor` for the same panel.
5. **Battery charge is integrated per peer.** The join stream is the host's save (`SendWorld` / `UnpackWorld`).
   `SnapshotProduction` → `MoonProductionSnapshot` carries `id`, `progress`, `running`, `charge` and `stores`. So a
   client starts from the host's `ChargeKwh` at join time and then integrates on its own. That is drift, not a
   divergence: both peers compute it from the same sun and from the host-driven `Running` / demand.

**What this means for the grid (Task 5):**
- Under S the `SolvePower` postfix runs on every peer, so **no host gate is needed for the solve**.
- It must be deterministic from replicated inputs: depots, belts, the clock, and the host-driven `Running` and stores.
- Anything the library keeps only in managed memory is per peer. Modded battery charge, if it lives in `ChargeKwh`
  or a sidecar, drifts the same way vanilla's does.
- **A hazard under S:** anything that charges a fuel generator from `FinishCycle` runs on the host only, because
  clients skip `FinishCycle`. Fuel power derived from a cycle event will therefore be missing or late on a client.
  Derive fuel supply from the replicated `Running` / `Progress` rather than from the cycle event.

**What a two-player session must confirm:**
- `[sun] session:` reads `IsHost True` / `Joined False` on the host and `IsHost False` / `Joined True` on the client.
- The `SolvePower` postfix fires on the client: the `[sun]` lines keep coming there at 1 Hz with a changing
  `SunFactor`.
- Both seats log the same `SunFactor` for the same depot at the same `LunarTime`.
- A client's machine `Power` / `Grid` match the host's.
- How far `ChargeKwh` drifts between seats over a few minutes.

## Not established

- **Panel fix:** no managed call to `MoonLookPanel.Show`, `SetRow` or `Rows` was made. The `Update` postfix route for a
  modded machine without the tool is untested.
- **Splitter:** that `Aimed`'s IsPowerNode check is what excludes routers is argued from the call list. The aim at the
  splitter is not proven by an independent check.
- **Stall threshold:** behaviour between Power 0 and 0.08, and whether an idle vanilla machine below `StallPower`
  starts a cycle.
- **Status text:** whether `powerStatus` / `flowStatus` drive `Status`, so that the "No power" text can be replaced.
- **ShowGrid's membership source:** exactly which structure it reads. It is not `powerLinks` or `powerAnchors`.
- **Tint, visually:** judged by eye only. The far-path part a cargo-3 canister uses was not established.
