# Structure style guide

How modded structures (machines, generators, anything StructureLib builds from a donor) should look so they sit next to
the vanilla ones, and how to model them with `kit.py`. Vehicles have their own page, [`vehicles/STYLE.md`](../../vehicles/STYLE.md);
the palette is the same.

## The vanilla structures

Card pictures from the game's build menu (dumped by StructureLib's `[Dev] DumpCards`):

| Pressure chamber (96) | Stone press (97) | Solar post (93) |
|---|---|---|
| ![Pressure chamber](reference/96-pressure-chamber.png) | ![Stone press](reference/97-stone-press.png) | ![Solar post](reference/93-solar-post--s.png) |

| Capsule catcher (100) | Conveyor belt (1000) |
|---|---|
| ![Capsule catcher](reference/100-capsule-catcher.png) | ![Conveyor belt](reference/1000-conveyor-belt.png) |

- **Pressure chamber**: a big horizontal white pressure drum banded with black and orange rings, with a round door, a
  hand wheel and a dial, lying on two white pedestals. It all stands on a dark angular base plate with an orange edge.
- **Stone press**: a row of units on one long dark base plate. Two tall white silos with orange railed tops feed a
  white-and-orange rotating drum, then a black furnace box with a glowing orange window, a black chimney and a small
  white tank.
- **Solar post**: one large dark-blue PV panel in a steel frame, tilted on a short white post with a cyan band. The
  post stands on a dark hexagonal foot trimmed with orange and a cyan glow line underneath.
- **Capsule catcher**: a wide white octagonal dish, its rim lined with orange ribbed pads, held up by four splayed
  white legs. The legs end in black screw jacks on small feet, around a central white box with an orange-grilled
  panel.
- **Conveyor belt**: a dark slatted belt between white side rails capped with orange bands and small cyan lights,
  carried on white trestle legs with black feet.

What they share: clean white panels with soft bevels; **dark (black plastic) base plates and housings**; orange on
rims, bands, moving or hot parts (the drum, the furnace window, the catcher's pads); steel only for frames and
fittings; one or two small cyan lights. Detail comes from stacked simple shapes (cylinders, boxes, rings), not
texture. They are friendly industrial kit, slightly toy-like, never rusty and never sci-fi.

## Palette and shapes

Use only the kit's materials (the loader maps the names to the game's own): `MS_BodyWhite` (panels, tanks, pedestals),
`MS_BlackPlastic` (base plates, skirts, housings, hopper mouths), `MS_OrangeMetal` (bands, intake lips, moving or
hot parts), `MS_LampOrange` (glowing windows, an intake ring), `MS_Steel` (frames, rails, chutes, fittings),
`MS_Glass` (windows, PV glass), `MS_Cyan` (one status light, or a thin glow band). `kit.export` refuses anything else.

- **Structures are boxy industrial units on a visible skid or foot**: a dark plate or skid under everything, a few
  clear white volumes on it. No floating parts.
- **Ports read as a hopper mouth (input) or a chute (output) at the exact port position**: `kit.hopper` puts the rim
  centre on the port; `kit.chute` puts the lip on it. A player should see where the belt goes without a label.
- Custom models hide every donor renderer, including the animated ones (the press's fill planes, emission, tracker
  tilt), so show status with static geometry instead: a cyan `MS_Cyan` light, an orange `MS_LampOrange` ring at the
  intake.

## Footprint, height, budget

Keep the model inside its donor's footprint and height (research/STRUCTURES_RECON.md, "Belts and ports (M1,
2026-11-02)" fact (e)). The game stores them in Unity axes; in Blender `halfFootprint (x, z)` is the half size along
X and Y, and a Unity point `(x, y, z)` is Blender `(-x, -z, y)`.

