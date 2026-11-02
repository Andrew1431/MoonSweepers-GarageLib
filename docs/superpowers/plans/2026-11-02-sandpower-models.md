# SandPower Models Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two purpose-built models for SandPower, the sand compactor and the briquette generator, made with Andrew's headless Blender kit, exported to the game's `.bin` mesh format with their belt ports authored as `Port_*` empties, plus the kit helpers and style page that make the next structure model routine.

**Architecture:** Build scripts under `mods/SandPower/blender/` call `shared/blender/kit.py` (materials, primitives, preview renders, export). The kit gains structure helpers (ports, a footprint guide, a structure preview stage). Each model is reviewed from its preview PNGs against `shared/blender/STRUCTURES.md` and the reference card images before it is accepted. No game, no C#: the library side (loading `.bin`, reading ports) is the other plan, `2026-11-02-structurelib-belts-and-models.md`.

**Tech Stack:** Blender 4.5.15 LTS (portable, `C:\Users\ryguy\projects\moonsweep\.downloads\blender-4.5\blender-4.5.15-windows-x64\blender.exe`), Python inside Blender, `shared/blender/kit.py`, `shared/blender/export_mesh_model.py`.

**Spec:** `docs/superpowers/specs/2026-11-02-structurelib-belts-and-models-design.md` (sections "Models", "Blender tooling and style", call 2 under "Three calls").

## Global Constraints

