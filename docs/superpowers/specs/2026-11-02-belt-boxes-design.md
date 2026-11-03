# Belt boxes: compact splitters and mergers — design

Date 2026-11-02. Author: Ryan's agent, from Ryan's request ("better splitters and mergers, Satisfactory's box
splitter/merger approach; vanilla's blueprint is too large and hard to work with"). Ryan opted out of the design
questions and asked for end-to-end delivery; every choice below not quoted from him is the agent's call.

Reads with: `research/SPLITTERS_RECON.md` (vanilla splitter and merger facts, the hooks, the three candidate designs),
`research/STRUCTURES_RECON.md` ("Belts and ports"), `src/StructureLib/README.md`,
`docs/superpowers/specs/2026-11-02-structurelib-belts-and-models-design.md` (ports and models, which this builds on).

## Intent

Vanilla's splitter and merger are 2.46 × 3.34 m Y-pieces: both outputs face forward, 3.2 m from the input, so every
output belt has to curve away before it goes anywhere, and a bus of them eats the base. Satisfactory's answer is a
box one belt wide with the input on one face and outputs on the other three (left, forward, right), round-robin
over outputs that can take a part, skipping blocked ones; the merger is the mirror, round-robin over inputs that have
something waiting. That is what we build: **two structures in a new mod, `mods/BeltBoxes`, on StructureLib**, plus
the small library additions they need.

## Goals

1. **Box splitter**: one input, up to three outputs on the other three faces, in a 1.4 × 1.4 m footprint. A
   canister arriving at the input goes to the next output in turn whose belt exists and can accept it; outputs with
   no belt or a blocked entrance are skipped; when none can take it the canister waits at the input belt's end (and
   the belt queues behind it, as vanilla does).
2. **Box merger**: up to three inputs, one output, same footprint. Canisters waiting at the inputs are served in
   turn (round-robin over inputs that have one waiting); when the output cannot accept, everything waits.
3. **Any cargo.** Both boxes pass whatever cargo arrives (vanilla dust/sand/stone and modded cargo), preserving
   cargo and kilograms. They never hold a canister: each hop moves it from the input belt's end to the output belt's
   start in the same frame.
