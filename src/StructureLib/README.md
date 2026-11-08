# StructureLib

The shared library for modded structures in Moon Sweepers: new buildings in the build menu, machines that turn one
cargo into another, new cargo kinds, and fuel generators. A modded structure borrows a vanilla structure's prefab and
rules, is kept out of the vanilla save (uninstalling a mod never breaks a save), and is checked between co-op players.
[SandPower](../../mods/SandPower) is the first mod built on it; [ExampleStructure](../../examples/ExampleStructure)
shows every registration pattern.

## Using it

Reference `src/StructureLib/StructureLib.csproj` (`Private="false"`), add `[BepInDependency(StructureLib.Plugin.Guid)]`,
and register everything from your plugin's `Load()`. Registering later is refused: the registry freezes when the first
world loads. Pass your plugin GUID as the owner; the co-op handshake reports it with your plugin's version.

```csharp
var fuel = MoonStructures.RegisterCargo(new CargoDefinition
{
    Id = "you.fuel", Value = 42,                 // claim the byte in docs/cargo-registry.md; never change either
    Name = "fuel pellet", ShortName = "pellet", UnitKilograms = 2, Price = 5m,
}, Guid);

MoonStructures.Register(new StructureDefinition
{
    Id = "you.press", Name = "Press", Category = "Production", Price = 800m,
    Note = "Presses 4 kg of sand into fuel every 8 s. Needs 12 kW.",
    BaseStructure = MoonWorld.StonePressKind,    // the vanilla kind it borrows from
    Processor = new ProcessorDefinition
    {
        RunningKw = 12, CycleSeconds = 8,
        Slots = { Slot.Input("Sand", Cargo.Sand, 16), Slot.Batch("Pressing", Cargo.Sand, 4), Slot.Output("Fuel", fuel, 10) },
    },
}, Guid);
```

- **Structures** (`MoonStructures.Register(StructureDefinition, ownerGuid)`): `Id` (stable, `author.structure`),
  `Name`, `Category` (a build-menu tab; a new name adds a tab), `Note` (the card line), `Price`, `BaseStructure` (a
  vanilla kind; foundations, decks, building parts, catwalk supports and the world grid are refused). Optional
  `CardImagePng` or `CardImageKey`; without them the card picture is rendered from the model.
- **Models:** `BuildModel(ModelContext)` returns a replacement root under `ctx.Parent`; it runs once per world and the
  game copies the result for every one built. `DecorateModel(GameObject, ModelContext)` runs on every copy (tint,
  extra parts). `ModelContext` carries the `Definition`, the `Parent` and the donor's vanilla build definition
  (`Donor`). Without either, the donor's model is used.
