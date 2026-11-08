# Power Grid Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Power poles form grids; each grid pools its modded sources (solar arrays, coalite furnaces), charges its battery banks, and gives every consumer the same satisfaction, Factorio style; vanilla panels are untouched.

**Architecture:** StructureLib keeps its `SolvePower` postfix as the only hook into the game's power, but the layer inside it is rebuilt: a pure pole graph (`Pure/PoleGraph.cs`) assigns every modded power structure to a grid, a pure solver (`Pure/GridSolve.cs`) computes one solve per grid, and the patch writes the results into the game's `MoonProductionState` fields exactly as today. `PowerDefinition` gains a role (Source, Relay, Storage). A new mod `mods/PowerGrid` ships the player-facing pole, solar array and battery bank; `examples/ExampleStructure` gets a test pole so library tests stay self-contained; SandPower depends on PowerGrid.

**Tech Stack:** .NET 8, BepInEx 6 IL2CPP, HarmonyX, Il2CppInterop; xunit (`tests/StructureLib.Tests`); headless Blender 4.5 (`shared/blender/kit.py`) for models.

**Spec:** `docs/superpowers/specs/2026-11-08-power-grid-design.md`

## Global Constraints

- Build per project; `-p:InstallToGame=false` while the game runs; close the game before installing. Tests: `dotnet test tests/StructureLib.Tests`.
- Every game call in try/catch with `CoreLog.Once`; `!= null` not `?.` on Unity objects; new game members go in `Plugin.Needed` (checked at Load); no new native patches: the only power hook is the `SolvePower` postfix and the existing leaf prefixes.
- Pure code (`src/StructureLib/Pure/*.cs`): BCL only, no Unity or game types, deterministic (iterate in depot-id order), allocation-free per solve in steady state.
- Names and ids from the spec: `ryanhartwig.power-pole`, `ryanhartwig.solar-array`, `ryanhartwig.battery-bank`; PowerGrid plugin GUID `com.ryanhartwig.powergrid`; pole `LinkRange` 24, `SupplyRadius` 12; array `PeakKw` 10; bank `BatteryKwh` 20, `RateKw` 10; prices $50 / $180 / $300.
- Spec order of supply: solar, then storage discharge, then fuel for the deficit only; fuel never charges a bank; satisfaction is one number per grid.
- Vanilla panels untouched; vanilla's sun surplus still feeds modded consumers in range; the grid covers only what vanilla left unmet (`FuelTopUp.Draw` semantics for every consumer).
- Commit messages end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`. Branch `power-grid` (off `coalite-rename`). Never commit saves, Steam ids or tokens.
- The work-order board returns 404 from this account: the PR description says the task was tracked in the PR.

## Review Focus

1. A consumer inside two poles' supply radii that belong to two different grids: it must join exactly one (the nearest pole), never draw from both. Test in Task 3.
2. Satisfaction exactly at `StallPower` (0.08) and a demand of 0 kW on a grid with no supply: no division by zero, an idle machine on a live grid reads satisfaction 1, an idle machine on a dead grid reads 0 (today's `GridShare` idle rule). Test in Task 4.
3. A bank with `RateKw` smaller than its share of the surplus, and a bank at full charge: the leftover surplus is not lost to a negative number and the conservation property still holds. Test in Task 4.
4. A pole removed mid-solve (depot gone between the graph rebuild and the write): no `KeyNotFound`, the graph rebuilds next solve. Test in Task 5 (self-test `grid-pole-removed`).
5. A vanilla machine partly powered by a vanilla panel (Power 0.4) inside a short grid (satisfaction 0.5): it gets 0.4 + 0.6 × 0.5 = 0.7, never less than 0.4 and never more than 1. Test in Task 4.

---

### Task 1: Recon: sun formula and co-op state flow

Measured in game on the current build (nothing from later tasks). Needs the game: take `game.lock` in the scratchpad per `memory/parallel-agents-convention.md`; slot 2 only; close the game after.

**Files:**
- Modify: `research/POWER_UX_RECON.md` (append "## D. Sun factor" and "## E. Co-op state flow")
- Modify: `src/StructureLib/DevPowerChecks.cs` (temporary logging only; remove before commit, or keep behind `[Dev] LogSun`)

- [ ] **Step 1: Sample the sun.** With DevMoney's `[Time] TimeOfDay` held at 0, 6, 9, 12, 15, 18 (local solar hours), for a vanilla solar twin node (`world.powerNodes`, `n.panel`, `n.record.kind == MoonWorld.SolarTwinKind`) log `n.state.SunFactor`, `n.state.Daylight`, `n.state.SunAim`, `n.state.TrackerTilt`, `MoonSolarSystem.Daylight`, `dot(MoonSolarSystem.SunDirectionAt(solar.LunarTime), n.up)` (`n.up` is the panel's up). Repeat for a solar post and a tracker if slot 2 has them.
- [ ] **Step 2: Fit.** Find the formula `f(sunDir, up, Daylight)` that reproduces `SunFactor` within 3 % at every sample for the twin and the post (the tracker tilts; note whether it reads `max(0, elevation)` independent of `up`). Write the formula and the table into section D. If no formula fits, write that the array will use `MoonSolarSystem.Daylight` world-wide and say by how much it misses.
- [ ] **Step 3: Co-op flow.** From the type dumps (`research/dump_*.txt`) and `MoonSession` / `MoonSteamLobby` members: does `MoonWorld.SolvePower` run on a client? Is `MoonProductionState` part of what the host streams (`SnapshotProduction` / `RestoreProduction` on the world blob, or a per-tick message)? Log `MoonSteamLobby.IsHost` and whether the `SolvePower` postfix fires on a client, in a two-player session with Andrew if one can be arranged within the task; otherwise from the dumps alone, and say so. Write section E with one of two verdicts: **(R) states replicate, clients do not solve** or **(S) every peer solves its own world**.
- [ ] **Step 4: Commit** `research: sun factor and co-op state flow for the power grid`.

**Produces:** section D's formula (Task 5 implements `Sun.Factor`), section E's verdict (Task 5 chooses the host gate).

### Task 2: Library API: power roles

**Files:**
- Modify: `src/StructureLib/StructureDefinition.cs` (`PowerDefinition`, new `PowerRole`)
- Create: `src/StructureLib/Pure/PowerRules.cs`
- Test: `tests/StructureLib.Tests/PowerRulesTests.cs`
- Modify: `src/StructureLib/MoonStructures.cs` (registration validation, the "power source" log word), `src/StructureLib/SpecBuilder.cs:97-99`, `src/StructureLib/Patches/PowerPatches.cs` (compile only: `Range` → `0f` placeholders; the rewrite is Task 5), `src/StructureLib/PowerLinksView.cs` (ring uses `SupplyRadius` for now), `src/StructureLib/Patches/PanelPatches.cs:128` (`Range` → `SupplyRadius` placeholder), `examples/ExampleStructure/Plugin.cs` (burner: drop `Range`, add `Role = PowerRole.Source`), `mods/SandPower/Plugin.cs` (furnace: same)

**Interfaces:**
- Produces:
  ```csharp
  public enum PowerRole { Source, Relay, Storage }
  public sealed class PowerDefinition
  {
      public PowerRole Role = PowerRole.Source;
      public SupplyMode SupplyMode = SupplyMode.Fuel; // Source only
      public float PeakKw;        // Source
      public float BatteryKwh;    // Fuel source buffer, or Storage capacity
      public float RateKw;        // Storage: max charge/discharge kW
      public float LinkRange;     // Relay
      public float SupplyRadius;  // Relay
  }
  public static class PowerRules
  {
      // errors, empty when valid
      public static IReadOnlyList<string> CheckDefinition(string id, PowerRole role, SupplyMode mode, float peakKw, float batteryKwh, float rateKw, float linkRange, float supplyRadius, bool hasProcessor, bool producesPower);
  }
  ```
  Rules: Source/Fuel needs `PeakKw > 0`, `BatteryKwh > 0`, a Processor with `ProducesPower`; Source/Solar needs `PeakKw > 0`, no Processor; Relay needs `LinkRange > 0`, `SupplyRadius > 0`, `SupplyRadius <= LinkRange`, no Processor; Storage needs `BatteryKwh > 0`, `RateKw > 0`, no Processor. A Processor with `ProducesPower` needs a Fuel Source definition (today's implicit rule, now checked).

- [ ] **Step 1: Write `PowerRulesTests`:** one `[Theory]` per role with a valid row and each violation row, asserting the error count and that each message contains the id and the offending field name (`"PeakKw"`, `"SupplyRadius"`, …); `Relay_SupplyRadiusAboveLinkRange_IsAnError` (12/24 ok, 25/24 error).
- [ ] **Step 2: Run** `dotnet test tests/StructureLib.Tests --filter PowerRules` → FAIL (type missing).
- [ ] **Step 3: Implement** `PowerRole`, the reshaped `PowerDefinition` and `PowerRules.CheckDefinition`; call it in `MoonStructures.Register` next to `RouterRules.CheckDefinition` (`:244`), refusing the definition on errors with the same logging shape. `SpecBuilder.PanelSpecFor`: `spec.range = d.Power.Role == PowerRole.Relay ? d.Power.SupplyRadius : 0f`.
- [ ] **Step 4: Fix the compile** in the files listed (placeholders only; behaviour unchanged except the ring radius). Example burner and SandPower furnace: `Power = new PowerDefinition { Role = PowerRole.Source, SupplyMode = SupplyMode.Fuel, PeakKw = ..., BatteryKwh = ... }`.
- [ ] **Step 5: Run** the whole suite and `dotnet build src/StructureLib -c Release -p:InstallToGame=false`, `dotnet build examples/ExampleStructure ...`, `dotnet build mods/SandPower ...` → green, 0 warnings.
- [ ] **Step 6: Commit** `StructureLib: PowerDefinition has a role (Source, Relay, Storage); PowerRules validates it`.

### Task 3: Pure pole graph

**Files:**
- Create: `src/StructureLib/Pure/PoleGraph.cs`
- Test: `tests/StructureLib.Tests/PoleGraphTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public readonly record struct Pole(int Id, float X, float Y, float Z, float LinkRange, float SupplyRadius);
  public readonly record struct Site(int Id, float X, float Y, float Z, float HalfX, float HalfZ, float Yaw); // a member's footprint
  public sealed class PoleGraph
  {
      public static PoleGraph Build(IReadOnlyList<Pole> poles);               // links two poles when either one's LinkRange reaches the other (link rule below)
      public int GridOf(int poleId);                                          // grid id = lowest pole id in the component; 0 unknown
      public IReadOnlyList<(int A, int B)> Links { get; }                     // each linked pair once, A < B, sorted
      public int GridCount { get; }
      public int PolesIn(int gridId);
      // nearest pole whose SupplyRadius reaches the site (distance from pole centre to the site's footprint rectangle, as MoonWorld.FootprintDistance measures it, computed here in pure code); 0 when none
      public int AttachSite(Site site, out int poleId, out float distance);
  }
  ```
  Link rule **(decided here)**: two poles link when their centre distance is `<= max(a.LinkRange, b.LinkRange)` (a long pole reaches a short one). Grid id: the lowest pole depot id in the component, made negative (`-(lowest id)`) so it cannot collide with vanilla grid ids (positive depot ids) nor with 0.
  Spatial index: a `Dictionary<(int,int,int), List<int>>` of cells of the largest `LinkRange`; `Build` is O(P · neighbours).

- [ ] **Step 1: Write `PoleGraphTests`:** `TwoPolesWithinRange_OneGrid` (24 m apart → same grid, one link); `TwoPolesBeyondRange_TwoGrids` (25 m); `ChainOfThree_OneGrid` (0, 20, 40 → links (1,2),(2,3), grid id -1); `LongPoleReachesShortPole` (ranges 40 and 10, 30 m apart → linked); `AttachSite_NearestReachingPole` (poles at x = 0 and x = 15, site centred at x = 9 with half-extent 1: footprint distance 8 to the first pole and 5 to the second → the pole at 15); `AttachSite_FootprintNotCentre` (site centre 13 m away, half-extent 2 → distance 11 ≤ 12 → attached); `AttachSite_NoneInRadius_ReturnsZero`; `Review focus 1: SiteInTwoGrids_JoinsNearestOnly`; `GridIds_AreNegativeAndStable` (ids don't change when a far pole is added).
- [ ] **Step 2: Run** `--filter PoleGraph` → FAIL.
- [ ] **Step 3: Implement** `PoleGraph` (union-find with path halving as `FuelGrid.Assign` does; footprint distance = distance from the point to the yaw-rotated rectangle in the local tangent plane, zero inside).
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `StructureLib: PoleGraph links poles into grids and attaches members`.

### Task 4: Pure grid solve

**Files:**
- Create: `src/StructureLib/Pure/GridSolve.cs`
- Test: `tests/StructureLib.Tests/GridSolveTests.cs`
- Delete: `src/StructureLib/Pure/FuelGrid.cs`, `tests/StructureLib.Tests/FuelGridTests.cs` (its `AvailableKw` and `Split` move here; `Assign` is replaced by `PoleGraph`). Keep `GridShare.cs` and `FuelTopUp.cs`.

**Interfaces:**
- Produces:
  ```csharp
  public readonly record struct SourceIn(int Id, bool Solar, float PeakKw, float SunFactor, double ChargeKwh);     // fuel: ChargeKwh buffer; solar: SunFactor 0..1
  public readonly record struct StorageIn(int Id, double ChargeKwh, float CapacityKwh, float RateKw);
  public readonly record struct ConsumerIn(int Id, float DemandKw, float VanillaPower);                            // VanillaPower: what vanilla's solve already covered (0 for modded machines with no vanilla panel)
  public readonly record struct GridResult(float SolarKw, float StorageKw /* + discharge, − charge */, float FuelKw, float DemandKw, float UnmetDemandKw, float Satisfaction,
                                           float[] SourceKw, double[] StorageDeltaKwh, float[] ConsumerPower, float[] ConsumerUsedKw);
  public static class GridSolve
  {
      public static float AvailableKw(float peakKw, double chargeKwh, float dt);           // from FuelGrid
      public static float[] Split(float kw, IReadOnlyList<float> availableKw);            // proportional, not in order
      public static GridResult Solve(IReadOnlyList<SourceIn> sources, IReadOnlyList<StorageIn> storage, IReadOnlyList<ConsumerIn> consumers, float dt, float stallPower);
  }
  ```
  Rules (spec goal 3 and 4): `unmet_i = Demand_i × (1 − VanillaPower_i)`; `deficit = Σ unmet`; solar = Σ `PeakKw × SunFactor`; `fromSolar = min(deficit, solar)`; surplus = solar − fromSolar charges storage by free capacity, each ≤ `RateKw × dt/3600` in kWh and ≤ `RateKw` in kW; `fromStorage = min(deficit − fromSolar, Σ min(RateKw, charge × 3600/dt))` split by charge; `fromFuel = min(remaining, Σ AvailableKw)` split proportionally; `satisfaction = deficit > 0 ? (fromSolar + fromStorage + fromFuel) / deficit : 1`; `ConsumerPower_i = VanillaPower_i + (1 − VanillaPower_i) × satisfaction`; an idle consumer (Demand 0) reads 1 when the grid has any solar, charge or fuel, else 0; `satisfaction` below `stallPower` is reported as is (the game stalls on it).

- [ ] **Step 1: Write `GridSolveTests`:** `SolarCoversDemand_FuelIdle` (array 10 kW sun 1, furnace 10 kW charge 6, demand 8 → solar 8, fuel 0, satisfaction 1); `SolarSurplusChargesBank` (surplus 2 kW, bank rate 10, dt 0.25 → +2 × 0.25/3600 kWh); `SurplusCappedByRate` (surplus 20, rate 10 → +10 kW worth, the rest not stored, no negative); `FullBankTakesNothing`; `NightBankCarriesLoad` (sun 0, bank 5 kWh, demand 8 ≤ rate 10 → storage 8, satisfaction 1); `FuelCoversOnlyDeficit` (sun 0, no bank, two furnaces 10 kW each, demand 12 → 6 and 6); `EvenShareWhenShort` (supply 5, consumers 6 and 4 → both 0.5); `Review 5: VanillaPartlyPowered` (VanillaPower 0.4, grid satisfaction 0.5 → 0.7); `Review 2: ZeroDemandLiveGrid_IsOne`, `ZeroDemandDeadGrid_IsZero`, `SatisfactionAtStall_Reported` (0.08 exactly); `Conservation` ([Theory] over 6 hand cases plus a seeded random case: `Σ ConsumerUsedKw − Σ Demand × VanillaPower == SolarKw + FuelKw + discharge` and `charge ≤ surplus`, tolerance 1e-3); `Split_Proportional` (available 10 and 30, take 20 → 5 and 15).
- [ ] **Step 2: Run** `--filter GridSolve` → FAIL.
- [ ] **Step 3: Implement** `GridSolve`; move `AvailableKw`/`Split` (Split becomes proportional, update any caller in Task 5); delete `FuelGrid`.
- [ ] **Step 4: Run** the whole suite → PASS (FuelGrid tests gone, nothing else references `FuelGrid` until Task 5 — if `PowerPatches` still does, leave a `#if false` stub there only until Task 5 and say so in the commit).
- [ ] **Step 5: Commit** `StructureLib: GridSolve pools a grid's sources and shares the load evenly`.

