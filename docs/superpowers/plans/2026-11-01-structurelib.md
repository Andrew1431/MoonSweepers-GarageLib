# StructureLib + SandPower Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A BepInEx library (`src/StructureLib`) that lets mods add structures, belt-fed machines, power sources and cargo kinds to Moon Sweepers with a vanilla-clean save, plus its first consumer (`mods/SandPower`: sand compactor → sand briquettes → briquette generator).

**Architecture:** Mirror GarageLib: a registry of string-id definitions, Harmony prefixes on the game's static per-kind switches that answer for modded kinds (hashed from the id) or remap to a vanilla donor, and a save sidecar that strips modded records out of `MoonLooseDustSnapshot` on write and re-injects them on load. Pure logic (hashing, validation, sidecar codec) lives in `src/StructureLib/Pure/` with no game references and is unit-tested with xunit; everything that touches the game is verified by a dev-build in-game self-test that logs `[selftest] PASS|FAIL` lines.

**Tech Stack:** C# 12 on net6.0 (game interop), BepInEx 6.0.0-be.791 IL2CPP, HarmonyLib, Il2CppInterop, System.Text.Json; xunit on net8.0 for pure tests; PowerShell 7 for scripts.

**Spec:** `docs/superpowers/specs/2026-11-01-structurelib-design.md` (read it first; `research/STRUCTURES_RECON.md` holds the game facts it rests on).

## Global Constraints

- Build per project, not the `.slnx` (needs SDK 9.0.200+; machine has 8.0.421): `dotnet build <proj> -c Release`. `Directory.Build.props` sets net6.0, `ImplicitUsings` disabled, `Nullable` disabled, `DEVBUILD` defined unless `-p:DevBuild=false`, and copies the DLL to `BepInEx/plugins/<AssemblyName>/` unless `-p:InstallToGame=false`.
- Close the game before any build that installs (loaded DLLs are locked): `taskkill //IM MoonSweepers.exe //F`. Launch with `"C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers\MoonSweepers.exe"` directly (Steam launch does nothing on this machine). Log: `<game>\BepInEx\LogOutput.log`.
- GUIDs (permanent): StructureLib `com.ryanhartwig.structurelib`, SandPower `com.ryanhartwig.sandpower`, example `com.ryanhartwig.structurelib.examplestructure`. Structure ids `ryanhartwig.sand-compactor`, `ryanhartwig.briquette-generator`; cargo id `ryanhartwig.sand-briquette` with byte value `3`. Never change after release.
- Kind hash: `kind = 1_000_000 + (int)(Fnv1a32(id) % (uint)(int.MaxValue - 1_000_000))`. Collision = hard error naming both ids. `additionalStructures` is never grown.
- Cargo values are declared bytes in `3..255`; duplicates and values `< 3` are hard errors. `MoonCargo` is a byte.
- Never write a modded kind or cargo value into the vanilla save. Sidecar file: `structurelib.json` in the save slot folder (`MoonWorld.progressStore.Root`).
- Never log full Steam ids: reuse GarageLib's `Redact` (keep the `steam-…` prefix and last 4 digits).
- Registration only from `Load()`; registry freezes on the first `MoonWorld.RestoreDepots` prefix, not on the first frame.
- Donor rule: a `BaseStructure` whose definition `module` has `Deck`, `BuildingPart`, `CatwalkSupport` or `WorldGrid` true is refused at registration.
- Dev tooling must refuse to auto-load save slot-1 unless `[Dev] AllowSlot1 = true`. Tests run on slot-2 (a copy of slot-1 made in Task 1).
- Board protocol (CLAUDE.md): claim before editing; `modpack.json`, `CONTRIBUTING.md`, `Directory.Build.*`, `shared/**`, `src/GarageLib/**` are hot paths, claim exact files. The board was unreachable on 2026-11-01; if still unreachable, work without it and say so in the hand-off.
- Commit locally at the end of every task with the message given. Push only when Ryan asks. Commit messages end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Log source names: `StructureLib` (library), `SandPower`, `ExampleStructure`. Self-test lines are exactly `[selftest] PASS <name>` / `[selftest] FAIL <name>: <detail>` / `[selftest] done: <n> passed, <m> failed`.

**In-game check recipe** (used by every task with a `SelfTest` step; `G` = game folder):
```bash
taskkill //IM MoonSweepers.exe //F 2>/dev/null; sleep 3
dotnet build examples/ExampleStructure/ExampleStructure.csproj -c Release -nologo -v q   # builds StructureLib too, installs both
(cd "$G" && cmd //c start "" MoonSweepers.exe)
# [Dev] AutoLoadSlot = 2 and [Dev] SelfTest = true in BepInEx/config/com.ryanhartwig.structurelib.cfg
until grep -q "\[selftest\] done" "$G/BepInEx/LogOutput.log"; do sleep 2; done
grep "\[selftest\]" "$G/BepInEx/LogOutput.log"
```
Expected: every assertion named in the task's step appears as `PASS`, and the `done` line has `0 failed`.

## Review Focus

1. **A modded kind reaching an inlined `additionalStructures[kind - 1]` read** would throw `IndexOutOfRange` deep in vanilla with no StructureLib frame. Test: Task 7's self-test places, selects in the menu, removes and refunds a modded structure, and Task 8 saves and reloads it; any vanilla exception in the log between `[selftest] begin` and `done` fails the run (Task 4 adds that log scan to the self-test).
2. **Two mods declaring the same cargo byte** must leave neither registered and log both ids; otherwise one fuel silently becomes the other. Test in Task 3 (`CargoRules_DuplicateValue_ReportsBothIds`).
3. **Vanilla dust fed into a machine with no dust slot** must be refused (`InputSlot` = -1) with the vanilla message, never dropped into slot 0. Test in Task 10 (`selftest inputslot-refuses-dust`).
4. **A sidecar written while a mod is missing** must keep that mod's records byte-for-byte (orphans), and the vanilla save must list none of them. Test in Task 8 (`SidecarCodec_RoundTrip_KeepsOrphansVerbatim` and `selftest save-strips-modded`).
5. **A game update renaming a member** must disable the library with one clear log line rather than half-patch. Test in Task 4 (`MemberCheck_MissingMember_ReportsNameAndDisables`).

---

### Task 1: S0 behaviour spike (research, no library code)

**Files:**
- Modify: `research/spike-structures/Plugin.cs` (add `WorldProbe` class and config)
- Create: `research/tools/copy-slot.ps1`
- Modify: `research/STRUCTURES_RECON.md` ("Still open" section becomes "Answered by S0")

**Interfaces:**
- Produces: facts for Tasks 7, 10, 11: (a) where the built view GameObject is instantiated on build and on restore; (b) how `PowerNode`s are created and what `SolvePower` reads per node (`SunFactor`, `spec.peakKw`, `ChargeKwh`); (c) the `FinishCycle` mass rule; (d) whether `EmitCargo` needs a connected outfeed belt to emit; (e) whether `TryBuildDepot(builder, direction, yaw, out reason, null, kind)` accepts a null check.

- [ ] **Step 1: Write `research/tools/copy-slot.ps1 -From 1 -To 2`**

Copies `%USERPROFILE%\AppData\LocalLow\DragDropGames\MoonSweepers\MoonSessions\slot-<From>` to `slot-<To>` with `robocopy /E`, refusing if the target exists unless `-Force`. Run it: `.\research\tools\copy-slot.ps1 -From 1 -To 2`. Expected: `slot-2` exists with the same files as `slot-1`.

