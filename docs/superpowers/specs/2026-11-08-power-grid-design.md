# Power grid: poles, pooled supply, even distribution — design

Date 2026-11-08. Author: Ryan's agent, from a design conversation with Ryan (quotes below are his). Every choice not
quoted from him is the agent's call and is marked **(call)**.

Reads with: `research/POWER_UX_RECON.md` and `research/STRUCTURES_RECON.md` ("Power", "Power sources (Task 11)",
"Interop hazards found"), `src/StructureLib/README.md` ("Power"), `src/StructureLib/Patches/PowerPatches.cs` (the
present power layer this design replaces), `docs/superpowers/specs/2026-11-02-power-ux-design.md` (the look-at panel
and arc mechanisms, which stay).

## Intent

Ryan: "instead of the current system that vanilla uses, where a power structure powers nearby buildings, I'd rather
create a power grid like Factorio has ... I want that to generate power statically, same way as the game, but then we
transmit the power through rods that we place ... a placeable power pole that transmits power and also supplies power
to nearby structures." And: "I'd kind of rather centralize the source. I think that's the whole point. So power isn't
coming from a single power generator, like a solar panel. Power is a centralized sum of all the machines that you
have. And that is distributed evenly amongst all consumers of power within the grid." On vanilla panels: "It's
probably cleaner to just create our own solar panels, right? So that we have control over how they work."

So: a **grid** is a connected set of **power poles**. Every modded source, storage unit and consumer that a pole
reaches belongs to that pole's grid. Each solve the grid pools all its sources into one supply, sums all its
consumers into one demand, and gives every consumer the same share. Sources no longer power anything directly: a
coalite furnace or a solar array with no pole in reach feeds nothing. Vanilla's own solar panels and their
panel-powers-neighbours behaviour are left exactly as they are; the grid is a parallel system built from modded
structures only.

## Why not change vanilla's solve

`MoonWorld.SolvePower` (9440 B native) rebuilds its node list every call from an inlined kind check, holds grid totals
in structs managed code cannot write back, and is run about four times a second (recon). Last week's attempt to let a
modded source feed a vanilla machine showed the limits: energy was created because a vanilla battery cannot be
debited. Pooling vanilla panels would repeat that. Hence Ryan's choice of our own panels, and this design's rule that
vanilla panels are neither sources nor storage of a modded grid.

## Goals

1. **Poles make grids.** A power pole links to every other pole within `LinkRange` (pole centre to pole centre). The
   connected poles are one grid. A grid with no pole does not exist: sources and consumers never link to each other
   directly.
2. **Poles reach members.** A modded structure with a power role (source, storage or consumer) joins the grid of the
   nearest pole whose `SupplyRadius` reaches its footprint (`MoonWorld.FootprintDistance`). A structure is in at most
   one grid. A structure no pole reaches is unpowered (consumer), idle (source), or inert (storage), and its status says
   so.