- **Model files:** `Model = "my_machine.bin"` names a model exported with `shared/blender` (see
  [`shared/blender/STRUCTURES.md`](../../shared/blender/STRUCTURES.md)) that sits next to your DLL (the build copies
  every `*.bin` beside a project's csproj into its plugin folder). With `Model` set and no `BuildModel`, the library
  builds the template from the donor's prefab: every donor renderer (mesh, skinned, particle, line...) is disabled (its
  colliders, views and scripts stay), and the model goes under a child of the root named `StructureLibModel` (scaled by
  `1 / scale`, layer copied, its lowest vertex on the structure's floor, local y = 0, as the kit floors models;
  `Port_*` nodes move with it). Materials named after the kit's palette (`MS_BodyWhite`, ...) become the game's own;
  any other name gets a material of its colour. The game's materials come from the player's equipment, which does not
  exist yet when a world loads: until it does, the model wears tinted stand-ins, which then become the game's own in
  place (on every copy already built), and the card picture is taken, or taken again, once they have. The card picture
  is rendered from that template. A missing or unreadable file logs one error and keeps the donor's model; the ports
  fall back to the declared `Ports` first, then the donor's.
  Limitation: the donor's animated effects (fill planes, emission, the press's moving parts) are hidden with its
  renderers and not retargeted onto the model; model static status lights instead.
  The `.bin` reader (`shared/MeshModel/MeshModel.cs`) is compiled into both GarageLib and StructureLib, so it is
  `internal` on the StructureLib side (GarageLib's `GarageLib.MeshModel` stays public): a mod referencing both
  assemblies sees one type and gets no CS0433; reference GarageLib if you need `MeshModel` yourself.
- **Cargo** (`MoonStructures.RegisterCargo(CargoDefinition, ownerGuid)`): `Id`, `Value` (a byte 3..255 claimed in
  [`docs/cargo-registry.md`](../../docs/cargo-registry.md)), `Name`, `ShortName`, `UnitKilograms`, `Price` (dollars
  per unit), `CanisterTint` (optional: multiplies the near canister view's dust fill, so the cargo reads as tinted
  dust on belts, on the ground and in the hand; the look-at panel colours its rows with it too), `CanisterModel`
  (optional: `"<file>.bin"` beside the DLL, exported with `shared/blender`, shown in place of the vanilla capsule on
  the near canister view, built at the view's origin and scale with the capsule's renderers hidden and its colliders
  kept, so the canister rides belts, is picked up and feeds machines exactly as before; size it like the capsule, see
  [STRUCTURES.md, "Canister models"](../../shared/blender/STRUCTURES.md#canister-models); a missing or unreadable
  file logs one error and keeps the tinted capsule). Far canisters (beyond about 100 m, drawn instanced) are neither
  tinted nor swapped and look like dust. It returns the `Cargo` handle for slots, or
  `Cargo.None` when refused. Vanilla cargo handles are `Cargo.Dust`, `Cargo.Sand`, `Cargo.Stone`.
- **Processors** (`StructureDefinition.Processor`): `RunningKw`, `CycleSeconds`, up to 8 `Slots`: at least one
  `Slot.Input`, at least one `Slot.Batch`, and one `Slot.Output` unless `ProducesPower`. Capacities are kilograms. A
  cycle starts when every Batch slot can be filled to capacity from an Input slot of the same cargo and the Output
  slot has room; it moves the batch into the Output slot at the same mass (4 kg in, 4 kg out).
- **Ports** (`StructureDefinition.Ports`, a `List<PortDefinition>`): belt ports, each `Name` (unique), `Position`
  (structure-local, Unity axes, metres; vanilla ports sit at deck height 0.85), `Direction` (the belt's flow direction at
  the port, for inputs and outputs alike: every vanilla donor's ports point local +X), `Output`, `Cargo`.
  `PortDefinition.In(...)` / `.Out(...)` build one. A `Model` whose file has `Port_<name>` empties wins over `Ports`:
  each empty is a port at its position, flowing along its Blender local +Y, with its `output` (0/1) and `cargo`
  (`Dust`, `Sand`, `Stone` or a cargo id) properties (`kit.port`). Null inherits the donor's ports, each given a cargo by name
  (`IN_SAND` sand, any other dust, kept when an Input slot holds it, else the first Input slot's; an output carries
  the Output slot's cargo); an empty list removes them. If the donor's build definition cannot be read when the
  registry freezes (and neither the model nor `Ports` gives any), the clone keeps the donor's ports untouched and a
  warning names the structure. When the registry freezes, an input port whose cargo no Input
  slot holds, an output port whose cargo is not the Output slot's, a duplicate name or a second output port drops the
  structure (logged); a port outside the donor footprint plus 1.2 m is only a warning. The cloned definition's
  `conveyorPorts` are rebuilt from them, named `sl:<cargo byte>:<Name>` so `MoonWorld.PortCargo` (prefixed) answers the
  port's cargo; the game shows "input" / "output" and "<structure name> input", never that name.
  `MoonStructures.PortsOf(def)` returns the resolved list.
- **Routers** (`StructureDefinition.Router`, a `RouterDefinition { Mode = RouterMode.Splitter | Merger }`): a belt
  splitter or merger with no processor. See [Routers](#routers).
- **Footprint** (`StructureDefinition.Footprint`, optional `Vector2`: full width along X, depth along Z, metres, both
  positive): written into the clone's `halfFootprint` (halved) instead of the donor's. With a `Model` it also replaces
  every donor collider by one `BoxCollider` over the footprint and the model's height. Null keeps the donor's.
- **Power** (`StructureDefinition.Power`): a `PowerDefinition` with a `Role` (`Source`, `Relay`, `Storage`),
  `SupplyMode` (`Fuel` or `Solar`), `PeakKw`, `BatteryKwh` (Storage capacity), `RateKw`, `LinkRange` and
  `SupplyRadius`. A fuel source also has a `ProducesPower` processor. `PowerRules` checks the numbers at
  registration. See [Power](#power) for the grid rules and limits.
- **Events** (main thread): `MoonStructures.Built` (`StructureBuilt`: definition, depot id, depot record, the decorated
  view) when a modded structure is built and again for each one restored at load; `Removed` (depot id) after one is
  removed; `CycleFinished` (`CycleFinished`: definition, depot id, live production state) when a modded machine
  finishes a cycle; `CargoEntered` (`CargoEntered`: definition, depot id, slot, cargo, kilograms, owner) when a canister
  at the end of a belt joined to one of its input ports is taken into an Input slot; `CargoEmitted` (`CargoEmitted`:
  definition, depot id, cargo, kilograms, belt id) when a modded machine (not a router) puts a canister of its output on the belt
  joined to its output port; `CargoRouted` (`CargoRouted`: definition, depot id, cargo, kilograms, from belt id, to
  belt id) when a router moves a canister from an input belt onto an output belt. A handler that throws is logged and
  does not stop the game.
- **Lookups:** `Definition(kind)`, `Definition(id)`, `PortsOf(def)`, `IsModded(kind)`, `IsModded(MoonCargo)`, `CargoDefinition(...)`,
  `All`, `AllCargo`, `Frozen`, `Disabled`.

## How it works

- **Hashed kinds.** A structure's kind number is FNV-1a of its id, mapped above vanilla's range
  (`1_000_000 + hash % (int.MaxValue - 1_000_000)`), so every install agrees on it without identical mod lists. A
  collision between two ids disables the library for the session (no modded structures or cargo) rather than
  renumbering. Cargo values are declared, not hashed: a byte has too little room.
- **Freeze.** The registry freezes when the first world loads: kinds are assigned, donors are checked, duplicate or
  out-of-range cargo is dropped, and so is any structure whose slots use dropped cargo. Every error names the id.
- **Cloned definitions.** Each world gets one copy of the donor's `MoonDepotBuildDefinition` per modded structure, with
  its name, price and (with `BuildModel`) its prefab. A `BuildDefinition` prefix answers the modded kind with it; other
  per-kind lookups (shape, grid, robots) answer as the donor. Placement, charging and refunds are the game's own.
- **Catalog.** Each structure is appended to the build menu's static catalog (a new tab if its category is new) with
  the image key `structurelib:<id>`; the card picture is rendered from the model once and swapped into open menus.
- **Sidecar.** The game re-reads every save it writes and refuses a kind it does not know, so modded depots, their
  machines' production states and canisters of modded cargo are cut out of the save while it is written, put back
  after, and written to `structurelib.json` beside the save. On load they go back into the load snapshot and restore
  like vanilla ones. Ownership rule: only a record the library confirmed in the world is replaced by its live state on
  the next save; every other record (a missing mod's, or one that failed to restore) is written back verbatim, so
  nothing is dropped or duplicated. A mod that is removed leaves its records waiting until it is installed again.
- **Machines.** A modded processor gets its own `ProcessorSpec`, and prefixes on the cycle methods run the generic
  cycle above, so the game's `StepProduction` drives it like a vanilla machine.
- **Belt input.** When a canister reaches the end of a belt joined to a machine's input, the game calls
  `MoonWorld.HandOff(belt, path, canister)` every frame until it answers true; for a modded kind it answers false, so a
  prefix decides for a belt whose end is a modded machine's input port: the canister must carry the port's cargo, and
  the first Input slot of that cargo with room takes it (all of it, or what fits) under the canister's owner. The
  canister is then removed (`RemoveCanister`), or reduced when only part fit, and `CargoEntered` fires. Otherwise the
  canister waits at the belt end, later ones queue behind it, and the machine's status reads "A <cargo> capsule is
  waiting at the <port cargo> input", as vanilla's does.
- **Belt output.** No library code moves cargo out: vanilla's `EmitProduct` (every 0.35 s) already puts a modded
  machine's output on the belt joined to its output port through `EmitCargo`, one 1 kg canister per call, about one
  every 0.7 s while the belt's entrance is clear, whatever the cargo's `UnitKilograms` (grit's is 3). With no belt
  joined, the output slot keeps its cargo and the machine's status reads
  "Connect a belt to the <product> output", set by vanilla. An `EmitCargo` postfix names the machine from the belt's
  start (`MachineWithPort(start, true)`, cached per belt) and raises `CargoEmitted`. Belts are vanilla records (their
  endpoints only) and survive a save and reload as they are; modded canisters riding them are kept in the sidecar and
  come back on the belt with their cargo.
- **Power.** The game's power solve never sees a modded kind; a `SolvePower` postfix attaches modded machines and fuel
  generators to grids afterwards. Rules in [Power](#power).
- **Co-op handshake.** Each player publishes a manifest of its registered structures and cargo; a client refuses to
  join a host whose StructureLib mods differ. Rules in [Co-op handshake](#co-op-handshake).
- **Safety.** If game members the library relies on are missing (a game update), it logs them and disables itself. A
  sidecar failure is logged and never stops the vanilla save or load.

The recon behind all this, with the evidence, is in
[`research/STRUCTURES_RECON.md`](../../research/STRUCTURES_RECON.md).

## Belts, ports and models in short

- **Ports are definition data.** Declare them in `Ports`, or author them in the model as `Port_<name>` empties
  (`kit.port(name, position, direction, output, cargo)`); a model's ports win. Every port flows along the structure's
  local +X like the vanilla donors' (inputs at the −X end, the output at the +X end), so a machine whose output feeds
  another stands on that machine's −X side.
- **In:** a belt ending at an input port hands its canisters to the first Input slot of the port's cargo
  (`CargoEntered`). **Out:** vanilla puts the Output slot's cargo on the belt joined to the output port, 1 kg per
  canister (`CargoEmitted`). Both survive a save and reload, canisters on the belt included.
- **Models:** `Model = "<file>.bin"` beside the DLL replaces the donor's look on the donor's template (colliders (unless `Footprint` is set),
  views and scripts kept). Its lowest vertex sits on the structure's floor, its materials start as tinted stand-ins
  and become the game's own within seconds of a world load, and the donor's animated effects are not carried over.
  SandPower's oven and generator are the worked example: a vanilla pressure chamber's sand rides a belt into the
  oven, whose coalite rides a belt into the generator (self-test `sandpower-chain`).

## Routers

A router is a structure that passes canisters between belts without processing them: a compact splitter or merger.

```csharp
MoonStructures.Register(new StructureDefinition
{
    Id = "you.box-splitter", Name = "Box splitter", Category = "Logistics", Price = 5m,
    BaseStructure = 4,                                   // the floodlight: no game script, one collider
    Router = new RouterDefinition { Mode = RouterMode.Splitter },
    Footprint = new Vector2(1.4f, 1.4f),
    Ports = new()
    {
        PortDefinition.In("In", Cargo.Dust, new Vector3(-0.70f, 0.85f, 0f), Vector3.right),
        PortDefinition.Out("Forward", Cargo.Dust, new Vector3(0.70f, 0.85f, 0f), Vector3.right),
        PortDefinition.Out("Left", Cargo.Dust, new Vector3(0f, 0.85f, 0.70f), Vector3.forward),
        PortDefinition.Out("Right", Cargo.Dust, new Vector3(0f, 0.85f, -0.70f), Vector3.back),
    },
}, Guid);
```

- **Definition.** `Router` with no `Processor` and no `Power` (either drops the structure at freeze). The floodlight (4)
  is the tested donor: no game script and one collider. Do not use the vanilla splitter or merger (11, 12): their
  prefabs carry `MoonMachineBelt` and ten colliders.
- **Vanilla still sees a router.** `MoonWorld.Routes(def, kind)` keys on the definition's ports, so any clone with three
  or four ports answers true whatever the donor (the floodlight answers false, its 4-port router clone true). The hop is
  still StructureLib's: the `HandOff` prefix answers for a router's input belt before vanilla's routing runs, and never
  hands it on: with the input port unresolved (no view yet during a load, none within tolerance) or a resolve that
  throws, the canister waits. Other
  `Routes`-gated vanilla paths on a placed box, cut-in offers from the belt tool among them, are untested (by-hand list);
  `Routes` itself must not be prefixed (a static leaf like `IsProcessor`).
- **Ports.** A splitter has exactly one input and one to three outputs; a merger one to three inputs and exactly one
  output; names unique (else the structure is dropped, logged). Router ports carry no cargo: a declared port's `Cargo`
  is ignored and a model's `Port_*` node needs no `cargo` property; each is written into the clone as `sl:0:<Name>` (dust,
  which is what vanilla's own splitter ports answer). Any cargo passes, vanilla or modded, with its kilograms, capacity
  and owner. Directions are the belt's flow: inward at inputs, outward at outputs.
- **Round-robin.** In the resolved port order (`PortsOf(def)`: the declared list, or a model's nodes in file order,
  which the exporter writes alphabetically). A splitter gives each canister to the next output in turn whose belt
  exists and whose entrance is clear, skipping the others; when none can take it, it waits at the input belt's end and
  the belt queues behind it. A merger serves its inputs in turn among those with a canister waiting; when the output
  belt is missing or blocked, everything waits. The turn is in memory only: it resets on load (and any world change) and
  goes with its router when that is removed; building or removing belts and structures elsewhere keeps it. One hop per input belt per frame; the output belt's entrance spacing bounds the rate.
- **Two differences from vanilla.** No cut-in: a router is placed free-standing and belts are joined to its ports.
  Canisters do not ride across the box: one vanishes at the input belt's end and a new canister (a new id) appears at
  the output belt's start in the same frame.
- **Events.** `CargoRouted` per hop (from and to belt ids). `CargoEntered` and `CargoEmitted` are not raised for
  routers.
- **Save.** Nothing new: the router's depot is in the sidecar, its belts are vanilla, canisters ride the belts.
- **Co-op.** As belt input: the host decides; clients untested.
- **Side outputs need room.** The belt tool needs a few metres for a belt that turns 90° or more out of a side port.
  Between two 1.4 m boxes in line, a side-to-side U-belt is refused at 1.3 m apart ("Turn too tight: give it more
  room.") and builds at 5 m apart (a 9.81 m belt each side).
- **Removing things.** Removing a router keeps its belts: they end (or start) at nothing, their canisters wait at the
  open end, and they save and reload that way. Removing a belt drops its canisters on the ground in its path, and the
  game then refuses a new belt along that path ("Blocked by an object.") until they are picked up. The game saves a few
  seconds after a removal.
- **Heavier canisters.** A canister of more than 1 kg hops whole (a 3 kg grit canister came out as 3 kg, capacity 3).

## Dev builds

A dev build (the default; `package.ps1` builds release DLLs with `-p:DevBuild=false`) adds a `[Dev]` section to
`BepInEx/config/com.ryanhartwig.structurelib.cfg` and an in-game self-test that logs `[selftest] PASS|FAIL <name>`
lines and `[selftest] done: N passed, M failed`. Leave the switches at their defaults outside a test run.

| Switch | What it does |
|---|---|
| `AutoLoadSlot`, `AllowSlot1`, `SelfTest` | Load a save slot from the main menu (slot 1, the real save, only with `AllowSlot1`) and run the self-test 20 s after the world loads. |
| `AutoBuild` | Comma-separated structure ids or vanilla kind numbers, built 10 s after load through the game's `TryBuildDepot` (`AutoBuildViaPlan`: through the build menu's own path instead). |
| `AutoBuildLayout` | `ring` (default): 10 m from the walker at 0°, 90°, 180°, 270°, 45°, … from its facing. `line`: in entry order along the structures' +X, `AutoBuildGap` (1.3 m) between neighbouring footprints (centres from each definition's half footprint), centred on the walker and 6 m to its side (10 m spacing and 10 m to the side put the end entries 18 m away, which the game refuses: "Move closer to the build site."), so each entry's output faces the next entry's input; use it for chains of three or more machines. |
| `AutoBuildGap` | With `line`: the metres between neighbouring footprints. One number is every gap; a comma-separated list gives the gaps in entry order, its last number repeating (`1.3,1.3,5,1.3` puts 5 m between the belt boxes of the chain below and keeps the ends within reach). |
| `AutoBelt` | `';'`-separated `<from>:<port>-><to>:<port>` belts between AutoBuild's depots, built 3 s after it through `TryBuildConveyor`. A port is a definition name (`IN_SAND`), a modded port's declared name (`Sand in`), a unique `input`/`output` label, or an index. |
| `AutoFeed` | `';'`-separated `<id or kind>:<cargo>:<kg>`, loaded through `CrewProcessorLoad` 5 s after AutoBuild. |
| `AutoSave`, `AutoRemove` | Request a save 10 s after AutoBuild and check the sidecar; remove the example lamp after its checks (false keeps it for a reload run). |
| `SunOverhead` | Set the lunar clock so the sun stands over the walker 5 s after load (the clock runs in real time, so a copied save is often at night and solar panels give nothing). Belt chains fed by a vanilla machine need it. |
| `DumpCards` | Write every build-menu card picture to `BepInEx/cache/structurelib/cards/` 30 s after load (`model-card` compares them). |
| `BreakModelPath` | Point every `Model` at a missing file (self-test `model-missing-falls-back`). |
| `RegisterBadPorts` | The example registers a structure whose port cargo no slot holds; the registry must drop it (`ports-rules-logged`). |
| `RegisterBoxProbe` | The example registers `ryanhartwig.example-box-probe`, a box splitter router (floodlight donor, 1.4 m footprint, In / Forward / Left / Right); 15 s after load its definition, view and colliders are logged and three dust canisters are put on the AutoBelt belt into it and followed for 10 s (CargoRouted count, riders). |
| `ForcePower`, `ScreenshotMenu` | Run modded processors at full power whatever the grid gives (isolating power problems only); screenshot the build menu after the self-test. |
| `ForceArcs` | Draw every fuel network's power arcs every frame, without the build-tool and aim gate (`arc-drawn`; it also empties full outputs of the example grinder and of pressure chambers within 20 m of the burner so something draws power). |
| `PowerUxProbe`, `PowerUxTest`, `PowerUxTeleport`, `PowerUxHome` | The power-UX recon probe (`DevPowerUx.cs`, `[powerux]` lines): traces the panel, solve, arcs and canister views and tours the AutoBuild depots with screenshots to `BepInEx/cache/structurelib/powerux-<n>.png`; `PowerUxTest` picks experiments (`fuel`, `link`, `anchor`, `arc`: the real arc gate, `tint`, `far`, `gates`: the panel with the pause menu open and in a vehicle); `PowerUxTeleport` / `PowerUxHome` (with the probe) move the walker before AutoBuild (to a free spot, or to a point). |

Checks whose setup a run lacks log `[selftest] ... not applicable` (an Info line, not a failure), and only then:
- `power-burner-supplies`, `power-grinder-runs-on-burner` and `power-no-burn-when-full` when the burner has no
  `AutoFeed` entry and is fed through a router (`power-node-registered` and `power-grid-joined` still run; a burner
  fed straight by a machine's belt with no `AutoFeed` still fails all five);
- `belt-chain-feeds` and `belt-save-clean` when the grinder's belt ends at a router;
- `belt-in-refuses-cargo` when no sand canister rode the belt into the grinder in its 90 s window (the chamber made
  none);
- every `beltboxes-*` check when BeltBoxes is not installed.

Power UX checks (`DevPowerChecks.cs`), one line each:
- `panel-generator`: the burner's panel has its own name, a `FUEL POWER` kicker and the rows OUTPUT, BATTERY, FUEL,
  POWERING (not applicable without a burner).
- `panel-machine`: the grinder's panel has a `PRODUCTION` kicker, a grit row and CYCLE, no POWER row without power
  and a POWER row first with it (not applicable without a grinder).
- `panel-vanilla-untouched`: a pressure chamber still gets vanilla's own panel (not applicable without a chamber).
- `power-vanilla-fed`: a pressure chamber AutoBuild built out of solar reach is raised to full power by fuel, says
  `Powered by ...`, runs, and the feeding generators' charge falls (not applicable unless AutoBuild names `96` and the
  burner and both are built, or when vanilla already powers the chamber).
- `arc-drawn`: with `ForceArcs`, `PowerLinksView` draws at least one arc within 60 s (not applicable without
  `ForceArcs`, or when neither `power-vanilla-fed` nor `power-grinder-runs-on-burner` runs).
- `cargo-tint`: after the other checks, a ground dust canister is made grit with the walker away, its near view reads
  grit's tint, dust views read white, a pooled view that showed grit reads its next canister's own colour, and the
  canister goes back to dust (it moves the walker 250 m away twice and puts it back). With grit registered with a
  `CanisterModel` (the example's `example_grit.bin`), the grit view must also carry its `StructureLibCargo_<value>`
  holder with every capsule renderer disabled, every other view the capsule with no holder on, and a view that showed
  the model must later show the capsule again.

The belt checks need their own configs: belt input with `AutoBuild = 96,ryanhartwig.example-grinder,94`, `AutoFeed =
96:Dust:10`, `AutoBelt = 96:output->ryanhartwig.example-grinder:Dust in` (`ring`, `SunOverhead`; `belt-canister-modded`
needs the long ring belt, 15 m); belt output and power with `AutoBuild = ryanhartwig.example-grinder,ryanhartwig.example-burner,ryanhartwig.example-lamp`,
`AutoFeed = ryanhartwig.example-grinder:Dust:6;ryanhartwig.example-burner:ryanhartwig.example-grit:4`, `AutoBelt =
ryanhartwig.example-grinder:Grit out->ryanhartwig.example-burner:Grit in`, `SunOverhead = true`, `ForceArcs = true`
(`arc-drawn`), `DumpCards = true` (`model-card`). In one combined run the chamber's sand queues on the grinder's belt and the belt-input checks fail.

Every standard config wants a fresh slot-2: with the game closed, replace
`%USERPROFILE%\AppData\LocalLow\DragDropGames\MoonSweepers\MoonSessions\slot-2` with
`..\.scratch\slot-2-backup-task11` (a clean copy of slot-2 kept outside the repo) before the run; a reload run takes the slot its chain run saved
instead. The game autosaves into the loaded slot, so a used slot keeps earlier runs' depots: AutoBuild reuses them,
their full outputs stop the machines, and a ring site next to them refuses the belt ("Turn too tight").

**Belt boxes** (`DevBoxes.cs`, with [BeltBoxes](../../mods/BeltBoxes) installed). Chain run: `AutoBuildLayout = line`,
`AutoBuildGap = 1.3,1.3,5,1.3`, `AutoBuild = 96,ryanhartwig.example-grinder,ryanhartwig.box-splitter,ryanhartwig.box-merger,ryanhartwig.example-burner`,
`AutoFeed = 96:Dust:12`, `AutoBelt` from the chamber to the grinder, the grinder to the splitter's `In`, the
splitter's `Forward`/`Left`/`Right` to the merger's `Back`/`Left`/`Right`, the merger's `Out` to the burner, with
`AutoSave`, `DumpCards` and `SunOverhead`. A feeder adds 1 kg grit canisters to the splitter's input belt while there
is room, and the burner's grit slot is emptied at 5 kg. In order: `beltboxes-registered`, `beltboxes-model` (both
boxes: holder, renderers, one box collider, ports at the model's nodes), `beltboxes-split-even` (12 hops, a third ± 1
each), `beltboxes-merge-fair` (the merger held by a sand canister until two canisters queue at the end of every input,
then six hops in turn order), `beltboxes-conserve` (grit in == grit out to 0.01 kg after the merge window),
`beltboxes-heavy` (a 3 kg canister hops and rides on at 3 kg, capacity 3), `beltboxes-split-skips` (Forward belt
removed: six hops to Left/Right only, none lost; the canisters it dropped are removed and counted, and the belt is
rebuilt), `beltboxes-save-clean` (a save with canisters stopped at the end of the splitter's input and of all three
middle belts, plus the grit balance over the whole run to 0.01 kg). Reload run (`AutoBuild`, `AutoFeed`, `AutoBelt`
empty, the chain run's slot): `beltboxes-reload` (both boxes, all five belts at their ports, every saved grit canister
restored and accounted for, the sand holder removed, then a merger hop or burner intake within 20 s), `beltboxes-model`
(both boxes), and last `beltboxes-remove-clean` (the merger removed with a canister stopped at the end of its Back
belt). Removal runs last because the game saves a few seconds after it.

## Status

| Area | Works (checked in game, single player) | Unsupported | Untested |
|---|---|---|---|
| Registration | Hashed kinds, freeze, donor and cargo checks, refusal messages | Foundations, decks, building parts and catwalk supports as donors | |
| Build menu | Catalog entry, note, rendered card art; placement through the menu's own path (`PlanStructure`, site check, `TryBuildStructure`, which records a free pose) saves and reloads cleanly with its pose (dev `AutoBuildViaPlan`) | | Placing a structure by hand (the real ghost and the remove tool) |
| Build and remove | Build, refund, removal, `Built` and `Removed` | | |
| Save | Sidecar for depots (pose included), production states and modded-cargo canisters; orphans kept; reload restores (see [Save audit](#save-audit)) | A modded robot dock's robots-off setting (resets on load); a modded garage holding vehicles | |
| Cargo | Names, unit mass, price, canister round trip, modded canisters riding belts (see [Cargo audit](#cargo-audit)); `CanisterTint` on the near canister view, a pooled view reused for another cargo getting its own colour back (`cargo-tint`); `CanisterModel` on the near canister view (the capsule's renderers off, its colliders kept) and the capsule back on a reused view (`cargo-tint` with the example grit lump) | Depositing modded cargo at the collector; co-op cargo events; `CanisterTint` and `CanisterModel` on far (instanced) canisters, which stay dust | Tint and model at the player's eye in real lighting (checked on screenshots only) |
| Processors | Cycle, batch moved at the same mass, `CycleFinished`, loading through `CrewProcessorLoad`; belt input through a declared port (`HandOff` prefix, `CargoEntered`; a canister of another cargo, or one arriving while its slot is full, waits at the belt end); belt output (vanilla `EmitProduct`/`EmitCargo`, 1 kg canisters, `CargoEmitted`); machine-to-machine chains by belt (grinder to burner; SandPower's vanilla pressure chamber to oven to generator, self-test `sandpower-chain`), saved and reloaded with canisters on the belts | Walker loading (`LoadProcessor` refuses: "Not a production machine"); a structure without a processor taking belt input | Belt input in co-op (the host decides; clients untested) |
| Ports | Declared ports (and a port on a port-less donor) written into the cloned definition as `sl:<cargo>:<Name>`; `PortCargo` answers the declared cargo; the game shows "input"/"output" and "<name> input/output"; refusal at freeze for a port cargo no slot holds | | Belts placed by hand onto modded ports (dev `AutoBelt` builds through the game's `TryBuildConveyor`); ports inherited from a donor (no example uses them) |
| Models | `Model` file on the donor template (donor renderers hidden, colliders kept unless `Footprint` is set), ports from its `Port_*` nodes, card picture rendered from it (the example burner); a missing file falls back to the donor model and declared ports | The donor's animated effects on a custom model | Donors with a non-unit `scale` or a skinned mesh (none of the donors checked so far has either) |
| Routers | Splitter and merger hops on the `HandOff` prefix with `CargoRouted` per hop: round-robin over three outputs (12 hops, 4 each), skipping a removed output belt with nothing lost, mergers serving three inputs in turn, a 3 kg canister hopping whole, grit balanced to 0.01 kg through both boxes (BeltBoxes self-test, `beltboxes-*`); both boxes, five belts and every canister saved and reloaded; `Footprint` written into the clone (`halfFootprint` 0.70 × 0.70) with the collider swap under a `Model`; side-port U-belts (5 m between boxes); routers, belts and waiting canisters saved and reloaded, a router removed mid-traffic with its belts kept | Cut-in onto an existing belt; canisters visibly crossing the box | Belts placed by hand onto router ports (0.7 m port spacing); vanilla paths gated by `Routes` (true for a router clone), cut-in offers among them; routers in co-op (the host decides; clients untested) |
| Power | Poles linking within `LinkRange` into grids, members attached to the nearest pole whose `SupplyRadius` reaches them (pure `PoleGraph`, `GridSolve` and `PowerRules` under xunit); the post-solve attach, vanilla top-up, sidecar bank charge, panels and arcs are built, with the in-game self-tests (`grid-*`, `power-*`, `panel-*`, `arc-*`) written but not yet run | The sun factor against vanilla's curve; the yaw/rotation of `FootprintDistance` away from the base; bank charge across a save; fuel and banks in co-op | Everything in game, and the two-player session (see the PR test plan) |
| Co-op | Manifest built and compared with itself | Graceful degradation for a client missing a mod | Both two-player cases (identical mods; client missing a mod) |

## Power

Modded power is a grid of poles, Factorio style. Poles link into a grid; a pole powers the modded sources, banks and
machines in its supply radius; each solve the grid pools its supply, sums its demand and gives every consumer the same
share. Sources never power anything directly. The attach runs after the game's own solve (`SolvePower` postfix); the
game's solve never sees modded kinds, and vanilla solar panels and their panel-powers-neighbours behaviour are left
exactly as they are. Design and reasons: `docs/superpowers/specs/2026-11-08-power-grid-design.md`,
`docs/DECISIONS.md`. Evidence: `research/STRUCTURES_RECON.md` ("Processors (Task 10)", "Power sources (Task 11)"),
`research/POWER_UX_RECON.md` (panel, arcs, sun, co-op).

- **Roles** (`PowerDefinition.Role`): **Source** (`SupplyMode.Solar`: `PeakKw`; `SupplyMode.Fuel`: `PeakKw` plus a
  `ProducesPower` processor), **Relay** (a pole: `LinkRange`, `SupplyRadius`) and **Storage** (`BatteryKwh` capacity,
  `RateKw` in and out). `BatteryKwh` is ignored for a Fuel source. A consumer is a `Processor` with `RunningKw`.
  `MoonStructures.RegisterStructure` validates the role's numbers (`PowerRules`) and refuses nonsense with a logged
  reason.
- **Grid rules.** Two poles link when their centre distance is at most the larger of their `LinkRange`s; the connected
  poles are one grid, and its id is `-(lowest pole depot id)`. A member (source, bank, consumer, vanilla machine)
  attaches to the nearest pole whose `SupplyRadius` reaches its footprint, measured with the game's
  `MoonWorld.FootprintDistance` (sphere-correct; a pure fallback exists). A member is in at most one grid. With no pole
  in reach a consumer is unpowered, a source idle and a bank inert. The graph is rebuilt only when the depot set or the
  loaded world changes; the per-solve work is the sums and writes.
- **Supply order, per grid** (Factorio's): solar first (each array `PeakKw x SunFactor`), then battery discharge
  (split by stored charge, each at most `RateKw`), then fuel for the remaining deficit only. Solar surplus charges the
  banks (split by free capacity, each at most `RateKw`). Fuel never charges a bank. One satisfaction per grid,
  `supply / demand` capped at 1, the same for every consumer: no favourites by distance, build order or kind.
- **Fuel sources have no kWh buffer.** A furnace offers `PeakKw` while a coalite burns (`Running`), and a burn starts
  only when the grid's last solve still had a deficit after solar and storage (a host-side gate, `GridState.WantsFuel`).
  Reason: every peer runs the solve and the host streams only Running, Progress, Cycles and stores, so a charge raised
  by a cycle event would never reach a client. Cost: a source can waste up to one burn at low demand. A modded
  machine's demand counts while it is Running or its next cycle is ready, so a fuel-only grid can bootstrap.
- **Sun factor** (provisional, read from the game's machine code, not yet measured): `s = SunDirectionAt(LunarTime)`,
  `h = dot(up, s)`, `d = smoothstep(clamp01((h + 0.0096) / 0.0192))`, `factor = max(0, dot(s, n)) x d`, with `n` the
  array's up; the fallback is `MoonSolarSystem.Daylight`. The dev command `[Dev] LogSun` samples it against a vanilla
  panel.
- **Vanilla top-up.** Vanilla's solve runs first. A consumer a pole reaches, vanilla or modded, gets
  `Power = vanilla share + (1 - vanilla share) x grid satisfaction`; the grid never lowers a vanilla machine, and an
  idle consumer on a live grid reads 1. A modded machine within a vanilla panel's range still runs on that panel's sun
  surplus and batteries first (`GridShare`, with the charging take-back). `Power`, `UsedKw`, `Grid` and the status text
  are rewritten every solve.
- **Leaf answers.** `IsPowerNode` is true for every modded depot with a `Power` definition, so poles, arrays and banks
  get the look-at panel; `IsSolarPanel` is false for every modded kind, so vanilla panel code and arcs ignore them;
  `IsProcessorId` is true for Relay, Storage and Solar depots, so each gets a production state (created after a load if
  missing). A pole's state carries the grid totals it shows.
- **Look-at panels** (`Patches/PanelPatches.cs`, texts from `Pure/PanelText.cs`): a pole shows `POWER GRID  ·  <n>
  POLES` with SUPPLY, DEMAND, STORAGE and SATISFACTION and a state (`Powering <k> machines`, `Grid short: <p> %`, or
  `Grid powered` on a client); a solar array `SOLAR POWER` (`Sun <p> %` or `Night`; OUTPUT, GRID); a bank `POWER
  STORAGE` (Charging, Discharging, Full, Empty, Idle; STORED, GRID); a fuel generator `FUEL POWER` (OUTPUT, FUEL,
  GRID; Burning, No fuel, No pole in reach, `Standing by: grid has power`). Machine statuses read `Powered by the
  grid`, `Grid short: <p> %` and `No power: build a power pole within <r> m` (`r` is the largest registered Relay
  `SupplyRadius`). Routers show no panel; vanilla structures keep their own.
- **Arcs** (`PowerLinksView.cs`, the library's own `MoonPowerLinks`). With the build tool held and a grid member aimed:
  pole-to-pole arcs, thinner pole-to-member arcs, and a `SupplyRadius` ring at the aimed pole (or the pole reaching the
  aimed member). Placing a Relay previews arcs from the ghost to every pole it would link, plus its ring. Nothing is
  drawn for vanilla grids.
- **Multiplayer: no host gate.** Verdict S (`research/POWER_UX_RECON.md` section F): every peer runs `SolvePower` over
  the replicated depot set, so every peer computes the same grid. Bank charge integrates on each peer, as vanilla
  batteries do; the host's save restores it through the StructureLib sidecar (a charge-only record). A client starts a
  bank at 0 until it joins fresh, a display-only divergence; fuel is host-gated so burns agree. The two-player session
  is still to be run.
- **Not supported / known limits:** a client's bank charge starts at 0; the sun formula is unmeasured; the rebuild runs
  whenever the depot set or loaded world changes; a fuel source can waste up to one burn at low demand; vanilla panels
  and batteries are not part of a modded grid (no energy moves between them and a pole grid); a source or bank without
  a pole in reach does nothing; a `Power` definition without a `Processor` only works for poles, arrays and banks
  (which the library gives a state itself); a look-at panel for routers.
- **A fuel source's `RunningKw` is ignored:** it always reads `Power` 1 and `DemandKw` 0 and never draws from the
  grid. Leave `RunningKw` 0 on a `ProducesPower` processor.
- **Do not patch `MoonWorld.TryPanel`:** a Harmony prefix on it killed the game natively after world load. See
  `research/STRUCTURES_RECON.md`, "Interop hazards found".
- The battery bank (kind 6) prefab has no production view; use `MoonWorld.SolarPostKind` (93) as a generator's
  `BaseStructure`, as the example burner does.

## Co-op handshake

On a join, the client refuses to enter a crew whose host has different StructureLib mods. Code: `Handshake.cs` (game
side) and `Pure/HandshakePayload.cs` (the registry-agnostic payload). Why this hook: `research/STRUCTURES_RECON.md`,
"Co-op session (Task 12)".

- **Channel, two levels:** key `structurelib.manifest`, value the base64 of `HandshakePayload.Encode` (magic `SLH`,
  then a deflated body: protocol, structures, cargo). Every StructureLib player in a lobby writes it as its lobby
  member data (`SetLobbyMemberData`); the host also writes it as lobby data (`SetLobbyData`), which a client can read
  before it is a member, as vanilla's listing version is. Every 2 s while `MoonSteamLobby.InLobby` each value is read
  back and written again if Steam does not hold it (a dropped write is retried). A manifest of 8192 characters or more
  is published as the marker `too-big` instead, so a client can tell "too big" from "absent". The game's own session
  messages and its `MoonCrewSession.Protocol` byte are untouched. Outside a lobby nothing runs.
- **Manifest:** one entry per registered structure (id, kind from the id hash, owning plugin GUID, that plugin's
  version from its BepInEx metadata) and per registered cargo (id, byte value, owner, version).
- **Client check:** a prefix on `MoonCrewSession.Offered` (the host's world offer, before any world byte is
  downloaded) reads the host's lobby data, then its member data, and compares that manifest with its own. On any problem
  it calls the session's own `EndJoin(join, line, leave: true)` and skips the offer.
- **Admit on uncertainty** (`HandshakePayload.ReadHost`): nothing published counts as a host without StructureLib
  (an empty manifest, so a client with mods is refused) only when the client is verifiably in the host's lobby
  (`MoonSteamLobby.InLobby && MoonSteamLobby.LobbyId == join.lobby`); Steam returns member data only to members.
  Outside the lobby, for the `too-big` marker, or for an unreadable value, the client logs a warning naming the cause
  and the join goes ahead unchecked. Any exception is logged and the join goes ahead unchecked too.
- **Host check:** the host compares each crewmate's manifest once per change, logs the problems and shows them with
  `MoonCrewSession.Notify`. It does not kick: the crewmate's game refuses itself on the same messages. A crewmate
  without StructureLib publishes nothing; the host logs that once and lets them in.
- **Messages** (`HandshakePayload.Compare(host, client)`, one per problem, all logged):
  - `missing mod <Owner> <Version> (structure <Id>)`, or `(cargo <Id>)`: the host has it, the client does not.
  - `version mismatch <Owner>: host <v> client <v>`
  - `cargo value conflict <Id>: host <n> client <n>`
  - `extra mod on client <Owner>`: the host would not render the client's structures.
  - `handshake protocol mismatch: host <n> client <n>` (alone; nothing else is compared).
- **Refusal line** shown by `EndJoin`: `Mods differ from the host's: ` and the first three messages joined by `; `,
  then `(+N more in BepInEx/LogOutput.log)` when there are more.
- **Untested:** both two-player cases (identical mods: the join succeeds and a host-built modded structure appears on
  the client; client without ExampleStructure: the join is refused with the `missing mod` message) need a second
  machine and have not been run. Whether `Offered` is reached on every join path (gathered lobby and listed shift) is
  read from the interop signatures, not traced. Only the single-player self-test `handshake-manifest` (live manifest,
  encode/decode, self-compare) has run.

## Save audit

The game writes the save through `MoonProgressStore.Serialize` (core sections, then one saver per section) and reads
it straight back; a section that names a depot or canister the save does not hold, or a kind it does not know, makes
it refuse the whole save. The modded depots therefore leave the save snapshot in the `MoonDepotSave.Write` prefix and
stay out until the `Serialize` finalizer, so every later saver sees only the depots the save holds. The build menu
records a free pose for what it places (`placedPose`), which `MoonStructurePoseSave` writes per depot; before this the
depots came back right after `MoonDepotSave.Write` and every save after a menu-placed modded structure was refused
("Structure pose ID"). The pose is part of the depot record, so the sidecar keeps it. Evidence and dumps:
`research/STRUCTURES_RECON.md`, "Per-depot savers audit".

| Saver | Names | Status |
|---|---|---|
| `Serialize` core (`canisters`, piles, inventories, vehicle storage, wallets, upgrades) | canister id | modded-cargo canisters stripped to the sidecar; the rest vanilla-safe |
| `MoonDepotSave` | depot id | stripped to the sidecar |
| `MoonStructurePoseSave` (derived from `depots`) | depot id | stripped with the depots; pose kept in the sidecar depot record |
| `MoonProductionSave` | depot id | stripped to the sidecar |
| (`cargo`) | canister id | stripped to the sidecar with the canisters |
| `MoonStructureAnchorSave` (`anchors`) | own id and kind | an anchor of a modded kind is left out of the vanilla save and put back after; not kept across a reload (none seen) |
| `MoonRobotSettingsSave` (`robotsOff`) | depot id | an entry naming a modded depot is left out and put back after; unsupported: a modded robot dock's robots-off setting resets on load |
| `MoonGarageSave` (garage depots) | depot id | sees no modded depot; a modded structure borrowing the garage is unsupported (untested) |
| `MoonOwnedVehicleSave` (`garage`) | depot id | unsupported: a vehicle kept in a modded garage would name a stripped depot (untested) |
| `MoonConveyorSave` | nothing (belt endpoints) | vanilla-safe: a belt between two modded machines saves and reloads as it is, with modded canisters on it restored from the sidecar (self-test `belt-save-clean`) |
| `MoonLoadoutSave`, `MoonVehicleStorageSave`, `MoonToolContainerSave`, `MoonUpgradeTreeSave`, `MoonPlayerRecordSave`, `MoonScoreSave`, `MoonRadioSave`, `MoonRegionTouchSave`, `MoonWaypointSave`, `MoonLandmarkSave` | nothing | vanilla-safe: no depot id, canister id or cargo member |
| (`premiums`, `station`) | unchecked | unknown; no modded structure is a station |

## Cargo audit

The game's `MoonCargo` is a byte enum (Dust 0, Sand 1, Stone 2), and vanilla code answers every value it does not
know as dust. A modded cargo (`MoonStructures.RegisterCargo`, values 3..255) is only as good as the members that see
it. This table lists every `MoonCargo`-typed member (and every member that carries a cargo byte) in the dumped game
types, and what a modded value gets there.

Status:
- **patched**: StructureLib answers for a registered modded value (guard: registry frozen, not disabled, value registered;
  anything else runs vanilla).
- **vanilla-safe**: vanilla stores or passes the value through unchanged, checked or argued below.
- **unsupported**: a modded value is not handled yet; what happens is noted.
- **unknown**: not checked.

| Member | Status | Notes |
|---|---|---|
| `MoonWorld.CargoName(MoonCargo)` (static) | patched | `CargoDefinition.Name`. Self-test `cargo-modded-name`; an unregistered value (251) still answers "moon dust" (`cargo-unregistered-is-vanilla`). |
| `MoonWorld.CargoShortName(MoonCargo)` (static) | patched | `CargoDefinition.ShortName`. Checked in `cargo-modded-name`. |
| `MoonWorld.UnitKilograms(MoonCargo)` (static) | patched | `CargoDefinition.UnitKilograms`. Vanilla answers 1 for an unknown value; example grit is 3 kg so the self-test `cargo-modded-kg` (expects 3) proves the prefix. |
| `MoonWorld.CargoValue(MoonCargo)` (instance, `Decimal`) | patched | `CargoDefinition.Price`, converted exactly (decimal bits). Self-test `cargo-modded-value`. |
| `MoonDroppedCanister.cargo` | vanilla-safe | Holds the byte as given: a canister built with 250 reads 250, and `MoveCanisterRecord` stores it in `world.canisters` unchanged. |
| `MoonDroppedCanister.IsDust` (getter) | vanilla-safe | Not patched: false for 250, true for Dust (self-test `cargo-isdust-false`). |
| `MoonDroppedCanister(..., MoonCargo cargo)` (constructor) | vanilla-safe | Stores the value. |
| `MoonLooseDustSnapshot.cargo` / `MoonCargoSnapshot.cargo` (byte, per non-dust canister) | patched | Filled by `SnapshotProduction`. Modded entries (and their canisters) are cut out of the vanilla save by the `MoonProgressStore.Serialize` prefix and kept in `structurelib.json` by cargo id; put back after the write. |
| `MoonCanisterSnapshot` | n/a | Has no cargo member; a modded-cargo canister is saved as a sidecar `CanisterRecord`. |
| Canister cargo on load (`RestoreCanisters`, `RestoreProduction`) | patched | Vanilla restores an out-of-range cargo as dust. StructureLib injects sidecar canisters in a `RestoreCanisters` prefix and sets their cargo in a `RestoreProduction` postfix through `MoveCanisterRecord`. |
| `MoonWorld/ProcessorSpec.slotCargo` (`MoonCargo[]`) | patched | A modded processor gets its own spec (`SpecBuilder`, answered by the `Processor(kind)` prefix); its `slotCargo` holds the slot cargo bytes, modded values included. The production sidecar records slot cargo by id (`SlotCargoIds`). Self-tests `processor-spec-slots`, `production-sidecar`. |
| `MoonWorld.InputSlot(int kind, MoonCargo)` (static) | patched | For a modded kind: the first Input slot holding the cargo, else -1 (a modded structure without a processor: -1). Vanilla kinds still answer as vanilla. Self-tests `inputslot-accepts-dust`, `inputslot-refuses-dust`. |
| `MoonWorld.PortCargo(ConveyorPort)` (static, returns `MoonCargo`) | patched | Takes the definition's port; a prefix answers the cargo encoded in an `sl:<cargo>:<Name>` port name. Belt input does not consult it (recon fact (a)). |
| `MoonWorld.HandOff(belt, path, id)` (instance, returns bool) | patched | Belt input. Vanilla refuses every modded machine by kind; a prefix takes a canister of the port's cargo into the first Input slot with room (`BeltIntake`), removes or reduces it, raises `CargoEntered`; a refused one waits at the belt end. Self-tests `belt-in-accepts`, `belt-in-waits-when-full`, `belt-in-refuses-cargo`. |
| `MoonWorld.CrewProcessorLoad(player, machine, MoonCargo, kg)` | patched | Vanilla does nothing for a modded machine (no `InputSlot` call; checked in game). A prefix loads the first Input slot that takes the cargo, up to its capacity, under the player's share (`MoonWorld.Put`). Self-test `feed-lands-in-slot0`. Like vanilla it caps at capacity and never touches the walker's cargo (the walker path, `LoadProcessor`, deducts itself and does not call it). `LoadProcessor` refuses a modded machine ('Not a production machine'). Belts do not come through here: belt input is `HandOff` (see How it works; vanilla never called `FeedProcessor` or `EnterMachine` in the recon). |
| `MoonWorld.EmitCargo(outfeed, owner, MoonCargo, kg, out reason)` (instance) | vanilla-safe | Reached for modded machines: vanilla `EmitProduct` calls it with a connected outfeed belt, 1 kg per call, and the grinder's grit canisters ride the belt with cargo 250 (recon "Belts and ports", facts (b), (c), (f)). A postfix (log and event only) raises `CargoEmitted` for a canister a modded machine put on its belt. Self-tests `belt-out-emits`, `belt-chain-feeds`, `belt-canister-modded`. |
| `MoonWorld.CrewCargoDeposit(player, MoonCargo, kg)` | unsupported | Deposit at the collector; pays through `CargoValue` (patched) but the rest of the path is not checked. Do not rely on depositing modded cargo yet. |
| `MoonWorld.AddPremium(player, MoonCargo, kg)` / `premiums` / `MoonPremiumSnapshot` | unknown | `MoonPremiumSnapshot` has no cargo member (station, player, kilograms); how `AddPremium` weighs a modded value is not checked. |
| `MoonWorld.LocalProcessorLoad` (`Action<int, MoonCargo, double>`) | unsupported | Co-op wire event. A modded value is not sent or received by StructureLib; clients without the mod would see dust. |
| `MoonWorld.LocalCargoDeposit` (`Action<MoonCargo, double, int>`) | unsupported | Co-op wire event, as above. |
| `MoonWorld.LocalCanister` / `RaiseLocalCanister` / `RaiseMachineCanister` / `ApplyCrewCanister` / `TruthHash` (`MoonDroppedCanister`, cargo inside) | unsupported | Co-op canister sync. Whether the cargo byte survives the wire is unknown; co-op is not supported. |
| `MoonCrewSession` (co-op world stream, joins) | unknown | Not dumped. A joining client receives the host's serialized world, which (through `Serialize`) has no modded canisters. |
| `MoonCanisterStreaming/FarShown.cargo` | unknown | Far-LOD canister drawing. A grit canister in the world (end-to-end run) logged no exceptions; far canisters stay dust (not tinted); see `CanisterTint`. |
| `MoonProductionState` | n/a | No `MoonCargo` member (slot stores are per owner; slot cargo comes from `ProcessorSpec.slotCargo`). |
| Cargo carried by a walker or a vehicle (`MoonPlayerRecordSnapshot`, `MoonLoadoutSave`, `MoonOwnedVehicle` / `MoonOwnedVehicleSave`, `MoonVehicleStorageSnapshot`, `MoonInventorySnapshot`, `MoonToolContainerSnapshot`) | n/a (checked) | The dumps of these records have no `MoonCargo` or cargo-byte member (walkers and vehicle storage hold kilograms only), and there is no separate carried-canister save record. Whether a canister held in hand or riding a vehicle is saved through `state.canisters` + `state.cargo` (and so stripped like a dropped one) is not checked: see the by-hand list in the SandPower README. |