### Task 5: The grid in the SolvePower postfix

**Files:**
- Modify: `src/StructureLib/Patches/PowerPatches.cs` (rewrite `AttachModded` and `LastSolve`)
- Create: `src/StructureLib/GridState.cs` (library-side view of the last solve for panels and arcs), `src/StructureLib/Sun.cs`
- Modify: `src/StructureLib/Patches/ProductionPatches.cs` (`IsPowerNode` prefix: true for every modded `Power != null`; `IsProcessorId`: true for Relay/Storage/Solar-source modded depots so the game allocates them a `MoonProductionState`; the cycle hooks `CanStartCycle`/`TryStartCycle`/`FinishCycle`/`EmitProduct` return "no cycle" when `d.Processor == null`)
- Modify: `src/StructureLib/Plugin.cs` (`Needed`: `MoonCleaners.MoonSolarSystem:Daylight`, `:SunDirectionAt`, `:LunarTime`, `MoonCleaners.MoonWorld:solarSystem`, `MoonCleaners.MoonSteamLobby:IsHost` if Task 1 verdict is R)
- Modify: `examples/ExampleStructure/Plugin.cs` (add `ryanhartwig.example-pole`: Relay, `LinkRange` 20, `SupplyRadius` 10, `BaseStructure = MoonWorld.SolarPostKind`, no model, Category "Power", $10; add `ryanhartwig.example-cell`: Storage, 1 kWh, 5 kW, same base)
- Modify: `src/StructureLib/DevPowerUx.cs`, `src/StructureLib/DevTools.cs` (self-tests)
- Create: `src/StructureLib/DevPowerGrid.cs` (self-tests listed below)

