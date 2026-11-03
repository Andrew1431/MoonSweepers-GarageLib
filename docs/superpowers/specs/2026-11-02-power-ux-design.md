# Power UX: the look-at panel, fuel power for vanilla machines, power arcs, modded canister tint — design

Date 2026-11-02. Author: Ryan's agent, from Ryan's play-test of the SandPower generator on the Release builds of
PR #2 / #1 / #3: "the new buildings have incorrect info when hovering"; "the power building also doesn't power a
nearby building despite having a feed of the sand briquettes"; "briquettes ... are currently rendering as regular
dust"; "We expect that the power will send a beam, like [the solar panel's], to the building". Ryan is not answering
design questions during this work; every choice below not quoted from him is the agent's call.

Reads with: `research/POWER_UX_RECON.md` (the four mechanisms, traced in game 2026-11-02; every number and method
below comes from it), `research/STRUCTURES_RECON.md` ("Power", "Power sources (Task 11)", "Interop hazards found"),
`src/StructureLib/README.md` ("Power", "Status").

## Intent

A modded fuel generator should behave, to the player, like the game's own power sources: hovering it shows its own
name and numbers; a vanilla machine within its range runs on it, with the same blue arc the game draws from a solar
panel to a machine; and a modded cargo's canisters look like their own thing. Today all three fall to vanilla defaults:
the tracker's panel (the game's `TryPanel` hands every unknown kind the tracker spec), no supply (the library attaches
only modded machines), and dust (`ShowCargo` treats every unknown cargo as dust).

## Goals

1. **Own look-at panel.** With the build tool held, aiming a modded generator shows a panel titled with its own name,
   kicker `FUEL POWER  ·  <Range> M RANGE`, and rows OUTPUT (supplied / peak kW), BATTERY (charge / capacity kWh),
   FUEL (kg in its input slot / capacity) and POWERING (machines it fed in the last solve). Aiming a modded machine
   shows the production layout a vanilla machine gets (kicker `PRODUCTION  ·  <IN> INTO <OUT>`, its status, one row per
   input and output slot, a CYCLE row, and with the tool a POWER row first), both with the tool and without it, as
   vanilla machines do. Vanilla kinds are untouched. Routers keep showing nothing (unchanged).
2. **Fuel feeds vanilla machines.** A vanilla machine within a fuel generator's `Range` whose own grid left part of its
   demand unmet draws the rest from the generator(s) of that network, every solve, with the energy taken out of their
   `ChargeKwh`. Its `Power`, `UsedKw` rise accordingly; its cycle runs. Modded machines keep today's rules.
3. **Arcs.** With the build tool held and a member of a fuel network aimed (the generator or any machine it fed), an
   arc is drawn from the generator to every machine it fed in the last solve, plus a ring of its `Range`, in the
   game's own line style (`MoonPowerLinks`). Drawn by the library, not by vanilla.
4. **Canister tint.** `CargoDefinition.CanisterTint` tints the near canister view (belt, ground, hand) of that cargo;
   vanilla cargo and untinted modded cargo keep the default look, including pooled views reused across cargo.
5. **SandPower** sets a tint for the briquette (dark pressed-sand brown) and its README says what the generator powers.
6. **Proven**: xunit tests on the pure text and top-up rules; DEVBUILD self-test checks for each goal with the example
   mod (burner, grinder, grit) and a vanilla pressure chamber; the standard self-test configs stay green; one by-hand
   tour in game with screenshots for the arc and the tint.

## Non-goals

