# StructureLib handoff — all 13 tasks complete, final review clean (2026-11-02)

Written for Ryan and Andrew. Since 2026-11-02 the work is **two stacked pull requests**: the library on branch
`structurelib-lib`, PR #2 against `main` (https://github.com/Andrew1431/MoonSweepers-GarageLib/pull/2), and
SandPower on branch `structurelib`, PR #1 against `structurelib-lib`
(https://github.com/Andrew1431/MoonSweepers-GarageLib/pull/1), whose diff is the mod alone. `origin/main` and the
dev-tooling branch are already merged into the library branch. SandPower was built on the library branch and
removed in its last commit, so the mod's development history is in PR #2 and its files are in PR #1.

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

## Belt ports and custom models (second plan, 2026-11-02)

Spec `docs/superpowers/specs/2026-11-02-structurelib-belts-and-models-design.md`; plans
`docs/superpowers/plans/2026-11-02-structurelib-belts-and-models.md` and `docs/superpowers/plans/2026-11-02-sandpower-models.md`;
ledger (git-ignored) `.superpowers/sdd/2026-11-02-structurelib-belts-and-models/progress.md`. Same branch, same rules:
nothing pushed. Design and plan commits 0a5b32d, 529374d, 8f490dc.

| Plan | Task | What | Status | Commits |
|---|---|---|---|---|
| Belts and models | 1 | Belt spike: `AutoBelt`, `DumpCards`, ring `AutoBuild`, belt and donor facts in the recon | complete, reviewed (1 fix round) | eb89d42, db2e3ca |
| Belts and models | 2 | Ports as definition data (`StructureDefinition.Ports`), written into the clone as `sl:<cargo>:<Name>`, `PortCargo` answers | complete, reviewed | 1d70ce5 |
| Belts and models | 3 | Belt input (`HandOff` prefix, `CargoEntered`) | complete, reviewed (1 fix round) | e64d016, 0bdd5ff |
| Belts and models | 4 | Belt output (vanilla emits; `CargoEmitted`), belts and modded canisters survive save/reload | complete, reviewed | 4a96222 |
| Belts and models | 5 | `Model = "<file>.bin"` on the donor template, ports from `Port_*` nodes, stand-in materials | complete, reviewed (1 fix round) | 7d04434, a17c066 |
| Belts and models | 6 | SandPower wired to its models and model ports; the chamber → compactor → generator belt chain in the self-test; `AutoBuildLayout = line`; `package.ps1` ships `.bin` files; docs | complete, reviewed (+ final fix wave) | 2d72352, a4bf6fb + final fix wave |
| SandPower models | 1 | Blender kit structure helpers (`kit.port`, hopper, chute, footprint guide), `STRUCTURES.md`, `check_bin.py` | complete | c931dff |
| SandPower models | 2 | Sand compactor model with `Sand in` / `Briquettes out` ports | complete | cb99d7b |
| SandPower models | 3 | Briquette generator model with a `Briquettes in` port | complete | c5f7b4b |

Unit tests: 130 green. In game (slot-2, Task 6): the chain run 34/0 (`sandpower-chain`, `sandpower-save-clean`,
`sandpower-model-swapped`, `sandpower-model-ports` and every earlier SandPower check PASS; first generator charge 47.7 s
after the chamber's dust load) and the reload run 28/0 (`sandpower-reload`: both belts, the briquette canister on the
sand belt and both production states back). `Moon save failed` 0 in both. After the final fix wave: 136 unit tests
green and the chain run 34/0 again, the first card render 5 ticks after the stand-in material swap.

**By hand for this plan** (not covered by any self-test):
- The belt tool onto modded ports: lay belts by hand from a pressure chamber's output to the compactor's sand hopper
  and from its chute to the generator's hopper. The self-test builds belts through `TryBuildConveyor`, never the tool.
- Both models at player scale, and their glow and steel look in the real lighting.
- The stand-in → game material switch a few seconds after a world loads (watch the models and the cards change).
- Uninstall SandPower with belts and briquettes attached, load, reinstall.
- Co-op: belts into and out of modded machines with two players (the host runs the machines; clients untested).

Deferred minor findings of this plan: the `Minor deferred:` lines in the ledger
(`grep "Minor deferred" .superpowers/sdd/2026-11-02-structurelib-belts-and-models/progress.md`), one per reviewed task.

## Power UX (2026-11-02/03)

Spec `docs/superpowers/specs/2026-11-02-power-ux-design.md`, plan `docs/superpowers/plans/2026-11-02-power-ux.md`,
recon `research/POWER_UX_RECON.md`; branch `power-ux` (off `structurelib-lib`), not pushed. What landed: modded
generators and machines show their own look-at panel instead of the tracker's (`Patches/PanelPatches.cs`: `ShowPanel`
prefix + postfix, an `Update` postfix for machines without the build tool, gated on menus, vehicles and a carried
canister; routers show none); fuel generators feed vanilla machines within their `Range` from `ChargeKwh` (the
`SolvePower` postfix, `Pure/FuelTopUp.cs`, a fully fed machine reads "Powered by <generator>"); the library draws its
own cyan arcs and one range ring with the build tool held on a fuel-network member (`PowerLinksView.cs`); and
`CanisterTint` is applied to the near canister view (`Patches/CanisterPatches.cs`; far canisters stay dust; the example
grit is tinted). Self-test checks: `panel-generator`, `panel-machine`, `panel-vanilla-untouched`, `power-vanilla-fed`,
`arc-drawn` (`[Dev] ForceArcs`), `cargo-tint`; dev probe `PowerUxProbe` / `PowerUxTest` / `PowerUxTeleport` /
`PowerUxHome`. Unit tests 180 green. Standard configs on a fresh slot-2 (Task 6): belt output + power 50/0, belt boxes
chain 49/0, reload 37/0; the first belt output + power attempt was 49/1 until `DumpCards` was added to the README
config. `cargo-tint` passed only in the belt output + power config. `power-vanilla-fed` passed in none of the three
standard configs, only in Task 3's own setup (a chamber AutoBuilt next to the burner); in the standard configs it is
not applicable (no `96` in AutoBuild, or the line layout puts the chamber outside the burner's network). The by-hand
test bed (slot-2 as this plan's runs left it: SandPower compactors 26 and 27, generator 28, chambers 29, 41 and 46,
example grinder 31 and burner 32; `[Dev] PowerUxProbe = true` with `PowerUxHome = 39.57,1993.00,-26.00`, the walker
spot that worked for the runs, puts the walker there; the recon point 22.39,1993.00,-29.77 is too crowded for
AutoBuild) is kept at `C:\Users\ryguy\projects\moonsweep\.scratch\slot-2-power-ux-bed`;
copy it over slot-2 to use it. slot-2 itself is now the belt boxes reload run's world.

**By hand for this plan** (not covered by any self-test):
- Arcs and the ring in real lighting and at the player's eye, with the real build tool (the self-test forces them; the
  probe forced the tool); the ring was never clearly seen in a screenshot.
