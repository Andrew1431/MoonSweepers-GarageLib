# StructureLib handoff — paused 2026-11-01 after Task 9

Written for whoever picks this up next (Ryan in a new Claude session, or Andrew's agent). Everything below is on
branch `structurelib` in Ryan's clone (`C:\Users\ryguy\projects\moonsweep\MoonSweepers-GarageLib`), 19 commits ahead
of `main`, **nothing pushed**. The plan is to raise one PR at the end.

- Spec: `docs/superpowers/specs/2026-11-01-structurelib-design.md`
- Plan (13 tasks): `docs/superpowers/plans/2026-11-01-structurelib.md`; tooling plan (2 tasks, not started):
  `docs/superpowers/plans/2026-11-01-dev-tooling.md`
- Recon: `research/STRUCTURES_RECON.md` (game facts; read "Interop hazards found" and "Answered by S0" first)
- Decisions: `docs/DECISIONS.md`
- Execution ledger (git-ignored, local to Ryan's clone): `.superpowers/sdd/2026-11-01-structurelib/progress.md`, with
  per-task briefs, implementer reports and review packages beside it. The ledger is the authoritative record of every
  review finding and ruling; this document summarises it.

## Status

| Task | What | Status | Commits |
|---|---|---|---|
| 1 | S0 behaviour spike (research) | complete, reviewed | 21d79f7, 7302e6b |
| 2 | Scaffolding + `KindAllocator` (FNV-1a kinds) | complete, reviewed | 319be24, c581e33 |
| 3 | Definitions, registry, validation rules | complete, reviewed | 450df63, 43ffbde |
| 4 | Plugin skeleton, member check, freeze at world load, self-test harness, example mod | complete, reviewed | 247e06e, e2f7407 |
| 5 | Cloned definitions, `BuildDefinition` prefix, per-kind remaps | complete, reviewed | 8696aa6, 6e3d596 |
| 6 | Build-menu catalog, categories, card art | complete, reviewed | 2d5d0d8, 5ff01a3 |
| 7 | Placement (dev `AutoBuild`), model hook, build/remove events | complete, reviewed | 9671fd4, 086faaa |
| 8 | Depot sidecar (`structurelib.json`), save safety | complete, reviewed (2 fix rounds) | e316858, 534e25c, a32328b |
| 9 | Cargo kinds + canister sidecar | **implemented, reviewed (5 Important), fix round 1 committed, scoped re-review pending** | 0eb9ed3, 97fb624 |
| 10 | Processors (`ProcessorSpec` synthesis, production sidecar) | not started | |
| 11 | Power sources (fuel supply on the vanilla grid) | not started | |
| 12 | Co-op handshake (missing-mod detection) | not started | |
| 13 | SandPower mod, cargo ledger, docs, modpack | not started | |
| Tooling 1–2 | `new-package.ps1`, `dev.ps1` | not started | |

What works in game today (dev self-test on save slot-2, 27 assertions passing, 25 xunit tests green): a modded
structure registers from a plugin, gets a hashed kind, appears in the build menu with a rendered card, can be built,
decorated, removed and refunded, is stripped from the vanilla save into `structurelib.json` and restored on load, and
survives its mod being removed (orphan kept). Modded cargo values answer name/short name/kg/price, and modded-cargo
canisters are stripped and re-injected the same way. The game saves normally with modded content present
(`Moon save failed` = 0 in every run since Task 8).

## Resume here

1. Open a session in the repo, `git checkout structurelib`, `git pull --rebase` is not needed (nothing remote).
2. Invoke `superpowers:subagent-driven-development` with the plan file. It reads the ledger and resumes at the first
   task without a `complete` line: **Task 9, mid fix loop**. The task review (opus) found five Important findings on
   the canister save path (canister claimed before its cargo was applied; sidecar commit gated on `MoonDepotSave.Write`
   although the vanilla write is the whole of `MoonProgressStore.Serialize`; per-save record hand-off; a shared fixed
   `.tmp` name across overlapping saves, which explains the stale-lamp flake; grit weight). Fix round 1 is committed as
   `97fb624` (30 unit tests green; modes A and B 27/27; `Moon save failed` 0). **Next step:** run
   `bash scripts/review-package PLAN 0eb9ed3 HEAD` from the skill's directory, dispatch the scoped re-review
   (`re-review-prompt.md`) against the findings listed in the ledger's Task 9 review line, then close Task 9 and start
   Task 10. The fix report is appended to `task-9-report.md`.
3. Facts the re-reviewer and Task 10 need (also in the ledger): the registry freeze moved from `RestoreDepots` to
   `RestoreCanisters`; canister cargo is re-applied in a `RestoreProduction` postfix via `MoveCanisterRecord` because
   the game restores modded cargo as dust; the strip runs in a `MoonProgressStore.Serialize` prefix (there is no
   canister saver); the sidecar commit decision now lives in `Pure/CommitOrder.cs` with a unique ordered `.tmp` name per
   save. The last self-test run in the log shows `21 passed, 2 failed`: that was a cleanup run with `AutoSave` on and
   no lamp, where `save-strips-modded` and `sidecar-written` fail by design (harness limit, not a regression).

## Rulings made so far (what to re-check if you disagree)

- No separate git worktree; work directly on branch `structurelib` in this clone.
- Work-order board unreachable (ArtifactData tool absent in the session); hot-path edits (`Directory.Build.targets`)
  made without a board claim.
- Task 2 collision test: the plan's single-letter `Contains` asserts were vacuous; test asserts `IdA`/`IdB`.
- Event payloads defined in Task 3 as placeholders; Task 7 reshaped `StructureBuilt`; Task 10 reshapes
  `CycleFinished`.
- `Freeze` is transactional (`Frozen` set last; any exception → `Disabled`); self-test hooks Unity's log.
- **Prefixing static `MoonWorld.IsProcessor` crashes the game at world load.** StructureLib does not patch it. Task 10
  must answer "is processor" another way (candidate: instance `MoonWorld.IsProcessorId(int id)` + `Processor(kind)`
  prefix). Other statics patch fine.
- Task 5's per-kind remaps were never observed firing for a built modded depot (vanilla either never asks or inlines
  them). Tasks 10/11 must verify each behaviour directly rather than rely on the remaps.
- Card art: keep `cardart-modded` as the plan wrote it, add `cardart-rendered`.
- The `MoonDepotSave.Write` strip prefix is unconditional (no `Frozen` guard): accepted as the safer form.
- `Merge` keeps records with a null/empty id verbatim (orphans).
- Sidecar `.tmp` is written in the prefix and committed in the finalizer only when the vanilla write did not throw.
- `reload-restores` fails only when the loaded sidecar held a lamp record that the world then lacks; absent otherwise.
- Vanilla `MoonDepotSnapshot.Copy()` zeroes contribution dollars, so the sidecar stores $0 shares (same as vanilla).

## Carried items for Tasks 10–13

- `WriteTmp` merges production and canister records with `_ => false`; live production records (Task 10) need
  per-record ownership like the depots' `inWorld` set or they will duplicate.
- `IsProcessor` cannot be prefixed (above). `ProcessorSpec` is a class, so a `Processor(kind)` prefix is safe.
- The S0 spike found `SolvePower` recomputes `SunFactor` itself and there is no per-node creation hook: fuel power
  (Task 11) goes through a `SolvePower` postfix (spec fallback).
- `MoonDepotBuildDefinition` donor for the generator: battery_bank (kind 6) may lack a production view; fall back to
  solar post (93) if so.
- GarageLib bug found in passing: `DevTools.Buy` relies on `MoonWorld.DevAddMoney`, which is a no-op in this build;
  `SetTeamDollars` works. Not fixed (outside scope); file on the board.

## Deferred minor findings (reviewers' lists, not fixed)

Collected in the ledger per task. Highlights: `DevTools.cs` is over 800 lines (split `SelfTest` out); `Remap.ToBase`
allocates its `Once` key before checking the count; `CargoOffenders` duplicates `CheckCargo` rules without a parity
test; shared `.tmp` path if saves ever overlap; `CheckRestored` is a postfix (a throwing `RestoreDepots` would drop
unrestored records on the next write); exception text in logs can include file paths.

## Environment state

- Game installed with BepInEx 6 be.791 + ModEngine 1.0.2; launch `MoonSweepers.exe` directly (Steam launch does nothing
  on this machine). Close the game before any installing build. Allow ≥10 s between a kill and a relaunch.
- Installed plugins: `StructureLib`, `ExampleStructure` (dev builds). The research spike DLL was removed.
- Dev config `BepInEx/config/com.ryanhartwig.structurelib.cfg`: `AutoLoadSlot = 2`, `SelfTest = true`, `AutoBuild`
  empty, `AutoRemove = true`, `AutoSave = false`, `ScreenshotMenu = false`.
- Save slots: slot-1 is Ryan's (never load it from dev tools; `AllowSlot1` guard). slot-2 is the test copy: vanilla
  solar twin + pressure chamber from the spike, no modded structures, an empty `structurelib.json`. slot-3 may exist as
  a backup of slot-2.
- .NET SDK 8 only: build per project (`.slnx` needs SDK 9). In-game check recipe is in the plan's Global Constraints
  (build `examples/ExampleStructure` with `-p:InstallToGame=true`, launch, grep `[selftest]`).

## Needs a human

- Two-player co-op test (Task 12) needs Andrew's machine.
- By-hand checks never done: placing a modded structure with the real ghost and removing it with the real remove tool
  (Task 7 Step 4); belt hand-off between a vanilla press and a modded machine (Task 13).
- Board access: Andrew must share the work-order artifact with Ryan as Editor; the session also needs the
  `ArtifactData` tool to be present.
- Optional: install .NET SDK 9.0.200+ so the `.slnx` builds in one command.
