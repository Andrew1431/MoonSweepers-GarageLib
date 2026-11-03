# Belt Boxes (compact splitter and merger) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A new mod `mods/BeltBoxes` ships a one-in/three-out box splitter and a three-in/one-out box merger on a 1.4 m footprint, routed by StructureLib's belt hooks, with their own Blender models, proven by an in-game chain and save/reload.

**Architecture:** StructureLib gains *routers*: a `StructureDefinition.Router` with no processor whose cargo-agnostic ports are served by a router branch in the existing `HandOff` prefix (splitter: round-robin over outputs whose belt can take the canister; merger: round-robin over inputs with a canister waiting), a `Footprint` override written into the cloned definition (with the donor's colliders replaced under a custom model), and a `CargoRouted` event. The mod is registrations, a README and two models; the library's DEVBUILD self-test proves the chain with the example mod's grinder and burner.

**Tech Stack:** C# (BepInEx 6 IL2CPP, HarmonyX), xunit, Blender 4.5.15 headless with `shared/blender/kit.py`, the DEVBUILD self-test harness (`DevTools.cs`, `DevBelts.cs`).

**Spec:** `docs/superpowers/specs/2026-11-02-belt-boxes-design.md` (binding; its "Rulings" and "Left to the builder" sections apply). Recon: `research/SPLITTERS_RECON.md`.

## Global Constraints

- **Branches.** Library tasks (1, 2, 3, 6) commit on `structurelib-lib`; mod tasks (4, 5) commit on `beltboxes` (branched from `structurelib-lib`). Before a mod task: `git checkout beltboxes && git merge --no-edit structurelib-lib`. Never rebase or force-push. The spec and this plan live on `beltboxes`.
- Build per project (SDK 8; the `.slnx` needs SDK 9), game closed: `dotnet build src/StructureLib/StructureLib.csproj -c Debug -p:InstallToGame=true`, same for `examples/ExampleStructure/ExampleStructure.csproj` and `mods/BeltBoxes/BeltBoxes.csproj`; zero warnings. Unit tests: `dotnet test tests/StructureLib.Tests/StructureLib.Tests.csproj -c Debug` (136 green at start).
- Every game run: restore slot 2 from `C:\Users\ryguy\projects\moonsweep\.scratch\slot-2-backup-task11` first; kill only after `[selftest] done`; wait ≥ 15 s after a kill before touching the slot, ≥ 30 s before relaunching; restore the slot after; reset the cfg to the state in `.superpowers/sdd/2026-11-02-structurelib-belts-and-models/task-4-report.md` "Machine state" (every new switch off). Procedure and keys: that report and `C:\Users\ryguy\.claude\projects\C--Users-ryguy-projects-moonsweep\memory\garagelib-repo.md`. `model-card` needs `DumpCards = true`; chain runs need `SunOverhead = true`.
- Never prefix `MoonWorld.IsProcessor` or any small static leaf that gates vanilla kinds (`Routes` included); never call an `out`-struct method from managed code. Every prefix early-outs for vanilla kinds (`Remap.Live(kind) == null` → vanilla). Never write a modded kind into the vanilla save. Never log Steam ids.
- Self-test lines: `[selftest] PASS <name>` / `[selftest] FAIL <name>: <detail>` / `[selftest] done: <n> passed, <m> failed`; checks that need BeltBoxes skip with `Assert(name, false, "BeltBoxes is not registered")` only when the mod is present but broken, and log `not applicable` (no Assert) when it is simply not installed, like `SandChainChecks` does.
- Blender: `C:\Users\ryguy\projects\moonsweep\.downloads\blender-4.5\blender-4.5.15-windows-x64\blender.exe -b --factory-startup --python <script>`, one process per script, never the Blender MCP; `shared/blender/STRUCTURES.md` is the style page; each model ≤ 3,000 triangles.
- Commit per task; every commit message ends with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`. No board claims possible (board unreachable): hot-path edits (`GarageLib.slnx`, `modpack.json`, `README.md`, `src/StructureLib/**`, `shared/**`) are listed in the hand-off.
- Pure logic goes in `src/StructureLib/Pure/` with xunit tests; nothing in `Pure/` references Unity or the game.

## Review Focus

1. A canister heavier than 1 kg (a partial or packaged capsule) reaching a splitter: it must hop whole with its kilograms, or wait with a logged refusal; never shrink, split or vanish (Task 6 `beltboxes-heavy`).
2. An output belt removed while canisters are flowing: later canisters go to the remaining outputs and the one that was mid-hop is not lost (Task 6 `beltboxes-split-skips` removes the Forward belt).
3. A box removed with belts joined and a canister waiting at its input: no exception, the waiting canister stays on its belt, the belts stay built (Task 6 `beltboxes-remove-clean`).
4. A splitter with a single output joined behaves as a straight pass-through; a merger with one input likewise (Task 2 `PickOutput_SingleOutput`, `MergerAccepts_SingleInput`).
5. A mod declaring a router with a processor or power, or with two inputs on a splitter: refused at freeze with a message naming the structure, the rest of the mod still registers (Task 2 `CheckDefinition_RouterExcludesProcessorAndPower`, `CheckPorts_SplitterNeedsOneInput`).

---

### Task 1: Spike — donor, hop mechanism, calls from the prefix, side-port belts (dev tooling, no library behaviour)

**Branch:** `structurelib-lib`.

**Files:**
- Modify: `src/StructureLib/DevBelts.cs` (new `#if DEVBUILD` probes behind `[Dev]` switches), `src/StructureLib/DevTools.cs` (bind the switches), `examples/ExampleStructure/Plugin.cs` (a DEVBUILD-only structure behind `StructureLibDevFlag("RegisterBoxProbe")`, like `RegisterBadPorts`)
- Modify: `research/SPLITTERS_RECON.md` (new section "Spike facts (Task 1)")
- Test: in-game probe runs; no unit tests

**Interfaces:**
- Produces (facts, binding for Tasks 3–6): (a) the donor kind for the boxes, with each candidate's prefab components (views, collider count) for kinds 4, 10, 11, 93 and whether a clone of 11 with a hashed kind gets any vanilla routing or a cut-in offer when placed next to a belt; (b) `EmitCargo(outfeed, owner, cargo, 3.0)`: the kilograms and capacity of the canister it makes, and whether a following `MoveCanisterRecord(new MoonDroppedCanister(... kilograms 3, capacity 3 ...), position, up, rotation)` rewrites them with the rider intact (log the rider's belt and distance before and after); (c) `PlaceRider(id, outPath, 0f)` for a canister waiting at the end of belt A onto belt B's start, followed by `HandOff` answering true: does the canister ride B with one rider record, does `riders`/`beltQueues` keep a stale entry for A (log `RidersText` of both belts over 3 s); (d) `EntranceClear(belt)`, `WaitingAtEnd(belt)`, `BeltStartingAt(point)`, `BeltEndingAt(point)` called from inside the `HandOff` prefix: safe (no throw, no native crash over two loads) and their answers; (e) `AutoBelt` from a +Z side port to a +Z side port of the next structure on the line (a U-shaped belt): routable or not, and the plan's `length`.
- Produces: `[Dev] RegisterBoxProbe` (example mod: `ryanhartwig.example-box-probe`, `BaseStructure = 11`, no processor, `Ports` null so the donor's ports stay), `[Dev] HopProbe = emit|place` (DevBelts: 10 s after AutoBelt, puts a 3 kg grit canister at the end of the first AutoBelt belt and hops it onto the second belt by the chosen mechanism, logging (b) or (c)), `[Dev] PrefixProbe` (log-only calls of (d) inside the prefix, 1 line per belt).

- [ ] **Step 1: Bind the three switches and the probe structure**

`DevTools.Bind`: `RegisterBoxProbe` (bool), `HopProbe` (string, ""), `PrefixProbe` (bool). Example mod: the probe structure as in Interfaces, Category "Logistics", Name "Box probe".

- [ ] **Step 2: Donor facts (a)**

Config: `AutoBuildLayout = line`, `AutoBuild = 96,ryanhartwig.example-box-probe`, `AutoBelt = 96:output->ryanhartwig.example-box-probe:IN`, `RegisterBoxProbe = true`. In `DevBelts`, 5 s after AutoBuild, for kinds 4, 10, 11, 93 log `world.BuildDefinition(k)`'s prefab component names (walk `GetComponentsInChildren<Component>(true)`, distinct type names, collider count). For the probe depot log its view's components, whether `MoonMachineBelt` is present, and what happens to a dust canister at the end of its belt over 10 s (our prefix refuses it: expected `does not go in here`; note any `EnterMachine`-like movement). Record (a) and pick the donor per the spec's order (11 first).

- [ ] **Step 3: Hop facts (b) and (c)**

Config: `AutoBuild = 96,ryanhartwig.example-grinder,ryanhartwig.example-burner`, `AutoBelt = 96:output->ryanhartwig.example-grinder:Dust in;ryanhartwig.example-grinder:Grit out->ryanhartwig.example-burner:Grit in`, `AutoFeed` empty, `HopProbe = emit` then a second run with `place`. Log before/after as in Interfaces. Record (b), (c).

- [ ] **Step 4: Prefix call safety (d) and side belts (e)**

`PrefixProbe = true` through two world loads (fact (d)); then one run with `AutoBuild = 96,ryanhartwig.example-box-probe,ryanhartwig.example-box-probe` and `AutoBelt = ryanhartwig.example-box-probe#1:OUT_A->ryanhartwig.example-box-probe#2:IN` to see a non-straight route (the probe's ports flow +Z: extend `AutoBelt`'s endpoint syntax with `#n` to pick the n-th depot of a kind if it lacks one). Record (e) with the plan's `routable` and `length` lines.

- [ ] **Step 5: Write the recon section and commit**

`research/SPLITTERS_RECON.md` "Spike facts (Task 1)": (a)–(e) with the log lines. Commit: `StructureLib: box spike (donor, hop and prefix-call facts, side-port belts); RegisterBoxProbe, HopProbe, PrefixProbe dev switches`.

---

### Task 2: Router rules and selection (pure code + tests)

**Branch:** `structurelib-lib`.

**Files:**
- Create: `src/StructureLib/Pure/RouterRules.cs`
- Modify: `src/StructureLib/StructureDefinition.cs` (`Router`, `Footprint`, `RouterDefinition`, `RouterMode`, `CargoRouted`)
- Test: `tests/StructureLib.Tests/RouterRulesTests.cs`

**Interfaces:**
- Produces: `public sealed class RouterDefinition { public RouterMode Mode; }`, `public enum RouterMode { Splitter, Merger }`, `StructureDefinition.Router` (`RouterDefinition`, null = not a router), `StructureDefinition.Footprint` (`Vector2?`, full width along X and depth along Z in metres), `public sealed class CargoRouted { public StructureDefinition Definition; public int DepotId; public Cargo Cargo; public double Kilograms; public int FromBeltId, ToBeltId; }`.
- Produces `public static class RouterRules`:
  - `IReadOnlyList<string> CheckDefinition(string id, bool hasRouter, bool hasProcessor, bool hasPower, (float x, float y)? footprint)`: errors `'{id}': a router has no processor.`, `'{id}': a router has no power.`, `'{id}': footprint {x} x {y} must be positive.` (footprint checked for any structure that sets it).
  - `IReadOnlyList<string> CheckPorts(string id, RouterMode mode, IReadOnlyList<(string Name, bool Output)> ports)`: duplicate names; splitter exactly 1 input and 1–3 outputs; merger 1–3 inputs and exactly 1 output; messages name the id and the counts.
  - `int PickOutput(int nextOut, IReadOnlyList<bool> canTake)`: first index at or after `nextOut` (wrapping) with `canTake[i]`, else -1; `nextOut` out of range is treated as 0.
  - `int Advance(int used, int count)` = `(used + 1) % count`.
  - `bool MergerAccepts(int turn, int input, IReadOnlyList<bool> waiting, bool outputClear)`: false if `!outputClear`; walking `j = (turn + k) % count` for k = 0…count−1: `j == input` → true; `waiting[j]` → false.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void CheckDefinition_RouterExcludesProcessorAndPower() { var e = RouterRules.CheckDefinition("b", true, true, true, null); Assert.Equal(2, e.Count); }
[Fact] public void CheckDefinition_FootprintMustBePositive() { Assert.Single(RouterRules.CheckDefinition("b", false, false, false, (0f, 1.4f))); }
[Fact] public void CheckPorts_SplitterNeedsOneInput() { var e = RouterRules.CheckPorts("b", RouterMode.Splitter, new[] { ("A", false), ("B", false), ("Out", true) }); Assert.Single(e); Assert.Contains("'b'", e[0]); }
[Fact] public void CheckPorts_SplitterOneToThreeOutputs() { Assert.Empty(RouterRules.CheckPorts("b", RouterMode.Splitter, new[] { ("In", false), ("F", true), ("L", true), ("R", true) })); Assert.Single(RouterRules.CheckPorts("b", RouterMode.Splitter, new[] { ("In", false), ("F", true), ("L", true), ("R", true), ("X", true) })); }
[Fact] public void CheckPorts_MergerOneOutput() { Assert.Single(RouterRules.CheckPorts("b", RouterMode.Merger, new[] { ("L", false), ("Out", true), ("Out2", true) })); }
[Fact] public void PickOutput_RoundRobinSkipsBlocked() { Assert.Equal(2, RouterRules.PickOutput(1, new[] { true, false, true })); Assert.Equal(0, RouterRules.PickOutput(2, new[] { true, false, false })); Assert.Equal(-1, RouterRules.PickOutput(0, new[] { false, false })); }
[Fact] public void PickOutput_SingleOutput() { Assert.Equal(0, RouterRules.PickOutput(5, new[] { true })); }
[Fact] public void Advance_Wraps() { Assert.Equal(0, RouterRules.Advance(2, 3)); }
[Fact] public void MergerAccepts_YieldsToEarlierWaitingInput() { Assert.False(RouterRules.MergerAccepts(0, 2, new[] { true, false, true }, true)); Assert.True(RouterRules.MergerAccepts(0, 2, new[] { false, false, true }, true)); Assert.True(RouterRules.MergerAccepts(2, 2, new[] { true, true, true }, true)); }
[Fact] public void MergerAccepts_OutputBlocked() { Assert.False(RouterRules.MergerAccepts(0, 0, new[] { true }, false)); }
[Fact] public void MergerAccepts_SingleInput() { Assert.True(RouterRules.MergerAccepts(0, 0, new[] { true }, true)); }
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test tests/StructureLib.Tests/StructureLib.Tests.csproj -c Debug`: build error (`RouterRules` undefined).
- [ ] **Step 3: Implement the Interfaces** in `RouterRules.cs` and `StructureDefinition.cs` (doc comments in the file's style).
- [ ] **Step 4: Run the tests** — 147 passed (136 + 11), 0 failed.
- [ ] **Step 5: Commit** — `StructureLib: router rules and selection (pure), RouterDefinition, Footprint, CargoRouted`.

---

### Task 3: Routers in the library (freeze checks, footprint and colliders, the hop, the event)

**Branch:** `structurelib-lib`.

**Files:**
- Create: `src/StructureLib/Patches/RouterHop.cs` (`internal static class RouterHop`)
- Modify: `src/StructureLib/MoonStructures.cs:255-285` (freeze), `src/StructureLib/PortDefinition.cs` (`PortResolver.Resolve`: router ports get `Cargo.Dust`), `src/StructureLib/Injection.cs:81-113` (footprint), `src/StructureLib/ModelSwap.cs` (colliders), `src/StructureLib/Patches/BeltPatches.cs:164-171` (router branch), `:119-130` (`Fresh` resets `RouterHop`), `src/StructureLib/MoonStructures.cs` (event `CargoRouted`, `RaiseCargoRouted`), `src/StructureLib/Plugin.cs` `NeededMembers` (`MoonWorld:EntranceClear`, `WaitingAtEnd`, `BeltStartingAt`, `BeltEndingAt`, the hop call from Task 1 (b)/(c), `MoonDepotBuildDefinition:halfFootprint`), `src/StructureLib/README.md`
- Test: `tests/StructureLib.Tests/RouterRulesTests.cs` (one more test), an in-game smoke run

**Interfaces:**
- Consumes: Task 2's types and `RouterRules`; Task 1 facts (a) donor, (b)/(c) hop mechanism, (d) call safety.
- Produces: `RouterHop.Hop(MoonWorld world, StructureDefinition d, int depot, PortDefinition inPort, int inBelt, int id) : bool` (true = taken; false = waits), `RouterHop.Reset()`; freeze: a router's ports checked by `RouterRules.CheckPorts` (not `PortRules.CheckPorts`), every router port's `Cargo` set to `Cargo.Dust` before encoding, `RouterRules.CheckDefinition` errors drop the structure; `Injection.DefinitionFor` writes `clone.halfFootprint = Footprint / 2` before `ApplyBuildModel`; `ModelSwap.Build` with `Footprint` set destroys every `Collider` on the donor template and adds one `BoxCollider` (centre (0, h/2, 0), size (width, h, depth), h = the model's height from the holder bounds); the `MoonStructures.CargoRouted` event; the footprint warning uses the override when set.

- [ ] **Step 1: Unit test for the cargo rule** — `[Fact] public void RouterPorts_AreDust()` on a small pure helper `RouterRules.PortCargoId => "dust"` used by the resolver (keeps the rule testable).
- [ ] **Step 2: Freeze checks, footprint, colliders, event** as in Interfaces. Router ports from a model's `Port_*` nodes with no `cargo` property must resolve (today a null cargo id would fail the cargo check: bypass it for routers).
- [ ] **Step 3: `RouterHop.Hop`** — per the spec "Hop": per-depot `nextOut`/`turn` in dictionaries cleared by `Reset()`; output belts by `world.BeltStartingAt(view pose × port.Position)` (-1 = none), input belts by `BeltEndingAt`; take = `EntranceClear(belt)`; waiting = `WaitingAtEnd(belt)`; the move by Task 1's mechanism, kilograms/capacity/owner preserved (spec fallback order); on success raise `CargoRouted` and log once per depot; any throw → log once, answer false. `BeltPatches.Intake`: `if (d.Router != null) return RouterHop.Hop(world, d, j.Depot, j.Port, belt, id);` before the cargo check (the `Joined` record needs the belt id: add it).
- [ ] **Step 4: Build, unit tests, smoke run** — builds 0 warnings; tests 148/148. In game with `RegisterBoxProbe` repurposed: the example mod's probe becomes a real router (`Router = new RouterDefinition { Mode = RouterMode.Splitter }`, `Footprint = new Vector2(1.4f, 1.4f)`, declared `Ports` as the spec's splitter table, donor from fact (a)); config `AutoBuild = 96,ryanhartwig.example-box-probe,ryanhartwig.example-burner`, `AutoBelt = 96:output->…box-probe:In;…box-probe:Forward->ryanhartwig.example-burner:Grit in` (wrong cargo for the burner on purpose: dust waits there). Expect `CargoRouted` lines for dust from the probe, the clone's `halfFootprint (0.70, 0.70)` logged, one BoxCollider on the view, no exceptions, two clean loads.
- [ ] **Step 5: README** — "Routers" section (definition, port rules, round-robin, the two differences from vanilla, co-op standing, `CargoRouted`); support table rows. Commit: `StructureLib: routers (splitter/merger hop on the HandOff prefix), Footprint override with collider swap, CargoRouted`.

---

### Task 4: The BeltBoxes mod (declared ports first)

**Branch:** `beltboxes` (merge `structurelib-lib` first).

**Files:**
- Create: `mods/BeltBoxes/BeltBoxes.csproj`, `Plugin.cs`, `README.md` (via `.\new-package.ps1 -Kind mod -Name BeltBoxes -Author ryanhartwig -Title "Belt Boxes"`)
- Modify: `modpack.json` (package `BeltBoxes`, description), `GarageLib.slnx` (if the scaffolder did not), root `README.md` (table row + tree line), `CONTRIBUTING.md` only if it lists mods by name
- Test: build + install, one in-game run

**Interfaces:**
- Consumes: Task 3's `Router`, `Footprint`; Task 1 (a) donor.
- Produces: `com.ryanhartwig.beltboxes` 0.1.0; `ryanhartwig.box-splitter` and `ryanhartwig.box-merger` with the spec's names, notes, Category `Logistics`, price 25, `Footprint (1.4, 1.4)`, declared `Ports` per the spec tables (positions ±0.7, y 0.85, directions as listed), `Model` unset until Task 5.

- [ ] **Step 1: Scaffold and register** per Interfaces; README from the SandPower README's shape (ports table, the two vanilla differences, layout tips, co-op note, previews to come).
- [ ] **Step 2: Build and install** `mods/BeltBoxes/BeltBoxes.csproj` (0 warnings) and run: `AutoBuildLayout = line`, `AutoBuild = 96,ryanhartwig.example-grinder,ryanhartwig.box-splitter,ryanhartwig.box-merger,ryanhartwig.example-burner`, `AutoFeed = 96:Dust:12`, `AutoBelt = 96:output->ryanhartwig.example-grinder:Dust in;ryanhartwig.example-grinder:Grit out->ryanhartwig.box-splitter:In;ryanhartwig.box-splitter:Forward->ryanhartwig.box-merger:Back;ryanhartwig.box-merger:Out->ryanhartwig.example-burner:Grit in`, `SunOverhead = true`. Expect: both boxes registered with 4 ports each, `CargoRouted` from both, grit entering the burner, existing self-test 0 failed, no exceptions. Keep the log as `task-4-smoke.log` in the plan workspace.
- [ ] **Step 3: Commit** — `BeltBoxes: box splitter and box merger on StructureLib routers (declared ports; models follow)`.

---

### Task 5: Models (runs in parallel from Task 1 on, in a worktree on branch `beltboxes-models` off `beltboxes`)

**Files:**
- Create: `mods/BeltBoxes/blender/box_splitter.py`, `box_merger.py`, the two `.blend`, `preview_{splitter,merger}_{hero,side,front,rear}.png`, `mods/BeltBoxes/box_splitter.bin`, `box_merger.bin`
- Later (on `beltboxes`, after the merge): `mods/BeltBoxes/Plugin.cs` (`Model = "box_splitter.bin"` / `"box_merger.bin"`, `Ports = null`), `README.md` (previews, port table from the model)
- Test: `shared/blender/check_bin.py` on both files; the port lines each script prints; one in-game run

**Interfaces:**
- Consumes: the spec's "Models" form and port tables (Blender axes: Unity (x, y, z) = Blender (−x, z, −y); the spec's Unity +X is Blender −X, Unity +Z is Blender −Y; `kit.port(name, loc, direction, output, cargo, parent)` with `cargo=None` for a router port); `shared/blender/STRUCTURES.md`; `mods/SandPower/blender/briquette_generator.py` on branch `structurelib` as the reference script shape (`git show structurelib:mods/SandPower/blender/briquette_generator.py`).
- Produces: models whose printed `PORT` lines give, in Unity space, exactly the spec's positions (±0.01) and directions; `Port_In`, `Port_Forward`, `Port_Left`, `Port_Right` on the splitter and `Port_Left`, `Port_Back`, `Port_Right`, `Port_Out` on the merger; footprint guide 0.7 × 0.7, height ≤ 1.05; ≤ 3,000 triangles each.

- [ ] **Step 1: Scripts** — the box form from the spec (steel body, deck stubs on ported faces at 0.85, lid arrows per port, Logistics accent from the kit palette); asserts on bounds and triangles like the reference; print the PORT lines.
- [ ] **Step 2: Build both, review the previews with Read, iterate until the checklist in `STRUCTURES.md` passes**; run `check_bin.py` on both `.bin`.
- [ ] **Step 3: Commit on `beltboxes-models`** — `BeltBoxes: box splitter and box merger models`. The controller merges it into `beltboxes` after Task 4.
- [ ] **Step 4 (on `beltboxes`): switch the plugin to the models** (`Model` set, `Ports = null`), build, run the Task 4 config once: the log's `ports from model Port_* nodes` lines match the declared positions within 0.01 m, both models visible (renderers enabled on the holder), `model-card` renders. Commit: `BeltBoxes: ports from the models, previews in the README`.

---

### Task 6: Self-test chain, reload, docs, hand-off

**Branch:** `structurelib-lib` for the self-test code and library docs; `beltboxes` for the mod README and the handoff lines about the mod (merge after).

**Files:**
- Create: `src/StructureLib/DevBoxes.cs` (`#if DEVBUILD`, `partial class DevTools`/`SelfTest`, like `DevBelts.cs`)
- Modify: `src/StructureLib/DevTools.cs` (call `BoxChecks()` in `Run`, `TickBoxes()` in `Tick`, save wait includes `BoxPending`), `docs/superpowers/HANDOFF-structurelib-2026-11-01.md` (status rows, by-hand list, hot paths), `research/STRUCTURES_RECON.md` (pointer to `SPLITTERS_RECON.md`), `mods/BeltBoxes/README.md` (what the self-test proved, by-hand list)
- Test: two game runs (chain, reload) with BeltBoxes installed; one run without it

**Interfaces:**
- Consumes: `MoonStructures.CargoRouted`, `AutoBelter.Results`, `Spawn(world, path, d, cargo, out why)`, `RidersText`, `DevTools.RequestSave()`, `Sidecar` restore lists (as `TickReload` uses them), `world.RemoveStructureNow`, `world.WaitingAtEnd`.
- Produces: the checks named in the spec's Verification 1–7, exactly these names: `beltboxes-registered`, `beltboxes-split-even`, `beltboxes-split-skips`, `beltboxes-merge-fair`, `beltboxes-conserve`, `beltboxes-heavy`, `beltboxes-remove-clean`, `beltboxes-save-clean`, `beltboxes-reload`, `beltboxes-model`; config `[Dev] AutoBelt` entries for the full chain (Task 4's four plus `…box-splitter:Left->…box-merger:Left` and `…box-splitter:Right->…box-merger:Right`).

- [ ] **Step 1: Checks** — per the spec's definitions: `split-even` after ≥ 12 hops each output within hops/3 ± 1; `split-skips` removes the Forward belt (`world.RemoveConveyor` or the dev removal the belt tests use) and asserts the next 6 hops go Left/Right only and hops in == hops out; `merge-fair` pins a canister at each merger input (`Spawn` at each belt's end while the output entrance is held by a stopped canister, then release) and asserts the next 6 hops rotate L/B/R twice; `conserve` over the run; `heavy` spawns a 3 kg grit canister at the splitter input and asserts a `CargoRouted` with 3 kg or a logged wait with the canister still 3 kg; `remove-clean` removes the merger with a canister waiting at Back, asserts no exception, the canister still rides its belt, then rebuilds nothing (the run ends); `save-clean` as the spec; `reload` and `model` in the second run.
- [ ] **Step 2: Chain run** — Task 4's config plus the two side belts, `AutoSave = true`, `DumpCards = true`, `AutoRemove = false`. Expect `done: N passed, 0 failed` with every `beltboxes-*` PASS; keep the log.
- [ ] **Step 3: Reload run** — `AutoBuild`/`AutoFeed`/`AutoBelt` empty, the chain run's slot kept: `beltboxes-reload` (both boxes, five belts, canisters with cargo, a hop within 20 s) and `beltboxes-model` PASS.
- [ ] **Step 4: Library run without BeltBoxes** — uninstall the mod's plugin folder, run the Task 4 config minus the boxes: every `beltboxes-*` check logs `not applicable`, `done: … 0 failed`. Reinstall nothing (leave the game without BeltBoxes).
- [ ] **Step 5: Docs and commits** — library: `StructureLib: box self-test (chain, skipping, fairness, conservation, heavy canister, removal, save/reload)`; mod branch after merging: handoff rows (BeltBoxes PR, by-hand: belt tool at 0.7 m spacing, models in lighting, removal mid-traffic, co-op), README. Commit: `BeltBoxes: README, hand-off`.
