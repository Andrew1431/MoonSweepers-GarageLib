# Coking oven: SandPower's coalite maker, built on the stone press's (kind 97) footprint. Sand arrives by belt
# into a black hopper on the +X end (Unity -X), an auger feeds the white oven housing, a steel ram under a white
# portal crosshead compacts it, and coalite bricks slide out of a steel chute on the -X end (Unity +X). Run from the repo root:
#   "<blender.exe>" -b --factory-startup --python mods/SandPower/blender/coking_oven.py
# Writes coking_oven.blend and preview_oven_{hero,side,front,rear}.png here and ../coking_oven.bin.
import os, sys, math
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN

# Donor: stone press (97), research/STRUCTURES_RECON.md "Belts and ports (M1, 2026-11-02)" fact (e).
HALF_X, HALF_Y, HEIGHT = 4.63, 3.22, 6.14     # halfFootprint (x, z) and height; Blender X, Y, Z
DECK = 0.85                                   # belt deck height: every vanilla port sits at y = 0.85
IN_X, OUT_X = 4.10, -3.90                     # the press's own port distances from centre (Unity -4.10 / +3.90)

kit.reset()
r = kit.root("CokingOven_Root")

SKID_T = 0.25
# Skid: one long black base plate on steel feet, like the vanilla machines' dark base plates.
kit.box("Skid", (7.7, 4.4, SKID_T - 0.06), (0.45, 0, 0.06 + (SKID_T - 0.06) / 2), BLACK, r, 0.05)
for x in (-3.1, 4.0):
    for y in (-1.95, 1.95):
        kit.box(f"Foot{x}{y}", (0.45, 0.45, 0.06), (x, y, 0.03), STEEL, r, 0.01)

# Press housing: the heavy white body the ram works into.
HX0, HX1 = -2.2, 2.8                          # housing X range
HZ1 = 2.65                                    # housing top
hc = (HX0 + HX1) / 2
kit.box("Housing", (HX1 - HX0, 3.2, HZ1 - SKID_T), (hc, 0, (SKID_T + HZ1) / 2), WHITE, r, 0.08)
kit.box("HousingPlinth", (HX1 - HX0 + 0.16, 3.36, 0.3), (hc, 0, SKID_T + 0.15), BLACK, r, 0.04)
# The two orange stripes: bands round the housing below the top and above the plinth.
kit.stripe("StripeUpper", (HX1 - HX0 + 0.02, 3.22, 0.12), (hc, 0, HZ1 - 0.35), r)
kit.stripe("StripeLower", (HX1 - HX0 + 0.02, 3.22, 0.08), (hc, 0, SKID_T + 0.45), r)
# Inspection door and window on the front (-Y) face.
kit.box("Door", (1.3, 0.06, 1.3), (hc - 0.9, -1.61, 1.35), WHITE, r, 0.03)
kit.box("DoorWindow", (0.7, 0.04, 0.4), (hc - 0.9, -1.645, 1.65), GLASS, r, 0.01)
kit.box("DoorHandle", (0.08, 0.06, 0.3), (hc - 0.35, -1.66, 1.3), BLACK, r, 0.01)
kit.box("Vent", (1.0, 0.04, 0.5), (hc + 1.0, -1.62, 1.35), BLACK, r, 0.01)

# Press portal: two white columns and a crosshead over the housing, steel ram with an orange band between them.
RX = 0.3                                      # ram axis X
TOP = 5.0
for y in (-1.25, 1.25):
    kit.box(f"Column{'L' if y > 0 else 'R'}", (0.5, 0.5, TOP - HZ1), (RX, y, (HZ1 + TOP) / 2), WHITE, r, 0.05)
    kit.box(f"ColumnFoot{'L' if y > 0 else 'R'}", (0.75, 0.75, 0.15), (RX, y, HZ1 + 0.075), BLACK, r, 0.03)
kit.box("Crosshead", (1.3, 3.2, 0.6), (RX, 0, TOP + 0.3), WHITE, r, 0.08)
kit.stripe("CrossheadCap", (1.32, 3.22, 0.04), (RX, 0, TOP + 0.55), r, BLACK)
kit.cyl("Ram", 0.42, TOP - HZ1, (RX, 0, (HZ1 + TOP) / 2), STEEL, r, "Z", 28, 0.02)
kit.cyl("RamBand", 0.45, 0.28, (RX, 0, 3.55), ORANGE, r, "Z", 28, 0.02)
kit.cyl("RamGland", 0.55, 0.22, (RX, 0, HZ1 + 0.11), BLACK, r, "Z", 28, 0.03)
kit.cyl("CylinderHead", 0.5, 0.35, (RX, 0, TOP + 0.6 + 0.175), BLACK, r, "Z", 24, 0.04)
# Hydraulic power pack on the back (+Y) side; its two lines climb the housing and the back column to the crosshead.
PX = RX
kit.box("PowerPack", (1.2, 0.7, 1.0), (PX, 2.0, SKID_T + 0.5), BLACK, r, 0.05)
kit.box("PowerPackLid", (1.24, 0.74, 0.08), (PX, 2.0, SKID_T + 1.0), WHITE, r, 0.02)
for i, dx in enumerate((-0.12, 0.12)):
    a, b, c = (PX + dx, 1.8, SKID_T + 1.05), (PX + dx, 1.66, HZ1 + 0.06), (PX + dx, 1.55, TOP - 0.02)
    kit.rod(f"HoseWall{i}", a, b, 0.04, BLACK, r, 8)
    kit.rod(f"HoseColumn{i}", b, c, 0.04, BLACK, r, 8)

