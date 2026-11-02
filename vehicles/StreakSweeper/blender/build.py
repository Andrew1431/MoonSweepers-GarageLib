# Streak sweeper concept: Fast line tier 2, after the Sprint vacuum. A rally-raid sweeper: low wedge body, low cab
# with wraparound glass, rear wing, big exposed rear wheels, side pods holding the hopper, and a front splitter
# carrying a full-width roller brush bar. Run from the repo root:
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --factory-startup --python vehicles/StreakSweeper/blender/build.py
import math, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN

HERE = os.path.dirname(os.path.abspath(__file__))
s = kit.stats("andrew1431.streak-sweeper")
brush = s["width"]                 # sweep strip = roller length (4.32 m)

kit.reset()
r = kit.root("StreakSweeper_Root")

# Wheels: small steering fronts, big exposed rear drivers
FY, FR, FX = -1.55, 0.42, 0.98
RY, RR, RX = 1.38, 0.62, 1.12
for x in (-FX, FX):
    kit.wheel(f"WheelF{'L' if x > 0 else 'R'}", (x, FY, FR), FR, 0.32, r, WHITE)
for x in (-RX, RX):
    kit.wheel(f"WheelB{'L' if x > 0 else 'R'}", (x, RY, RR), RR, 0.5, r, WHITE)

# Chassis: black skirt, white wedge body with a long sloping nose
kit.box("Skirt", (1.5, 4.3, 0.22), (0, -0.05, 0.4), BLACK, r, 0.05)
kit.wedge("Body", (1.5, 4.1, 0.62), (0, -0.1, 0.8), WHITE, r, slope=0.42, bevel=0.05)
kit.box("RearDeck", (1.5, 1.2, 0.2), (0, 1.35, 1.0), WHITE, r, 0.05)
NOSE = math.atan2(0.62, 4.1 * 0.42)  # nose ramp angle; stripes lie on it
for x in (-0.36, 0.36):            # twin racing stripes down the nose
    kit.box(f"NoseStripe{x}", (0.12, 1.75, 0.02), (x, -1.29, 0.80 + 0.012), ORANGE, r, 0.003, rot=(NOSE, 0, 0))

# Side pods = the hopper. Long white wedges between the wheels, orange lids, black intake ducts from the splitter
PX, PY, PL = 1.02, -0.2, 1.75
for sx in (-1, 1):
    t = 'L' if sx > 0 else 'R'
    kit.wedge(f"Pod{t}", (0.6, PL, 0.76), (sx * PX, PY, 0.78), WHITE, r, slope=0.35, bevel=0.05)
    kit.box(f"PodLid{t}", (0.54, PL * 0.62, 0.04), (sx * PX, PY + PL * 0.18, 1.18), ORANGE, r, 0.015)
    kit.box(f"PodSill{t}", (0.6, PL + 0.05, 0.16), (sx * PX, PY, 0.36), BLACK, r, 0.04)
    kit.stripe(f"PodStripe{t}", (0.012, PL * 0.85, 0.07), (sx * (PX + 0.305), PY + 0.05, 0.88), r)
    kit.box(f"PodIntake{t}", (0.36, 0.08, 0.22), (sx * PX, PY - PL / 2 + 0.06, 0.52), BLACK, r, 0.02)
    kit.box(f"PodVent{t}", (0.012, 0.35, 0.16), (sx * (PX + 0.305), PY + PL * 0.33, 0.64), BLACK, r, 0.005)
    # front fender over the steering wheel, black arch; quarter fender over the rear tyre top
    kit.box(f"FenderF{t}", (0.4, 0.8, 0.05), (sx * FX, FY, FR * 2 + 0.05), BLACK, r, 0.02)
    kit.rod(f"FenderFStay{t}", (sx * 0.7, FY + 0.15, 0.68), (sx * (FX - 0.1), FY + 0.15, FR * 2 + 0.04), 0.025, BLACK, r)
    kit.box(f"FenderB{t}", (0.56, 0.7, 0.05), (sx * RX, RY - 0.12, RR * 2 + 0.05), BLACK, r, 0.02)
    kit.rod(f"FenderStay{t}", (sx * 0.75, RY - 0.12, 1.05), (sx * (RX - 0.2), RY - 0.12, RR * 2 + 0.03), 0.025, BLACK, r)
    kit.box(f"MudFlap{t}", (0.48, 0.03, 0.42), (sx * RX, RY + RR + 0.1, 0.32), BLACK, r, 0.01)
    kit.box(f"FlapBracket{t}", (0.62, 0.06, 0.06), (sx * 1.05, RY + RR + 0.1, 0.54), BLACK, r, 0.01)
    kit.stripe(f"FlapStripe{t}", (0.48, 0.035, 0.05), (sx * RX, RY + RR + 0.1, 0.48), r)

