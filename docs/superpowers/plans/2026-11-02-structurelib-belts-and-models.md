# StructureLib Belt Ports and Custom Models Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Modded machines take and give cargo by belt through ports declared on their definition, and a modded structure ships its own `.bin` model; SandPower's compactor and generator use both, proven by an in-game chain test.

**Architecture:** Ports become data on `StructureDefinition` (declared, or read from `Port_*` nodes in the model file) and are written into the cloned `MoonDepotBuildDefinition.conveyorPorts`, so the game's belt tool snaps and joins on its own; belt transfer goes through prefixes on the vanilla entry points, or a library-side bridge if vanilla refuses modded kinds before reading ports (the M1 spike decides). Models reuse GarageLib's `MeshModel` reader (moved to `shared/`) and `DataVehicles.Swap`'s approach: the donor prefab is the template, its renderers are hidden, the mesh hangs under a holder at the template root. The Blender models themselves are a separate plan (`2026-11-02-sandpower-models.md`) run in parallel after Task 1.

**Tech Stack:** C# (net6.0, BepInEx 6 IL2CPP, HarmonyX), xunit (net8.0 tests), Blender 4.5.15 headless for one throwaway test model, the existing dev self-test harness.

**Spec:** `docs/superpowers/specs/2026-11-02-structurelib-belts-and-models-design.md` (read it; the calls in "Three calls the builder check asked for" are binding).

## Global Constraints

- Build per project (`dotnet build <proj> -c Release`; SDK 8 here, the `.slnx` needs SDK 9). Install builds: `dotnet build examples/ExampleStructure/ExampleStructure.csproj -c Release -nologo -v q -p:InstallToGame=true` builds and installs StructureLib too. Close the game first (`taskkill //IM MoonSweepers.exe //F`), launch `"C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers\MoonSweepers.exe"` directly, ≥10 s between kill and relaunch. Log `<game>\BepInEx\LogOutput.log`; dev cfg `<game>\BepInEx\config\com.ryanhartwig.structurelib.cfg`.
- Every run starts from the slot-2 backup `C:\Users\ryguy\projects\moonsweep\.scratch\slot-2-backup-task11` and ends with it restored, the cfg reset (`AutoBuild`, `AutoFeed`, `AutoBelt` empty; every new switch off; `AutoRemove = true`, `AutoSave = false`, `AutoLoadSlot = 2`, `SelfTest = true`), the game closed, logs copied to `.superpowers/sdd/2026-11-02-structurelib-belts-and-models/task-N-logs/`. SandPower is installed only during Task 6 and uninstalled after.
- Never prefix the static `MoonWorld.IsProcessor`; never call `MoonWorld.TryPanel` or any `out`-struct method from managed code (native crashes). A new static prefix (`PortCargo`) is proven safe by two clean world loads with it on, else dropped (bisect as Task 5 of the first plan did).
- Every prefix early-outs for vanilla kinds (`Remap.Live(kind) == null` → run vanilla). Never write a modded kind or cargo value into the vanilla save. Never log full Steam ids (`Redact`).
- Self-test lines are exactly `[selftest] PASS <name>` / `[selftest] FAIL <name>: <detail>` / `[selftest] done: <n> passed, <m> failed`. Dev tooling is DEVBUILD-only (`DevTools.cs` is wrapped in `#if DEVBUILD`).
- Unit tests: `dotnet test tests/StructureLib.Tests -c Release` (111 green at start). Pure logic in `Pure/` with tests.
- Blender for the throwaway test model: `C:\Users\ryguy\projects\moonsweep\.downloads\blender-4.5\blender-4.5.15-windows-x64\blender.exe -b --factory-startup --python <script>`; one process per script; never the Blender MCP.
- Hot paths touched without a board claim (board unreachable): `Directory.Build.targets`, `src/GarageLib/GarageLib.csproj`, `shared/**`; say so in the hand-off.
- Commit per task with the message given; every commit message ends with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- `DevTools.cs` (~1400 lines) and `Sidecar.cs` (~1000) are not split in this plan; new dev code for belts goes in a new file `DevBelts.cs` (same `#if DEVBUILD`, `partial class DevTools`).

## Review Focus