3. **Pooled supply, even share.** Each solve, per grid, in Factorio's order **(call)**: free sources first, then
   storage, then fuel.
   - `demand` = the sum of every consumer's `RunningKw` while it is running.
   - `solar` = the sum of every solar source's `PeakKw × SunFactor` this solve. It covers demand first. Any solar
     surplus charges storage, split across storage units in proportion to their free capacity, each at most its
     `RateKw`. Solar never goes to waste while a bank has room.
   - If `solar < demand`: storage discharges to cover the deficit, split across units in proportion to their stored
     charge, each at most its `RateKw` and its charge.
   - If still short: fuel sources deliver **only the remaining deficit**, split in proportion to their available kW
     (`PeakKw`, at most what their stored charge lasts over the solve, as `FuelGrid.AvailableKw` does today), so two
     furnaces burn evenly and neither burns while the sun or the banks can carry the grid. Fuel never charges storage:
     a furnace's own buffer is its storage (it fills by burning coalite, as today, and stops burning when full).
     **(amended 2026-11-08 during implementation)** Fuel sources have no kWh buffer: a furnace offers `PeakKw` while a
     coalite burns (`Running`), and a burn starts only when the grid's last solve still had a deficit after solar and
     storage (`GridState.WantsFuel`, host side). See Multiplayer: clients skip `FinishCycle`, so a charge raised by a
     cycle event would never reach them. A source can waste up to one burn at low demand.
   - satisfaction = `(solar + discharged + fuel) / demand`, capped at 1, **the same number for every consumer** on the
     grid. No consumer is favoured by distance, build order or kind.
   - A consumer's satisfaction below the game's `StallPower` (0.08) stalls it, as vanilla does; above that, its cycle
     progresses at `satisfaction` speed (unchanged from today's machine accounting).
   - `SunFactor` is the grid's, not vanilla's: the library computes it from the lunar clock (`MoonSolarSystem`:
     `Daylight`, `SunDirectionAt`, which DevMoney already drives) and the array's placement normal. The recon task
     below settles the formula against the sun factor vanilla panels show, so an array and a twin side by side agree.
     **(amended 2026-11-08 during implementation)** The formula in the code is provisional: it is read from the game's
     machine code, not sampled. `s = SunDirectionAt(LunarTime)`, `h = dot(up, s)`,
     `d = smoothstep(clamp01((h + 0.0096) / 0.0192))`, `factor = max(0, dot(s, n)) x d` with `n` the array's up;
     fallback `MoonSolarSystem.Daylight`. Sampling against a vanilla twin (`[Dev] LogSun`) is pending.
     A modded array answers `IsSolarPanel` **false** and, **(amended 2026-11-08 during implementation)**,
     `IsPowerNode` **true** (originally false). `IsSolarPanel` stays false so vanilla's panel code, sun-surplus
     bookkeeping and arcs never treat it as a vanilla panel; `IsPowerNode` must be true for every modded depot with
     a `Power` definition (poles, arrays, banks) so the look-at panel (a `TryPanel` path) reaches them. The library
     owns everything else about it.
4. **Vanilla panels still power what they reach; the grid tops up.** **(call, Ryan may flip it)** Vanilla's solve
   runs first and powers whatever vanilla machines its panels reach. Today the library also lets a modded machine
   within a vanilla panel's range run on that panel's sun surplus and batteries (`GridShare`, the charging take-back).
   Both stay. A consumer that a pole reaches, vanilla or modded, is a consumer of that grid **for the share of its
   demand that vanilla's panels left unmet** (today's `FuelTopUp` rule, now for every consumer); the grid writes
   `Power` / `UsedKw` / `Grid`, reapplied every solve, and never lowers a consumer below what a vanilla panel gave it.
   Reasons: we cannot stop a vanilla panel feeding a vanilla machine, so the rule would otherwise differ by kind; a
   coking oven beside a solar twin that stalls because a distant grid is short would read as a bug; and every existing
   play-test base keeps working after the update. A base with no vanilla panels gets pure Factorio behaviour. The
   alternative, modded consumers ignoring vanilla panels entirely, is a one-rule change in the pure solver and a
   status text; Ryan can ask for it after a play-test.