- [ ] **Step 2: Add `[Dev] AutoLoadSlot` (int, default -1) to the spike**

Copy `GarageLib/DevTools.LoadSlot2` into the spike as `AutoLoad(int slot)`, generalised to `/slot-<n>`, refusing slot 1. Replace the `AutoStartSlot` logic (it never worked) with this.

- [ ] **Step 3: Add `WorldProbe` to the spike: build a rig and log it**

20 s after `MoonSession.InGame`: find the local `MoonWalker` (`MoonWalker.Local` or `FindFirstObjectByType<MoonWalker>`), `world.DevAddMoney(player, 20000m)`, then place at `walker.transform.position + walker.transform.forward * 12` on the sphere (`direction = position.normalized`): `TryBuildDepot(walker, direction, 0f, out reason, null, MoonWorld.SolarTwinKind)` and, 10 m further, `MoonWorld.PressureChamberKind`. Log `(ok, reason)` for each. If `null` for the check is refused, run `BeginDepotCheck(direction, yaw, kind)` and step `DepotPlacementSteps(check)` via `MoveNext()` until `check.Done`, then pass it. Record which worked in the recon note (fact e). Then every second for 120 s log each `MoonProductionState` (`Id, Kind, Running, Progress, Power, DemandKw, UsedKw, Grid, ChargeKwh, SuppliedKw, BatteryKw, SunFactor, Daylight`) and each `MoonPowerGrid`. At t+30 s call `world.CrewProcessorLoad(player, chamberId, MoonCargo.Dust, 10)` and log `stores` per slot before and after each cycle.

- [ ] **Step 4: Add traces for the hook candidates**