- Canister tint in real lighting at the player's eye, on a belt as well as on the ground (only ground canisters were
  screenshotted; the tint multiplies the dust fill, so grit reads darker blue-grey dust).
- A fed vanilla machine's status text and panel (`Powered by <generator>`; the POWER row still reads "no panels in
  reach").
- Far canisters (beyond about 100 m) stay plain dust: walk away from a tinted belt and look back.
- The no-tool panel in states not tested: the map open, driving (the pause menu and sitting in a vehicle were checked).

## Before merging (Ryan or Andrew)

1. **Merge PR #2 (the library) first, with a merge commit or a rebase, not a squash.** A squash would make PR #1's
   diff against `main` show the whole library again. Then retarget PR #1 to `main`; its diff stays mod-only.
2. **Fix `package.ps1`'s release note** (it appends `' and GarageLib'` for every non-GarageLib package): SandPower
   needs StructureLib, StructureLib needs nothing. Derive the dependency from the csproj `ProjectReference` or a small
   name→dependency map. Do it before any `-Release`.
3. **While the PRs are stacked:** anything a mod needs from the library (a fix, a feature) goes on `structurelib-lib`,
   never on a mod branch; the mod branch then rebases onto it. The next structure mod is a branch off
   `structurelib-lib` with a PR against it, touching only `mods/<Name>/**` plus its registration lines
   (`modpack.json`, `GarageLib.slnx`, root `README.md` row, `docs/cargo-registry.md` row). Two open mod PRs
   conflict on those registration lines by one line each; resolve by keeping both.
4. The work-order board was unreachable in every session; hot-path edits on these branches: `Directory.Build.targets`,
   `GarageLib.slnx`, `modpack.json`, `README.md`, `CONTRIBUTING.md`, `package.ps1`, `src/GarageLib/GarageLib.csproj`,
   `shared/**`.

## Needs a human (by hand, in this order)

- **Build-menu placement, real ghost and remove tool** (the Critical finding of the final review was fixed through the
  menu's placement path driven from the dev tool, not by hand): place a lamp, the compactor and the generator from the
  menu with a non-zero rotation, snapped or supported on a vanilla structure; save twice; expect no "vanilla write
  threw" / "Structure pose ID" line, `Sidecar written: N structures` and `(N placed with a free pose)` in the "moved"
  line, and no `vanilla depots on a modded one` warning. Reload: same rotation in the world view. Remove with the
  remove tool, save: the sidecar drops it and the save is still clean.
- **Belts**: superseded by the second plan (belt input and output work and are self-tested); what is left by hand is
  in its by-hand list above.
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
- **Before release: the native crash at load during the card render.** Twice in roughly 40 loads (first plan Task 7,
  2026-11-01 20:57; second plan Task 6, 2026-11-02 04:52) the game died with `0xC0000005`, a null read at
  `UnityPlayer.dll+0x10a652f` (dumps `%LOCALAPPDATA%\CrashDumps\MoonSweepers.exe.492412.dmp` and `.874884.dmp`), with
  the BepInEx log ending inside `IconRenderer`'s first or second icon-camera render, both times after a relaunch 5–9 s
  after a forced kill. The first time no custom model existed, so the load-time card render (first-plan code) is the
  suspect, not the SandPower models. Reproduce with about 20 launches per arm, varying one thing at a time: relaunch
  delay (under 10 s vs 30 s or more), card rendering on vs off or delayed a few frames after "Session in game", the
  icon camera's MSAA (8x vs 1x), SandPower vs the example only; read the native stack with WinDbg and Unity's symbol
  server. Evidence: `research/STRUCTURES_RECON.md`, the Task 6 chain paragraph. A mitigation is in (final fix wave:
  no card render until 5 ticks after the stand-in shader swap or `QueueAll`, and the icon camera enabled a frame after
  it is created); it is unproven against the crash, so the reproduction arms above remain.
- Optional: install .NET SDK 9.0.200+ so `GarageLib.slnx` builds in one command (not buildable here, SDK 8.0.421).

## Known limitations shipped as documented (both READMEs say so)

Canister input into modded machines by hand (belts work since the second plan); collector selling untested; vanilla machines are never
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
