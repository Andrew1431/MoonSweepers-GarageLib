# Decisions log

Fundamental decisions about the repo and its libraries, newest first, so every maintainer and agent sees them in git.
Andrew asked (2026-11-01) for a change log the agents contribute to; the work-order board is the other copy, for
whoever can write there. One line per decision: date, who, what, where the detail lives.

- 2026-11-08, Ryan (poles and a pooled grid instead of per-source ranges): **Power grid: poles and pooled supply:**
  modded power is a grid of poles (`PowerRole.Relay`, `LinkRange` 24 m, `SupplyRadius` 12 m); a pole powers the modded
  sources, banks and machines in reach and every consumer on the grid gets the same satisfaction. Supply order is
  Factorio's: solar, then batteries, then fuel for the remaining deficit only; fuel never charges a bank. (1)
  **Vanilla panels are untouched:** `SolvePower` is native, holds its totals in structs managed code cannot write back,
  and an earlier attempt to feed a vanilla machine from a modded source created energy (a vanilla battery cannot be
  debited); so the grid is a parallel system of modded structures (PowerGrid's pole, array and bank), vanilla machines
  get only a top-up (`vanilla share + (1 - vanilla share) x satisfaction`, never lowered). (2) **Fuel has no kWh
  buffer:** multiplayer verdict S (every peer runs `SolvePower`; the host streams only Running, Progress, Cycles and
  stores; clients skip `FinishCycle`), so a charge raised by a cycle event would never reach a client; a furnace offers
  `PeakKw` while a coalite burns and a burn starts only when the last solve still had a deficit (host-gated,
  `GridState.WantsFuel`). Cost: up to one burn wasted at low demand. No host gate on the solve; bank charge integrates
  per peer and the host's save restores it through the sidecar. (3) **`IsPowerNode` stays true for modded power
  depots** (poles, arrays, banks) so they get the look-at panel; `IsSolarPanel` stays false for every modded kind so
  vanilla panel code ignores them. (4) **The sun formula is provisional:** read from the game's machine code
  (`SunDirectionAt`, a smoothstep at the horizon, `max(0, dot(sun, normal))`), not yet sampled against a vanilla panel.
  SandPower now depends on PowerGrid; `PowerDefinition.Range` is gone. Detail:
  `docs/superpowers/specs/2026-11-08-power-grid-design.md`, `src/StructureLib/README.md` "Power".
- 2026-11-04, Ryan: **SandPower renamed before release:** cargo `ryanhartwig.sand-briquette` -> `ryanhartwig.coalite`
  ("coalite"), `ryanhartwig.sand-compactor` "Sand compactor" -> `ryanhartwig.coking-oven` "Coking oven",
  `ryanhartwig.briquette-generator` "Briquette generator" -> `ryanhartwig.coalite-furnace` "Coalite furnace"; model
  files, port nodes (`Port_Coalite ...`) and dev checks follow. Vanilla names machines after their mechanism (Pressure
  chamber, Stone press, Fuel generator) and "briquette generator" named the burner after its fuel; the ids were
  unreleased, so they could still change. The GUID `com.ryanhartwig.sandpower`, the `SandPower` folder and cargo value 3
  stay. Older lines below keep the old names.
- 2026-11-04, Ryan (briquettes read as dark capsules in play): **A cargo may bring its own canister model:**
  `CargoDefinition.CanisterModel` (a `.bin` beside the mod's DLL) is built under the near canister view in place of
  the capsule, whose renderers are hidden and whose colliders, rigidbody and scripts are kept (riding, pickup and
  hoppers unchanged); far, instanced canisters stay the dust capsule, as with the tint. Models are sized against the
  capsule's measured box (`shared/blender/STRUCTURES.md`, "Canister models"); the kit gains `MS_Sand`, a matte made
  from its colour. SandPower's briquette is the first (`sand_briquette.bin`).
- 2026-11-03, agent (Ryan not answering design questions during this work): **Power UX for modded structures:** the
  library draws its own power arcs and range ring with a second `MoonPowerLinks` (vanilla's `ShowGrid` cannot include
  modded members, and `powerLinks` / `powerGrids` are not written), shown with the build tool held on a fuel-network
  member, with one ring (the type has a single ring renderer); fuel generators feed vanilla machines only within their
  own `Range` (fuel never flows through a vanilla grid), the energy taken from `ChargeKwh`; `CanisterTint` is applied
  to the near canister view only (far, instanced canisters stay dust); modded generators and machines get their own
  look-at panel, routers none. Detail: `docs/superpowers/specs/2026-11-02-power-ux-design.md`,
  `research/POWER_UX_RECON.md`, `src/StructureLib/README.md` "Power".
- 2026-11-02, Ryan: **Belt ports are definition data, authored in the model:** a structure's ports come from its
  model's `Port_*` nodes (else `StructureDefinition.Ports`, else the donor's); modded machines take belt input
  (`HandOff`, `CargoEntered`) and give belt output (vanilla emits, `CargoEmitted`); `Model = "<file>.bin"` swaps a
  custom model onto the donor template. SandPower is belt-fed with its own models. Detail:
  `docs/superpowers/specs/2026-11-02-structurelib-belts-and-models-design.md`, `src/StructureLib/README.md`.
- 2026-11-02, Ryan: **SandPower 0.1.0 shipped values:** sand briquette cargo 3, 2 kg/unit, $7/unit; sand compactor
  (stone press, $800) 16 kg sand, 4 kg per 8 s cycle, 12 kW, mass kept (4 kg sand -> 2 briquettes), 10 kg output;
  briquette generator (solar post 93, not the battery bank: no production view; $1,500) 10 kg store,
  one 2 kg briquette per 20 s, 10 kW peak, 6 kWh battery (+3 kWh per burn), 24 m, StructureLib machines only.
  Detail: `mods/SandPower/README.md`.
- 2026-11-01, Ryan + Andrew: **Structure kinds are hashed from the string id** (FNV-1a into a wide sparse range), not
  allocated in sorted order, so co-op players agree on numbers without identical mod lists. Cargo values are declared
  bytes claimed in `docs/cargo-registry.md`. Detail: `docs/superpowers/specs/2026-11-01-structurelib-design.md`.
- 2026-11-01, Ryan: **StructureLib** (`src/StructureLib`) is the second shared library; first consumer **SandPower**
  (`mods/SandPower`): belt-fed sand compactor → sand briquettes → briquette generator feeding the vanilla power grid
  (superseded in part: what shipped feeds StructureLib machines only and is not belt-fed; see the 2026-11-02 SandPower
  0.1.0 line above).
  No modded foundations or decks this milestone. GUIDs `com.ryanhartwig.structurelib`, `com.ryanhartwig.sandpower`.
- 2026-11-01, Ryan + Andrew: **Co-op handshake (missing-mod detection) ships with StructureLib's first release;**
  graceful degradation for clients missing a mod is deferred, for vehicles and structures alike.
- 2026-11-01, Andrew: the repo is a **monorepo of end-game mods** (vehicles, buildings, mechanics), both maintainers
  have equal authority, push straight to `main` after pulling, coordinate through the work-order board in `CLAUDE.md`.
