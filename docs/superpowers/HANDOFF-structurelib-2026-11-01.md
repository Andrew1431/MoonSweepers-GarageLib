# StructureLib handoff — all 13 tasks complete, final review clean (2026-11-02)

Written for Ryan and Andrew. Everything below is on branch `structurelib` in Ryan's clone
(`C:\Users\ryguy\projects\moonsweep\MoonSweepers-GarageLib`), 34 commits ahead of the branch point `e700a21`,
**nothing pushed, nothing merged**. The dev-tooling plan (`new-package.ps1`, `dev.ps1`) landed separately on branch
`worktree-dev-tooling` (worktree `.claude/worktrees/dev-tooling`, 4 commits off `origin/main`), also unpushed.

- Spec: `docs/superpowers/specs/2026-11-01-structurelib-design.md` (binding authority)
- Plan: `docs/superpowers/plans/2026-11-01-structurelib.md`; tooling plan `docs/superpowers/plans/2026-11-01-dev-tooling.md`
- Recon (game facts, hazards, saver audit): `research/STRUCTURES_RECON.md`
- Decisions: `docs/DECISIONS.md`; cargo bytes: `docs/cargo-registry.md`
- Execution ledger (git-ignored, Ryan's clone only): `.superpowers/sdd/2026-11-01-structurelib/progress.md` — every
  review finding, every controller ruling (`grep "Ruling:"`), the deferred-findings index
  (`final-review-deferred-index.txt`), per-task briefs, reports, review packages and game logs. Kept on purpose.

## Status

| Task | What | Status | Commits |
|---|---|---|---|
| 1–8 | Recon, kinds, registry, plugin, clones, catalog, placement, depot sidecar | complete, reviewed | 21d79f7 … a32328b |
| 9 | Cargo kinds + canister sidecar | complete, reviewed (1 fix round) | 0eb9ed3, 97fb624 |
| 10 | Processors: `ProcessorSpec` synthesis, generic cycle, production sidecar, `SolvePower` grid attach | complete, reviewed (1 fix round) | 9396bd2, 25fe999 |
| 11 | Fuel power sources, modded-only grids (negative ids) | complete, reviewed (1 docs fix round) | 0b046a0, 3477b97 |
| 12 | Co-op handshake over Steam lobby data | complete, reviewed (1 fix round); two-player untested | de46f47, 88638ad |
| 13 | SandPower, cargo ledger, READMEs, modpack, slnx | complete, reviewed (1 docs fix round) | 9ab9d9a, ca2bd75, c872bbe |
| Final | Whole-branch review + one fix wave + scoped re-review: clean | | ba9fed2 |

Unit tests: 111 green (`dotnet test tests/StructureLib.Tests -c Release`). In-game dev self-test on slot-2: every run
since Task 8 ends with `0 failed` and `Moon save failed` 0; the last runs (final fix wave) were 31/0, 40/0, 27/0.

What works in game, proven by the self-test: a modded structure registers, gets a hashed kind, appears in the build
menu with rendered card art, builds (dev `AutoBuild` and the build menu's own placement path), decorates, removes with
refund, is kept out of the vanilla save in `structurelib.json` with its pose and restored on load, survives its mod being
uninstalled (orphan kept) and reinstalled. Modded cargo answers name/short name/kg/price; modded-cargo canisters
round-trip the sidecar. A modded processor (example grinder, SandPower compactor) takes `CrewProcessorLoad` input,
cycles on vanilla solar power or on a modded generator, keeps its slots across save/load. A modded fuel generator
(example burner, SandPower generator) burns fuel into stored charge, supplies StructureLib machines within range on
the vanilla grid or on its own negative-id grid, never burns into a full battery, keeps its charge across reload. The
handshake manifest builds, encodes and round-trips in single player.

## Before merging (Ryan or Andrew)

1. **Merge `origin/main` into `structurelib` by hand** (a dry run showed conflicts in `GarageLib.slnx`, `README.md`,
   `modpack.json`; `Directory.Build.targets` and `CONTRIBUTING.md` auto-merge):
   - `modpack.json`: main is `0.2.0` with `["GarageLib","SharedUpgrades","LateGameVehicles"]` (Ms Choppa was pulled
     out and `modpack-v0.2.0` is already released). Resolve to version `0.3.0`, packages
     `GarageLib, SharedUpgrades, LateGameVehicles, StructureLib, SandPower`, description merged. Do **not** keep this
     branch's `0.1.0`/`MsChoppa` side: `package.ps1 -Release` would skip the modpack as "already released".
   - `GarageLib.slnx`: both sides add a `/mods/` folder; keep both `SharedUpgrades` and `SandPower` plus this branch's
     `/tests/` folder and the StructureLib/ExampleStructure entries.
   - `README.md`: keep main's rewritten install section; add this branch's StructureLib and SandPower rows.
2. **Fix `package.ps1`'s release note** (both sides append `' and GarageLib'` for every non-GarageLib package): SandPower
   needs StructureLib, StructureLib needs nothing. Derive the dependency from the csproj `ProjectReference` or a small
   name→dependency map. Do it in the merge commit or right after, before any `-Release`.
3. Then merge `worktree-dev-tooling` (independent; touches `new-package.ps1`, `new-vehicle.ps1`, `dev.ps1`,
   `templates/**`, `tests/scripts/**`, one section each in `README.md`/`CONTRIBUTING.md`).
4. The work-order board was unreachable in every session; hot-path edits on this branch: `Directory.Build.targets`,
   `GarageLib.slnx`, `modpack.json`, `README.md`, `CONTRIBUTING.md` (`package.ps1` untouched but needs item 2).

## Needs a human (by hand, in this order)

- **Build-menu placement, real ghost and remove tool** (the Critical finding of the final review was fixed through the
  menu's placement path driven from the dev tool, not by hand): place a lamp, the compactor and the generator from the
  menu with a non-zero rotation, snapped or supported on a vanilla structure; save twice; expect no "vanilla write
  threw" / "Structure pose ID" line, `Sidecar written: N structures` and `(N placed with a free pose)` in the "moved"
  line, and no `vanilla depots on a modded one` warning. Reload: same rotation in the world view. Remove with the
  remove tool, save: the sidecar drops it and the save is still clean.
- **Belts**: sand into the compactor, briquettes out, briquettes into the generator (unsupported and untested; the
  vanilla port path refuses modded machines, so expect refusal, and confirm the save stays clean).
- **Carried cargo**: pick up / vehicle-carry a briquette canister, save, inspect the save for cargo value 3.
- **Power UI**: the generator's interaction panel with a negative grid id; the re-added panel node during a placement
  preview; `MoonSolarPanelView.Animate` on the burner (the post donor has 0 kWh serialized).
- **Two-player (Andrew's machine)**: identical mods → join succeeds and a host-built modded structure appears on the
  client; client without ExampleStructure → refused with the `missing mod` line. Watch: which `Admit` branch each join
  path takes (lobby data / member data / "cannot check"); the "wrote the manifest as lobby (member) data" lines appear
  once per lobby, not five times then muted; the host logs "wrote the manifest as lobby data", not "Steam refused"; a
  refusal comes from a real manifest, not the empty-manifest branch; what `EndJoin` inside the `Offered` prefix does to
  the join coroutine; whether `why` is shown as a dialog; whether the host streams `SendWorld` to a departed client.
- **Balance (Ryan, mechanics)**: each generator burn adds 3 kWh (two burns fill the 6 kWh battery) against 10 kW peak
  output; $7 per 2 kg briquette; compactor 12 kW / 8 s.
- Optional: install .NET SDK 9.0.200+ so `GarageLib.slnx` builds in one command (not buildable here, SDK 8.0.421).

## Known limitations shipped as documented (both READMEs say so)

Belt and canister input into modded machines; belt output and collector selling untested; vanilla machines are never
fed by a modded generator and vanilla grid totals never see modded demand (`MoonPowerGrid` is a struct that cannot be
written back); no power link is drawn to modded structures; a generator's `RunningKw` is ignored; `SupplyMode.Solar`
supplies nothing; `CanisterTint` is declared but never applied; the walker's `LoadProcessor` refuses modded machines;
a modded-kind anchor or "robots off" entry is stripped from the save and not persisted; the handshake admits a join it
cannot check (client not yet in the lobby, oversize manifest, host without StructureLib) and never kicks from the host;
a client joining within ~2 s of lobby creation can be wrongly refused.

## Rulings (every decision made on your behalf, in order; details in the ledger)

1. No worktree; work on `structurelib` in the clone. 2. Board unreachable; hot-path edits without a claim. 3. Task 5's
remap prefixes in one `KindRules` class. 4. Task 2 collision test asserts real ids (the plan text was vacuous).
5. Event payloads were placeholders in Task 3, reshaped in Tasks 7/10. 6. `Freeze` is transactional, `Disabled` on any
exception; the self-test hooks Unity's log. 7. **Static `MoonWorld.IsProcessor` is never prefixed** (native crash);
`IsProcessorId` + `Processor(kind)` instead. 8. Card art: keep `cardart-modded`, add `cardart-rendered`.
9. Unconditional strip prefix. 10. Null-id records are orphans. 11. `.tmp` in the prefix, commit in the finalizer only
when the vanilla write did not throw. 12. `reload-restores` fails only when the sidecar held a lamp the world lacks.
13. Grid membership for modded processors in Task 10 via a `SolvePower` postfix (load-bearing for S2/S3).
14. Beyond-brief Task 10 changes accepted (`CrewProcessorLoad` prefix, generic cycle, `pendingKilograms`).
15. Belt/canister input out of Task 10. 16. No per-node hook exists; fuel supply through the same postfix.
17. Modded-only grids get a StructureLib-owned negative id. 18. **`TryPanel` is never patched** (native crash).
19. The handshake compares and refuses on the client (spec S4); the host never kicks; a client without StructureLib
getting in is the spec's accepted gap. 20. The generator burns 2 kg (one briquette) per cycle, spec text over plan
number; balance left to Ryan. 21. Modpack row/description edited in place; other README/CONTRIBUTING edits additive.
22. The documented `AutoBuild` order replaces the brief's unreachable one. 23. The generator note says "Powers
StructureLib machines"; accuracy over verbatim plan text. 24. Recon heading names with task numbers may stay as link
text. 25. The older DECISIONS line gets a parenthetical. 26. One final fix wave covering C1 and I1/I4/I5/I6/I7f/I8;
merge-time items (modpack version, `package.ps1` note) left to the merge. 27. Parked after the fix wave: the README's
"with its pose" is proven for identity rotation only (the by-hand check covers it); a null `ModdedKinds` fallback could
let modded production reach a vanilla save that is then refused (narrow, safe); anchor/robots-off entries not persisted
(accepted for 0.1.0). 28. The plan workspace is kept, not deleted.

Deferred minor findings (none blocking; triaged by the final review as ship-as-known-limitation or follow-up): per-solve
interop enumeration and allocations in the power postfix; an `AddNode` duplicate guard; battery draw not capped at
stored kWh; `SerializeFin` overwriting a deferred commit; Il2Cpp lists mutated under `gate` in older inject code;
unclaimed production record pairs; `priceDollars` truncation; `Redact` first-only; `heard` never pruned; the
tautological `handshake-manifest` kind compare; the `sandpower-briquette-out` disjunct; README Output-slot wording;
`DevTools.cs` (~1400 lines, DEVBUILD-only) and `Sidecar.cs` (~1000) splits can follow the merge; SandPower's
assertions living in the library's dev file want a consumer-mod assertion hook.

## Environment state

- Game installed with BepInEx 6 be.791 + ModEngine 1.0.2; launch `MoonSweepers.exe` directly (Steam launch does nothing
  here). Close the game before any installing build. Allow ≥10 s between a kill and a relaunch. The game autosaves into
  the loaded slot on its own; dev runs restore slot-2 from `C:\Users\ryguy\projects\moonsweep\.scratch\slot-2-backup-task11`.
- Installed plugins: `GarageLib` (this branch's build), `StructureLib`, `StructureLib.ExampleStructure` (dev builds of
  ba9fed2). `SandPower` is **not** installed (built with `-p:InstallToGame=true` only during Task 13).
- Dev config `BepInEx/config/com.ryanhartwig.structurelib.cfg`: `AutoLoadSlot = 2`, `SelfTest = true`, `AutoBuild` and
  `AutoFeed` empty, `AutoBuildViaPlan` off, `AutoRemove = true`, `AutoSave = false`, `ForcePower = false`,
  `AllowSlot1 = false`.
- Save slots: slot-1 is Ryan's (never auto-loaded). slot-2 is the test copy, byte-identical to the backup: vanilla solar
  twin + pressure chamber, no modded structures, empty `structurelib.json`, no `.tmp` files.
- .NET SDK 8 only: build per project. The in-game check recipe is in the plan's Global Constraints.
