# Coalite furnace: SandPower's power producer, built on the solar post's (kind 93) small footprint. Coalite
# arrives by belt into a black hopper on the +X end (Unity -X) over a firebox with big glowing windows; the white boiler
# drum beside it drives the generator, a steel radiator stack sits behind the hopper and the exhaust stack with its
# orange heat band tops out at the post's height. Run from the repo root:
#   "<blender.exe>" -b --factory-startup --python mods/SandPower/blender/coalite_furnace.py
# Writes coalite_furnace.blend and preview_furnace_{hero,side,front,rear}.png here and ../coalite_furnace.bin.
import os, sys, math
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "shared", "blender"))
import kit, bpy
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN

# Donor: solar post (93), research/STRUCTURES_RECON.md "Belts and ports (M1, 2026-11-02)" fact (e).
HALF_X, HALF_Y, HEIGHT = 0.91, 0.64, 1.44     # halfFootprint (x, z) and height; Blender X, Y, Z
DECK = 0.85                                   # belt deck height (every vanilla port sits at y = 0.85)
IN_X = 0.90                                   # port on the +X footprint edge (Unity x = -0.90)

kit.reset()
r = kit.root("CoaliteFurnace_Root")

# Base: one black plinth over the whole footprint, everything stands on it.
BASE = 0.12
kit.box("Base", (1.8, 1.26, BASE), (0, 0, BASE / 2), BLACK, r, 0.03)

# Firebox under the hopper: white box with big glowing windows (orange plate behind dark glass) on the front and the
# intake end.
FX0, FX1, FZ1 = -0.02, 0.86, 0.55
fxc = (FX0 + FX1) / 2
kit.box("Firebox", (FX1 - FX0, 1.2, FZ1 - BASE), (fxc, 0, (BASE + FZ1) / 2), WHITE, r, 0.03)
kit.box("FireboxFoot", (FX1 - FX0 + 0.04, 1.24, 0.06), (fxc, 0, BASE + 0.03), BLACK, r, 0.015)
WZ = 0.33
# The window is dark glass in a black frame; the glowing coal bed (MS_LampOrange) sits just proud of the glass inside
# a dark rim, so it glows whether or not the game's glass is see-through.
for s, tag in ((-1, "Front"), (1, "Back")):
    kit.box(f"FireFrame{tag}", (0.7, 0.012, 0.32), (fxc, s * 0.602, WZ), BLACK, r, 0.01)
    kit.box(f"FireWindow{tag}", (0.62, 0.012, 0.26), (fxc, s * 0.609, WZ), GLASS, r, 0.004)
    kit.box(f"FireGlow{tag}", (0.52, 0.01, 0.17), (fxc, s * 0.614, WZ - 0.02), LAMP, r, 0.004)
kit.box("FireFrameEnd", (0.012, 0.98, 0.3), (FX1 + 0.002, 0, WZ), BLACK, r, 0.01)
kit.box("FireWindowEnd", (0.012, 0.9, 0.24), (FX1 + 0.009, 0, WZ), GLASS, r, 0.004)
kit.box("FireGlowEnd", (0.01, 0.8, 0.15), (FX1 + 0.014, 0, WZ - 0.02), LAMP, r, 0.004)

# Intake: full-width black hopper with an orange lip on top of the firebox. The footprint ends 1 cm past the port, so
# the hopper sits inboard with its outer rim on the port: the belt end lands on the lip and coalite drops in.
HOP_W, HOP_D = 0.44, 1.24
kit.hopper("Hopper", (IN_X - HOP_W / 2, 0, DECK), HOP_W, HOP_D, DECK - FZ1, r, throat=0.6)

# Radiator stack behind the hopper: thin steel fins edge-on to the front, across the full width, on black rails.
RX0, RX1, RZ1 = 0.03, 0.42, 1.0
rxc = (RX0 + RX1) / 2
for i in range(9):
    x = RX0 + 0.025 + i * (RX1 - RX0 - 0.05) / 8
    kit.box(f"Fin{i}", (0.014, 1.12, RZ1 - FZ1 - 0.05), (x, 0, (FZ1 + RZ1) / 2 - 0.01), STEEL, r, 0)
for y in (-0.58, 0.58):
    kit.box(f"FinRail{'L' if y > 0 else 'R'}", (RX1 - RX0, 0.05, 0.05), (rxc, y, RZ1 - 0.02), BLACK, r, 0.01)
    kit.box(f"FinFoot{'L' if y > 0 else 'R'}", (RX1 - RX0, 0.05, 0.05), (rxc, y, FZ1 + 0.025), BLACK, r, 0.01)