**Interfaces:**
- Consumes: `PoleGraph`, `GridSolve`, `FuelTopUp` (vanilla machines' `VanillaPower`), `GridShare.Draw` only for the vanilla-panel sun-surplus part of a modded consumer (unchanged), `PowerRules`.
- Produces (for Tasks 6 and 7):
  ```csharp
  internal static class GridState
  {
      internal sealed class Grid { public int Id; public int Poles; public float SolarKw, FuelKw, PeakKw, StorageKw, DemandKw, UsedKw, Satisfaction; public double StoredKwh; public float CapacityKwh; public List<int> PoleIds; public List<int> MemberIds; }
      internal static IReadOnlyDictionary<int, Grid> Grids { get; }           // by grid id (negative)
      internal static bool GridOfDepot(int depotId, out int gridId);          // poles and members
      internal static IReadOnlyList<(int A, int B)> PoleLinks { get; }
      internal static IReadOnlyList<(int Pole, int Member)> Reach { get; }     // pole → member it serves
      internal static int Version { get; }                                     // bumps when the graph was rebuilt
  }
  internal static class Sun { internal static float Factor(MoonWorld world, Vector3 position, Vector3 up); } // Task 1's formula; Daylight fallback
  ```
  Per-solve flow (host, or every peer under verdict S): (1) if the depot set changed (`depots.Count` or a hash of ids, kept on `GridState`), rebuild `PoleGraph` from Relay depots and re-attach every modded Power/Processor depot and every vanilla machine node with demand (sites from `MoonDepotSnapshot` x/y/z/yaw and the definition's footprint half-extents); (2) per grid, build `SourceIn`/`StorageIn`/`ConsumerIn` lists in depot-id order (reused lists, cleared), consumers' `VanillaPower` = for a modded machine the satisfaction `GridShare.Draw` gives from its vanilla node's sun surplus (today's code, kept), for a vanilla machine `state.Power` as vanilla left it; (3) `GridSolve.Solve`; (4) write: consumers via `SetPower` (`Grid` = grid id) or, for vanilla machines, `Power`/`UsedKw` only plus `PoweredBy(s, "the grid")` when fully fed; fuel sources via `SetSupply`; solar sources: `SuppliedKw`, `Power` 1, `Grid`; storage: `ChargeKwh ± delta`, `BatteryKw`, `Grid`; poles: `Grid`, and the grid totals into `SuppliedKw` (supply), `DemandKw` (demand), `UsedKw` (used), `ChargeKwh` (stored) so clients can read them; (5) `AddNode` for fuel sources as today. Unreached: consumer `Grid` 0 with `powerStatus` `No power: build a power pole within <SupplyRadius> m` where `<SupplyRadius>` is the first registered Relay's (12 for PowerGrid); source and storage `Grid` 0.
  Host gate: under verdict R, the postfix returns early when `!MoonSteamLobby.IsHost` and the session is a lobby client; under S it always runs.

- [ ] **Step 1: Write the self-tests** in `DevPowerGrid.cs` (DEVBUILD, `[Dev] AutoBuild` lays the depots out; use the example pole/cell/burner/grinder and a vanilla pressure chamber, all built by the self-test's own `AutoBuild` list as today's power checks do):
  - `grid-pole-has-state`: `world.ProductionOf(poleDepot) != null` after one solve.
  - `grid-chain-even-share`: poles at 0, 18, 36 m; burner (12 kW, fed) at 0 within 10 m of pole 1; grinder (1 kW) and chamber (10 kW) at 36 within 10 m of pole 3; the burner is the grid's only source (no solar array exists in the example mod, so the sun does not matter): both consumers read the same `Power` (within 0.01) and `Grid` = pole 1's negative id; `GridState.Grids[g].Poles == 3`.
  - `grid-bank-carries`: example cell charged by the burner's surplus (burner 12 kW, demand 1 kW → cell gains charge over 5 s), then burner fuel removed (`AutoRemove` of its stock) → grinder keeps `Power` 1 while the cell drains.
  - `grid-no-pole-no-power`: burner and grinder 5 m apart with no pole → grinder `Grid` 0, status contains `power pole`; burner `SuppliedKw` 0.
  - `grid-vanilla-topup`: chamber with `Power` < 1 inside pole 3's radius: after the solve `Power` ≥ before and `powerStatus` contains `grid` when 1.
  - `Review 4: grid-pole-removed`: remove pole 2 via `RemoveStructureNow` between two solves; no `[error]` line from `solvepower` in the log; grinder `Grid` becomes 0 or a new id within two solves.
  - Rewrite `power-burner-supplies`, `power-grinder-runs-on-burner`, `power-vanilla-fed`, `arc-drawn` to the pole layout (same assertions, through a pole).
- [ ] **Step 2: Implement** `Sun`, `GridState`, the `ProductionPatches` changes, the rewritten `AttachModded`; remove `LastSolve` (`FedCountOf` becomes `GridState` lookups; Task 6 changes the panel row).
- [ ] **Step 3: Build** all three projects → green. **Run** the xunit suite → green.
- [ ] **Step 4: Run in game** (game.lock; install with `InstallToGame=true` with the game closed; `[Dev] SelfTest=true`, slot 2): every `[selftest]` line above `ok`; no `Build QoL`/`StructureLib` error lines. Restore slot 2 from `.scratch/slot-2-backup-task11` after.
- [ ] **Step 5: Commit** `StructureLib: poles form grids; pooled supply shared evenly (SolvePower postfix rewritten)`.

### Task 6: Panels and status text

**Files:**
- Modify: `src/StructureLib/Pure/PanelText.cs` (add `Pole`, `Solar`, `Storage`; `Generator` gets a GRID row in place of POWERING and the kicker `FUEL POWER` without the range; `Machine` status strings)
- Test: `tests/StructureLib.Tests/PanelTextTests.cs`
- Modify: `src/StructureLib/Patches/PanelPatches.cs` (`Draw` branches on `d.Power.Role`; `ShowWithoutTool` unchanged: poles, sources and storage show only with the tool, like vanilla power)
- Modify: `src/StructureLib/DevPowerChecks.cs` (`panel-generator` rows become OUTPUT, BATTERY, FUEL, GRID; add `panel-pole`, `panel-solar`, `panel-storage`)

**Interfaces:**
- Consumes: `GridState.Grids`, `GridState.GridOfDepot`.
- Produces:
  ```csharp
  public static PanelLayout Pole(string name, int poles, float supplyKw, float peakKw, float usedKw, float demandKw, double storedKwh, float capacityKwh, float satisfaction, bool onGrid);
  public static PanelLayout Solar(string name, float suppliedKw, float peakKw, float sunFactor, bool onGrid, int gridPoles);
  public static PanelLayout Storage(string name, double chargeKwh, float capacityKwh, float batteryKw, bool onGrid, int gridPoles);
  ```
  Texts (spec goal 8): pole kicker `POWER GRID  ·  <n> POLES` (`1 POLE`); rows SUPPLY `<made> / <peak> kW` cyan, DEMAND `<used> / <asked> kW` cyan or amber when `used < asked`, STORAGE `<stored> / <capacity> kWh` mint, SATISFACTION `<pct> %` cyan or amber below 100; state `Grid short: <pct> %` amber when short, else `Powering <k> machine(s)`; a pole in no grid (impossible: a lone pole is its own grid) is not a case. Solar: kicker `SOLAR POWER`, state `Sun <pct> %` or `Night`, rows OUTPUT and GRID (`<n> poles` or `No pole in reach` coral). Storage: kicker `POWER STORAGE`, state `Charging` / `Discharging` / `Full` / `Empty`, rows STORED and GRID. Machine statuses written by Task 5: `Powered by the grid`, `Grid short: <pct> %`, `No power: build a power pole within <r> m`; `Machine()` keeps rendering `status` as given.

- [ ] **Step 1: Write the tests:** `Pole_Short` (poles 3, supply 5/10, used 5/10, stored 0/20, satisfaction 0.5 → kicker `POWER GRID  ·  3 POLES`, DEMAND amber `5.0 / 10 kW`, SATISFACTION `50 %`, state `Grid short: 50 %`); `Pole_SinglePoleWording` (`1 POLE`); `Pole_Full` (state `Powering 2 machines`); `Solar_Night` (sun 0 → `Night`, OUTPUT `0.0 / 10 kW`); `Solar_NoPole` (GRID coral `No pole in reach`); `Storage_States` ([Theory] batteryKw +1/−1/0 at full/0 at empty); `Generator_GridRow` (rows OUTPUT, BATTERY, FUEL, GRID; kicker `FUEL POWER`).
- [ ] **Step 2: Run** `--filter PanelText` → FAIL on the new ones.
- [ ] **Step 3: Implement** the layouts and the `Draw` branches (pole totals read from `GridState`, falling back to the pole's own state fields written in Task 5 when `GridState` is empty, i.e. on a client).
- [ ] **Step 4: Run** the suite → PASS; build → green; in game, `panel-*` self-tests `ok`.
- [ ] **Step 5: Commit** `StructureLib: look-at panels for poles, solar arrays and battery banks; grid status texts`.

### Task 7: Arcs and placement preview

**Files:**
- Modify: `src/StructureLib/PowerLinksView.cs`
- Modify: `src/StructureLib/DevPowerGrid.cs` (`arc-grid-links`, `arc-preview`)
- Modify: `src/StructureLib/Plugin.cs` (`Needed`: the `MoonDepotBuilder` members read for the ghost: `plan`, `HoldingBuildTool`, the selected kind field; BuildQoL's `FoundationRun.cs` names them)

**Interfaces:**
- Consumes: `GridState.PoleLinks`, `GridState.Reach`, `GridState.GridOfDepot`, `PoleGraph` (for the preview: a temporary graph with the ghost added).

Behaviour (spec goal 9): with the tool held and a depot aimed whose grid is known: `Arc` for every `PoleLinks` pair of that grid (cyan, `Width`), an `Arc` from each pole to each member in `Reach` of that grid (thinner: `Width × 0.6`), one `Ring` of `SupplyRadius` at the aimed pole, else at the nearest pole of the grid. While the tool holds a Relay kind and a placement ghost exists: an `Arc` from the ghost position (+2 m up) to every existing pole within `max(ghost.LinkRange, pole.LinkRange)`, and a `Ring` of the ghost's `SupplyRadius` at the ghost. Nothing drawn for vanilla grids.

- [ ] **Step 1: Write the self-tests:** `arc-grid-links`: with `[Dev] ForceArcs`, after the chain of Task 5 is built, `PowerLinksView.ArcsDrawn == 2 (links) + reach count`; `arc-preview`: select the example pole in the builder (the `SelectStructure` step `DevTools.cs:440` already exists for the lamp) with the ghost 10 m from pole 1 → `ArcsDrawn ≥ 1` and `RingAt` (new internal, the ring centre) within 0.5 m of the ghost.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Build; in game both checks `ok`; a screenshot of the preview and of an aimed pole saved to the scratchpad and referenced in the PR.**
- [ ] **Step 4: Commit** `StructureLib: pole-to-pole arcs, reach arcs and the placement preview`.

### Task 8: The PowerGrid mod

Models can start in parallel with Tasks 3–7 (they touch only `mods/PowerGrid/blender/**`); the plugin needs Task 2's API and Task 5's behaviour.

**Files:**
- Create: `mods/PowerGrid/PowerGrid.csproj` (copy of `mods/SandPower/SandPower.csproj` with the names changed; or `.\new-package.ps1 -Kind mod -Name PowerGrid -Author ryanhartwig -Title "Power Grid"` from the worktree root, which also adds it to `GarageLib.slnx`), `mods/PowerGrid/Plugin.cs`, `mods/PowerGrid/README.md`, `mods/PowerGrid/blender/power_pole.py`, `solar_array.py`, `battery_bank.py` (+ the `.blend`, previews and `mods/PowerGrid/<name>.bin` they write)
- Modify: `GarageLib.slnx` (if not by the script), `modpack.json` (`"PowerGrid"` after `"StructureLib"`, before `"SandPower"`)

**Interfaces:**
- Consumes: Task 2's `PowerDefinition`; `MoonStructures.Register(def, Guid)`; `Config.Bind("Grid", "LinkRange", 24f, ...)`, `("Grid", "SupplyRadius", 12f, ...)` read at Load into the pole's definition (the config file is `BepInEx/config/com.ryanhartwig.powergrid.cfg`).

Definitions (`Footprint` is full width × depth in metres): pole `BaseStructure = MoonWorld.SolarPostKind` (a power node base that has a production view), `Footprint = (0.6, 0.6)`, `Model = "power_pole.bin"`; array `BaseStructure = MoonWorld.SolarTwinKind`, `Footprint = (4, 2)`, `Model = "solar_array.bin"`; bank `BaseStructure = MoonWorld.SolarPostKind`, `Footprint = (1.2, 1.2)`, `Model = "battery_bank.bin"`. Category `"Power"`. Notes: pole `Links to poles within 24 m and powers machines within 12 m. Grids pool every source and share the load evenly.`; array `10 kW at full sun into the power grid. Needs a power pole within 12 m.`; bank `Stores 20 kWh for the grid, 10 kW in or out. Needs a power pole within 12 m.`

- [ ] **Step 1: Models** (the `making-3d-models` skill / `3d-modeler` agent; three scripts in the style of `mods/SandPower/blender/coking_oven.py`, built headless with the portable Blender at `C:\Users\ryguy\projects\moonsweep\.downloads\blender-4.5\blender-4.5.15-windows-x64\blender.exe`): pole = 0.3 m base plate, 5 m mast, a 1.2 m cross-arm with two insulator knobs, `MS_Steel`-style kit materials; array = a 4×2 m tilted panel (20°) on an A-frame, dark glass top; bank = a 1.2 m cube of cells with a lamp. Previews beside the astronaut; no ports (none of the three carries cargo).
- [ ] **Step 2: Plugin** with the three registrations and the `[Grid]` config; README with a table of the three, the grid rules in four sentences, and the config keys.
- [ ] **Step 3: Build** `dotnet build mods/PowerGrid -c Release -p:InstallToGame=false` → green; `package.ps1` dry run lists `PowerGrid` with its three `.bin` files (`.\package.ps1 -WhatIf` if supported, else read the script's glob).
- [ ] **Step 4: In game** (game closed to install): the three appear in the Power tab under the spec names and prices; place a pole, an array and a bank; the array's panel reads `Sun <pct> %` by day; a pressure chamber in the pole's radius reads `Powered by the grid`. Screenshots to the scratchpad.
- [ ] **Step 5: Commit** `PowerGrid: power pole, solar array and battery bank`.

### Task 9: SandPower on the grid

**Files:**
- Modify: `mods/SandPower/Plugin.cs` (`[BepInDependency("com.ryanhartwig.powergrid")]`; furnace Note `Burns one coalite every 20 s for 10 kW into the power grid. Needs a power pole within 12 m.`; comment prose), `mods/SandPower/SandPower.csproj` (ProjectReference to PowerGrid, `Private="false"`, only if the plugin references a type; otherwise the attribute alone), `mods/SandPower/README.md` (the furnace powers the grid through a pole; Requires PowerGrid)

- [ ] **Step 1: Edit; build SandPower → green.**
- [ ] **Step 2: In game:** a furnace fed with coalite, a pole within 12 m, a coking oven in the pole's radius at night: the oven runs; the furnace panel's GRID row shows the pole count. Without the pole the oven's status names a power pole.
- [ ] **Step 3: Commit** `SandPower: the furnace powers the grid through a power pole`.

### Task 10: Docs, decisions and the test plan

**Files:**
- Modify: `src/StructureLib/README.md` ("## Power" rewritten: roles, the grid rules, supply order, vanilla top-up, panels, arcs, the host gate, not-supported list; the hazards rows stay), `docs/DECISIONS.md` (dated entry: poles and pooled supply; why vanilla panels are untouched), `docs/superpowers/specs/2026-11-08-power-grid-design.md` (if Task 1 changed the sun rule or picked verdict S, record it)
- Create: the PR description in the scratchpad (summary, the three structures, what moved in the library, test plan with the self-test names, the two-player session as an unchecked box, the board note)

- [ ] **Step 1: Write the docs; `grep -rn "Range\b" src/StructureLib/README.md mods/*/README.md` shows no stale fuel-range sentence.**
- [ ] **Step 2: Full build of every project in `GarageLib.slnx` with the game closed, `dotnet test` green, one by-hand tour in game: build a grid of four poles, two arrays, a bank, a furnace, two ovens and a chamber; watch a day-night cycle with DevMoney's `TimeOfDay`; screenshots of the preview, an aimed pole at noon and at midnight.**
- [ ] **Step 3: Commit** `docs: power grid`; push `power-grid`; open the PR against `coalite-rename` with the description; bind it in the app.
- [ ] **Step 4: Two-player session** (Ryan and Andrew): both seats see the same pole panel numbers, the same machine statuses and the same arcs for one grid; a client-placed pole joins the grid on both seats. Recorded in the PR as done or as the one open item.
