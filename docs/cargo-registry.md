# Cargo registry

The game's cargo is a byte (`MoonCargo`). Values 0..2 belong to the game; StructureLib mods declare a value in 3..255
on their `CargoDefinition` (`Value`). This file is the ledger of claimed values for every mod in this repo.

## Rules

- **Range:** a mod's cargo uses a value from 3 to 255. StructureLib refuses anything below 3.
- **Duplicates are hard errors.** Two loaded cargo with the same value are both dropped when the registry freezes, and
  every structure whose slots use them is dropped with them. Claim a value here before you use it.
- **A value never changes after release.** Saves keep cargo by id in `structurelib.json`, but the world and co-op
  peers hold the value, and the co-op handshake refuses a join when host and client disagree on it.

## Claiming a value

Pick the lowest free value, add a row below in the same commit (or pull request) as the mod that uses it, and add a
task on the [work-order board](https://claude.ai/artifact/Wam9isLuKoJkHGHSi3pVJx) naming the value so two people
don't claim it at once. A mod outside this repo can claim a value the same way.

## Claimed values

| Value | Id | Owner GUID | Mod | Claimed |
|---|---|---|---|---|
| 0 | `dust` | (game) | Moon Sweepers | vanilla |
| 1 | `sand` | (game) | Moon Sweepers | vanilla |
| 2 | `stone` | (game) | Moon Sweepers | vanilla |
| 250 | `ryanhartwig.example-grit` | `com.ryanhartwig.structurelib.examplestructure` | [ExampleStructure](../examples/ExampleStructure) (dev only, never released) | 2026-11-01 |