Postfixes logging arguments and `__result` (bounded, 20 each): `MoonWorld.RevealBuilt(GameObject view, int belt)`, `MoonBuildReveal` `Awake`/`Start`, `MoonDepotConstruction` `Awake`, `MoonProductionView.Setup(world, id)`, `MoonWorld.PowerCandidates`, `MoonWorld.SolvePower` (log `powerNodes.Count` and first node's `panel`, `spec.name`, `state.SunFactor`, `state.SuppliedKw`), `MoonWorld.FinishCycle` (log every slot's total kg before and after via a prefix/postfix pair), `MoonWorld.EmitProduct`, `MoonWorld.EmitCargo`, `MoonWorld.NewProduction`, `MoonWorld.SnapshotCanisters` / `RestoreCanisters` (count and each canister's `cargo`).

- [ ] **Step 5: Run it**

Build with `-p:InstallToGame=true`, set `AutoLoadSlot = 2` in `BepInEx/config/com.ryanhartwig.ms.structurespike.cfg`, launch, wait for `[probe] done`. Expected: log contains `[probe] built solar twin ok=True`, `[probe] built chamber ok=True`, at least one `[trace] FinishCycle` pair, at least one `[trace] SolvePower`.

- [ ] **Step 6: Write the answers into `research/STRUCTURES_RECON.md`**

Replace "Still open" with "Answered by S0 (date)": one bullet per fact (a) to (e), each naming the method and the observed values. If (a) or (b) has no clean hook, say which fallback from the spec applies.

- [ ] **Step 7: Commit**

```bash
git add research/spike-structures research/tools/copy-slot.ps1 research/STRUCTURES_RECON.md
git commit -m "research: S0 behaviour spike for structures, production and power"
```

---

### Task 2: Project scaffolding and `KindAllocator`

**Files:**
- Create: `src/StructureLib/StructureLib.csproj`, `src/StructureLib/Pure/Fnv1a.cs`, `src/StructureLib/Pure/KindAllocator.cs`
- Create: `tests/StructureLib.Tests/StructureLib.Tests.csproj`, `tests/StructureLib.Tests/KindAllocatorTests.cs`
- Create: `examples/ExampleStructure/ExampleStructure.csproj` (empty `Plugin.cs` comes in Task 4)

**Interfaces:**
- Produces: `static uint Fnv1a.Hash32(string s)` (UTF-8 bytes, offset basis 0x811C9DC5, prime 16777619); `static int KindAllocator.KindOf(string id)`; `const int KindAllocator.FirstModdedKind = 1_000_000`; `static IReadOnlyDictionary<string,int> KindAllocator.Assign(IEnumerable<string> ids, Func<string,int> kindOf = null)` throwing `KindCollisionException(string idA, string idB, int kind)`; `static bool KindAllocator.IsModded(int kind)` (`kind >= FirstModdedKind`).

- [ ] **Step 1: Create the three project files**

`src/StructureLib/StructureLib.csproj`: same as GarageLib's (AssemblyName/RootNamespace `StructureLib`, `GenerateDocumentationFile`). `examples/ExampleStructure/ExampleStructure.csproj`: like `ExampleVehicle.csproj` with a `ProjectReference` to `$(RepoRoot)src\StructureLib\StructureLib.csproj` `Private="false"`. `tests/StructureLib.Tests/StructureLib.Tests.csproj`: `TargetFramework` `net8.0` (overrides the props), `InstallToGame` `false`, `IsPackable` false, packages `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, and `<Compile Include="$(RepoRoot)src\StructureLib\Pure\*.cs" LinkBase="Pure" />` (the Rotorcraft shared-source pattern; the test project never references the game DLLs, so remove the inherited interop `Reference` items with `<ItemGroup><Reference Remove="@(Reference)" /></ItemGroup>` and set a property `SkipGameDirCheck=true`; add `Condition="'$(SkipGameDirCheck)' != 'true'"` to the `CheckGameDir` target in `Directory.Build.targets`).

- [ ] **Step 2: Write the failing tests**

```csharp
[Fact] public void Fnv1a_KnownVectors() { Assert.Equal(0x811C9DC5u, Fnv1a.Hash32("")); Assert.Equal(0xE40C292Cu, Fnv1a.Hash32("a")); }
[Fact] public void KindOf_IsHashFoldedIntoRange() { Assert.Equal(1_680_518_573, KindAllocator.KindOf("a")); }
[Fact] public void KindOf_NeverBelowFirstModdedKind() { foreach (var id in new[] { "", "a", "ryanhartwig.sand-compactor", "zzz" }) Assert.InRange(KindAllocator.KindOf(id), 1_000_000, int.MaxValue); }
[Fact] public void KindOf_IsDeterministicAndOrderIndependent() { Assert.Equal(KindAllocator.KindOf("x.one"), KindAllocator.Assign(new[] { "x.two", "x.one" })["x.one"]); }
[Fact] public void Assign_Collision_NamesBothIds() { var e = Assert.Throws<KindCollisionException>(() => KindAllocator.Assign(new[] { "a", "b" }, _ => 1_000_001)); Assert.Contains("a", e.Message); Assert.Contains("b", e.Message); Assert.Equal(1_000_001, e.Kind); }
[Fact] public void IsModded_SplitsAtFirstModdedKind() { Assert.False(KindAllocator.IsModded(192)); Assert.False(KindAllocator.IsModded(1000)); Assert.True(KindAllocator.IsModded(1_000_000)); }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/StructureLib.Tests -c Release`. Expected: build error, `Fnv1a` not defined.

- [ ] **Step 4: Implement `Fnv1a.Hash32` and `KindAllocator`**

`KindOf`: `FirstModdedKind + (int)(Fnv1a.Hash32(id) % (uint)(int.MaxValue - FirstModdedKind))`. `Assign` groups by kind and throws on the first group with two ids, ordinal-sorted so the message is stable.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/StructureLib.Tests -c Release`. Expected: 6 passed. Also `dotnet build src/StructureLib -c Release -p:InstallToGame=false`. Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/StructureLib tests/StructureLib.Tests examples/ExampleStructure Directory.Build.targets
git commit -m "StructureLib: project scaffolding, FNV-1a kind allocator with tests"
```

---

### Task 3: Definitions, registry and validation rules

**Files:**
- Create: `src/StructureLib/StructureDefinition.cs`, `src/StructureLib/MoonStructures.cs`, `src/StructureLib/Pure/Rules.cs`
- Test: `tests/StructureLib.Tests/RulesTests.cs`

**Interfaces:**
- Produces (public API, exactly as the spec's sample): `sealed class StructureDefinition { string Id, Name, Category, Note; decimal Price; int BaseStructure; byte[] CardImagePng; string CardImageKey; Func<ModelContext,GameObject> BuildModel; Action<GameObject,ModelContext> DecorateModel; ProcessorDefinition Processor; PowerDefinition Power; int Kind {get; internal set;} = -1; string Owner {get; internal set;} }`; `sealed class ProcessorDefinition { float RunningKw, CycleSeconds; List<Slot> Slots; bool ProducesPower; }`; `struct Slot { SlotRole Role; string Name; Cargo Cargo; double Capacity; static Slot Input/Batch/Output(string name, Cargo cargo, double capacity); }` with `enum SlotRole { Input, Batch, Output }`; `sealed class PowerDefinition { float PeakKw, BatteryKwh, Range; SupplyMode SupplyMode = SupplyMode.Fuel; }` with `enum SupplyMode { Solar, Fuel }`; `sealed class CargoDefinition { string Id; byte Value; string Name, ShortName; double UnitKilograms; decimal Price; Color? CanisterTint; }`; `readonly struct Cargo { byte Value; string Id; static Cargo Dust/Sand/Stone; MoonCargo ToMoon(); }`; `sealed class ModelContext { StructureDefinition Definition; Transform Parent; MoonDepotBuildDefinition Donor; }`.
- `static class MoonStructures { StructureDefinition Register(StructureDefinition, string ownerGuid); Cargo RegisterCargo(CargoDefinition, string ownerGuid); bool Frozen; IReadOnlyCollection<StructureDefinition> All; IReadOnlyCollection<CargoDefinition> AllCargo; bool IsModded(int kind); StructureDefinition Definition(int kind); StructureDefinition Definition(string id); bool IsModded(MoonCargo); CargoDefinition CargoDefinition(MoonCargo); event Action<StructureBuilt> Built; event Action<int> Removed; event Action<CycleFinished> CycleFinished; internal void Freeze(Func<int, DonorTraits> donorLookup); }`.
- Pure: `record struct DonorTraits(bool Exists, bool Deck, bool BuildingPart, bool CatwalkSupport, bool WorldGrid)`; `static class Rules { IReadOnlyList<string> CheckCargo(IEnumerable<(string Id, byte Value)>); IReadOnlyList<string> CheckProcessor(string id, IReadOnlyList<(SlotRole Role, double Capacity)> slots, bool producesPower, int maxSlots = 8); string CheckDonor(string id, int baseKind, DonorTraits traits); }` returning human-readable error strings (empty list / null = ok).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void CargoRules_DuplicateValue_ReportsBothIds() { var e = Rules.CheckCargo(new[] { ("a.x", (byte)3), ("b.y", (byte)3) }); Assert.Single(e); Assert.Contains("a.x", e[0]); Assert.Contains("b.y", e[0]); Assert.Contains("3", e[0]); }
[Fact] public void CargoRules_ValueBelow3_Rejected() { Assert.Contains(Rules.CheckCargo(new[] { ("a.x", (byte)2) }), m => m.Contains("a.x") && m.Contains("3..255")); }
[Fact] public void CargoRules_Valid_NoErrors() { Assert.Empty(Rules.CheckCargo(new[] { ("a.x", (byte)3), ("b.y", (byte)255) })); }
[Fact] public void ProcessorRules_MoreThan8Slots_Rejected() { var slots = Enumerable.Repeat((SlotRole.Input, 1.0), 9).ToList(); Assert.Contains(Rules.CheckProcessor("p", slots, false), m => m.Contains("8")); }
[Fact] public void ProcessorRules_NeedsOneOutputUnlessPower() { Assert.NotEmpty(Rules.CheckProcessor("p", new[] { (SlotRole.Input, 1.0), (SlotRole.Batch, 1.0) }, false)); Assert.Empty(Rules.CheckProcessor("p", new[] { (SlotRole.Input, 1.0), (SlotRole.Batch, 1.0) }, true)); Assert.NotEmpty(Rules.CheckProcessor("p", new[] { (SlotRole.Input, 1.0), (SlotRole.Batch, 1.0), (SlotRole.Output, 1.0) }, true)); }
[Fact] public void ProcessorRules_NeedsInputAndBatch() { Assert.NotEmpty(Rules.CheckProcessor("p", new[] { (SlotRole.Output, 1.0) }, false)); }
[Fact] public void DonorRules_FoundationLikeRefused() { Assert.NotNull(Rules.CheckDonor("s", 101, new DonorTraits(true, Deck: true, false, false, false))); Assert.NotNull(Rules.CheckDonor("s", 74, new DonorTraits(true, false, BuildingPart: true, false, false))); Assert.NotNull(Rules.CheckDonor("s", 0, new DonorTraits(false, false, false, false, false))); Assert.Null(Rules.CheckDonor("s", 97, new DonorTraits(true, false, false, false, false))); }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/StructureLib.Tests -c Release`. Expected: build error, `Rules` not defined.

- [ ] **Step 3: Implement `Pure/Rules.cs`, then `StructureDefinition.cs` and `MoonStructures.cs`**

`Register`: null/blank id → `ArgumentException`; after freeze → log error and return; duplicate id → log error, return the first; `Processor` present → `Rules.CheckProcessor`, any error → log and drop the processor. `RegisterCargo`: same shape; value rules are checked across all cargo at `Freeze` (pairwise), so `RegisterCargo` only checks `3..255` and duplicate id immediately. `Freeze(donorLookup)`: `KindAllocator.Assign` over all ids (collision → log error with both ids and disable the library via `MoonStructures.Disabled = true`); per definition `Rules.CheckDonor` with `donorLookup(def.BaseStructure)` (error → drop that definition, log); `Rules.CheckCargo` over all cargo (any error → drop every cargo named in the errors, log). Log one summary line like GarageLib's "Registry frozen".

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/StructureLib.Tests -c Release`. Expected: 13 passed. `dotnet build src/StructureLib -c Release -p:InstallToGame=false`: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/StructureLib tests/StructureLib.Tests
git commit -m "StructureLib: definitions, registry and validation rules"
```

---

### Task 4: Plugin skeleton, member check, freeze at world load, self-test harness, example mod

**Files:**
- Create: `src/StructureLib/Plugin.cs`, `src/StructureLib/CoreLog.cs` (copy of GarageLib's `CoreLog` + `CoreState`, namespace `StructureLib`), `src/StructureLib/Pure/MemberCheck.cs`, `src/StructureLib/DevTools.cs`, `src/StructureLib/Patches/FreezePatches.cs`
- Create: `examples/ExampleStructure/Plugin.cs`
- Test: `tests/StructureLib.Tests/MemberCheckTests.cs`

**Interfaces:**
- Produces: `Plugin.Guid = "com.ryanhartwig.structurelib"`, `Plugin.Version = "0.1.0"`; `static class MemberCheck { IReadOnlyList<string> Missing(IEnumerable<string> needed, Func<string,string,bool> hasMember) }` where `needed` entries are `"Namespace.Type:Member"`; `DevTools` config `[Dev] AutoLoadSlot` (int, -1), `AllowSlot1` (bool, false), `SelfTest` (bool, false), `AutoBuild` (string, comma list of structure ids or vanilla kind numbers), `AutoFeed` (string, `"<structureId>:<cargoId|Dust|Sand|Stone>:<kg>"`); `static class SelfTest { void Assert(string name, bool ok, string detail = null); void Begin(); void Done(); }` logging the exact lines from Global Constraints, plus a log scan that fails the run if any line matching `Exception|Error  :` appears between `begin` and `done`.
- Example mod registers `ryanhartwig.example-lamp` (`BaseStructure = 4` floodlight, `Price = 50m`, `Category = "Services"`, `Note = "StructureLib example."`) and, from Task 10 on, `ryanhartwig.example-grinder`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public void MemberCheck_MissingMember_ReportsNameAndDisables() { var missing = MemberCheck.Missing(new[] { "A.B:C", "A.B:D" }, (t, m) => m == "C"); Assert.Equal(new[] { "A.B:D" }, missing); }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/StructureLib.Tests -c Release`. Expected: build error.

- [ ] **Step 3: Implement `MemberCheck`, `Plugin`, `CoreLog`, `FreezePatches`, `DevTools`, example `Plugin.cs`**

`Plugin.Load()` order, as GarageLib: bind config; `MemberCheck.Missing` over `MoonWorld:BuildDefinition, MoonWorld:RestoreDepots, MoonWorld:TryBuildDepot, MoonWorld:IsProcessor, MoonWorld:IsPowerNode, MoonWorld:Processor, MoonWorld:InputSlot, MoonWorld:CargoName, MoonWorld:CargoValue, MoonDepotSave:Write, MoonProductionSave:Write, MoonDepotBuilder:baseStructures, MoonDepotBuilder:CardArt, MoonDepotBuilder:ProductionNote` (any missing → one error line, return); register injected types; patch groups one at a time with try/catch and a count line; add `CoreDriver`. `FreezePatches`: prefix on `MoonWorld.RestoreDepots(MoonLooseDustSnapshot)` that, if not frozen, calls `MoonStructures.Freeze(kind => DonorTraits from __instance.BuildDefinition(kind)?.module)`. `DevTools.Tick`: `AutoLoadSlot` as the spike's `AutoLoad` (refuses slot 1 without `AllowSlot1`); 20 s after in-game run `SelfTest` if enabled. First assertions: `registry-frozen` (`MoonStructures.Frozen`), `example-kind-hashed` (`Definition("ryanhartwig.example-lamp").Kind == KindAllocator.KindOf("ryanhartwig.example-lamp")`).

- [ ] **Step 4: Run tests to verify they pass; run the in-game check**

Run: `dotnet test tests/StructureLib.Tests -c Release`. Expected: 14 passed. Then the in-game check recipe. Expected: `PASS registry-frozen`, `PASS example-kind-hashed`, `done: 2 passed, 0 failed`, and a `StructureLib 0.1.0 loaded (N patch groups, 0 failed)` line.

- [ ] **Step 5: Commit**

```bash
git add src/StructureLib examples/ExampleStructure tests/StructureLib.Tests
git commit -m "StructureLib: plugin skeleton, member check, freeze at world load, self-test harness"
```

---

### Task 5: Definition clones, `BuildDefinition` prefix and per-kind remaps

**Files:**
- Create: `src/StructureLib/Injection.cs`, `src/StructureLib/Patches/DefinitionPatches.cs`
- Modify: `src/StructureLib/DevTools.cs` (assertions)

**Interfaces:**
- Produces: `static class Injection { MoonDepotBuildDefinition DefinitionFor(StructureDefinition d, MoonWorld world); void Ensure(MoonWorld world); }` (clone cached per definition per world; `ScriptableObject.Instantiate(donor)` then `displayName = d.Name`, `priceDollars = (int)d.Price`, `name = "StructureLib_" + d.Id`); `static class Remap { bool ToBase(ref int kind, string what) }` as GarageLib's.
- Patches (all `[HarmonyPrefix]`, all guarded by `MoonStructures.Frozen && IsModded(kind)`): `MoonWorld.BuildDefinition(int kind)` → `__result = DefinitionFor(def, __instance); return false`; remap to `BaseStructure` for `HexPieceKind`, `IsEdgeRail`, `IsWorldHex`, `IsWorldSquare`, `IsWorldGrid`, `IsRobotDock`, `MapKindOf`, `FarSpec`, `IsSolarPanel`, `IsProcessor`, `IsPowerNode`, `PowerRange` (the last four are replaced by real answers in Tasks 10 and 11; for now remap); `MoonDepotBuilder.CanBuild(int kind)` → true; `MoonDepotBuilder.ProductionNote(int kind)` → `def.Note`; `MoonWorld.AcceptsText(int kind)` → `""` for now.

- [ ] **Step 1: Add the failing self-test assertions**

`builddef-modded-nonnull`: `world.BuildDefinition(lamp.Kind) != null`; `builddef-modded-distinct`: `.Pointer != world.BuildDefinition(4).Pointer`; `builddef-modded-named`: `.displayName == "Example lamp"` and `.priceDollars == 50`; `builddef-unknown-null`: `world.BuildDefinition(1_000_000 + 12345) == null` (an unregistered hashed kind falls through to vanilla, which returns null, no exception); `canbuild-modded`: `builder.CanBuild(lamp.Kind)`; `note-modded`: `builder.ProductionNote(lamp.Kind) == "StructureLib example."`.

- [ ] **Step 2: Run the in-game check to verify they fail**

Expected: `FAIL builddef-modded-nonnull` (vanilla returns null for kinds above 192).

- [ ] **Step 3: Implement `Injection` and `DefinitionPatches`**

- [ ] **Step 4: Run the in-game check to verify they pass**

Expected: all six `PASS`, `0 failed`.

- [ ] **Step 5: Commit**

```bash
git add src/StructureLib
git commit -m "StructureLib: cloned definitions, BuildDefinition prefix, per-kind remaps"
```

---

### Task 6: Build-menu catalog, categories and card art

**Files:**
- Create: `src/StructureLib/IconRenderer.cs` (copy GarageLib's; model source = the donor prefab instantiated under the stage, `DecorateModel` applied), `src/StructureLib/Patches/CatalogPatches.cs`
- Modify: `src/StructureLib/Injection.cs` (`EnsureCatalog`), `src/StructureLib/DevTools.cs`

**Interfaces:**
- Produces: `Injection.EnsureCatalog()`: grows the static `MoonDepotBuilder.baseStructures` with `new CatalogEntry(def.Kind, def.Name, def.Category, "structurelib:" + def.Id)` per modded definition (idempotent: skip kinds already present) and appends unknown `def.Category` strings to `MoonDepotBuilder.catalogCategories`; called from `Ensure`. `IconRenderer.Get(StructureDefinition) -> Sprite` (null until rendered; PNG from `CardImagePng` wins; `CardImageKey` means "use vanilla art with that key", no render).
- Patches: `[HarmonyPrefix] MoonDepotBuilder.CardArt(string image)`: if `image` starts with `"structurelib:"`, `__result = IconRenderer.Get(def) ?? vanilla art of the donor's catalog image`, `return false`. `[HarmonyPostfix] MoonDepotBuilder.BuildCatalog`: log the instance `structureEntries.Length` and whether every modded kind is present.

- [ ] **Step 1: Add the failing self-test assertions**

`catalog-has-modded`: `MoonDepotBuilder.baseStructures.Any(e => e.kind == lamp.Kind && e.category == "Services")`; `catalog-instance-has-modded`: after `builder.RefreshCatalog()`, `builder.structureEntries.Any(e => e.kind == lamp.Kind)`; `cardart-modded`: `builder.CardArt("structurelib:ryanhartwig.example-lamp") != null` (wait up to 5 s for the renderer).

- [ ] **Step 2: Run the in-game check to verify they fail**

Expected: `FAIL catalog-has-modded`.

- [ ] **Step 3: Implement `EnsureCatalog`, `CatalogPatches`, `IconRenderer`**

- [ ] **Step 4: Run the in-game check to verify they pass; manual look**

Expected: three `PASS`. Then by hand in slot-2: open the build menu, Services tab shows "Example lamp" with a rendered picture and the note; note the result in the hand-off as checked or unchecked.

- [ ] **Step 5: Commit**

```bash
git add src/StructureLib
git commit -m "StructureLib: build-menu catalog entries, categories and card art"
```

---

### Task 7: Placement, build, model hook, removal

**Files:**
- Create: `src/StructureLib/Patches/ModelPatches.cs`, `src/StructureLib/Patches/BuildPatches.cs`
- Modify: `src/StructureLib/DevTools.cs` (`AutoBuild`), `src/StructureLib/MoonStructures.cs` (raise `Built`, `Removed`)

**Interfaces:**
- Consumes: Task 1 facts (a) and (e).
- Produces: `sealed class StructureBuilt { StructureDefinition Definition; int DepotId; MoonDepotSnapshot Record; GameObject View; }`; `DevTools.AutoBuild`: for each entry, place 8 m apart in front of the local walker using the method Task 1 proved (`TryBuildDepot` with `null` or a stepped `DepotPlacementCheck`), after `DevAddMoney` if the wallet is short; log `[dev] built <id|kind> depot=<id> ok=<bool> reason='<reason>'`. Patches: postfix on the view-instantiation hook from fact (a) → if the depot's kind is modded, apply `BuildModel` (replace children) or `DecorateModel`, name the root `"ModStructure_" + id`, raise `Built`; postfix `MoonWorld.TryBuildDepot` to log modded builds; postfix `MoonWorld.RemoveStructureNow(int id)` to raise `Removed` for modded kinds; `StructureRefund` is left to vanilla (it reads the cloned definition's price).

- [ ] **Step 1: Add the failing self-test assertions**

With `AutoBuild = ryanhartwig.example-lamp`: `build-modded-ok` (the `[dev] built` line reported ok); `build-modded-depot-kind` (`world.depots` contains a record with `kind == lamp.Kind`); `build-modded-view-decorated` (a GameObject named `ModStructure_ryanhartwig.example-lamp` exists; the example's `DecorateModel` adds a child `"ExampleLampTag"`); `remove-modded-refund` (`world.StructureRefund(id)` equals `50m` or vanilla's refund fraction of 50, record the fraction; then `RemoveStructureNow(id)` and the record is gone); `no-vanilla-exceptions` (the log scan from Task 4).

- [ ] **Step 2: Run the in-game check to verify they fail**

Expected: `FAIL build-modded-view-decorated` at least.

- [ ] **Step 3: Implement `AutoBuild`, `ModelPatches`, `BuildPatches`**

- [ ] **Step 4: Run the in-game check to verify they pass; manual placement**

Expected: five `PASS`. By hand: select Example lamp in the menu, place it with the real ghost, remove it with the real remove tool. Record in the hand-off.

- [ ] **Step 5: Commit**

```bash
git add src/StructureLib
git commit -m "StructureLib: placement via dev AutoBuild, model hook, build/remove events"
```

---

### Task 8: Depot sidecar (save safety)

**Files:**
- Create: `src/StructureLib/Pure/SidecarModel.cs`, `src/StructureLib/Pure/SidecarCodec.cs`, `src/StructureLib/Sidecar.cs`, `src/StructureLib/Patches/SavePatches.cs`
- Test: `tests/StructureLib.Tests/SidecarCodecTests.cs`
- Modify: `src/StructureLib/DevTools.cs` (`AutoSave`, assertions)

**Interfaces:**
- Produces (pure): `sealed class SidecarFile { int Version = 1; string Written; List<StructureRecord> Structures; List<ProductionRecord> Production; List<CanisterRecord> Canisters; }`; `sealed class StructureRecord { string Id; int DepotId; string Owner; float X,Y,Z,Yaw; bool PlacedPose; float Qx,Qy,Qz,Qw; int Support; double Stored; int Vehicles; int Style; List<ShareRecord> Contributions; }`; `sealed class ShareRecord { string Owner; double Amount; }`; `ProductionRecord` and `CanisterRecord` are defined now (fields per the spec's "Left to the builder" sidecar schema, cargo as string id) and used in Tasks 9 and 10; `static class SidecarCodec { string Serialize(SidecarFile f); SidecarFile Deserialize(string json); SidecarFile Merge(SidecarFile live, SidecarFile loaded, Func<string,bool> idIsRegistered) }` where `Merge` keeps every record of `loaded` whose `Id` (or cargo id) is not registered, verbatim, and takes `live` for the rest.
- Game side: `static class Sidecar { void LoadFor(string root, IntPtr snapshot); void InjectDepots(MoonWorld, MoonLooseDustSnapshot); List<MoonDepotSnapshot> StripDepots(MoonLooseDustSnapshot); void PutBackDepots(MoonLooseDustSnapshot, List<MoonDepotSnapshot>); }` mirroring GarageLib's `Sidecar.cs` (gate, `.tmp` + `.bak`, unreadable file moved aside, root from `CoreState.SaveRoot`). `SavePatches`: `MoonDepotSave.Write` prefix/finalizer pair, `MoonWorld.RestoreDepots` prefix (after the freeze prefix; use `[HarmonyPriority(Priority.Low)]` on this one) that injects. Depot ids: keep the saved `DepotId`; if `snapshot.depots` already has that id, assign `snapshot.nextDepotId++` and log.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void SidecarCodec_RoundTrip_KeepsOrphansVerbatim() { var loaded = new SidecarFile { Structures = { new StructureRecord { Id = "gone.mod", DepotId = 7, Owner = "steam-1", X = 1, Stored = 2.5 } } }; var live = new SidecarFile { Structures = { new StructureRecord { Id = "here.mod", DepotId = 9 } } }; var merged = SidecarCodec.Merge(live, loaded, id => id == "here.mod"); var back = SidecarCodec.Deserialize(SidecarCodec.Serialize(merged)); Assert.Equal(2, back.Structures.Count); var orphan = back.Structures.Single(s => s.Id == "gone.mod"); Assert.Equal(7, orphan.DepotId); Assert.Equal(2.5, orphan.Stored); Assert.Equal("steam-1", orphan.Owner); }
[Fact] public void SidecarCodec_Merge_LiveReplacesRegistered() { var loaded = new SidecarFile { Structures = { new StructureRecord { Id = "here.mod", DepotId = 1 } } }; var live = new SidecarFile(); var merged = SidecarCodec.Merge(live, loaded, _ => true); Assert.Empty(merged.Structures); }
[Fact] public void SidecarCodec_Deserialize_Garbage_Throws() { Assert.ThrowsAny<Exception>(() => SidecarCodec.Deserialize("{not json")); }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/StructureLib.Tests -c Release`. Expected: build error.

- [ ] **Step 3: Implement the pure codec, then `Sidecar.cs` and `SavePatches.cs`**

- [ ] **Step 4: Run tests; add self-test assertions and run the in-game check**

Run: `dotnet test tests/StructureLib.Tests -c Release`. Expected: 17 passed. Self-test with `AutoBuild = ryanhartwig.example-lamp` and `AutoSave = true` (request a save 10 s after build, as GarageLib's dev tool does): `save-strips-modded` (prefix saw a record with the modded kind and the finalizer put it back: `world.depots` still has it after save); `sidecar-written` (`structurelib.json` exists in `CoreState.SaveRoot` and `Deserialize` finds `Id == "ryanhartwig.example-lamp"`); `vanilla-save-clean` (read the slot's vanilla save with `MoonDepotSave`-free means is not possible, so assert on the stripped list instead: `strippedCount == moddedCount` and the vanilla `state.depots.Count` during `Write` equals `world.depots.Count - moddedCount`); `reload-restores` (second run with `AutoBuild` empty: `world.depots` contains the modded kind after load and `Built` fired with `View != null`); `orphan-kept` (run once with ExampleStructure removed from plugins: log shows `kept in the sidecar untouched`, vanilla loads, and the file still has the record). Expected: all `PASS` across the three runs; record each run in the hand-off.

- [ ] **Step 5: Commit**

```bash
git add src/StructureLib tests/StructureLib.Tests
git commit -m "StructureLib: structurelib.json sidecar for modded depots, strip on save and inject on load"
```

---

### Task 9: Cargo kinds

**Files:**
- Create: `src/StructureLib/Patches/CargoPatches.cs`
- Modify: `src/StructureLib/Sidecar.cs` + `SavePatches.cs` (canisters), `src/StructureLib/DevTools.cs`, `examples/ExampleStructure/Plugin.cs` (register `ryanhartwig.example-grit`, value `250`, "example grit", "grit", 1 kg, `Price = 2m`)

**Interfaces:**
- Consumes: Task 1 fact on canister save/restore method names.
- Produces: prefixes guarded by `MoonStructures.IsModded(cargo)`: `MoonWorld.CargoName`, `CargoShortName`, `UnitKilograms` (static), `CargoValue` (instance) → from the `CargoDefinition`; `MoonDroppedCanister.IsDust` getter → false for modded; canister sidecar: strip canisters whose `cargo` is modded in the canister save prefix (`CanisterRecord { int Id; string Owner; string CargoId; double Kilograms, Capacity; float[] Position, Up; float[] Rotation; bool PhysicalPose; }`), put back in the finalizer, inject in the `RestoreCanisters` prefix (cargo id → current value; unknown id → orphan). Audit list in `src/StructureLib/README.md`: every `MoonCargo`-typed member in `research/dump_MoonWorld.txt` and the canister/cargo snapshot dumps, marked patched / vanilla-safe (passes the value through) / unsupported.

- [ ] **Step 1: Add the failing self-test assertions**

`cargo-modded-name` (`MoonWorld.CargoName((MoonCargo)250) == "example grit"`), `cargo-modded-value` (`world.CargoValue((MoonCargo)250) == 2m`), `cargo-modded-kg` (`UnitKilograms == 1`), `cargo-unregistered-is-vanilla` (`CargoName((MoonCargo)251) == "moon dust"`, the vanilla fallthrough, so an unregistered value still behaves as vanilla does), `cargo-isdust-false` (a `MoonDroppedCanister` constructed with cargo 250 has `IsDust == false`).

- [ ] **Step 2: Run the in-game check to verify they fail**

Expected: `FAIL cargo-modded-name` ("moon dust").

- [ ] **Step 3: Implement `CargoPatches`, canister sidecar, README audit table**

- [ ] **Step 4: Run the in-game check to verify they pass**

Expected: five `PASS`, plus `save-strips-modded` and `reload-restores` still `PASS`.

- [ ] **Step 5: Commit**

```bash
git add src/StructureLib examples/ExampleStructure
git commit -m "StructureLib: modded cargo kinds with name/value patches and canister sidecar"
```

---

### Task 10: Processors

**Files:**
- Create: `src/StructureLib/Patches/ProductionPatches.cs`, `src/StructureLib/SpecBuilder.cs`
- Modify: `src/StructureLib/Sidecar.cs` + `SavePatches.cs` (production records), `src/StructureLib/DevTools.cs` (`AutoFeed`), `examples/ExampleStructure/Plugin.cs` (register `ryanhartwig.example-grinder`: `BaseStructure = MoonWorld.PressureChamberKind`, `Price = 100m`, `Category = "Production"`, `Processor`: `RunningKw = 1`, `CycleSeconds = 4`, slots `Input("Dust", Cargo.Dust, 10)`, `Batch("Grinding", Cargo.Dust, 2)`, `Output("Grit", grit, 10)`)

**Interfaces:**
- Consumes: Task 1 facts (c) and (d).
- Produces: `static class SpecBuilder { MoonWorld.ProcessorSpec Build(StructureDefinition d) }` (one instance cached per definition; `name = d.Name`, `product = output slot's cargo name or "power"`, `runningKw`, `cycleSeconds`, `slotNames/slotCargo/slotCapacity` from `Slots`, `output` = index of the Output slot or -1 when `ProducesPower`; built by `new ProcessorSpec()` and property sets, falling back to cloning `ChamberSpec` field-by-field if construction fails at runtime). Prefixes for modded kinds: `MoonWorld.IsProcessor` → `Processor != null`; `MoonWorld.Processor(int kind)` → the built spec; `MoonWorld.InputSlot(int kind, MoonCargo cargo)` → index of the first `Input` slot whose cargo matches, else -1; `MoonWorld.AcceptsText(int kind)` → `"<cargo> or <cargo>"` over input slots; `MoonWorld.IsPowerNode` → true when `Processor != null || Power != null`. Production sidecar: `ProductionRecord { int DepotId; float Progress; bool Running; double Charge; List<List<ShareRecord>> Stores; List<string> SlotCargoIds; }` stripped from `state.production` by depot id in the `MoonProductionSave.Write` prefix, injected in the `RestoreProduction` prefix. `MoonStructures.CycleFinished` raised from a `FinishCycle` postfix for modded kinds (`CycleFinished { StructureDefinition Definition; int DepotId; MoonProductionState State; }`). `DevTools.AutoFeed`: parse `"<structureId>:<cargo>:<kg>"`, find the depot id of the first built record of that kind, call `world.CrewProcessorLoad(player, depotId, cargo, kg)` 5 s after `AutoBuild`.

- [ ] **Step 1: Add the failing self-test assertions**

With `AutoBuild = 94,ryanhartwig.example-grinder` (a vanilla solar twin for power, then the grinder, 8 m apart so they are in range) and `AutoFeed = ryanhartwig.example-grinder:Dust:6`: `processor-spec-slots` (`world.ProductionOf(id).stores.Length == 3`); `inputslot-accepts-dust` (`MoonWorld.InputSlot(grinder.Kind, MoonCargo.Dust) == 0`); `inputslot-refuses-dust` (`MoonWorld.InputSlot(lamp.Kind, MoonCargo.Dust) == -1` and `InputSlot(grinder.Kind, MoonCargo.Stone) == -1`); `feed-lands-in-slot0` (after `AutoFeed`, `state.Stored(0) >= 6`); `cycle-produces-grit` (within 30 s, either `state.Stored(2) > 0` or an `EmitCargo` postfix saw `cargo == (MoonCargo)250`; which one depends on fact (d), assert the one that applies and log the other); `production-sidecar` (after `AutoSave`, the sidecar has a `ProductionRecord` for the grinder's depot id with `SlotCargoIds[2] == "ryanhartwig.example-grit"`); `production-reload` (second run: `ProductionOf(id)` exists with `Stored(0)` within 0.01 of the saved value).

- [ ] **Step 2: Run the in-game check to verify they fail**

Expected: `FAIL processor-spec-slots` (`ProductionOf` null or wrong slot count).

- [ ] **Step 3: Implement `SpecBuilder`, `ProductionPatches`, production sidecar, `AutoFeed`**

- [ ] **Step 4: Run the in-game check to verify they pass**

Expected: seven `PASS` over two runs, `0 failed`, and every earlier assertion still `PASS`.

- [ ] **Step 5: Commit**

```bash
git add src/StructureLib examples/ExampleStructure
git commit -m "StructureLib: modded processors with synthesized ProcessorSpec, input slots and production sidecar"
```

---

### Task 11: Power sources

**Files:**
- Create: `src/StructureLib/Patches/PowerPatches.cs`
- Modify: `src/StructureLib/SpecBuilder.cs` (`PanelSpecFor`), `src/StructureLib/DevTools.cs`, `examples/ExampleStructure/Plugin.cs` (register `ryanhartwig.example-burner`: `BaseStructure = 6` battery_bank, `Price = 100m`, `Category = "Power"`, `Power = { PeakKw = 5, BatteryKwh = 2, Range = 20, SupplyMode = Fuel }`, `Processor = { RunningKw = 0, CycleSeconds = 5, Slots = Input("Grit", grit, 10), Batch("Burning", grit, 1), ProducesPower = true }`)

**Interfaces:**
- Consumes: Task 1 fact (b): where `PowerNode`s are made and what `SolvePower` reads.
- Produces: prefixes for modded kinds: `MoonWorld.IsSolarPanel` → `Power != null && SupplyMode == Solar`; `MoonWorld.PowerRange` → `Power?.Range ?? remap to base`; `MoonWorld.TryPanel(int, out PanelSpec)` → `__result = false; return false` (never let vanilla write the out struct for modded kinds; managed code never calls it). Node spec: per fact (b), either a postfix on the node-creation method setting `node.panel = true; node.spec = SpecBuilder.PanelSpecFor(def)` (`PanelSpecFor` clones `MoonWorld.PostPanel` and sets `name, peakKw, batteryKwh, range`), or a `SolvePower` postfix adding supply for modded fuel nodes. Fuel supply: `FinishCycle` postfix for `ProducesPower` kinds adds `BatteryKwh` worth of charge per burned batch unit capped at `BatteryKwh` (`state.ChargeKwh = min(ChargeKwh + d.Power.BatteryKwh / 2, d.Power.BatteryKwh)`, i.e. two cycles fill the battery); if `SolvePower` multiplies supply by `SunFactor`, a `SolvePower` prefix sets `SunFactor = 1` and `Daylight = true` on modded fuel states each tick. `CanStartCycle` for a fuel processor must be false when `ChargeKwh >= BatteryKwh` (don't burn into a full battery): prefix on `CanStartCycle(MoonProductionState s, ProcessorSpec spec, out double batch)` for modded `ProducesPower` kinds.

- [ ] **Step 1: Add the failing self-test assertions**

With `AutoBuild = ryanhartwig.example-burner,ryanhartwig.example-grinder` (no vanilla solar this time; 8 m apart) and `AutoFeed = ryanhartwig.example-burner:ryanhartwig.example-grit:4`: `power-node-registered` (`world.powerNodes` has a node whose `record.kind == burner.Kind`); `power-grid-joined` (within 20 s both depots report the same non-zero `state.Grid`); `power-burner-supplies` (`ProductionOf(burnerId).SuppliedKw > 0` or `ChargeKwh > 0` within 30 s); `power-grinder-runs-on-burner` (grinder `Power > 0.5` within 30 s while fed dust via a second `AutoFeed` entry, so `AutoFeed` must accept a `;`-separated list); `power-no-burn-when-full` (after 60 s idle with no grinder demand, `Cycles` stops increasing once `ChargeKwh >= 2`).

- [ ] **Step 2: Run the in-game check to verify they fail**

Expected: `FAIL power-node-registered` or `power-grid-joined`.

- [ ] **Step 3: Implement `PowerPatches` and `PanelSpecFor`**

If the battery_bank (kind 6) prefab turns out to have no `MoonProductionView` or no usable anchor for `PowerAnchor`, switch the example burner's `BaseStructure` to `MoonWorld.SolarPostKind` (93) and record the reason in the recon note; SandPower's generator in Task 13 follows the same choice.

- [ ] **Step 4: Run the in-game check to verify they pass**

Expected: five `PASS`, all earlier assertions still `PASS`.

- [ ] **Step 5: Commit**

```bash
git add src/StructureLib examples/ExampleStructure
git commit -m "StructureLib: modded power sources, fuel-driven supply on the vanilla grid"
```

---

### Task 12: Co-op handshake (missing-mod detection)

**Files:**
- Create: `src/StructureLib/Handshake.cs`, `src/StructureLib/Pure/HandshakePayload.cs`
- Test: `tests/StructureLib.Tests/HandshakeTests.cs`
- Modify: `research/STRUCTURES_RECON.md` (crew session findings), `src/StructureLib/README.md`

**Interfaces:**
- Produces (pure): `sealed class RegistryManifest { int Protocol = 1; List<ManifestEntry> Structures; List<ManifestEntry> Cargo; }`, `sealed class ManifestEntry { string Id; int Value; string Owner; string Version; }`; `static class HandshakePayload { byte[] Encode(RegistryManifest); RegistryManifest Decode(byte[]); IReadOnlyList<string> Compare(RegistryManifest host, RegistryManifest client) }` returning one message per problem: `"missing mod <Owner> <Version> (structure <Id>)"`, `"version mismatch <Owner>: host <v> client <v>"`, `"cargo value conflict <Id>: host <n> client <n>"`, `"extra mod on client <Owner>"` (extra client mods are reported too: the host would not render them).
- Game side (`Handshake.cs`): a research step first (this task's Step 1) dumps `MoonCrewSession` and finds the join path; then hook the client's join completion to send the manifest through the lobby's data channel the game exposes (or, if there is no reusable channel, the `MoonSteamLobby` metadata), compare on the host, and on any problem refuse with `MoonCrewSession`'s own leave/kick path plus a dialog line built from `Compare`'s messages. Document the chosen hook in the README.

- [ ] **Step 1: Research the crew session**

`research/tools/dump-types.ps1 MoonCrewSession MoonSteamLobby` into `research/`; read the join flow (`Join`, `SendWorld`, `CrewJoin`, `IncomingFile`, `Kind` enum) and pick the hook. Write the finding into the recon note.

- [ ] **Step 2: Write the failing tests**

```csharp
[Fact] public void Handshake_RoundTrip() { var m = new RegistryManifest { Structures = { new ManifestEntry { Id = "a.x", Value = 1_000_001, Owner = "com.a", Version = "0.1.0" } } }; var back = HandshakePayload.Decode(HandshakePayload.Encode(m)); Assert.Equal("a.x", back.Structures.Single().Id); Assert.Equal(1_000_001, back.Structures.Single().Value); }
[Fact] public void Handshake_Compare_MissingOnClient() { var host = Manifest(("a.x", "com.a", "0.1.0")); var client = new RegistryManifest(); var p = HandshakePayload.Compare(host, client); Assert.Contains(p, s => s.Contains("missing mod com.a 0.1.0")); }
[Fact] public void Handshake_Compare_VersionMismatch() { var p = HandshakePayload.Compare(Manifest(("a.x", "com.a", "0.1.0")), Manifest(("a.x", "com.a", "0.2.0"))); Assert.Contains(p, s => s.Contains("version mismatch com.a")); }
[Fact] public void Handshake_Compare_CargoValueConflict() { var host = new RegistryManifest { Cargo = { new ManifestEntry { Id = "c", Value = 3, Owner = "com.a", Version = "1" } } }; var client = new RegistryManifest { Cargo = { new ManifestEntry { Id = "c", Value = 4, Owner = "com.a", Version = "1" } } }; Assert.Contains(HandshakePayload.Compare(host, client), s => s.Contains("cargo value conflict c")); }
[Fact] public void Handshake_Compare_Identical_NoProblems() { Assert.Empty(HandshakePayload.Compare(Manifest(("a.x", "com.a", "0.1.0")), Manifest(("a.x", "com.a", "0.1.0")))); }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/StructureLib.Tests -c Release`. Expected: build error.

- [ ] **Step 4: Implement `HandshakePayload` then `Handshake.cs`**

- [ ] **Step 5: Run tests; two-player check**

Run: `dotnet test tests/StructureLib.Tests -c Release`. Expected: 22 passed. Two-player test needs a second machine (Andrew): identical mods → join succeeds and a modded structure built by the host appears on the client (`WriteCrewStructures` relays the kind); client without ExampleStructure → join refused with the `missing mod` message. If no second machine is available, mark both as **untested** in the hand-off; do not claim them.

- [ ] **Step 6: Commit**

```bash
git add src/StructureLib tests/StructureLib.Tests research
git commit -m "StructureLib: co-op registry handshake that refuses joins with missing or mismatched mods"
```

---

### Task 13: SandPower mod, cargo ledger, docs, modpack

**Files:**
- Create: `mods/SandPower/SandPower.csproj`, `mods/SandPower/Plugin.cs`, `mods/SandPower/README.md`, `docs/cargo-registry.md`, `src/StructureLib/README.md` (complete it: API, how it works, status, audit table from Task 9)
- Modify: `modpack.json` (add `"StructureLib", "SandPower"`), `README.md` (package table + layout), `CONTRIBUTING.md` (new-mod section, SDK note), `docs/DECISIONS.md` (one line: SandPower shipped values)

**Interfaces:**
- Consumes: the whole public API from Task 3.
- Produces: `SandPower.Plugin.Guid = "com.ryanhartwig.sandpower"`, `Version = "0.1.0"`; registrations exactly: cargo `ryanhartwig.sand-briquette` value `3`, "sand briquette", "briquette", `UnitKilograms = 2`, `Price = 7m` (spec: ≈$7 per 2 kg unit; adjust to the yield rule Task 1 found and record the final number in the README); `ryanhartwig.sand-compactor`: Name "Sand compactor", Category "Production", `Price = 800m`, `BaseStructure = MoonWorld.StonePressKind`, Processor `RunningKw = 12`, `CycleSeconds = 8`, slots `Input("Sand", Cargo.Sand, 16)`, `Batch("Pressing", Cargo.Sand, 4)`, `Output("Briquettes", fuel, 10)`, Note "Presses 4 kg of moon sand into sand briquettes every 8 s. Needs 12 kW."; `ryanhartwig.briquette-generator`: Name "Briquette generator", Category "Power", `Price = 1500m`, `BaseStructure = 6`, Power `PeakKw = 10`, `BatteryKwh = 6`, `Range = 24`, Fuel, Processor `RunningKw = 0`, `CycleSeconds = 20`, slots `Input("Briquettes", fuel, 10)`, `Batch("Burning", fuel, 1)`, `ProducesPower = true`, Note "Burns one sand briquette every 20 s for 10 kW. A 6 kWh battery for the gaps. Powers machines within 24 m, wirelessly."

- [ ] **Step 1: Claim the hot files on the board** (`modpack.json`, `CONTRIBUTING.md`, `README.md`) or note the board was unreachable.

- [ ] **Step 2: Write the failing self-test assertions**

Build ExampleStructure and SandPower both installed; `AutoBuild = 94,ryanhartwig.sand-compactor,ryanhartwig.briquette-generator`, `AutoFeed = ryanhartwig.sand-compactor:Sand:8`: `sandpower-registered` (both ids have hashed kinds and the cargo has value 3); `sandpower-compactor-cycles` (`Cycles >= 1` within 30 s); `sandpower-briquette-out` (output slot or `EmitCargo` with cargo 3); `sandpower-generator-charges` (after `AutoFeed = ryanhartwig.briquette-generator:ryanhartwig.sand-briquette:2`, `ChargeKwh > 0` within 40 s).

- [ ] **Step 3: Run the in-game check to verify they fail**

Expected: `FAIL sandpower-registered`.

- [ ] **Step 4: Implement SandPower, write the docs, add to `modpack.json`**

`docs/cargo-registry.md`: a table `Value | Id | Owner GUID | Mod | Claimed` with 0-2 vanilla and 3 briquette, and the claim rule from the spec. `package.ps1` must list the new packages: run `.\package.ps1 -Only SandPower` (builds release, zips to `dist/`) and check the zip has `BepInEx/plugins/SandPower/SandPower.dll` and the README; do not pass `-Release`.

- [ ] **Step 5: Run the in-game check to verify they pass; manual chain**

Expected: four `PASS`, all earlier assertions still `PASS`. By hand in slot-2: build a solar twin, a compactor and a generator from the menu, belt sand from the vanilla pressure chamber's outfeed into the compactor and briquettes into the generator, watch the generator power the compactor once the twin is removed. Record exactly what was checked in the hand-off.

- [ ] **Step 6: Commit**

```bash
git add mods/SandPower docs src/StructureLib/README.md modpack.json README.md CONTRIBUTING.md
git commit -m "SandPower: sand compactor and briquette generator on StructureLib; docs, cargo ledger, modpack"
```

---

## Self-review notes

- Spec coverage: goals 1-4 → Tasks 5-11; goal 5 (tooling) → separate plan `2026-11-01-dev-tooling.md`; S0 → Task 1; S4 handshake → Task 12; S5 → Task 13; S6 release is not a task (only when asked). Non-goals are enforced by `Rules.CheckDonor` (foundations) and by the absence of degradation code.
- Type consistency: `Cargo` struct, `Slot`, `SlotRole`, `StructureBuilt`, `CycleFinished`, `DonorTraits`, `SidecarFile` record names are used identically in Tasks 3, 7, 8, 9, 10, 11, 13.
- Review Focus items 1-5 are pinned to Tasks 7/8, 3, 10, 8, 4 respectively.