- A panel for routers (vanilla's `Aimed` never aims them; a separate feature if wanted).
- Replacing vanilla's status text on a fed vanilla machine ("No power: build solar panels within reach") unless the
  writable `powerStatus` field turns out to drive it (the implementer tries it once; if it does not, the text stays and
  the README says so).
- Tinting the far instanced canister path (beyond ~100 m canisters are drawn with the dust fill parts). Accepted and
  documented.
- Making vanilla's own `ShowGrid` include modded members, writing `powerGrids`, or patching `TryPanel` / `IsProcessor`
  (unpatchable; see the recon).
- Fuel generators feeding vanilla machines *through* a vanilla grid (a machine 40 m away on the same solar grid): only
  machines within the generator's own `Range` are fed.
- A custom canister texture or mesh per cargo (tint only; `_BaseMap` swap is a later option).

## Anti-goals (a failure even if it works)

- Creating or losing energy: every kW a vanilla machine takes from fuel comes out of a generator's `ChargeKwh` at
  `kW × dt / 3600`, and a generator never supplies more than `PeakKw` or more than its charge lasts.
- A prefix that runs for vanilla kinds, or any new patch on a short native leaf (every method patched here is in the
  recon's size table with a normal prologue; `MoonProductionInteraction.SetRow` is a leaf and is never patched).
- Breaking the existing self-test checks or the 159 unit tests; a native crash.
- A panel that shows the tracker's numbers for anything modded.

## Constraints

- Library changes on branch `power-ux` off `structurelib-lib`, merged into `structurelib-lib` (PR #2) when done; the mod
  branches (`structurelib`, `beltboxes`) merge the library afterwards (merge commits, never force-push). The SandPower
  tint lands on `structurelib`.
- Same toolchain and rules as before: .NET SDK 8, build per project, `Directory.Build.props` installs by default (Release
  checks use `-p:InstallToGame=false` while dev-testing), game closed when installing, slot-2 only, 15 s after a kill
  before saves, 30 s before relaunch. Ryan keeps the game closed and is not playing; dev runs are free.
- `MoonLookPanel.Show/SetRow/Rows`, `MoonPowerLinks.Begin/Arc/Ring/End`, `MoonDustCanisterView.properties` and the
  `_BaseColor` property are the proven surfaces; `PowerAnchor(id, out point)` is the anchor for vanilla ends (false for
  modded ends, which use the depot position + 2 m up).

## Design

### A. Look-at panel (`Patches/PanelPatches.cs`, `Pure/PanelText.cs`)

- A prefix on `MoonProductionInteraction.ShowPanel(MoonWorld world, MoonDepotSnapshot record, bool power)` (6912 B,
  normal prologue). `Remap.Live(record.kind)` null → return true (vanilla). Otherwise draw through
  `__instance.look.Show(what, name, state, colour, opacity)`, `look.SetRow(i, label, fill, value, colour)` per row and
  `look.Rows(n)`, with `opacity = __instance.board.HudOpacity` (fallback 1), and return false.
- Rows are computed by pure code from plain numbers so they are testable: `PanelText.Generator(...)` and
  `PanelText.Machine(...)` return `(string Kicker, string Title, string State, PanelColour StateColour, PanelRow[] Rows)`
  where `PanelRow = (string Label, float Fill, string Value, PanelColour Colour)` and
  `enum PanelColour { Cyan, Amber, Coral, Mint, Muted, Dust, Sand, Stone, Tint }`. The patch maps colours to the
  `MoonProductionInteraction` statics; `Dust (0.72,0.74,0.78)`, `Sand (0.86,0.66,0.38)` and `Stone (0.95,0.42,0.11)`
  are the vanilla row colours seen in the recon; `Tint` is the slot cargo's `CanisterTint` (Muted when none).
- **Generator** (`Power != null && Processor.ProducesPower`): kicker `FUEL POWER  ·  {Range:G} M RANGE` (two spaces each
  side of `·`, as vanilla); title `Name`; state and colour: running → `Burning {fuel short name}` Mint; not running and
  charge ≥ capacity → `Battery full: burns when power is drawn` Muted; not running and fuel slot empty →
  `No fuel: feed {fuel name} by belt` Coral; else `Starting` Muted. Rows: `OUTPUT` `{SuppliedKw:F1} / {PeakKw:G} kW`
  fill supplied/peak Cyan; `BATTERY` `{ChargeKwh:F1} / {BatteryKwh:G} kWh` fill charge/capacity Mint; `FUEL`
  `{kg:0.#} / {cap:G} kg` fill kg/cap Tint; `POWERING` `{n} machine(s)` or `no machine running` fill n>0?1:0, Cyan when
  n > 0 else Amber. `Rows(4)`.
- **Machine** (`Processor` without `ProducesPower`): kicker `PRODUCTION  ·  {IN} INTO {OUT}` with the first Input slot's
  and the first Output slot's short names upper-cased (`PRODUCTION` alone when either is missing); title `Name`; state =
  `state.Status` (what our cycle code already writes; empty → `Idle`), Cyan while Running, Amber otherwise, Coral when
  `Grid == 0`. Rows, in order: with `power` true a `POWER` row first: `Grid == 0` → `no power source in reach` Coral
  fill 0; else `{Power*100:F0}% · {UsedKw:F1} of {RunningKw:G} kW` Cyan fill Power. Then one row per Input and Output
  slot (Batch slots skipped): label = slot cargo short name upper-cased, `{kg:0.#} / {cap:G} kg`, fill kg/cap, colour
  Dust/Sand/Stone for vanilla cargo, Tint for modded. Then `CYCLE`: running → `{Progress*100:F0}% · {Cycles} done`
  fill Progress Cyan; else `{Cycles} done` fill 0 Muted. Rows capped at `MoonLookPanel.MaxRows` (the CYCLE row is kept,
  the last slot rows dropped first).
- **Without the tool**: a postfix on `MoonProductionInteraction.Update` (2688 B, normal prologue): when
  `builder.HoldingBuildTool` is false and `Aimed(world)` is a depot of a modded machine (Processor, not ProducesPower)
  with a production state, call `__instance.ShowPanel(world, record, false)` (our prefix draws it). Routers and
  generators without the tool: unchanged (nothing). The implementer verifies in game with the probe's tour that
  vanilla's own Hide in the same frame does not blank it; if it does, drive it from `CoreDriver.LateUpdate` instead.

### B. Fuel power to vanilla machines (`Patches/PowerPatches.cs`, `Pure/FuelTopUp.cs`)

- `AttachModded` gains vanilla members. From `world.powerNodes` after the vanilla solve: every node with `panel == false`,
  a vanilla kind (`Remap.Live(record.kind) == null`), `state.DemandKw > 0` and `state.Power < 1` is a candidate; it
  joins `FuelGrid.Assign` as a non-source member with `VanillaGrid = state.Grid` (so `FuelGrid`'s range rule decides:
  within a generator's `Range`, centre to centre, chained like today). Its draw is `FuelTopUp.Draw(power, demandKw,
  fuelKw)` → `(float Power, float UsedKw, float DrawnKw)`: the unmet share `demand × (1 − power)` from fuel only, never
  the vanilla grid's sun or batteries (vanilla already applied those); `Power = power + drawn / demand`,
  `UsedKw = demand × Power`. The drawn kW is split over the network's generators with `FuelGrid.Split` and comes out
  of their `AvailableKw` and `ChargeKwh` exactly as for modded machines. `Grid` is left as vanilla set it.
- Modded machines are served first (today's order), then vanilla members in depot-id order; each solve reapplies the
  raise (vanilla resets the fields every solve, and `StepProduction` reads them after our postfix; recon B).
- `powerStatus`: the implementer sets `state.powerStatus = "Powered by {generator Name}"` on a fed vanilla machine
  once and checks `Status` in the log; kept if it changes the text, removed (and documented) if not.
- The ledger records, per solve, `Fed: Dictionary<int generatorId, List<int memberIds>>` (members the generator gave
  > 0 kW to, vanilla and modded) and `NetworkOf: Dictionary<int depotId, int networkId>` for the arcs and the panel's
  POWERING row (`PowerPatches.LastSolve`).
- Log lines like today's: `Power: vanilla '<display name>' depot N draws X kW from fuel generator(s) ...` once per
  depot/grid.

### C. Arcs (`PowerLinksView.cs`)

- The library owns one `MoonPowerLinks` created lazily as `new MoonPowerLinks(world, interaction.transform)` once a
  `MoonProductionInteraction` exists (found with `FindFirstObjectByType`, re-found if destroyed), disposed on world
  change (`Dispose()`).
- Every `CoreDriver.Update` in game: `show = builder.HoldingBuildTool && aimed is in LastSolve.NetworkOf` (aimed =
  `interaction.Aimed(world)`, a depot id, 0/−1 when none). When `show`: `Begin()`, for each generator of that network:
  `Ring(centre, Range, Cyan at alpha 0.35)` and `Arc(from, to, 0.06, Cyan)` per fed member; `End()`. When it drew last
  frame and `show` is false: `Clear()`. Ends: `world.PowerAnchor(id, out p)` when true, else depot position +
  `position.normalized × 2 m` (the moon is a sphere centred on the origin).
- DEVBUILD `[Dev] ForceArcs`: draws for every network without the tool/aim gate (for the self-test and screenshots).
  `PowerLinksView.ArcsDrawn` counts arcs drawn in the last frame (self-test).

### D. Canister tint (`Patches/CanisterPatches.cs`)

- A postfix on `MoonDustCanisterView.ShowCargo(MoonCargo cargo)` (1152 B, normal prologue): colour =
  `CargoPatches.Live(cargo)?.CanisterTint ?? Color.white`; `__instance.properties.SetColor("_BaseColor", colour)` and
  on `dustRenderer`: `GetPropertyBlock(block)`, `SetColor`, `SetPropertyBlock(block)`. White for everything else, since
  views are pooled. Nothing for the far path (documented).
- `examples/ExampleStructure`: grit gets `CanisterTint = new Color(0.55f, 0.55f, 0.60f)` (dev only, so the self-test
  can see a tint). SandPower (branch `structurelib`): briquette `CanisterTint = new Color(0.42f, 0.33f, 0.24f)`.

### Self-test (DEVBUILD, `DevPowerUx.cs` keeps the recon probe; new checks in `DevTools.cs` / a new `DevPowerChecks.cs`)

- `panel-generator`: with the burner built, call `interaction.ShowPanel(world, burnerRecord, true)` and read the texts
  (the probe's `PanelTexts`): title = burner Name, kicker starts `FUEL POWER`, row labels OUTPUT/BATTERY/FUEL/POWERING;
  then `look.Hide()`.
- `panel-machine`: same for the grinder with `power false`: kicker starts `PRODUCTION`, a row labelled with grit's
  short name upper-cased, a CYCLE row; and with `power true` a POWER row first.
- `power-vanilla-fed`: when AutoBuild names a vanilla `96` (pressure chamber) and the burner, and AutoFeed gives the
  chamber dust and the burner grit: within 60 s the chamber's `Power ≥ 0.99` in a solve where vanilla left it < 0.99
  (the ledger keeps `VanillaPowerBefore` per fed depot), its `Cycles` increased, and the burner's `ChargeKwh` fell.
  "not applicable" when the chamber is not within the burner's `Range` or vanilla already powers it fully.
- `arc-drawn`: with `ForceArcs`, `PowerLinksView.ArcsDrawn ≥ 1` once the burner fed something (chamber or grinder).
- `cargo-tint`: a streaming view (`MoonCanisterStreaming.views`) whose canister carries grit has `properties`
  `_BaseColor` equal to grit's tint (±0.01), and a dust one has white.
- The three standard configs (chain, reload, model) stay green.

## What's left to the builder

Exact wording of panel states beyond those listed; the `PanelText` API shape as long as it is pure and tested; where
the ledger lives in `PowerPatches` (a small `LastSolve` class is suggested); the MoonLookPanel row cap handling; the
order of the self-test checks; log wording. Any surface that does not behave as the recon says is recorded in the
ledger and in `research/POWER_UX_RECON.md` with the trace, and the design is adjusted by the controller's ruling.