1. A belt joined to a modded input port while the machine's slot is full: the canister must wait at the belt end, not vanish and not be duplicated (Task 3 `belt-in-waits-when-full`).
2. A modded cargo canister on a belt across save and reload: it must come back on the belt with its cargo, not as dust and not lost (Task 4 `belt-save-clean`).
3. A model file missing or corrupt at registration: the structure keeps the donor model, logs once, and everything else (ports from declared `Ports`, card) still works (Task 5 `model-missing-falls-back`).
4. A port declared outside the donor footprint plus reach: warning, not a refused registration; a port whose cargo matches no slot: a registration error naming the port (Task 2 `PortRules` tests).
5. The player's belt tool pointed at a modded port: snapping and joining must behave as on a vanilla machine (by hand only; the hand-off lists it; Task 1 records what the definition-driven path does with a cloned definition).

---

### Task 1: M1 spike — belts, ports and donor facts (research + dev tooling, no library behaviour)

**Files:**
- Create: `src/StructureLib/DevBelts.cs` (`#if DEVBUILD`, `partial class DevTools`: `AutoBelt`, `DumpCards`, belt probes)
- Modify: `src/StructureLib/DevTools.cs` (bind `AutoBelt`, `DumpCards` config; ring layout for `AutoBuild`; call `DevBelts` ticks), `src/StructureLib/Patches/ProductionPatches.cs` (temporary traces only if needed, removed before commit), `research/STRUCTURES_RECON.md` (new section "Belts and ports (M1, 2026-11-02)"), `examples/ExampleStructure/Plugin.cs` (nothing unless a trace needs it)
- Test: in-game probe runs; no unit tests

**Interfaces:**
- Produces: `[Dev] AutoBuild` ring layout: entries placed 10 m from the walker at headings 0°, 90°, 180°, 270°, 45°… (so four structures sit within 24 m of each other and the walker); `[Dev] AutoBelt` = `"<from>:<port>-><to>:<port>;…"` where `<from>`/`<to>` is a structure id or vanilla kind number resolved to the depot `AutoBuild` built, `<port>` a port name (`PortName(def, port)`) or 0-based index; builds each belt with `MoonWorld/ConveyorPlan { start = PortPoints(from)[i], startDirection, startStructure = fromDepot, end = PortPoints(to)[j], endDirection, endStructure = toDepot, startUp/endUp = surface normals (position.normalized) }` through `CheckConveyor` then `TryBuildConveyor(walker, plan, out reason, out id)`, logging `[dev] belt <from>:<port> -> <to>:<port> id=<id> ok=<bool> reason='<reason>'`; runs 3 s after `AutoBuild` finishes and before `AutoFeed`. `[Dev] DumpCards` (bool): once the catalog exists, writes every `MoonDepotBuilder.CardArt(entry.image)` sprite (vanilla and modded) to `<game>\BepInEx\cache\structurelib\cards\<kind>-<name>.png` using `IconRenderer`'s PNG writer, and logs the count.
- Produces (recon facts, the contract for Tasks 2–5): (a) which method a canister reaching a belt end joined to a machine port goes through (`EnterMachine` vs `FeedProcessor`, call order, arguments, what it checks for the vanilla chamber), and what happens to a canister at a belt end joined to the example grinder's port (refused by what, and where the canister sits: still on the belt at the end, or dropped); (b) the emit size and cadence vanilla uses (`EmitCargo` kg per call for the chamber's sand); (c) whether a modded cargo canister (grit, 250) rides a belt and arrives at the far end (placed with `PlaceCarriedOnBelt` or spawned at the belt start); (d) whether a Harmony prefix on static `PortCargo(ConveyorPort)` loads two clean worlds, and what `PortCargo` returns for the chamber's and press's ports (name → cargo rule); (e) for kinds 96, 97, 93: `halfFootprint`, `height`, `floorOffset`, `scale`, `reach`, each `conveyorPorts[]` entry (name, position, direction, output), the prefab's renderer bounds in local space (min/max), and which transform `MoonSolarPanelView` rotates on the post (the name path from the root); (f) whether `MachineJoins`, `MachinePortSnaps`, `BeltsAtPorts`, `MachineOutfeed`, `EmitProduct` run for a modded kind whose clone has ports (trace postfixes with bounded logging), i.e. whether the definition-driven path reads the clone's ports or refuses by kind first.

- [ ] **Step 1: Ring layout and `AutoBelt`, `DumpCards` dev tools**

Implement in `DevBelts.cs` and the binds in `DevTools.cs`. Cards dumped on the first menu catalog build. Port points come from `world.PortPoints(record)`; log each depot's ports (`[dev] ports of <id>: <n> <name> in/out at (x,y,z) dir (…)`).