# Low cab with wraparound glass and a white roof cap
CY = 0.05
kit.box("CabBase", (1.36, 1.5, 0.12), (0, CY, 1.16), WHITE, r, 0.04)
kit.wedge("Canopy", (1.28, 1.45, 0.46), (0, CY, 1.44), GLASS, r, slope=0.48, bevel=0.06)
kit.wedge("Roof", (1.12, 0.7, 0.06), (0, CY + 0.36, 1.68), WHITE, r, slope=0.25, bevel=0.02)
kit.stripe("RoofStripe", (0.16, 0.6, 0.012), (0, CY + 0.4, 1.715), r)
kit.box("Seat", (0.5, 0.45, 0.3), (0, CY + 0.25, 1.35), BLACK, r, 0.05)
kit.box("RoofLightBar", (0.8, 0.1, 0.07), (0, CY + 0.06, 1.69), BLACK, r, 0.02)
for x in (-0.27, -0.09, 0.09, 0.27):
    kit.lamp(f"RoofLamp{x}", (x, CY + 0.005, 1.69), (0.14, 0.02, 0.05), r)
kit.box("Snorkel", (0.12, 0.12, 0.6), (0.72, CY + 0.55, 1.42), BLACK, r, 0.03)
kit.box("SnorkelCap", (0.16, 0.2, 0.08), (0.72, CY + 0.5, 1.74), BLACK, r, 0.02)
kit.rod("Antenna", (-0.5, CY + 0.6, 1.24), (-0.5, CY + 0.75, 2.15), 0.012, BLACK, r)
kit.cyl("AntennaTip", 0.035, 0.07, (-0.5, CY + 0.75, 2.17), LAMP, r)

# Rally-raid spare tyre lying on the rear deck, under the wing
kit.cyl("Spare", 0.38, 0.24, (0, 1.38, 1.22), BLACK, r, "Z", 28, 0.04)
kit.cyl("SpareHub", 0.2, 0.25, (0, 1.38, 1.22), WHITE, r, "Z", 16, 0.01)

# Rear wing on swan-neck pylons, black endplates, orange edge
WY, WZ = 1.95, 1.9
kit.box("Wing", (2.3, 0.5, 0.06), (0, WY, WZ), WHITE, r, 0.025)
kit.stripe("WingStripe", (2.3, 0.04, 0.065), (0, WY + 0.25, WZ), r)
for sx in (-1, 1):
    kit.box(f"WingEnd{sx}", (0.04, 0.56, 0.22), (sx * 1.17, WY, WZ - 0.04), BLACK, r, 0.01)
    kit.rod(f"WingPylon{sx}", (sx * 0.45, 1.75, 1.08), (sx * 0.45, WY, WZ - 0.03), 0.04, BLACK, r)

# Tail: black diffuser, orange lamps, cyan running lights, hopper dump chute
kit.box("Diffuser", (1.4, 0.3, 0.26), (0, 2.05, 0.42), BLACK, r, 0.03)
kit.box("TailPanel", (1.5, 0.06, 0.32), (0, 1.97, 0.85), BLACK, r, 0.02)
for x in (-0.55, 0.55):
    kit.lamp(f"TailLamp{x}", (x, 2.005, 0.88), (0.26, 0.03, 0.1), r)
kit.lamp("TailCyan", (0, 2.005, 0.76), (0.6, 0.03, 0.04), r, CYAN)
kit.box("DumpChute", (0.4, 0.2, 0.22), (0, 2.12, 0.6), BLACK, r, 0.03)

# Front splitter carrying the brush bar
SY = -2.35
kit.box("Splitter", (2.0, 0.7, 0.07), (0, SY, 0.3), BLACK, r, 0.02)
kit.box("SplitterLip", (2.0, 0.05, 0.04), (0, SY - 0.35, 0.3), ORANGE, r, 0.01)
for x in (-0.38, 0.38):
    kit.lamp(f"Headlight{x}", (x, -2.21, 0.42), (0.3, 0.03, 0.08), r)
kit.lamp("NoseCyan", (0, -2.21, 0.33), (0.5, 0.03, 0.03), r, CYAN)
BY, BZ, BR = -2.9, 0.18, 0.17
kit.roller_brush("Brush", (0, BY, BZ), brush, BR, r)
kit.box("BrushBeam", (brush, 0.12, 0.1), (0, BY + 0.06, BZ + BR + 0.12), BLACK, r, 0.02)
for x in (-0.75, 0.75):            # struts from splitter to the beam
    kit.rod(f"BrushArm{x}", (x, SY - 0.2, 0.32), (x, BY + 0.06, BZ + BR + 0.1), 0.04, BLACK, r)
for sx in (-1, 1):                 # diagonal braces out to the bar tips, orange tip lamps
    kit.rod(f"BrushBrace{sx}", (sx * 0.9, SY - 0.05, 0.34), (sx * brush * 0.38, BY + 0.06, BZ + BR + 0.12), 0.03, BLACK, r)
    kit.lamp(f"BarTip{sx}", (sx * (brush / 2 - 0.08), BY + 0.06, BZ + BR + 0.2), (0.1, 0.1, 0.06), r)

kit.save(os.path.join(HERE, "streak_sweeper.blend"))
kit.preview(r, os.path.join(HERE, "preview"))
kit.export(r, os.path.join(HERE, "..", "streak_sweeper.bin"))
