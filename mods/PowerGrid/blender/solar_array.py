# Solar array: PowerGrid's 10 kW source (base SolarTwinKind, footprint 4 x 2 m). A 4 x 2 m panel of dark glass cells
# on a white tray, tilted 20 degrees to face the front (-Y), on three steel A-frames over black ground rails, with a
# white inverter box and cyan light under the high edge. Run from the repo root:
#   "<blender.exe>" -b --factory-startup --python mods/PowerGrid/blender/solar_array.py
# Writes solar_array.blend and preview_array_{hero,side,front,rear}.png here and ../solar_array.bin.
import os, sys, math
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN

HALF_X, HALF_Y = 2.0, 1.0                     # Footprint (4, 2) / 2; Blender X, Y = Unity -x, -z
PANEL_W, PANEL_D, PANEL_T = 3.96, 1.96, 0.08  # panel tray: width along X, length up the slope, thickness
TILT = math.radians(20)                       # panel rises toward +Y, faces -Y
LOW_EDGE = 0.5                                # height of the panel's low (front) edge underside
COLS, ROWS = 8, 4                             # glass cells
FRAMES_X = (-1.55, 0.0, 1.55)                 # A-frame stations

kit.reset()
r = kit.root("SolarArray_Root")
c, s = math.cos(TILT), math.sin(TILT)
# Panel centre: the tray's mid-plane, so its low underside edge sits at LOW_EDGE.
PC = (0.0, 0.0, LOW_EDGE + (PANEL_D / 2) * s + (PANEL_T / 2) * c)


def on_panel(u, v, w):
    """Panel-local (u along X, v up the slope, w out of the face) to world."""
    return (PC[0] + u, PC[1] + v * c - w * s, PC[2] + v * s + w * c)


ROT = (TILT, 0, 0)
# Tray with a black back and an orange stripe along the low edge.
kit.box("PanelTray", (PANEL_W, PANEL_D, PANEL_T), PC, WHITE, r, 0.02, ROT)
kit.box("PanelBack", (PANEL_W - 0.1, PANEL_D - 0.1, 0.03), on_panel(0, 0, -PANEL_T / 2 - 0.015), BLACK, r, 0.01, ROT)
kit.box("PanelStripe", (PANEL_W - 0.3, 0.05, 0.012), on_panel(0, -PANEL_D / 2 + 0.06, PANEL_T / 2 + 0.006), ORANGE, r, 0.003, ROT)
# Cells: dark glass tiles with white gaps between them.
GAP = 0.035
inner_w, inner_d = PANEL_W - 0.12, PANEL_D - 0.22
cw, cd = (inner_w - GAP * (COLS - 1)) / COLS, (inner_d - GAP * (ROWS - 1)) / ROWS
v0 = -PANEL_D / 2 + 0.15
for i in range(COLS):
    for j in range(ROWS):
        u = -inner_w / 2 + cw / 2 + i * (cw + GAP)
        v = v0 + cd / 2 + j * (cd + GAP)
        kit.box(f"Cell{i}_{j}", (cw, cd, 0.02), on_panel(u, v, PANEL_T / 2 + 0.008), GLASS, r, 0.006, ROT)

# Support: a steel beam along X under the panel's middle, carried by three A-frames on two black ground rails.
APEX = on_panel(0, 0, -PANEL_T / 2 - 0.03 - 0.07)
kit.cyl("TorqueTube", 0.07, 3.5, (0, APEX[1], APEX[2]), STEEL, r, "X", 16, 0.0)
LEG_Y = 0.78
RAIL_T = 0.1
TIE_Z = 0.34
for k, x in enumerate(FRAMES_X):
    kit.box(f"Saddle{k}", (0.16, 0.22, 0.12), (x, APEX[1], APEX[2] + 0.02), BLACK, r, 0.02)
    for side, y in (("F", -LEG_Y), ("B", LEG_Y)):
        kit.rod(f"Leg{k}{side}", (x, y, RAIL_T), (x, APEX[1], APEX[2] - 0.05), 0.04, STEEL, r, 10)
    f = (TIE_Z - RAIL_T) / (APEX[2] - 0.05 - RAIL_T)    # how far up the legs the tie sits
    ya, yb = -LEG_Y + f * (APEX[1] + LEG_Y), LEG_Y + f * (APEX[1] - LEG_Y)
    kit.rod(f"Tie{k}", (x, ya, TIE_Z), (x, yb, TIE_Z), 0.025, STEEL, r, 8)
for side, y in (("F", -LEG_Y), ("B", LEG_Y)):
    kit.box(f"Rail{side}", (3.5, 0.2, RAIL_T), (0, y, RAIL_T / 2), BLACK, r, 0.02)
for x in (-1.75, 1.75):
    for y in (-LEG_Y, LEG_Y):
        kit.box(f"Foot{x}{y}", (0.26, 0.26, 0.04), (x, y, 0.02), STEEL, r, 0.01)

# Inverter: white box on the back rail with a vent and cyan status light, a black cable up to the panel back.
IX = 0.8
kit.box("Inverter", (0.5, 0.26, 0.36), (IX, LEG_Y, RAIL_T + 0.18), WHITE, r, 0.03)
kit.box("InverterVent", (0.34, 0.02, 0.08), (IX, LEG_Y - 0.135, RAIL_T + 0.26), BLACK, r, 0.005)
kit.cyl("StatusLight", 0.03, 0.03, (IX + 0.15, LEG_Y - 0.14, RAIL_T + 0.1), CYAN, r, "Y", 12, 0.005)
kit.rod("Cable", (IX - 0.12, LEG_Y, RAIL_T + 0.36), on_panel(IX - 0.12, 0.6, -PANEL_T / 2 - 0.03), 0.025, BLACK, r, 8)

# Fit, measured on the model parts only (the guide is added after).
lo, hi = kit.bounds(r)
print(f"MODEL bounds lo=({lo.x:.2f},{lo.y:.2f},{lo.z:.2f}) hi=({hi.x:.2f},{hi.y:.2f},{hi.z:.2f})")
assert lo.z >= -0.01, f"floor below ground: {lo.z}"
assert -HALF_X <= lo.x and hi.x <= HALF_X and -HALF_Y <= lo.y and hi.y <= HALF_Y, "outside the footprint"
kit.footprint_guide(HALF_X, HALF_Y, hi.z, r)

kit.save(os.path.join(HERE, "solar_array.blend"))
kit.preview(r, os.path.join(HERE, "preview_array"), structure=True)
kit.export(r, os.path.join(HERE, "..", "solar_array.bin"))