# Boiler drum across the full width on black saddles, steel end caps.
DX, DR = -0.44, 0.4
DZ = BASE + 0.08 + DR
kit.cyl("Drum", DR, 1.14, (DX, 0, DZ), WHITE, r, "Y", 32, 0.04)
for y in (-0.59, 0.59):
    kit.cyl(f"DrumCap{'L' if y > 0 else 'R'}", DR * 0.8, 0.04, (DX, y, DZ), STEEL, r, "Y", 24, 0.01)
    kit.cyl(f"DrumHub{'L' if y > 0 else 'R'}", DR * 0.3, 0.06, (DX, y * 1.02, DZ), BLACK, r, "Y", 16, 0.01)
for y in (-0.35, 0.35):
    kit.box(f"Saddle{y}", (0.6, 0.12, 0.2), (DX, y, BASE + 0.1), BLACK, r, 0.02)
for y in (-0.42, 0.42):
    kit.cyl(f"DrumRing{'L' if y > 0 else 'R'}", DR + 0.012, 0.05, (DX, y, DZ), BLACK, r, "Y", 32, 0.01)
# Steam pipe from the firebox into the drum.
kit.rod("SteamPipe", (FX0 + 0.05, 0.3, FZ1 - 0.08), (DX + 0.25, 0.3, DZ + 0.2), 0.04, STEEL, r, 10)

# Exhaust stack off the drum top: steel, orange heat band, black cap level with the post's top.
SX, SY = DX - 0.05, 0.22
STACK_TOP = 1.43
kit.cyl("StackBoot", 0.13, 0.1, (SX, SY, DZ + DR - 0.02), BLACK, r, "Z", 16, 0.02)
kit.cyl("Stack", 0.08, STACK_TOP - 0.05 - (DZ + DR), (SX, SY, (DZ + DR + STACK_TOP - 0.05) / 2), STEEL, r, "Z", 16, 0.01)
kit.cyl("HeatBand", 0.088, 0.09, (SX, SY, 1.22), ORANGE, r, "Z", 16, 0.01)
kit.cyl("StackCap", 0.11, 0.05, (SX, SY, STACK_TOP - 0.025), BLACK, r, "Z", 16, 0.01)

# Status: cyan light on a small black control pod on the drum top, front side.
kit.box("ControlPod", (0.2, 0.16, 0.1), (DX + 0.02, -0.28, DZ + DR - 0.02), BLACK, r, 0.02)
kit.cyl("StatusLight", 0.05, 0.06, (DX + 0.02, -0.28, DZ + DR + 0.06), CYAN, r, "Z", 12, 0.01)
# Output cable to the grid: black conduit from the drum hub down into the base.
kit.rod("Cable", (DX - 0.1, -0.58, DZ - 0.15), (DX - 0.32, -0.56, BASE + 0.01), 0.025, BLACK, r, 8)

kit.port("Coalite in", (IN_X, 0, DECK), (-1, 0, 0), False, "ryanhartwig.coalite", r)

# Fit, measured on the model parts only (the guide is added after).
lo, hi = kit.bounds(r)
print(f"MODEL bounds lo=({lo.x:.3f},{lo.y:.3f},{lo.z:.3f}) hi=({hi.x:.3f},{hi.y:.3f},{hi.z:.3f})")
assert lo.z >= -0.01, f"floor below ground: {lo.z}"
assert -HALF_X <= lo.x and hi.x <= HALF_X and -HALF_Y <= lo.y and hi.y <= HALF_Y, "outside the donor footprint"
assert hi.z <= HEIGHT, f"taller than the donor: {hi.z}"
kit.footprint_guide(HALF_X, HALF_Y, HEIGHT, r)

def unity(v):
    return (-v[0], v[2], -v[1])

bpy.context.view_layer.update()
for o in sorted((c for c in r.children if c.name.startswith("Port_")), key=lambda o: o.name):
    p = o.matrix_world.translation
    d = o.matrix_world.to_3x3().col[1]
    print("PORT %s output=%d cargo=%s unity pos=(%.2f, %.2f, %.2f) dir=(%.2f, %.2f, %.2f)" %
          (o.name, o["output"], o["cargo"], *[round(x, 4) + 0.0 for x in unity(p)], *[round(x, 4) + 0.0 for x in unity(d)]))

kit.save(os.path.join(HERE, "coalite_furnace.blend"))
kit.preview(r, os.path.join(HERE, "preview_furnace"), structure=True)
kit.export(r, os.path.join(HERE, "..", "coalite_furnace.bin"))