- Run every script headless, one process each: `"<blender.exe>" -b --factory-startup --python <script.py>` from the repo root. Never the Blender MCP.
- Only the kit's materials: `MS_BodyWhite`, `MS_BlackPlastic`, `MS_OrangeMetal`, `MS_LampOrange`, `MS_Glass`, `MS_Steel`, `MS_Cyan`. Metres, Z up, floor at Z = 0, one root empty `<Name>_Root`, everything parented under it. Under 10,000 triangles per structure.
- The model must fit the donor's footprint and height from `research/STRUCTURES_RECON.md` "Belts and ports (M1, 2026-11-02)" fact (e): compactor on the stone press (kind 97), generator on the solar post (kind 93). Read that section before modelling; if it is missing, stop and report NEEDS_CONTEXT.
- Ports are `Port_<name>` empties with custom properties `output` (0 or 1) and `cargo` (`Sand`, `Dust`, `Stone` or a cargo id such as `ryanhartwig.sand-briquette`); the empty's local +Y is the flow direction (into the machine for inputs, out for outputs); its position is the port position. Port positions must lie at the model's visible intake (a hopper mouth) or outlet (a chute), on the footprint edge. Check `shared/blender/export_mesh_model.py` for how custom properties are written (`obj.keys()` / ID properties) and store them that way.
- Reference images: `<game>\BepInEx\cache\structurelib\cards\*.png` written by the other plan's Task 1 (`DumpCards`); copy the chamber (96), stone press (97), solar post (93), catcher and conveyor cards into `shared/blender/reference/` (small PNGs, under 200 KB each) so the style page can show them. If the folder is missing, model from `vehicles/STYLE.md` alone and say so.
- Vehicle scripts are untouched; `vehicles/STYLE.md` is unchanged; new guidance lives in `shared/blender/STRUCTURES.md`.
- Commit per task with the message given; every commit message ends with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`. Never commit `.blend1` backups.

## Review Focus

1. A port empty whose flow axis points the wrong way (out of the machine on an input): the game would snap belts backwards; the kit's `port()` sets the axis from a `direction` argument and the export check prints the Unity-space direction for a human to read (Task 1 smoke test asserts the printed direction).
2. A model whose floor is not at Z = 0: the loader aligns by the lowest vertex, so a model floating in Blender still lands on the ground, but a skid below Z = 0 would sink; the preview shows the ground plane (Task 1 stage) and the build scripts assert `bounds(root).lo.z >= -0.01`.
3. Triangle budget: the preview script prints the count; over 10,000 is a build failure in the script.
4. Scale read against the astronaut figure: the previews include the 1.8 m figure beside the structure (Task 1 stage).
5. Material names outside the palette render as flat fallback colours in game: `kit.reset()` creates only the seven; the export fails loudly on any other material name (Task 1 adds that check to `kit.export`).

---

### Task 1: Kit helpers for structures, style page, reference cards

**Files:**
- Modify: `shared/blender/kit.py` (add `port`, `footprint_guide`, `stage_structure` preview framing, `hopper`, `chute`, material check in `export`, triangle count in `preview`)
- Create: `shared/blender/STRUCTURES.md`, `shared/blender/reference/` (card PNGs), `shared/blender/smoke_structure.py` (throwaway smoke script, kept as the kit's example), `shared/blender/check_bin.py` (reads a `.bin` with `struct` and prints nodes, props, triangle count; run inside Blender's Python or any Python 3)
- Test: run `smoke_structure.py` and `check_bin.py`

**Interfaces:**
- Produces: `kit.port(name, loc, direction, output, cargo, parent)` → an empty `Port_<name>` at `loc` whose local +Y points along `direction` (a unit vector in Blender space), with ID properties `output` (float 0/1) and `cargo` (str), parented under `parent`; `kit.footprint_guide(half_x, half_y, height, parent)` → a wireframe-displayed box (`display_type = 'WIRE'`) named `FootprintGuide`, excluded from export (name prefix `Guide_` is skipped by `export`); `kit.hopper(name, loc, width, depth, height, parent, mat=BLACK)` → an open-top tapered box (intake); `kit.chute(name, loc, length, width, parent, mat=STEEL)` → an angled trough (outlet); `kit.stage_structure()` → the preview stage with the ground plane visible and the figure placed beside the model's +X side at 1.8 m; `kit.preview(..., structure=True)` uses it and prints `TRIANGLES <n>`, failing (raise) above 10,000; `kit.export` raises on a material name outside the palette and skips `Guide_*` objects. `check_bin.py <file.bin>` prints `node <name> parent=<i> pos=(x,y,z) rot=(x,y,z,w) props={…} tris=<n>` per node and a total.

- [ ] **Step 1: Write the smoke script (the failing test)**

`shared/blender/smoke_structure.py`: `kit.reset()`, root `Smoke_Root`, a 2×2×1.5 m white box on a black skid, one `hopper` on the −Y face, one `chute` on the +Y face, `port("In", (0,-1.1,0.8), (0,1,0), False, "Sand", root)`, `port("Out", (0,1.1,0.4), (0,1,0), True, "ryanhartwig.sand-briquette", root)`, `footprint_guide(1.2, 1.2, 1.6, root)`, `preview(root, "shared/blender/_smoke/preview", structure=True)`, `export(root, "shared/blender/_smoke/smoke.bin")`. Then `check_bin.py shared/blender/_smoke/smoke.bin` must print both `Port_*` nodes with `props={'output': 0.0, 'cargo': 'Sand'}` / `{'output': 1.0, 'cargo': 'ryanhartwig.sand-briquette'}`, no `Guide_` node, and a total under 10,000. `_smoke/` is git-ignored (add to `.gitignore`).

- [ ] **Step 2: Run it to verify it fails**

Run: `"<blender.exe>" -b --factory-startup --python shared/blender/smoke_structure.py`. Expected: `AttributeError: module 'kit' has no attribute 'port'`.

- [ ] **Step 3: Implement the kit helpers and `check_bin.py`**

Confirm in `export_mesh_model.py` how ID properties reach the file (it writes `u8 props` per node: find the source of those props; if it reads `obj.keys()`, set `obj["output"] = 0.0`, `obj["cargo"] = "Sand"`; if it reads something else, adapt `port()` to it and say so in the report). Direction: build the empty's rotation so its local +Y equals `direction` (`Vector((0,1,0)).rotation_difference(direction)`).

- [ ] **Step 4: Run the smoke script and the checker; look at the previews**

Expected: `PREVIEW …`, `TRIANGLES <n>` under 10,000, `Exported … -> …smoke.bin`; `check_bin.py` output as in Step 1. Read the four preview PNGs with the Read tool: the box sits on the ground plane, the figure is beside it, the hopper and chute are visible.

- [ ] **Step 5: Write `STRUCTURES.md` and copy the reference cards**

One page: what vanilla structures look like (from the cards: describe each in two lines), the shared palette and shapes (white panels, black plastic skirts, housings and hopper mouths, orange on moving or hot parts and intake lips, steel frames and chutes, one cyan status light), "structures are boxy industrial units on a visible skid or foot", "ports read as a hopper mouth (input) or a chute (output) at the exact port position", footprint and height rules, triangle budget, the `Port_*` convention, the run command, and the preview review checklist. Embed the reference PNGs with relative links.

- [ ] **Step 6: Commit**

```bash
git add shared/blender .gitignore
git commit -m "blender kit: structure helpers (ports, hopper, chute, footprint guide, structure stage), STRUCTURES.md style page, bin checker, reference cards"
```

---

### Task 2: Sand compactor model

**Files:**
- Create: `mods/SandPower/blender/sand_compactor.py`, `mods/SandPower/blender/sand_compactor.blend`, `mods/SandPower/blender/preview_compactor_{hero,side,front,rear}.png`, `mods/SandPower/sand_compactor.bin`

**Interfaces:**
- Consumes: Task 1 helpers; recon fact (e) for kind 97 (half footprint, height, port positions of the press for scale, renderer bounds).
- Produces: `sand_compactor.bin` with root `SandCompactor_Root`, ports `Port_Sand in` (input, cargo `Sand`, flow into the machine) and `Port_Briquettes out` (output, cargo `ryanhartwig.sand-briquette`), both on the footprint edge at belt deck height (use the press's own port height from fact (e) so belts meet it at the same level), under 10,000 triangles.

- [ ] **Step 1: Write the build script**

Design, within the press's footprint and height: a heavy white press housing on a black skid, a hopper (black, orange lip) on the input side at the `Sand in` port, a vertical ram column in steel with an orange band, a steel chute at the `Briquettes out` port, a cyan status light on the control box, two orange stripes. Script asserts floor at Z ≥ −0.01, prints the ports in Unity space (apply the exporter's map (x,y,z) → (−x,z,−y) to each port's position and direction), previews with `structure=True`, exports.

- [ ] **Step 2: Run it; read the previews; iterate**

Run the script; `check_bin.py` the output; read all four PNGs. Iterate until it reads as a compactor at a glance beside the figure and the hopper and chute sit at the ports. Record the triangle count.

- [ ] **Step 3: Commit**

```bash
git add mods/SandPower/blender/sand_compactor.py mods/SandPower/blender/sand_compactor.blend mods/SandPower/blender/preview_compactor_*.png mods/SandPower/sand_compactor.bin
git commit -m "SandPower: sand compactor model with belt ports"
```

---

### Task 3: Briquette generator model

**Files:**
- Create: `mods/SandPower/blender/briquette_generator.py`, `.blend`, `preview_generator_{hero,side,front,rear}.png`, `mods/SandPower/briquette_generator.bin`

**Interfaces:**
- Consumes: Task 1 helpers; recon fact (e) for kind 93 (the post's footprint is small and tall: the generator must stay within it, so it is a compact upright unit).
- Produces: `briquette_generator.bin`, root `BriquetteGenerator_Root`, one port `Port_Briquettes in` (input, cargo `ryanhartwig.sand-briquette`) with a hopper at the footprint edge at belt deck height; under 10,000 triangles.

- [ ] **Step 1: Write the build script**

Design: a white boiler drum on a black base, a black hopper with an orange lip at the port, a steel exhaust stack with an orange heat band, a glass firebox window with an orange glow (`MS_LampOrange` inner plate), a cyan status light, a small steel radiator fin stack; no solar panel, nothing above the post's height.

- [ ] **Step 2: Run it; read the previews; iterate**

As Task 2.

- [ ] **Step 3: Commit**

```bash
git add mods/SandPower/blender/briquette_generator.py mods/SandPower/blender/briquette_generator.blend mods/SandPower/blender/preview_generator_*.png mods/SandPower/briquette_generator.bin
git commit -m "SandPower: briquette generator model with a belt port"
```