5. **Three new structures in a new mod, `mods/PowerGrid`** (names are **(call)**, Ryan has renamed things before and
   may again; ids are permanent after release so settle them before shipping):

   | Structure | Id | Role | Numbers **(call)** | Footprint **(call)** |
   |---|---|---|---|---|
   | Power pole | `ryanhartwig.power-pole` | relay | `LinkRange` 24 m (the solar twin's range), `SupplyRadius` 12 m | 1 cell, a thin mast with a cross-arm; placeable on ground or foundation like any structure |
   | Solar array | `ryanhartwig.solar-array` | source, solar | `PeakKw` 10 at full sun, no battery | 2×1 cells, a tilted panel on a frame |
   | Battery bank | `ryanhartwig.battery-bank` | storage | `CapacityKwh` 20, `RateKw` 10 in or out | 1 cell, a crate of cells with a charge lamp |

   Prices **(call)**: the rule governs and the numbers follow it. The recon measured the solar twin at $150 for
   7.5 kW and 4 kWh, about $20 per kW with its battery thrown in. So: pole $50, solar array $180 (10 kW, no battery,
   needs poles: a little under the twin per kW), battery bank $300 (20 kWh at $15 per kWh). The builder checks the
   vanilla prices in game before shipping and keeps these ratios.
   `LinkRange` and `SupplyRadius` are also read from the mod's BepInEx config (`[Grid]`), so Ryan can tune them in a
   play-test without a rebuild; the structure numbers stay in code.

   **Packaging (call).** The pole is a `PowerDefinition` with the Relay role, so any mod can define one; nothing about
   poles lives in the library beyond the role. `PowerGrid` ships the player-facing pole, array and bank. SandPower
   declares a BepInEx dependency on PowerGrid (`[BepInDependency("com.ryanhartwig.powergrid")]`) and both ship in the
   modpack; the furnace's README says it needs PowerGrid's pole. `examples/ExampleStructure` defines its own test pole
   (and keeps its burner, with `Range` removed) so the library's DEVBUILD self-tests and the xunit tests never
   reference another mod's ids. The library's existing self-tests (`power-burner-supplies`,
   `power-grinder-runs-on-burner`, `power-vanilla-fed`, `arc-drawn`) are rewritten around the example pole.
6. **SandPower joins the grid.** The coalite furnace keeps `PeakKw` 10 (no buffer, see goal 3), loses its 24 m direct
   range, and powers only through a pole. Its Note becomes "Burns one coalite every 20 s for 10 kW into the power
   grid. Needs a power pole within 12 m." The coking oven is a consumer like any modded machine. Nothing else in
   SandPower changes.
7. **Library API.** `PowerDefinition` is reshaped around roles. **(call)** Shape:
   ```csharp
   public enum PowerRole { Source, Relay, Storage }
   public sealed class PowerDefinition
   {
       public PowerRole Role;
       public SupplyMode SupplyMode;      // Source only: Solar or Fuel
       public float PeakKw;               // Source
       public float BatteryKwh;           // Storage capacity; ignored for Fuel sources (no buffer; amended 2026-11-08 during implementation, see Multiplayer)
       public float RateKw;               // Storage: max charge / discharge kW
       public float LinkRange;            // Relay: pole to pole
       public float SupplyRadius;         // Relay: pole to member footprint
   }
   ```
   `Range` is removed (nothing released uses it). Consumers are what they are today: a `Processor` with `RunningKw`.
   `MoonStructures.RegisterStructure` validates the role's numbers at registration and refuses nonsense (a Relay with
   no ranges, a Storage with no capacity) with a logged reason.
