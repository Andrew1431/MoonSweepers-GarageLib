# StructureLib: belt ports and custom models (SandPower deliverable)

Second StructureLib milestone, 2026-11-02. Ryan's brief: SandPower need not be hand-fed yet, but it must be **belt
fed**; and the shipped structures must have **their own models**, built the way Andrew builds vehicles (headless
Blender scripts on `shared/blender/kit.py`, exported to the `.bin` mesh format GarageLib reads). Everything lands on the
open PR (branch `structurelib`). Ryan was asleep while this was designed: every decision below is the coordinator's
call, made to be undone cheaply, and is marked **(call)**.

## Intent

A player builds the SandPower compactor and generator from the menu, belts sand into the compactor from the vanilla
pressure chamber, belts the briquettes into the generator, and the generator powers the compactor. The two structures
look like purpose-built machines, not re-tinted vanilla props. Mod authors get the same two abilities (belt ports,
custom models) through the StructureLib API with no Harmony of their own.

## Goals

1. **Belt input** into a modded machine's input port: a canister riding a belt enters the matching input slot
   (cargo-checked), the canister is consumed, the slot gains its kilograms, owners kept.
2. **Belt output** from a modded machine's output port: when the output slot holds cargo and an outfeed belt is joined,
   the machine emits a canister of its output cargo onto the belt, as the vanilla chamber does with sand.
3. **Ports are definition data**: a modded structure declares its ports (name, local position, direction, output flag,
   cargo); a port-less donor (the solar post under the generator) can still have ports. Belt snapping, placement and
   joining use the game's own definition-driven paths.
4. **Custom models**: a modded structure ships a `.bin` mesh next to its DLL; the library swaps it onto the donor prefab
   template while keeping every donor component (colliders, production view, interaction), aligns it to the floor, and
   the build-menu card shows the new model.
5. **Ports authored in Blender**: `Port_<name>` empties in the model carry the port data; the loader derives the
   definition's ports from them, so geometry and ports cannot drift apart.
6. **Two SandPower models** (compactor, generator) built with the kit under Blender 4.5.15, with previews, in the
   vehicle style guide's palette, fitting their donor footprints, with intake and outlet geometry at the ports.
7. **The chain is proven in game by the self-test**: chamber → belt → compactor → belt → generator, built by a dev tool,
   runs without hand feeding; saves and reloads clean with belts attached and modded canisters on them.

## Non-goals (this milestone)