| Donor | half footprint (Blender X, Y) | height | renderer bounds (Unity min / max) | ports (Unity position, direction) |
|---|---|---|---|---|
| Pressure chamber (96) | 4.11, 2.63 | 6.16 | (-4.08, 0.00, -2.60) / (4.08, 6.17, 2.60) | `IN` (-2.95, 0.85, 0.00); `OUT` (2.95, 0.85, 0.00); both dir (1, 0, 0) |
| Stone press (97) | 4.63, 3.22 | 6.14 | (-4.60, -0.49, -3.19) / (4.60, 6.16, 3.19) | `IN_DUST` (-4.10, 0.85, -1.25); `IN_SAND` (-4.10, 0.85, 1.25); `OUT` (3.90, 0.85, 0.00); all dir (1, 0, 0) |
| Solar post (93) | 0.91, 0.64 | 1.44 | (-0.89, 0.00, -0.61) / (0.89, 1.46, 0.61) | none |

- Floor at Z = 0. The loader lands the model's lowest vertex on the donor's lowest renderer bound, so nothing may hang
  below the skid; build scripts assert `kit.bounds(root)[0].z >= -0.01`.
- Belt ports sit at the belt deck height, **0.85 m** (every vanilla port has y = 0.85), on the footprint edge. A belt
  snaps within 0.05 m of the port (`PortTolerance`) and reaches 1.2 m (`PortReach`).
- Draw the donor box with `kit.footprint_guide(half_x, half_y, height, root)`; it renders as a yellow wireframe and
  never reaches the `.bin`.
- **Under 10,000 triangles** per structure. `kit.preview(..., structure=True)` prints `TRIANGLES <n>` and raises above
  that. Bevels count: prefer `bevel=0` on small repeated parts.

## Ports in the model

A port is an empty named `Port_<name>` (the name the game shows, e.g. `Port_Sand in`), parented under the root:

```python
kit.port("Sand in", (4.1, 0, 0.85), (-1, 0, 0), False, "Sand", root)          # input: +Y points into the machine
kit.port("Briquettes out", (-4.1, 0, 0.85), (-1, 0, 0), True, "ryanhartwig.sand-briquette", root)  # output: out of it
```

- Position = the empty's position (where the belt end snaps). Local **+Y = the flow direction**: into the machine for
  an input, out of it for an output (the game's own ports use the belt's flow direction for both).
- ID properties `output` (0.0 or 1.0) and `cargo` (`Sand`, `Dust`, `Stone` or a cargo id). The exporter writes every
  object's ID properties (`obj.keys()`, skipping `_`-prefixed keys) as node props, which StructureLib reads.
- `python shared/blender/check_bin.py <file.bin>` (or `"<blender.exe>" -b --factory-startup --python
  shared/blender/check_bin.py -- <file.bin>`) prints every node, its props and triangles, and for `Port_*` nodes the
  position and flow direction in Unity axes (`rootpos=`, `dir=`): what the loader will see. Blender +Y is Unity -Z.

## Running a build

One headless Blender process per script, from the repo root, never the Blender MCP:

    "C:\Users\ryguy\projects\moonsweep\.downloads\blender-4.5\blender-4.5.15-windows-x64\blender.exe" -b --factory-startup --python mods/<Mod>/blender/<name>.py

(or `"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe"` where Blender is installed). A script does
`kit.reset()`, builds under `kit.root("<Name>_Root")`, draws the footprint guide, then `kit.save(...)`,
`kit.preview(root, ..., structure=True)` and `kit.export(root, ...)`. `shared/blender/smoke_structure.py` is a
complete small example and the kit's self-test (writes to the git-ignored `shared/blender/_smoke/`).

## Preview review checklist

Read all four PNGs (`hero`, `side`, `front`, `rear`) before calling a model done:

1. It stands on the ground plane: skid or foot touching, nothing sunk or floating.
2. It fits the yellow footprint box, in plan and height.
3. Scale reads right against the 1.8 m figure beside its +X side (hidden in the side view).
4. Every input has a visible hopper mouth and every output a chute, exactly where the port is; the port arrows are not
   rendered, so check positions with `check_bin.py`.
5. Palette only, white dominant, dark base, orange where things move, heat or take material in, one cyan light.
6. `TRIANGLES` under 10,000; `check_bin.py` shows each `Port_*` with the right `output`, `cargo` and `dir`.
7. It looks like it belongs next to the cards above: same chunky bevels, same simple stacked shapes.
