# Power pole: PowerGrid's grid node (base SolarPostKind, footprint 0.6 x 0.6 m). A black base plate with a white
# plinth, a 5 m steel mast with an orange band and a junction box, a 1.2 m white cross-arm near the top with two
# insulator knobs, and an orange beacon on the cap. Run from the repo root:
#   "<blender.exe>" -b --factory-startup --python mods/PowerGrid/blender/power_pole.py
# Writes power_pole.blend and preview_pole_{hero,side,front,rear}.png here and ../power_pole.bin.
import os, sys, math
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN

HALF_X, HALF_Y = 0.3, 0.3                     # Footprint (0.6, 0.6) / 2; Blender X, Y = Unity -x, -z
PLATE_T = 0.06                                # steel base plate
PLINTH_H = 0.3                                # the 0.3 m base the mast stands on
MAST_H = 5.0
MAST_R = 0.1
ARM_LEN = 1.2                                 # cross-arm, along X: overhangs the 0.6 m footprint at height (by brief)
ARM_Z = PLATE_T + PLINTH_H + MAST_H - 0.35    # cross-arm centre height

kit.reset()
r = kit.root("PowerPole_Root")

# Base: black plate on the ground, four steel anchor bolts, white plinth with an orange band.
kit.box("BasePlate", (0.58, 0.58, PLATE_T), (0, 0, PLATE_T / 2), BLACK, r, 0.02)
for x in (-0.21, 0.21):
    for y in (-0.21, 0.21):
        kit.cyl(f"Bolt{x}{y}", 0.035, 0.06, (x, y, PLATE_T + 0.03), STEEL, r, "Z", 8, 0.01)
kit.box("Plinth", (0.34, 0.34, PLINTH_H), (0, 0, PLATE_T + PLINTH_H / 2), WHITE, r, 0.04)
kit.stripe("PlinthBand", (0.35, 0.35, 0.06), (0, 0, PLATE_T + PLINTH_H - 0.08), r)

# Mast: steel tube with a black collar at the foot, an orange band at head height and a black cap.
Z0 = PLATE_T + PLINTH_H
TOPZ = Z0 + MAST_H
kit.cyl("Mast", MAST_R, MAST_H, (0, 0, Z0 + MAST_H / 2), STEEL, r, "Z", 16, 0.0)
kit.cyl("MastCollar", MAST_R + 0.04, 0.16, (0, 0, Z0 + 0.08), BLACK, r, "Z", 16, 0.02)
kit.cyl("MastBand", MAST_R + 0.01, 0.3, (0, 0, Z0 + 1.9), ORANGE, r, "Z", 16, 0.0)
kit.cyl("MastCap", MAST_R + 0.03, 0.08, (0, 0, TOPZ + 0.04), BLACK, r, "Z", 16, 0.02)

# Junction box on the front (-Y) of the mast at hand height: white box, cyan status light, conduit down to the plinth.
JZ = Z0 + 1.1
kit.box("JunctionBox", (0.26, 0.14, 0.36), (0, -MAST_R - 0.07, JZ), WHITE, r, 0.03)
kit.box("JunctionLid", (0.2, 0.02, 0.06), (0, -MAST_R - 0.15, JZ + 0.1), BLACK, r, 0.005)
kit.cyl("StatusLight", 0.03, 0.03, (0, -MAST_R - 0.155, JZ - 0.06), CYAN, r, "Y", 12, 0.005)
kit.rod("Conduit", (0.06, -MAST_R - 0.07, JZ - 0.18), (0.06, -MAST_R - 0.07, Z0 + 0.02), 0.025, BLACK, r, 8)

# Cross-arm: white beam across the mast with a black clamp, steel braces, an insulator knob at each end.
kit.box("ArmBeam", (ARM_LEN, 0.12, 0.12), (0, 0, ARM_Z), WHITE, r, 0.025)
kit.box("ArmClamp", (0.3, 0.3, 0.2), (0, 0, ARM_Z), BLACK, r, 0.03)
for s in (-1, 1):
    kit.rod(f"ArmBrace{'L' if s > 0 else 'R'}", (s * 0.42, 0, ARM_Z - 0.06), (s * MAST_R, 0, ARM_Z - 0.55), 0.022, STEEL, r, 8)
    x = s * (ARM_LEN / 2 - 0.1)
    kit.cyl(f"InsulatorPin{'L' if s > 0 else 'R'}", 0.025, 0.12, (x, 0, ARM_Z + 0.12), STEEL, r, "Z", 8, 0.0)
    for i, (rad, z) in enumerate(((0.11, 0.14), (0.095, 0.22), (0.08, 0.30))):
        kit.cyl(f"Insulator{'L' if s > 0 else 'R'}{i}", rad, 0.05, (x, 0, ARM_Z + z), BLACK, r, "Z", 16, 0.015)
    kit.cyl(f"InsulatorKnob{'L' if s > 0 else 'R'}", 0.06, 0.07, (x, 0, ARM_Z + 0.37), ORANGE, r, "Z", 16, 0.02)

# Beacon on top.
kit.cyl("BeaconBase", 0.07, 0.06, (0, 0, TOPZ + 0.11), BLACK, r, "Z", 16, 0.01)
kit.cyl("Beacon", 0.055, 0.1, (0, 0, TOPZ + 0.19), LAMP, r, "Z", 16, 0.02)

# Fit: everything inside the footprint except the cross-arm assembly, which the brief sizes at 1.2 m.
import bpy
from mathutils import Vector
bpy.context.view_layer.update()
lo, hi = kit.bounds(r)
print(f"MODEL bounds lo=({lo.x:.2f},{lo.y:.2f},{lo.z:.2f}) hi=({hi.x:.2f},{hi.y:.2f},{hi.z:.2f})")
OVERHANG = ("Arm", "Insulator")
for o in r.children_recursive:
    if o.type == "MESH" and not o.name.startswith(OVERHANG):
        for c in o.bound_box:
            p = o.matrix_world @ Vector(c)
            assert abs(p.x) <= HALF_X + 1e-3 and abs(p.y) <= HALF_Y + 1e-3, f"{o.name} outside the footprint"
assert lo.z >= -0.01, f"floor below ground: {lo.z}"
HEIGHT = hi.z
kit.footprint_guide(HALF_X, HALF_Y, HEIGHT, r)

kit.save(os.path.join(HERE, "power_pole.blend"))
kit.preview(r, os.path.join(HERE, "preview_pole"), structure=True)
kit.export(r, os.path.join(HERE, "..", "power_pole.bin"))