- [ ] **Step 2: Run the chamber-to-grinder probe**

Config: `AutoBuild = 94,96,ryanhartwig.example-grinder`, `AutoFeed = 96:Dust:10` (vanilla kind as a feed target: extend `AutoFeed` to accept a kind number), `AutoBelt = 96:Sand out->ryanhartwig.example-grinder:Dust in` (use the real vanilla port names the log printed), `AutoSave = true`, `DumpCards = true`. Trace postfixes (bounded, 20 each) on `EnterMachine`, `FeedProcessor`, `PortCargo` (postfix only, no prefix yet), `MachineJoins`, `MachineOutfeed`, `EmitProduct`, `EmitCargo`, `BeltsAtPorts`, `MachinePortSnaps`. Expected in the log: the chamber emits sand canisters onto the belt (fact b), they travel to the grinder end, and the log shows which method refuses them and where they stop (fact a, f).
Then a second run with `AutoBelt` from the grinder's `Grit out` port to the chamber's `Dust in` (wrong cargo on purpose) and a grit canister placed on that belt (`PlaceCarriedOnBelt` or `world` canister creation as the canister sidecar's injector does) to answer fact (c).

- [ ] **Step 3: `PortCargo` prefix safety**

Add a `[HarmonyPrefix]` on `PortCargo` that only logs and returns true; two world loads (fact d). If either dies natively, remove it and record the crash as in "Interop hazards found". Record what `PortCargo` returns for every vanilla port seen.

- [ ] **Step 4: Donor facts**

From `world.BuildDefinition(kind)` for 96, 97, 93 log every field in fact (e) and the prefab's renderer bounds (instantiate under an inactive holder as `IconRenderer` does, measure, destroy). For the post, log the transform path `MoonSolarPanelView` rotates (`Animate` moves which child: compare local rotations across two frames).

- [ ] **Step 5: Write the recon section and remove traces**

`research/STRUCTURES_RECON.md` gets "Belts and ports (M1, 2026-11-02)" with facts (a)–(f) as bullets with log excerpts (ids redacted), plus the card PNG folder path. Temporary trace postfixes are deleted; `AutoBelt`, `DumpCards`, the ring layout and the `AutoFeed` kind-number extension stay.

- [ ] **Step 6: Commit**

```bash
git add src/StructureLib research/STRUCTURES_RECON.md
git commit -m "StructureLib: M1 belt spike — AutoBelt and DumpCards dev tools, ring AutoBuild layout, belt and donor facts in the recon"
```

---

### Task 2: Ports as definition data

**Files:**
- Create: `src/StructureLib/PortDefinition.cs`, `src/StructureLib/Pure/PortRules.cs`, `src/StructureLib/Patches/PortPatches.cs`
- Modify: `src/StructureLib/StructureDefinition.cs` (`Ports`), `src/StructureLib/MoonStructures.cs` (validate at freeze), `src/StructureLib/Injection.cs` (rebuild `clone.conveyorPorts`), `src/StructureLib/DevTools.cs` (assertions), `examples/ExampleStructure/Plugin.cs` (grinder and burner ports), `src/StructureLib/README.md`
- Test: `tests/StructureLib.Tests/PortRulesTests.cs`

**Interfaces:**
- Produces: `public sealed class PortDefinition { public string Name; public Vector3 Position; public Vector3 Direction; public bool Output; public Cargo Cargo; }`; `StructureDefinition.Ports` (`List<PortDefinition>`, null = inherit the donor's ports mapped by name as Task 1 fact (d) describes). Pure: `static class PortRules { IReadOnlyList<string> CheckPorts(string id, IReadOnlyList<(string Name, bool Output, string CargoId)> ports, IReadOnlyList<(SlotRole Role, string CargoId)> slots) }` → errors: duplicate port name; an input port whose cargo matches no Input slot (`"'<id>': port '<name>' takes <cargo> but no input slot does"`); an output port whose cargo is not the Output slot's (`"… emits <cargo> but the output slot holds <cargo2>"`); more than one output port; `static string WarnOutsideFootprint(string id, string port, Vector3 pos, Vector2 half, float reach)` → warning text or null when inside `half + reach`. `Injection.Clone` rebuilds `clone.conveyorPorts` from the resolved ports (`MoonDepotBuildDefinition/ConveyorPort { name = "sl:" + Name, position, direction, output }`), logging the rule applied. `PortPatches`: the `PortCargo` answer for modded ports as Task 1 fact (d) allows (prefix on the static keyed on the `sl:` name prefix / `port.structure` being a modded depot; else prefixes on the callers Task 1 found), returning the port's `Cargo.ToMoon()`. `MoonStructures.PortsOf(StructureDefinition)` → the resolved list (for Tasks 3–5). Resolution order per the spec: model `Port_*` nodes (Task 5 fills this in; Task 2 leaves the hook `Func<StructureDefinition, List<PortDefinition>> ModelPortsProvider` null) → declared `Ports` → donor ports by name.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void CheckPorts_InputCargoMustMatchASlot() { var e = PortRules.CheckPorts("g", new[]{("Dust in", false, "dust")}, new[]{(SlotRole.Input, "sand")}); Assert.Contains(e, s => s.Contains("port 'Dust in' takes dust")); }
[Fact] public void CheckPorts_OutputCargoMustBeTheOutputSlots() { var e = PortRules.CheckPorts("g", new[]{("Grit out", true, "dust")}, new[]{(SlotRole.Input,"dust"),(SlotRole.Output,"ryanhartwig.example-grit")}); Assert.Single(e); }
[Fact] public void CheckPorts_DuplicateNameAndSecondOutputRefused() { var e = PortRules.CheckPorts("g", new[]{("A", true, "x"),("A", true, "x")}, new[]{(SlotRole.Output,"x")}); Assert.Equal(2, e.Count); }
[Fact] public void CheckPorts_Valid() { Assert.Empty(PortRules.CheckPorts("g", new[]{("Dust in", false, "dust"),("Grit out", true, "grit")}, new[]{(SlotRole.Input,"dust"),(SlotRole.Batch,"dust"),(SlotRole.Output,"grit")})); }
[Fact] public void WarnOutsideFootprint_OnlyBeyondReach() { Assert.Null(PortRules.WarnOutsideFootprint("g","p", new Vector3(2,0,0), new Vector2(1.5f,1.5f), 1f)); Assert.NotNull(PortRules.WarnOutsideFootprint("g","p", new Vector3(4,0,0), new Vector2(1.5f,1.5f), 1f)); }
```
(`Vector3`/`Vector2` in tests: use the pure numeric tuples `(float x, float y, float z)` if the test project cannot reference UnityEngine; the pure method takes tuples and the game-side caller converts.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/StructureLib.Tests -c Release --filter PortRules`. Expected: build error, `PortRules` missing.

- [ ] **Step 3: Implement `PortRules`, `PortDefinition`, `Ports`, clone rebuild, `PortPatches`; example ports**

Example grinder: `Ports = { Input "Dust in" (Cargo.Dust) at the donor chamber's dust input port position/direction from Task 1 fact (e), Output "Grit out" (grit) at the chamber's sand output port }`. Example burner (solar post donor, no ports): `Ports = { Input "Grit in" (grit) at (0, 0.6, 1.2) facing −Z }` (edit from fact (e) so it sits at the post's footprint edge). Self-test assertions: `ports-on-clone` (`world.BuildDefinition(grinder.Kind).conveyorPorts.Length == 2` with names `sl:Dust in`, `sl:Grit out`), `ports-burner-added` (post donor has 0, burner clone has 1), `ports-cargo-answer` (`PortCargo` for the grinder's runtime input port, found through `ConveyorPorts(near, radius, list)` around the built grinder, equals `MoonCargo.Dust`, and for `Grit out` equals `(MoonCargo)250`), `ports-rules-logged` (a deliberately bad example definition registered only under `[Dev] RegisterBadPorts = true` is dropped at freeze with the rule's message).

- [ ] **Step 4: Run tests and the in-game check**

Run: `dotnet test tests/StructureLib.Tests -c Release`. Expected: 116 passed. In game with `AutoBuild = ryanhartwig.example-grinder,ryanhartwig.example-burner`: four `PASS`, every earlier applicable assertion `PASS`, `0 failed`.

- [ ] **Step 5: Commit**

```bash
git add src/StructureLib tests/StructureLib.Tests examples/ExampleStructure
git commit -m "StructureLib: ports as definition data, written into the cloned definition, with validation and PortCargo answers"
```

---

### Task 3: Belt input

**Files:**
- Create: `src/StructureLib/Patches/BeltPatches.cs` (or `BeltBridge.cs` if Task 1 fact (a)/(f) force the bridge), `src/StructureLib/Pure/BeltIntake.cs`
- Modify: `src/StructureLib/MoonStructures.cs` (`CargoEntered` event), `src/StructureLib/DevTools.cs` (assertions), `src/StructureLib/README.md`
- Test: `tests/StructureLib.Tests/BeltIntakeTests.cs`

**Interfaces:**
- Consumes: Task 1 facts (a), (c), (f); Task 2 `MoonStructures.PortsOf`; existing `InputSlot(kind, cargo)` prefix and `MoonWorld.Put(store, owner, kg)` as used by the `CrewProcessorLoad` prefix.
- Produces: pure `static class BeltIntake { static (int Slot, double Kilograms, string Refusal) Decide(IReadOnlyList<(SlotRole Role, string CargoId, double Capacity, double Stored)> slots, string canisterCargoId, double canisterKg) }` → the first Input slot of that cargo with room; takes `min(canisterKg, room)`; refusal `"<cargo> does not go in here"` when no slot, `"full"` when no room (the canister waits). Game side: on the entry point Task 1 identified (`EnterMachine`/`FeedProcessor` prefix for modded kinds, or the bridge's per-tick step), apply `Decide`, `Put` the kilograms under the canister's owner, remove the canister when fully taken (reduce `kilograms` otherwise, via `MoveCanisterRecord` as the sidecar does), raise `MoonStructures.CargoEntered { Definition, DepotId, Slot, Cargo, Kilograms, Owner }`, return false to skip vanilla. Refusals leave the canister where vanilla leaves a refused one (fact a).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Decide_TakesIntoMatchingInputSlot() { var r = BeltIntake.Decide(new[]{(SlotRole.Input,"sand",16.0,4.0),(SlotRole.Batch,"sand",4.0,0.0),(SlotRole.Output,"b",10.0,0.0)}, "sand", 5.0); Assert.Equal((0, 5.0, null), r); }
[Fact] public void Decide_PartialWhenNearlyFull() { var r = BeltIntake.Decide(new[]{(SlotRole.Input,"sand",16.0,15.0)}, "sand", 5.0); Assert.Equal((0, 1.0, null), r); }
[Fact] public void Decide_FullWaits() { Assert.Equal("full", BeltIntake.Decide(new[]{(SlotRole.Input,"sand",16.0,16.0)}, "sand", 5.0).Refusal); }
[Fact] public void Decide_WrongCargoRefused() { Assert.Equal("dust does not go in here", BeltIntake.Decide(new[]{(SlotRole.Input,"sand",16.0,0.0)}, "dust", 5.0).Refusal); }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/StructureLib.Tests -c Release --filter BeltIntake`. Expected: build error.

- [ ] **Step 3: Implement `BeltIntake`, the game-side intake, `CargoEntered`**

Self-test (`AutoBuild = 94,96,ryanhartwig.example-grinder`, `AutoFeed = 96:Dust:10`, `AutoBelt = 96:<sand out>->ryanhartwig.example-grinder:Dust in`): the chamber's sand canisters must be **refused** by the grinder (`Dust in` takes dust, not sand) → `belt-in-refuses-cargo` (a `CargoEntered` never fires, the canister waits at the belt end, no vanilla exception); then a second belt from a dust source: `AutoBelt` also accepts `catcher` if Task 1 found a dust belt source, else the self-test spawns a dust canister at the belt start the way fact (c) did → `belt-in-accepts` (`CargoEntered` with slot 0, `Stored(0)` grows by the canister kg within 30 s), `belt-in-waits-when-full` (fill slot 0 to 10 kg via `AutoFeed`, then a belt canister: not consumed, still present after 10 s, no duplicate), `belt-canister-modded` (a grit canister placed on a belt rides to the far end, cargo still 250).

- [ ] **Step 4: Run tests and the in-game check**

Run: `dotnet test tests/StructureLib.Tests -c Release`. Expected: 120 passed. In game: four `PASS`, `0 failed`, `Moon save failed` 0.

- [ ] **Step 5: Commit**

```bash
git add src/StructureLib tests/StructureLib.Tests
git commit -m "StructureLib: belt input into modded machines through declared ports, CargoEntered event"
```

---

### Task 4: Belt output and save safety with belts

**Files:**
- Modify: `src/StructureLib/Patches/BeltPatches.cs` (emit), `src/StructureLib/Patches/ProductionPatches.cs` (`EmitProduct` prefix for modded kinds), `src/StructureLib/Pure/BeltIntake.cs` (rename to `BeltTransfer.cs` with `EmitSize`), `src/StructureLib/DevTools.cs`, `src/StructureLib/README.md`
- Test: `tests/StructureLib.Tests/BeltTransferTests.cs`

**Interfaces:**
- Consumes: Task 1 fact (b) emit size; `MachineOutfeed(d)`; vanilla `EmitCargo(outfeed, owner, cargo, kg, out reason)`; the canister sidecar.
- Produces: pure `BeltTransfer.EmitSize(double stored, double vanillaUnit, double capacity)` → `min(stored, vanillaUnit)` (0 when stored is 0); `EmitProduct` prefix for modded kinds: when `MachineOutfeed(d)` is non-null and the output slot holds cargo, call vanilla `EmitCargo` with the output cargo and `EmitSize`, debit the slot on success (`MoonWorld.Move`/`Take` as the generic cycle does), keep vanilla's "Connect a belt to the <product> output" status when the belt is missing; raise `MoonStructures.CargoEmitted { Definition, DepotId, Cargo, Kilograms, BeltId }`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void EmitSize_IsTheVanillaUnitOrWhatIsLeft() { Assert.Equal(2.0, BeltTransfer.EmitSize(6.0, 2.0, 10.0)); Assert.Equal(1.5, BeltTransfer.EmitSize(1.5, 2.0, 10.0)); Assert.Equal(0.0, BeltTransfer.EmitSize(0.0, 2.0, 10.0)); }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/StructureLib.Tests -c Release --filter EmitSize`. Expected: build error.

- [ ] **Step 3: Implement emit; assertions**

Self-test (`AutoBuild = 94,ryanhartwig.example-grinder,ryanhartwig.example-burner`, `AutoFeed = ryanhartwig.example-grinder:Dust:6`, `AutoBelt = ryanhartwig.example-grinder:Grit out->ryanhartwig.example-burner:Grit in`, `AutoSave = true`): `belt-out-emits` (`CargoEmitted` with cargo 250 within 40 s; a canister with cargo 250 exists on the belt), `belt-chain-feeds` (the burner's slot 0 gains grit within 60 s via `CargoEntered`), `belt-save-clean` (save with the belt joined and a grit canister on it: `Moon save failed` 0; second run with `AutoBuild` empty: belt restored (`world.conveyors` has it), the canister back on the belt with cargo 250, `reload-restores` still PASS).

- [ ] **Step 4: Run tests and the two in-game runs**

Run: `dotnet test tests/StructureLib.Tests -c Release`. Expected: 121 passed. In game: three `PASS` over the two runs, `0 failed`.

- [ ] **Step 5: Commit**

```bash
git add src/StructureLib tests/StructureLib.Tests
git commit -m "StructureLib: belt output from modded machines onto outfeed belts, CargoEmitted event, belts survive save and reload"
```

---

### Task 5: Custom models from `.bin` files, ports from the model, card from the model

**Files:**
- Move: `src/GarageLib/MeshModel.cs` → `shared/MeshModel/MeshModel.cs` (`git mv`, no code change)
- Modify: `src/GarageLib/GarageLib.csproj` and `src/StructureLib/StructureLib.csproj` (`<Compile Include="$(RepoRoot)shared\MeshModel\MeshModel.cs" Link="Shared\MeshModel.cs" />`), `Directory.Build.targets` (`CopyPluginToGame` also copies `$(MSBuildProjectDirectory)\*.bin`), `src/StructureLib/StructureDefinition.cs` (`Model`), `src/StructureLib/Injection.cs` (default `BuildModel` when `Model` set; `ModelPortsProvider`), `src/StructureLib/MoonStructures.cs` (resolve the model path from the registering assembly's folder at `Register`), `src/StructureLib/DevTools.cs`, `src/StructureLib/README.md`, `examples/ExampleStructure/Plugin.cs` (lamp gets `Model = "example_lamp.bin"`), `templates/Mod/Plugin.cs` (commented `Model` line)
- Create: `src/StructureLib/ModelSwap.cs`, `src/StructureLib/StructureMaterials.cs`, `src/StructureLib/Pure/ModelPorts.cs`, `examples/ExampleStructure/blender/example_lamp.py` (throwaway: a 1 m box on a 0.2 m skid with a cyan lamp and one `Port_Test in` empty), `examples/ExampleStructure/example_lamp.bin`
- Test: `tests/StructureLib.Tests/ModelPortsTests.cs`

**Interfaces:**
- Consumes: `MeshModel.LoadFile(path)`, `MeshModel.Instantiate(holder, skip, material)`, `MeshModel.Node.Props`; Task 2 `PortDefinition` and `ModelPortsProvider`; Task 1 fact (e) (post's rotating transform name).
- Produces: `StructureDefinition.Model` (string, file name next to the mod DLL; resolved to `ModelPath` at `Register` from `Assembly.GetCallingAssembly().Location`); pure `static class ModelPorts { static List<(string Name, (float x,float y,float z) Pos, (float x,float y,float z) Dir, bool Output, string CargoId)> FromNodes(IEnumerable<(string Name, (float,float,float) Pos, (float x,float y,float z,float w) Rot, IReadOnlyDictionary<string, object> Props)> nodes) }` → every node named `Port_<name>`: name without the prefix, position as is (already Unity space), direction = the node's Blender local +Y axis expressed in Unity space: the exporter maps (x, y, z) → (−x, z, −y), so Blender +Y becomes Unity **−Z**, and direction = `Rot * (0,0,−1)` normalised (the kit's `check_bin.py` prints `dir=(0,0,-1)` for an identity port, which is the oracle); `output` from prop `output` (float ≠ 0), cargo from prop `cargo` (string; `Dust`/`Sand`/`Stone` or a cargo id). `StructureMaterials.For(name, colour)`: the seven `MS_*` names → `MoonPrototypeModels` (found as GarageLib's `IconRenderer.FindModels` does: first `MoonPrototypeEquipment` whose `Models.shell` is set), else `models.Mat("StructureLib_" + name, colour)`, logged once. `ModelSwap.Build(StructureDefinition d, ModelContext ctx)` = the default `BuildModel`: `Instantiate(donor.prefab, ctx.Parent)`, disable every `MeshRenderer`/`SkinnedMeshRenderer`, measure the donor's lowest renderer bound in template-local space, `holder = new GameObject("StructureLibModel")` under the template root with `localScale = Vector3.one / max(donor.scale, 0.01f)`, `model.Instantiate(holder, n => n.Name.StartsWith("Port_"), StructureMaterials.For)`, set `holder.localPosition.y` so the model's lowest vertex equals the donor's lowest bound, copy the root layer to every node; assert no ancestor of the holder is the post's rotating transform (fact e); on any exception log once and return null (donor model kept). `Injection.ModelPortsProvider` = `d => ModelPorts.FromNodes(MeshModel.LoadFile(d.ModelPath).Nodes…)` when `Model` is set and the file loads. Card: `IconRenderer` already photographs `clone.prefab` (now the swapped template); `model-card` proves it.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void FromNodes_ReadsPortEmpties() { var ports = ModelPorts.FromNodes(new[]{("Port_Sand in", (1f,0.5f,0f), (0f,0f,0f,1f), (IReadOnlyDictionary<string,object>)new Dictionary<string,object>{["output"]=0f,["cargo"]="Sand"}), ("Body", (0f,0f,0f),(0f,0f,0f,1f), new Dictionary<string,object>())}); var p = Assert.Single(ports); Assert.Equal("Sand in", p.Name); Assert.False(p.Output); Assert.Equal("Sand", p.CargoId); Assert.Equal((0f,0f,-1f), p.Dir); }
[Fact] public void FromNodes_RotatedPortPointsAlongLocalMinusZ() { var ports = ModelPorts.FromNodes(new[]{("Port_Out", (0f,0f,0f), (0f,0.7071068f,0f,0.7071068f), (IReadOnlyDictionary<string,object>)new Dictionary<string,object>{["output"]=1f,["cargo"]="ryanhartwig.example-grit"})}); Assert.True(ports[0].Output); /* a 90° yaw about Unity Y turns (0,0,-1) into (-1,0,0) */ Assert.InRange(ports[0].Dir.x, -1.01f, -0.99f); }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/StructureLib.Tests -c Release --filter ModelPorts`. Expected: build error.

- [ ] **Step 3: Implement; build the throwaway example model**

`examples/ExampleStructure/blender/example_lamp.py` uses `shared/blender/kit.py` (`box`, `lamp`, and an empty named `Port_Test in` with custom props `output = 0`, `cargo = "Dust"`; props are exported from `obj["key"]` custom properties: check `export_mesh_model.py`'s prop reading and match it), saves `blender/example_lamp.blend`, previews, exports `examples/ExampleStructure/example_lamp.bin`. Run it with the Blender 4.5.15 path from Global Constraints. Lamp definition: `Model = "example_lamp.bin"`, `Ports` left null (model ports win). Self-test: `model-swapped` (the built lamp's view has a child `StructureLibModel` with ≥1 enabled `MeshRenderer`, every donor renderer outside it disabled, the donor's collider count unchanged from a run without `Model`: record the donor's collider count once in Task 1's facts or log it here), `model-ports` (`world.BuildDefinition(lamp.Kind).conveyorPorts` has one port `sl:Test in` at the file's position), `model-card` (the lamp's card sprite's pixels differ from the floodlight donor's card: compare the two PNGs `DumpCards` wrote, byte-inequality is enough), `model-missing-falls-back` (`[Dev] BreakModelPath = true` points the lamp at a missing file: donor model shown, one Error line, `ports` fall back to declared/donor, every other assertion still PASS).

- [ ] **Step 4: Run tests, build both GarageLib and StructureLib, run the in-game check**

Run: `dotnet test tests/StructureLib.Tests -c Release` → 123 passed. `dotnet build src/GarageLib/GarageLib.csproj -c Release -p:InstallToGame=false` → succeeds (the moved file compiles in both). In game (`AutoBuild = ryanhartwig.example-lamp`): four `PASS`, `0 failed`; a second run with `BreakModelPath = true` for `model-missing-falls-back`.

- [ ] **Step 5: Commit**

```bash
git add shared/MeshModel src/GarageLib src/StructureLib tests/StructureLib.Tests examples/ExampleStructure templates/Mod Directory.Build.targets
git commit -m "StructureLib: custom models from .bin files on the donor template, ports read from Port_ nodes, shared MeshModel reader"
```

---

### Task 6: SandPower models and ports, the belt chain, docs, PR

**Files:**
- Modify: `mods/SandPower/Plugin.cs` (`Model` for both, ports from the models), `mods/SandPower/README.md`, `src/StructureLib/README.md`, `src/StructureLib/DevTools.cs` (`sandpower-chain`), `docs/DECISIONS.md` (one line), `docs/superpowers/HANDOFF-structurelib-2026-11-01.md` (status + by-hand list), `research/STRUCTURES_RECON.md` (chain run facts)
- Consumes: `mods/SandPower/sand_compactor.bin` and `mods/SandPower/briquette_generator.bin` from the models plan (`docs/superpowers/plans/2026-11-02-sandpower-models.md`), each with `Port_*` nodes: compactor `Sand in` (input, Sand), `Briquettes out` (output, `ryanhartwig.sand-briquette`); generator `Briquettes in` (input). **Do not start Step 3 until both files exist in `mods/SandPower/`**; the controller says when.

- [ ] **Step 1: Wire `Model` on both SandPower definitions; `Ports` null (model ports)**

- [ ] **Step 2: Add `sandpower-chain`**

Config: `AutoBuild = 94,96,ryanhartwig.sand-compactor,ryanhartwig.briquette-generator` (ring layout), `AutoFeed = 96:Dust:12`, `AutoBelt = 96:<sand out>->ryanhartwig.sand-compactor:Sand in;ryanhartwig.sand-compactor:Briquettes out->ryanhartwig.briquette-generator:Briquettes in`, `AutoSave = true`. Assertion `sandpower-chain`: within 180 s the generator's `ChargeKwh > 0` with no `AutoFeed` entry for the compactor or the generator; also `CargoEntered` seen for both machines and `CargoEmitted` for the compactor with cargo 3. `sandpower-model-swapped` (both views have `StructureLibModel`), `sandpower-model-ports` (compactor clone has 2 ports, generator 1).

- [ ] **Step 3: Run with SandPower installed (`-p:InstallToGame=true`), then uninstall it**

Expected: `sandpower-chain`, both model assertions and every earlier SandPower assertion `PASS`, `0 failed`, `Moon save failed` 0, reload run keeps belts, canisters and models.

- [ ] **Step 4: Docs**

SandPower README: how to belt it (port names, what each takes), the models (previews linked), limits (hand feeding still unsupported). StructureLib README: `Ports`, `Model`, `Port_*` convention, `CargoEntered`/`CargoEmitted`, the swap rules and the animated-effects limitation, `AutoBelt`/`DumpCards`. HANDOFF: status rows for this plan, by-hand list (belt tool onto modded ports by hand; models at player scale). DECISIONS: one line.

- [ ] **Step 5: Commit**

```bash
git add mods/SandPower src/StructureLib docs research
git commit -m "SandPower: custom models and belt ports; the chamber-to-generator belt chain runs in the self-test; docs"
```