4. **Vanilla belt tool.** Belts join to the box ports with the normal tool, labelled like any StructureLib port.
5. **Save safe.** Boxes, their belts and the canisters on those belts survive save and reload through the existing
   StructureLib paths. The round-robin position may reset on load (vanilla's does).
6. **Own models** in Andrew's style (Blender 4.5.15, `shared/blender/kit.py`), with the ports authored as `Port_*`
   nodes in the model, a build-menu card rendered from them, and a visible cue of flow direction on each face.
7. **Proven in the DEVBUILD self-test** with the example mod's grinder feeding a splitter whose three outputs run
   through belts into a merger feeding the example burner; distribution, skipping and fairness asserted from events.

## Non-goals

- Cut-in placement onto an existing belt (vanilla's `CutIn`). A box is placed free-standing and belts are joined to
  it. Documented as a difference from vanilla.
- Canisters visibly crossing the box (vanilla rides them on internal routes). Ours vanish at the input end and
  appear at the output start; the box is 1.4 m, so the gap is short. Documented.
- Smart or programmable splitters (Satisfactory's cargo filters), priority mergers, overflow outputs. Later, maybe,
  as options on the same structures.
- Hand feeding, stacking boxes, raised or elevated variants, replacing or hiding the vanilla splitter and merger.
- Running vanilla's own routing (recon design A). Not attempted in this milestone.

## Anti-goals (a failure even if it works)

- A box that drops or duplicates a canister on any path: cargo in must equal cargo out, every time, including under
  a blocked output, a removed belt, a removed box, and a reload mid-traffic.
- Stalling vanilla belts or machines, or a prefix that runs code for vanilla kinds.
- A library change that breaks SandPower or the example mod's existing tests (136 unit tests, the self-test's belt
  checks).
- A native crash (the library's one open crash is at world load in the card render; nothing here may touch the
  card path).

## Constraints

- Branches: library additions go on **`structurelib-lib`** (PR #2, still open); the mod goes on **`beltboxes`**,
  branched from it, PR against `structurelib-lib`, touching `mods/BeltBoxes/**` plus its registration lines only.
  Library commits are carried into `beltboxes` by merge, not rebase (the harness refuses force-pushes).
- `.NET SDK 8` only; build per project; the game must be closed to install (`-p:InstallToGame=true`).
- Test save: slot 2 (backup `C:\Users\ryguy\projects\moonsweep\.scratch\slot-2-backup-task11`); the hygiene rules in
  the handoff (15 s after a kill before restoring, 30 s before relaunching) are binding.
- No Steam ids, saves or tokens in the repo. Plugin GUID and structure ids never change after release.
- Blender: `C:\Users\ryguy\projects\moonsweep\.downloads\blender-4.5\blender-4.5.15-windows-x64\blender.exe`,
  headless (`-b --factory-startup --python`), the kit's palette and triangle budget checks must pass.
- The work-order board is unreachable from these sessions (every prior session); record hot-path edits in the PR.

## The design

### Library: routers (on `structurelib-lib`)

Recon design B. A **router** is a StructureLib structure with no processor whose ports are served by the library's
existing belt hooks.

- `StructureDefinition.Router` (new): `RouterDefinition { RouterMode Mode }` with `RouterMode.Splitter` and
  `RouterMode.Merger`. A definition with `Router` set must have no `Processor` and no `Power`; this is checked at
  freeze with the other definition rules.
- **Ports on a router.** `PortRules` accepts, for a router, between one and four ports: a splitter has exactly one
  input and one to three outputs; a merger one to three inputs and exactly one output. Router ports carry **no
  cargo**: they are written into the clone with the dust byte (`sl:0:<Name>`), which is what vanilla's own splitter
  ports answer to `PortCargo` (recon section 1), and the hop ignores cargo. A model's `Port_*` nodes on a router
  therefore need no cargo property; a declared `PortDefinition` on a router ignores its `Cargo`. The existing rules (ports must lie on the footprint, at deck height 0.85, unique names) still apply.
- **`StructureDefinition.Footprint`** (new, optional): full width and depth in metres, written into the clone's
  `halfFootprint`. Needed because the box is 1.4 m square and no small vanilla donor matches; harmless for other
  structures. When unset, the donor's footprint stays, as today. When set together with a `Model`, the model swap
  also replaces the donor's root colliders with one box collider covering the footprint and the model's height, so
  a big donor's collision does not outlive its mesh.
- **Donor.** The builder picks it in the spike from the small vanilla kinds (the vanilla splitter 11 first, since it
  is a Logistics item with no production, power or solar view, stands on dust and costs $5; else the packager 10 or
  the floodlight 4), by reading each prefab's components and confirming that a clone of it with a hashed kind gets
  no vanilla routing (`Routes`, cut-in offers) and no stray view. The solar post 93 is not it: its view carries a
  production view and a solar-panel view.
- **Co-op.** Routers inherit belt input's standing: the host decides, clients untested (README support table). No
  new guard this milestone; the README and handoff say so, next to the two-player test already owed.
- **Hop.** `BeltPatches.Intake` (the `HandOff` prefix) gains a router branch; vanilla kinds are untouched as today.
  - *Splitter.* Per depot a transient `nextOut` index. Starting at `nextOut`, take the first output port, in port
    order, whose belt exists and whose entrance is clear; move the canister there; set `nextOut` to the port after
    the one used; answer true. If no output can take it, answer false (the canister waits; vanilla queues the belt).
  - *Merger.* Per depot a transient `turn` input index. On a `HandOff` from input *i*: if the output has no belt or
    its entrance is not clear, answer false. Otherwise, if some other input *j* ≠ *i* is nearer in turn order from
    `turn` than *i* and has a canister waiting at its belt end, answer false (yield). Otherwise move the canister to
    the output, set `turn` to the input after *i*, answer true. No input starves: an input with nothing waiting is
    skipped, and `turn` advances past every served input.
  - *Moving the canister.* The hop must preserve cargo, kilograms and owner. The builder settles the mechanism in a
    first spike, in this order of preference: (1) `PlaceRider`/`MoveCanisterRecord` of the **same** canister onto the
    output belt's start, if a trace shows vanilla's rider bookkeeping stays consistent (recon design C); else (2)
    `EmitCargo` onto the output belt followed by `RemoveCanister` of the arriving one (design B), which the library
    already uses, provided `EmitCargo` carries the kilograms through. Either way the two steps happen in one prefix
    call, and a failure between them must be impossible or logged and left in the "waiting" state, never a loss.
    If (2) is the mechanism and `EmitCargo` makes 1 kg capacity-1 canisters whatever it is given (recon fact (b)),
    the hop rewrites the new canister's kilograms, capacity and owner from the arriving one through
    `MoveCanisterRecord` (the call the library already uses to shrink a partly taken canister). Only if that too
    fails do canisters over 1 kg wait at the input as a documented limit; a canister is never split, buffered or
    dropped.
  - Events: `CargoEntered` is **not** raised for routers (nothing enters a slot). A new `CargoRouted { Definition,
    DepotId, Cargo, Kilograms, FromBeltId, ToBeltId }` is raised per hop, so the self-test can count distribution.
- **Throughput.** One hop per input belt per frame; the output belt's own `EntranceClear` spacing bounds the rate
  at the belt's 1.47 canisters/s. No buffering inside the box.
- **Save.** Nothing new: a router's depot record is saved by the sidecar like any modded depot; belts are vanilla
  endpoints; riders are on belts. `nextOut`/`turn` live in memory and reset on load.
- **Dev harness.** `AutoBelt` must accept a router's output port as a source and its input ports as targets (it
  matches ports by position today, so this should already hold; verify). Self-test checks (DEVBUILD only, skip
  themselves with "BeltBoxes is not registered" when the mod is absent) are listed under Verification.

### The mod: `mods/BeltBoxes`

- Plugin `com.ryanhartwig.beltboxes`, version `0.1.0`, `[BepInDependency(StructureLib.Plugin.Guid)]`; scaffolded
  with `new-package.ps1 -Kind mod -Name BeltBoxes -Author ryanhartwig`.
- **Box splitter** `ryanhartwig.box-splitter`, Category `Logistics`, price $25 (vanilla's is $5; ours is a
  convenience, priced above it but trivially). `BaseStructure` = the solar post (kind 93, as the generator uses: a
  small donor with no production view), `Footprint` 1.4 × 1.4, `Model = "box_splitter.bin"`, `Router = Splitter`,
  `Ports` null (the model's `Port_*` nodes win). Flow runs along structure +X like every vanilla production donor
  (the dev line layout and AutoBelt assume it): `Port_In` on the −X face flowing +X, `Port_Forward` on +X flowing
  +X, `Port_Left` on the +Z face flowing +Z, `Port_Right` on −Z flowing −Z (left and right as seen looking along
  +X with Y up), all at y 0.85 and on the footprint edge (±0.7). Note text: "Splits one belt into up to three, taking turns. Skips an output that is blocked or has no belt."
- **Box merger** `ryanhartwig.box-merger`, same category, price and donor, `Model = "box_merger.bin"`,
  `Router = Merger`: `Port_Left` on the +Z face flowing −Z (into the box), `Port_Back` on −X flowing +X,
  `Port_Right` on −Z flowing +Z, `Port_Out` on +X flowing +X. Note: "Merges up to three belts into one, taking turns."
- **README** in the SandPower README's shape: what each box does, the ports table with positions and directions, the
  two differences from vanilla (no cut-in, no visible crossing), how to lay one out (a belt may leave any face
  straight), preview images.
- Registration lines: `modpack.json` (package `BeltBoxes`, description), `GarageLib.slnx`, root `README.md` row and
  tree line, `CONTRIBUTING.md` only if it lists mods. No cargo registry row (no new cargo).

### Models

- Built from `mods/BeltBoxes/blender/box_splitter.py` and `box_merger.py` with the kit, following
  `shared/blender/STRUCTURES.md`; a `.blend`, four previews beside the 1.8 m astronaut, and the `.bin` each. Triangle
  budget and palette checks pass.
- **Form.** A squat steel box 1.4 m square, deck at 0.85 m like a belt, overall height about 1.0 m, with a short
  tread stub on each ported face at belt height so a joined belt reads as continuous, and a chevron or arrow on the
  lid per port showing flow direction (in: pointing into the box; out: pointing away). The splitter's lid is marked
  with one arrow in and three out; the merger's with three in and one out. Unported faces are plain. Style: the
  kit's steel and the Logistics accent used by the vanilla belt couplers (the builder picks the nearest kit palette
  entry). Keep both under 3,000 triangles: they will be built in dozens.
- Ports are `Port_*` empties in the model as above; the exporter's axis mapping (Blender (x, y, z) → Unity (−x, z,
  −y); flow = Rot × (0, 0, −1)) is the one documented in `shared/blender/STRUCTURES.md`.