- Hand feeding (the walker's `LoadProcessor`) and canister-in-hand input. Deferred, documented.
- Catcher dust directly into a modded machine (dust comes through the vanilla chamber).
- Vanilla machines fed by modded power; vanilla grid totals seeing modded demand.
- Animated model parts beyond what the donor's production view already animates (fill planes, emission, hum).
- New Blender materials: the vehicle palette (`MS_*`) is the only one.
- Collector selling of briquettes (untested; left as is).

## Anti-goals

- Changing any vanilla port, belt or machine behaviour for vanilla kinds. Every prefix early-outs unless the kind is
  modded.
- Writing a modded cargo value or kind into the vanilla save. Belts are vanilla records (endpoints only) and stay in the
  vanilla save; canisters of modded cargo keep going through the canister sidecar.
- Removing donor colliders or scripts when swapping the model. The donor prefab stays whole; only its renderers are
  hidden.
- Patching `MoonWorld.IsProcessor` (static, crashes) or calling `TryPanel` or any `out`-struct method. If
  `PortCargo(ConveyorPort)` (static) crashes when prefixed, it is not prefixed; the design must have a fallback (below).
- Hard-coding SandPower into the library. SandPower uses only the public API.

## Constraints (game facts, from `research/STRUCTURES_RECON.md` and the dumps)

- `MoonDepotBuildDefinition.conveyorPorts[]` holds `ConveyorPort { name, position, direction, output }` (local space).
  Belt snapping, placement and joining are definition-driven: `MachinePortSnaps(def, kind, …)`, `MachineJoins(kind,
  port)`, `BeltsAtPorts(def, position, rotation)`, `MachinesAtPorts`, `MachineWithPort(point, output)`,
  `FreeMachinePort(machine, output, point)`, `PortPoints(record)`, `ConveyorPorts(near, radius, ports)`,
  `PortLabel/PortName(def, port)`. StructureLib already clones the definition per modded kind, so the clone's
  `conveyorPorts` is ours to set.
- Input path: `MoonWorld.EnterMachine(MoonDepotSnapshot machine, int input, int id)` and
  `FeedProcessor(machine, int input, int id)`, with `id` a canister on a belt and `input` a slot index. The cargo a port
  accepts comes from the static `MoonWorld.PortCargo(MoonWorld/ConveyorPort port)`, whose argument is the **runtime**
  port struct `{ position, direction, output, structure (depot id), belt, occupied, label }`, not the definition's
  `ConveyorPort`: a prefix can find the machine by `port.structure` and the port by `label`. Which input entry point is
  reached for a belt end joined to a machine port, and what each checks, is unknown: **the spike (Task 1) traces them.**
- Belt plans: `MoonWorld/ConveyorPlan { start, startDirection, startUp, end, endDirection, endUp, startStructure,
  endStructure, startBelt, endBelt, replaces }` (a class with a parameterless ctor), passed to `CheckConveyor` /
  `TryBuildConveyor(builder, plan, out reason, out id)`. Port points for a built depot come from `PortPoints(record)`
  and `ConveyorPorts(near, radius, list)`. `MoonConveyorKit.Load()` holds `minLength`, `maxLength`, `minRadius`,
  `maxSlope`, `pricePerMetre` (dumps: `research/dump_MoonWorld_ConveyorPlan_ConveyorPort.txt`,
  `research/dump_MoonConveyorKit.txt`, `research/dump_MoonConveyorPath.txt`).
- Output path: `MachineOutfeed(d)` finds the belt at the output port; `EmitProduct` → `EmitCargo(outfeed, owner, cargo,
  kg, out reason)`; without a belt `EmitProduct` leaves the output slot alone and sets status "Connect a belt to the
  <product> output" (recon fact (d)). `EmitCargo` has an `out string` (not a struct): safe to call and patch.
- Belts: `MoonWorld.TryBuildConveyor(builder, ConveyorPlan plan, out reason, out id)`, `CheckConveyor`,
  `BeltStartingAt/BeltEndingAt(Vector3)`, `MoonConveyorSnapshot { id, owner, start, startDirection, end, endDirection }`,
  `MoonConveyorPath`. `ConveyorPlan` is a nested type (`MoonWorld/ConveyorPlan`); dump it in the spike.
- Canisters: `MoonDroppedCanister { id, owner, kilograms, capacity, position, up, rotation, physicalPose, cargo }`;
  modded cargo values 3..255; `IsDust` is already false for them. The canister sidecar strips and re-injects them.
- Models: the mesh format and loader exist (`src/GarageLib/MeshModel.cs`, `shared/blender/export_mesh_model.py`,
  format `MH6M` v1: materials, nodes with parent/pos/rot/scale/props, flat-shaded meshes, submeshes by material).
  Empties export with their transforms and props. Blender is Z-up with the nose at −Y; the exporter maps
  (x, y, z) → (−x, z, −y) into Unity. GarageLib's `GameMaterials` maps the seven `MS_*` names to
  `MoonPrototypeModels` materials (shell, dark, orange, glass, metal, cyan); `DataVehicles.Swap` hides the base
  renderers, instantiates the model under a holder, and offsets it so the bottoms match.
- StructureLib's `BuildModel` already replaces the clone's `prefab` (and skins) with a template under an inactive
  holder; the card renderer photographs `clone.prefab`; `DecorateModel` runs on every spawned copy.
- Donor footprints: `halfFootprint`, `height`, `floorOffset`, `scale`, `reach` are definition fields; the values for the
  stone press (97), the solar post (93) and the pressure chamber (96, reference) are **read in the spike**, with the
  renderer bounds of each donor prefab.
- Blender: Andrew's scripts name `Blender 4.5`; this machine has 3.5 and 4.4 installed and now a verified portable
  **4.5.15** at `C:\Users\ryguy\projects\moonsweep\.downloads\blender-4.5\blender-4.5.15-windows-x64\blender.exe`
  (sha256 checked against blender.org). `kit.py` runs under it (Dune jumper rebuilt: 186 nodes, 14,164 triangles).
  Headless only, one process per script, never the Blender MCP (CLAUDE.md).
- `package.ps1` already copies every `*.bin` next to a package's csproj into its zip (line 77). The dev install
  (`Directory.Build.targets`) copies the DLL only: **the build must also copy `*.bin` beside the DLL** (hot path,
  one line).
- The work-order board is unreachable. Hot paths touched: `Directory.Build.targets`, `src/GarageLib/GarageLib.csproj`
  (file move), `shared/**`.

## The how, as decided

### Ports **(call)**

```csharp
public sealed class PortDefinition
{
    public string Name;          // shown by the game ("Sand in"), unique per structure
    public Vector3 Position;     // structure-local, Unity axes (Y up), metres
    public Vector3 Direction;    // flow direction at the port (into the machine for inputs, out of it for outputs)
    public bool Output;
    public Cargo Cargo;          // the cargo this port carries (an input slot's cargo, or the output slot's)
}
public sealed class StructureDefinition { …; public List<PortDefinition> Ports; public string Model; … }
```

Resolution order when the clone is built: `Ports` from the model file's `Port_*` nodes (Goal 5) if `Model` is set and
the file has any; else `Ports` as declared; else the donor's ports, each mapped to a cargo by name match against the
processor's slots (the library logs which rule applied). The clone's `conveyorPorts` is rebuilt from the result. A port
whose cargo matches no slot (input) or is not the output slot's cargo (output) is a registration error, reported the
way slot errors are (`Rules.CheckPorts`, pure, tested). Port positions are validated to lie within the donor's
footprint plus `reach`; outside is a warning, not an error.

### Belt input and output **(call, shape; the spike may move the exact hook)**

- Prefixes for modded kinds on the input entry point the spike identifies (`EnterMachine` and/or `FeedProcessor`):
  find the input slot whose cargo equals the canister's cargo (`InputSlot(kind, cargo)`, already prefixed), refuse if
  none or full (same reason strings vanilla uses where readable, else "`<name>` does not take `<cargo>`"), else move the
  canister's kilograms into the slot under the canister's owner (`MoonWorld.Put`, as `CrewProcessorLoad`'s prefix does),
  remove the canister through the game's own removal (`RemoveCanister`, as the canister sidecar does), raise
  `MoonStructures.CargoEntered { Definition, DepotId, Slot, Cargo, Kilograms, Owner }`, and skip vanilla.
- `PortCargo(ConveyorPort)` for a modded port must answer the port's declared cargo. First choice: a prefix keyed on the
  port name (modded ports get a name prefix `sl:` so the static can recognise them without a kind). If prefixing the
  static crashes the game (bisect as Task 5 did), fallback: prefix every caller the spike found (`MachineJoins`,
  `FreeMachinePort`, `EnterMachine`) instead, and document it.
- Output: `EmitProduct` for a modded kind with an outfeed belt joined (`MachineOutfeed(d)` non-null) calls vanilla
  `EmitCargo(outfeed, owner, cargo, kg, out reason)` with the output slot's cargo and the vanilla emit size the spike
  measured for the chamber (or the whole slot if vanilla emits whole), debits the slot, and keeps the vanilla status
  text when the belt is missing. The generic cycle's `FinishCycle` is unchanged.
- Modded canisters on belts: `EmitCargo` makes a `MoonDroppedCanister` with `cargo` = the modded value; the canister
  sidecar already strips and re-injects them; `IsDust` is false. The spike confirms a modded canister rides a belt and
  arrives at a port (vanilla moves canisters by id, not by cargo).
- Dev tool `[Dev] AutoBelt`: `"<fromStructureId|kind>:<port>-><toStructureId|kind>:<port>"` list, `;`-separated; builds
  a belt between two built depots' ports through `ConveyorPlan` + `TryBuildConveyor`, logging reason strings. Runs
  after `AutoBuild`, before `AutoFeed`.

### Models **(call)**

- `shared/MeshModel/MeshModel.cs`: `src/GarageLib/MeshModel.cs` moves here (git mv, no code change, namespace stays
  `GarageLib`) and is `<Compile Include>`d by both `GarageLib.csproj` and `StructureLib.csproj`. StructureLib gets its
  own `StructureMaterials` (the seven-name map to `MoonPrototypeModels`, found as GarageLib's `IconRenderer.FindModels`
  does, with the colour fallback) — a 30-line copy, not a GarageLib reference (SandPower must not need GarageLib).
- `StructureDefinition.Model = "sand_compactor.bin"` (file next to the registering mod's DLL). The library supplies the
  default `BuildModel` when `Model` is set and `BuildModel` is null: instantiate the donor prefab as the template under
  the model holder, disable every `MeshRenderer`/`SkinnedMeshRenderer` on it, instantiate the mesh under a child
  `StructureLibModel` with `localScale = 1 / def.scale` (so the definition's `scale` nets out) and `localPosition.y`
  such that the model's lowest vertex sits at the donor's lowest renderer bound (as `DataVehicles.Swap`), copy the
  root's layer onto every node, skip `Port_*` nodes (they are data). Any exception keeps the donor model and logs once.
  `DecorateModel` still runs per copy and can find the holder by name.
- The card renderer already photographs `clone.prefab`; verify the card shows the model, else render from the holder.
- `Port_<name>` convention in the model: an empty whose props are `output` (0/1), `cargo` (string id or `Dust`/`Sand`/
  `Stone`) and whose local +Y axis (Blender) is the flow direction; position = the empty's position. The exporter
  already writes empties and props; `kit.port(name, loc, dir, output, cargo)` adds one. The loader converts with the
  exporter's axis map (already applied to node transforms).
- Model stays under ~10k triangles per structure; one root `<Name>_Root`; floor at Z = 0 in Blender.

### Blender tooling and style **(call)**

- `shared/blender/kit.py` gains structure helpers: `port(...)`, `stage_structure()` preview framing (no astronaut
  behind the rear corner but beside it, at 1.8 m, so the footprint reads), `footprint(name, half_x, half_z, height)` a
  transparent guide box the build script draws from the donor numbers and removes before export (previews show it
  as a wireframe), `cargo_intake(...)`/`outlet(...)` primitives (a hopper mouth, a chute) in the palette.
- `shared/blender/STRUCTURES.md`: one page: palette and shapes shared with vehicles (white panels, black plastic
  skirts and housings, orange accents on moving parts and intakes, steel frames, cyan status lights), structures are
  boxy industrial units on a visible skid or foot, ports read as a hopper mouth (input) or a chute (output) at the
  exact port position, keep within the donor footprint and height, show the reference cards the spike captured.
- Build scripts live with the mod: `mods/SandPower/blender/sand_compactor.py`, `briquette_generator.py`, saving
  `mods/SandPower/blender/<name>.blend`, `preview_{hero,side,front,rear}.png`, and exporting `mods/SandPower/<name>.bin`.
  Each script prints its ports in Unity space so the README can list them.
- Reference images: the spike's dev tool `[Dev] DumpCards` writes every catalog card sprite (vanilla and modded) to
  `BepInEx/cache/structurelib/cards/<kind>-<name>.png`; the modelling agents read the chamber, press, solar post,
  catcher and conveyor cards for style. The stage also captures one in-world screenshot per donor if the dev tool can
  place the camera (nice to have).

### SandPower **(call: numbers unchanged from 0.1.0)**

- Compactor: donor stone press (97), ports: `Sand in` (input, `Cargo.Sand`), `Briquettes out` (output,
  `ryanhartwig.sand-briquette`), positions from the model.
- Generator: donor solar post (93), ports: `Briquettes in` (input), no output port; `Model = briquette_generator.bin`.
- The chain in the self-test: vanilla pressure chamber (96) built by `AutoBuild`, fed dust by `AutoFeed`, belted to the
  compactor; compactor belted to the generator; `AutoBelt` builds both belts; assertions below.

### Tooling and packaging

- `Directory.Build.targets`: copy `*.bin` next to the project file into `BepInEx/plugins/<AssemblyName>/` alongside the
  DLL when installing (one `<Copy>` line).
- `package.ps1` already ships `*.bin`; verify with `-Only SandPower`.
- `new-package.ps1 -Kind mod` template gets a commented `Model = "..."` line and a `blender/` note.

### Three calls the builder check asked for **(call, Ryan asleep; cheap to reverse)**

1. **If the vanilla belt path refuses modded kinds before it reads the ports** (an inlined `IsProcessor` in
   `MachineJoins`, `EnterMachine`, `MachineOutfeed` or `EmitProduct`, as every input refusal so far has been), a
   **library-side belt bridge is allowed**: the belt tool's snapping and joining stay vanilla and definition-driven
   (Goal 3 means "do not reimplement the belt tool"), and StructureLib handles the transfer itself. Shape: a per-tick
   step over modded machines with joined belts (`BeltEndingAt(input port point)` / `BeltStartingAt(output port
   point)`), consuming a canister that has reached the end of an input belt (nearest canister to the port point within
   the belt's end tolerance; the spike measures how vanilla parks a refused canister), and emitting through vanilla
   `EmitCargo` on the outfeed belt or, if that refuses, by spawning a modded canister at the belt start with the
   game's own canister creation. If the belt tool itself cannot join a modded port (snapping refuses before the
   definition is read), **belt work stops at M1 with a written finding** and the models still ship. If modded cargo
   canisters cannot ride a belt at all (a per-cargo table indexed by the byte), belt **input** of vanilla cargo still
   ships and belt output is documented unsupported. The milestone never shrinks silently: the recon section says which
   case happened.
2. **Model swap hides every donor renderer, including the ones the production view animates.** Fill planes, emission
   and tracker tilt are lost on a custom model; the models carry static status geometry instead (cyan `MS_Cyan` lights,
   an orange `MS_LampOrange` ring at the intake). The holder is parented to the **template root**, never to a child the
   donor's view rotates (the solar post's tracker): the swap asserts that no animated donor transform is an ancestor of
   the holder, and the spike records which transform `MoonSolarPanelView` aims. Retargeting the view's effects onto
   named model nodes is a later milestone, listed in the README as a limitation.
3. **The chain test runs on vanilla solar power, option (a).** The compactor needs 12 kW and the generator peaks at
   10 kW with an empty battery, so the chain cannot bootstrap itself; the vanilla chamber at its head needs the vanilla
   grid anyway. `sandpower-chain` asserts transport and processing by belt (dust → chamber → sand belt → compactor →
   briquette belt → generator charge > 0), with every structure inside the solar twin's range. "The generator powers
   the compactor" was already proven by Task 13's run with no vanilla node (`sandpower-compactor-cycles` at power 0.83
   on grid −26) and stays as that assertion. SandPower's numbers do not change. To fit four structures within reach,
   the dev `AutoBuild` gains a ring layout (entries at 10 m from the walker at successive headings) instead of the
   8 m × (i + 1) line.

### Two plans, two sessions **(call)**

The Blender work (kit helpers, `STRUCTURES.md`, the two SandPower models) touches `shared/blender/**` and
`mods/SandPower/blender/**` only, needs no game and no library code, and can run while the belt work occupies the
game. It is its own plan (`docs/superpowers/plans/2026-11-02-sandpower-models.md`) started in its own session once the
M1 spike has written the donor dimensions, port positions and reference cards into the recon note. The library plan
(`docs/superpowers/plans/2026-11-02-structurelib-belts-and-models.md`) carries M1, M2, M3 and M5; M5's SandPower wiring
waits for the two `.bin` files and the ports they declare.

## Milestones

- **M1 Spike (research, no library code)**: dump `ConveyorPlan`; dev tool builds a belt from the chamber to the example
  grinder (donor pressure chamber already has ports) and traces `EnterMachine`, `FeedProcessor`, `PortCargo`,
  `MachineJoins`, `MachineOutfeed`, `EmitProduct`, `EmitCargo` for one dust canister in and one sand canister out;
  reads the three donors' footprint fields and renderer bounds; captures the cards; tests whether a `PortCargo` prefix
  crashes. Output: recon section "Belts and ports (M1)" with the facts (a) input hook and its checks, (b) emit size,
  (c) whether modded canisters ride belts, (d) `PortCargo` patchability, (e) donor dimensions and port positions.
- **M2 Ports + belt I/O** in the library, example grinder gets a `Sand out`... (example: grinder `Dust in` / `Grit out`,
  burner `Grit in`), assertions `belt-in-accepts`, `belt-in-refuses-cargo`, `belt-out-emits`, `belt-canister-modded`,
  `belt-save-clean` (save and reload with belts joined and a modded canister on a belt).
- **M3 Models**: shared loader, `Model`, ports from `Port_*`, card from the model, assertions `model-swapped`
  (holder present, donor renderers disabled, colliders intact), `model-ports` (definition ports equal the file's),
  `model-card` (card sprite differs from the donor's) with a throwaway test `.bin` built by a script in the example.
- **M4 Blender**: kit helpers, `STRUCTURES.md`, the two SandPower models with previews reviewed against the style page
  (an agent reads the PNGs), exported `.bin`s under 10k triangles each.
- **M5 SandPower + docs**: `Model` and ports wired, the chain self-test `sandpower-chain` (dust → chamber → sand belt →
  compactor → briquette belt → generator charge > 0 within 120 s, no hand feeding), READMEs, handoff, PR update.

## Testing bar

- Pure logic in `Pure/` with xunit (port resolution and validation, model node → port conversion, emit sizing).
- Every game-side behaviour has a `[selftest]` assertion in the existing harness; runs start from the clean slot-2
  backup; logs kept per run.
- Each model has four previews looked at by a reviewer agent against `STRUCTURES.md` before it is accepted, and an
  in-game screenshot of the built structure if the dev tool can take one.
- By hand (Ryan): belts placed with the real belt tool onto the modded ports; the models in the world at player
  scale.

## Left to the builder

Exact hook choice from the spike; reason strings; emit cadence; the port name prefix; how `DumpCards` finds the
sprites; the preview camera for structures; the exact shapes of the two machines within the style page.