# Intake: black hopper with an orange lip whose rim centre is the Sand in port, over an auger into the housing.
HOP_W, HOP_D, HOP_H = 1.0, 1.6, DECK - SKID_T - 0.05
kit.hopper("Hopper", (IN_X, 0, DECK), HOP_W, HOP_D, HOP_H, r)
kit.cyl("Auger", 0.2, IN_X - HX1, ((IN_X + HX1) / 2, 0, SKID_T + 0.25), STEEL, r, "X", 20, 0.02)
kit.box("AugerFoot", (0.5, 0.6, 0.12), (IN_X, 0, SKID_T + 0.06), BLACK, r, 0.02)
kit.cyl("IntakeRing", 0.26, 0.08, (HX1 + 0.05, 0, SKID_T + 0.25), LAMP, r, "X", 20, 0.01)

# Outlet: black discharge box on the -X end, steel chute whose lip is the Coalite out port.
DX0 = -3.0
kit.box("Discharge", (HX0 - DX0 + 0.1, 2.0, 1.5), ((HX0 + DX0) / 2 - 0.05, 0, SKID_T + 0.75), BLACK, r, 0.05)
kit.box("DischargeHood", (HX0 - DX0 + 0.2, 2.1, 0.1), ((HX0 + DX0) / 2 - 0.05, 0, SKID_T + 1.55), WHITE, r, 0.02)
CHUTE_A = 25
kit.chute("Chute", (OUT_X, 0, DECK), (DX0 - OUT_X) + 0.15, 1.0, r, STEEL, direction=(-1, 0, 0), angle=CHUTE_A, depth=0.22)
tan_a = math.tan(math.radians(CHUTE_A))
for i, (dx, y) in enumerate(((0.35, -0.2), (0.7, 0.18))):    # two coalite bricks sliding out, resting on the chute floor
    bx = OUT_X + dx
    kit.box(f"Coalite{i}", (0.24, 0.22, 0.14), (bx, y, DECK + dx * tan_a + 0.07 / math.cos(math.radians(CHUTE_A))),
            BLACK, r, 0.02).rotation_euler = (0, -math.radians(CHUTE_A), 0)

# Control box with the cyan status light, beside the housing on the front (-Y) side.
kit.box("ControlBox", (0.9, 0.5, 1.4), (1.9, -2.05, SKID_T + 0.7), WHITE, r, 0.05)
kit.box("ControlScreen", (0.6, 0.04, 0.4), (1.9, -2.31, SKID_T + 1.05), GLASS, r, 0.01)
for i in range(3):
    kit.box(f"ControlButton{i}", (0.1, 0.04, 0.08), (1.7 + i * 0.2, -2.31, SKID_T + 0.65), BLACK if i else ORANGE, r, 0.01)
kit.cyl("StatusLight", 0.09, 0.14, (1.9, -2.05, SKID_T + 1.47), CYAN, r, "Z", 16, 0.02)
kit.rod("Conduit", (1.9, -1.8, SKID_T + 0.2), (1.9, -1.62, SKID_T + 0.2), 0.05, BLACK, r, 8)

# Ports: Unity -X is Blender +X, so the flow along Unity +X is Blender (-1, 0, 0) for both.
kit.port("Sand in", (IN_X, 0, DECK), (-1, 0, 0), False, "Sand", r)
kit.port("Coalite out", (OUT_X, 0, DECK), (-1, 0, 0), True, "ryanhartwig.coalite", r)

# Fit, measured on the model parts only (the guide is added after).
lo, hi = kit.bounds(r)
print(f"MODEL bounds lo=({lo.x:.2f},{lo.y:.2f},{lo.z:.2f}) hi=({hi.x:.2f},{hi.y:.2f},{hi.z:.2f})")
assert lo.z >= -0.01, f"floor below ground: {lo.z}"
assert -HALF_X <= lo.x and hi.x <= HALF_X and -HALF_Y <= lo.y and hi.y <= HALF_Y, "outside the donor footprint"
assert hi.z <= HEIGHT, f"taller than the donor: {hi.z}"
kit.footprint_guide(HALF_X, HALF_Y, HEIGHT, r)

def unity(v):
    return (-v[0], v[2], -v[1])

import bpy
bpy.context.view_layer.update()
for o in sorted((c for c in r.children if c.name.startswith("Port_")), key=lambda o: o.name):
    p = o.matrix_world.translation
    d = o.matrix_world.to_3x3().col[1]
    print("PORT %s output=%d cargo=%s unity pos=(%.2f, %.2f, %.2f) dir=(%.2f, %.2f, %.2f)" %
          (o.name, o["output"], o["cargo"], *[round(x, 4) + 0.0 for x in unity(p)], *[round(x, 4) + 0.0 for x in unity(d)]))

kit.save(os.path.join(HERE, "coking_oven.blend"))
kit.preview(r, os.path.join(HERE, "preview_oven"), structure=True)
kit.export(r, os.path.join(HERE, "..", "coking_oven.bin"))