## Verification (what "done" means)

Unit tests (pure code): router port rules (counts, wildcard encoding, mode/processor exclusivity), the splitter's
selection (`nextOut` with outputs present/absent/blocked), the merger's yield rule (fairness over three inputs,
no starvation, output blocked), footprint override. All existing tests still pass.

Self-test in game (DEVBUILD, BeltBoxes installed), with `AutoBuild`/`AutoBelt` laying out: example grinder →
splitter; splitter Left/Forward/Right → three belts → merger Left/Back/Right; merger Out → example burner.
Dust fed only to the grinder. Checks:
1. `beltboxes-registered`: both kinds registered with the four and four ports, footprint 1.4 × 1.4 on the clones.
2. `beltboxes-split-even`: after ≥ 12 hops, each output has had between a third ± one of the hops (`CargoRouted`
   counts per `ToBeltId`).
3. `beltboxes-split-skips`: with the Forward belt removed (or its entrance held by a stopped canister), hops go only
   to Left and Right, none lost (hops in == hops out of the splitter over the window).
4. `beltboxes-merge-fair`: with all three merger inputs holding waiting canisters, the next six hops come from the
   three inputs twice each, in rotation.
5. `beltboxes-conserve`: over the run, grit emitted by the grinder == grit entered into the burner + grit riding
   belts + grit waiting, within one canister.
