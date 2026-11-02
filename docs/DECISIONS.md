# Decisions log

Fundamental decisions about the repo and its libraries, newest first, so every maintainer and agent sees them in git.
Andrew asked (2026-11-01) for a change log the agents contribute to; the work-order board is the other copy, for
whoever can write there. One line per decision: date, who, what, where the detail lives.

- 2026-11-02, Ryan: **SandPower 0.1.0 shipped values:** sand briquette cargo 3, 2 kg/unit, $7/unit; sand compactor
  (stone press, $800) 16 kg sand, 4 kg per 8 s cycle, 12 kW, mass kept (4 kg sand -> 2 briquettes), 10 kg output;
  briquette generator (solar post 93, not the battery bank: no production view; $1,500) 10 kg store, 1 kg per 20 s,
  10 kW peak, 6 kWh battery (+3 kWh per burn), 24 m, StructureLib machines only. Detail: `mods/SandPower/README.md`.
- 2026-11-01, Ryan + Andrew: **Structure kinds are hashed from the string id** (FNV-1a into a wide sparse range), not
  allocated in sorted order, so co-op players agree on numbers without identical mod lists. Cargo values are declared
  bytes claimed in `docs/cargo-registry.md`. Detail: `docs/superpowers/specs/2026-11-01-structurelib-design.md`.
- 2026-11-01, Ryan: **StructureLib** (`src/StructureLib`) is the second shared library; first consumer **SandPower**
  (`mods/SandPower`): belt-fed sand compactor → sand briquettes → briquette generator feeding the vanilla power grid.
  No modded foundations or decks this milestone. GUIDs `com.ryanhartwig.structurelib`, `com.ryanhartwig.sandpower`.
- 2026-11-01, Ryan + Andrew: **Co-op handshake (missing-mod detection) ships with StructureLib's first release;**
  graceful degradation for clients missing a mod is deferred, for vehicles and structures alike.
- 2026-11-01, Andrew: the repo is a **monorepo of end-game mods** (vehicles, buildings, mechanics), both maintainers
  have equal authority, push straight to `main` after pulling, coordinate through the work-order board in `CLAUDE.md`.