8. **Status and panel text.** With the build tool held, aiming a pole shows the grid readout, kicker
   `POWER GRID  ·  <n> POLES`, rows SUPPLY (`made / peak kW`), DEMAND (`used / asked kW`, amber when short), STORAGE
   (`stored / capacity kWh`), and SATISFACTION (`<pct> %`, amber below 100). Aiming a solar array or battery bank
   shows a source/storage panel in the same style (OUTPUT or STORED, plus the GRID row). Consumers keep the production
   panel; their status text reads `Powered by the grid`, `Grid short: <pct> %` or `No power: build a power pole within
   12 m` (the pole's `SupplyRadius`). The furnace's panel keeps its OUTPUT / BATTERY / FUEL rows and gets GRID instead
   of POWERING. Texts are pure functions with tests, as `PanelText` is today.
9. **Arcs and preview.** While placing a pole, an arc is previewed from the ghost to every pole it would link to, in
   the game's line style, and the ghost shows its `SupplyRadius` ring (the game's `MoonPowerLinks.ShowPreview`
   mechanism, or the library's own `MoonPowerLinks` as `PowerLinksView` does today). With the build tool held and any
   grid member aimed, the library draws pole-to-pole arcs for that grid and an arc from each pole to each member it
   reaches, plus the aimed pole's ring. Vanilla's arcs for vanilla grids are untouched.
10. **Multiplayer.** Required outcome: on every peer, the same grid shows the same readouts, statuses and arcs, and
    machines run at the same satisfaction. How that is reached depends on a fact nobody has measured: whether the game
    replicates `MoonProductionState` (`Power`, `ChargeKwh`, `SuppliedKw`) from host to clients, or every peer runs
    `SolvePower` / `StepProduction` itself over a replicated depot set. The co-op recon so far is from type dumps only,
    and power in co-op is listed as untested in the library README. So the plan starts with a recon task (below) and
    the solver is written so either model works: pure functions of the depot set and the states, deterministic in
    iteration order (sorted by depot id), allocation-free per solve. If states replicate, the host solves and clients
    display; grid totals a pole shows come from a replicated state the host writes (the builder gives the pole one the
    way a processor has one). If every peer simulates, every peer runs the same solve and must agree; the fuel drain
    then has to be deterministic too. "Untested in co-op" is **not** acceptable for the PR that ships the grid: the
    test plan includes one two-player session (Ryan and Andrew, as PR #5's README plans for belt scrap) checking a
    pole chain, a short grid and a furnace from both seats.

    **(amended 2026-11-08 during implementation)** Recon verdict S (`research/POWER_UX_RECON.md` section F): every peer
    runs `SolvePower` over the replicated depot set; the host streams only Running, Progress, Cycles and stores, and
    clients skip `FinishCycle`. So there is **no host gate** on the solve: every peer computes the same grid from
    replicated inputs. Fuel has no kWh buffer (goal 3), so nothing that only the host's cycle events change feeds the
    supply; a burn starts only on the host, gated by the grid's last deficit. Bank charge integrates per peer, as
    vanilla batteries do; the host's save restores it through the StructureLib sidecar (a charge-only record), and a
    client starts a bank at 0 until it joins fresh (a display-only divergence). The two-player session must confirm:
    both seats see the same pole panel numbers, machine statuses and arcs for one grid; a client-placed pole joins the
    grid on both seats; a bank's displayed charge stays close between seats after a join; a furnace burns only when
    the grid is short and both seats agree.
11. **Performance.** The pole graph and the member-to-pole assignment are rebuilt only when the set of depots changes
    (a build, a removal, a world load); the per-solve work is the sums and the writes, linear in members. A spatial
    index (cells of `LinkRange`) keeps the rebuild well under a millisecond for a few hundred poles. The solve path
    allocates nothing per call in steady state.
12. **Proven.** xunit tests on the pure grid rules (`Pure/PowerGrid.cs` or similar: union of poles, member assignment,
    pooled supply, even satisfaction, storage charge/discharge split, fuel drain split, the top-up rule for vanilla
    machines) including the energy-conservation property (over any solve, `Σ delivered + Σ stored − Σ discharged =
    Σ source output`, within float tolerance). DEVBUILD self-tests in the game: one pole chain of three with a solar
    array at one end and two consumers at the other (even share when short at night), a battery bank filling by day
    and carrying the night, a furnace with and without a pole in reach, a vanilla pressure chamber topped up through a
    pole. One by-hand tour with screenshots of the preview arcs, the grid readout and a short grid.

## Recon first

Two facts the plan's first task measures in game before the solver is written, with findings appended to
`research/POWER_UX_RECON.md`:

1. **Sun.** How a vanilla panel's `SunFactor` relates to `MoonSolarSystem.Daylight` / `SunDirectionAt` and the panel's
   orientation, by sampling a twin's node over a held lunar day (DevMoney's `[Time] TimeOfDay` holds the sun). The
   array uses the formula that reproduces vanilla's curve; if none does within a few percent, a world-wide `Daylight`
   factor is acceptable and the spec is updated to say so.
2. **Co-op state flow.** Whether `SolvePower` and `StepProduction` run on a client, and whether a `MoonProductionState`
   written on the host is seen on the client, by logging from both seats in a two-player session (or, failing a second
   player, from the type dumps plus a loopback client if the game allows one). Goal 10 picks its model from this.

**(amended 2026-11-08 during implementation)** Item 1 settled provisionally: the formula was read from the game's
machine code (see goal 3), not sampled; the sampling with `[Dev] LogSun` is still pending and the spec is updated
again if the curve disagrees by more than a few percent. Item 2 settled as verdict S (see goal 10): every peer
simulates, so there is no host gate.

## Non-goals

- Changing anything about vanilla panels: their ranges, their grids, their batteries, their panel text, their arcs.
- Vanilla panels as sources or storage of a modded grid (the energy-bookkeeping problem above). May be revisited if
  play shows a base wants its old solar field on the grid; the right fix then is a modded panel that replaces them.
- Priorities, power switches, consumer classes, separate charge and discharge rates, transmission loss, pole tiers
  (a long-range pylon), underground or wired connections. A second pole type is the obvious first extension.
- Rewriting `PowerLinksView`'s drawing style or the look-at panel plumbing: both exist and are reused.

## Anti-goals

- **Energy from nowhere.** Every kWh a consumer receives came out of a source's output or a storage unit's charge in
  the same solve. The conservation test pins this.
- **Favouritism.** No consumer is powered ahead of another on the same grid, for any reason. Even share is the point.
- **Per-frame cost that grows with poles squared.** Rebuild on change, sum per solve.
- **A grid that depends on vanilla's grid state.** No reads of `powerGrids` structs feed a modded grid's numbers.
- **Breaking vanilla by patching more of it.** No new native patches beyond what the power layer uses today
  (`SolvePower` postfix, the leaf-table prefixes). The hazards list in `STRUCTURES_RECON.md` applies in full.

## Constraints

- BepInEx 6 IL2CPP, HarmonyX, .NET 8; build per project; `-p:InstallToGame=false` while the game runs.
- Every game call in try/catch with `Plugin.Once`; `!= null` not `?.` on Unity objects; reflection `Needed` member
  list checked at Load; measure any new patch target (none expected).
- Multiplayer support in full (host solves, clients display); save compatibility for poles, arrays and banks as
  ordinary modded depots with their states.
- Repo rules: never commit Steam IDs, tokens or saves; ids permanent after release; everything on branches and PRs
  shareable with Andrew; the work-order board returns 404 from this account, so the task is tracked in the PR instead
  and the PR description says so.
- Branching: `power-grid` off `coalite-rename` (the tip with the coalite names and the brick), so the stack is
  #7 → #8 → #9 → power grid. Library changes and the PowerGrid mod may be one PR or two stacked ones, builder's call.

## What Ryan decided

- A grid, not per-source ranges. Poles transmit and supply. Supply is pooled and shared evenly. Our own solar panels,
  not vanilla's. (All quoted above.)

## What the builder decides

- Pole, array and bank models and their exact footprints (`shared/blender/kit.py`, headless Blender, previews beside
  the astronaut as the other structures do; the `making-3d-models` skill and `3d-modeler` agent are available).
- The pure solver's data shapes and the spatial index.
- How the pole carries grid totals to clients (goal 10).
- Whether to reuse the game's `ShowPreview` for the placement arcs or draw them with the library's own
  `MoonPowerLinks` (whichever proves stable; the recon's hazards apply).
- How the furnace's buffer is represented in the solver, as long as goal 3 holds: it delivers only the deficit after
  solar and banks, it never charges a bank, and it fills only by burning coalite.
- Library changes and the PowerGrid mod as one PR or two stacked ones.
- Test scenario geometry and the self-test names.
- Exact strings beyond those given in goal 8; wording follows the game's (sentence case, `  ·  ` separators as
  `PanelText` does today).

## Open to Ryan (not blocking a plan)

- Structure names and ids (goal 5). The builder uses the table; Ryan renames before release if he wants.
- Whether a second, long-range pole should be in the first version (default: no).
- Goal 4's call: vanilla panels keep powering modded machines in their range, the grid tops up. The pure alternative
  (modded machines draw from the grid only) is a small change if play shows the mixed rule is confusing.
- Goal 3's order: solar, then banks, then fuel (Factorio's). The alternative, fuel and solar sharing the load evenly so
  furnaces always burn, is also a small solver change.