6. `beltboxes-save-clean`: a save with canisters waiting at the splitter input and on all three middle belts writes
   cleanly (no "Moon save failed"); after reload (second run) both boxes, all five belts and the canisters with their
   cargo are back, and the chain resumes (a hop is seen within 20 s).
7. `beltboxes-model`: both clones carry the `StructureLibModel` holder with renderers enabled and the ports at the
   model's node positions.
8. `vanilla-save-clean` and every existing belt check still pass; `Moon save failed` 0; no exceptions.

By hand (Ryan, listed in the README and handoff): placing both boxes from the menu and joining belts to all four
faces with the belt tool at the 0.7 m port spacing; look of the models in real lighting; removing a box with belts
attached; removing a belt mid-traffic.

## Left to the builder

- The hop mechanism (spike, see above) and whether `EntranceClear` is safe from the prefix.
- Where `nextOut`/`turn` live (a dictionary keyed by depot id in the belt patches is fine, dropped with the belt
  caches on world change).
- The footprint is a tunable between 1.4 and 1.6 m until Ryan's by-hand belt-tool check at the 0.7 m port spacing;
  a misjoin there grows the box rather than blocking release.
- Self-test timing windows and the stopped-canister trick for check 3.
- The models' detailing within the form above; the Logistics accent colour.
- Whether `CONTRIBUTING.md` needs a line (only if it lists mods by name).

## Rulings the controller already made

- Design B/C over A: vanilla routing is a native path gated by `Routes(def, kind)`, a small static leaf of the same
  shape as `IsProcessor`, whose prefix crashed the game at world load; and its canisters would sit inside a depot the
  sidecar strips from the vanilla save. Cost if wrong: we lose cut-in and the visible crossing, both documented.
- Library additions land on the open library PR rather than a third stacked PR: one feature, one mod PR, and the
  library PR is unmerged and unreviewed. Cost if wrong: PR #2 grows by a small, self-contained feature.
- Four-port boxes with side outputs (Satisfactory parity) over a two-output Y like vanilla's: the side outputs are
  the whole point of the request.
