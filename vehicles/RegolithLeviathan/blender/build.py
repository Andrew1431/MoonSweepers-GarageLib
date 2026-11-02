# Regolith leviathan concept: Wide line tier 3. A slow tracked crawler with a raised offset cab, a climbing conveyor
# feeding a monumental hopper, and two wing booms carrying gangs of orange roller brushes plus corner disc brushes
# across the full sweep strip. Run from the repo root:
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --factory-startup --python vehicles/RegolithLeviathan/blender/build.py
import math, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN

HERE = os.path.dirname(os.path.abspath(__file__))
s = kit.stats("andrew1431.regolith-leviathan")
half = s["width"] / 2              # sweep strip: outermost brush housings sit at +-half

kit.reset()
r = kit.root("RegolithLeviathan_Root")

# ---- Tracks: two long stadium-shaped crawler belts with light road-wheel hubs on the outside
TX, TW, TL, TH = 1.85, 0.95, 7.4, 1.5      # track centre x, width, length, height
TY = 0.4                                     # track centre y
for sx in (-1, 1):
    side = "L" if sx > 0 else "R"
    x = sx * TX
    kit.box(f"Track{side}", (TW, TL, TH), (x, TY, TH / 2), BLACK, r, 0.7)
    for i in range(26):                     # cleats on the top run
        y = TY - TL / 2 + 0.9 + i * (TL - 1.8) / 25
        kit.box(f"Track{side}_Cleat{i}", (TW + 0.04, 0.12, 0.06), (x, y, TH + 0.01), BLACK, r, 0)
    xo = x + sx * (TW / 2 + 0.02)
    for j, (y, rad) in enumerate(((TY - TL / 2 + 0.72, 0.6), (TY + TL / 2 - 0.72, 0.6))):
        kit.cyl(f"Track{side}_Idler{j}", rad, 0.06, (xo, y, 0.75), WHITE, r, "X", 24, 0.02)
        kit.cyl(f"Track{side}_IdlerCap{j}", rad * 0.35, 0.1, (xo, y, 0.75), BLACK, r, "X", 16, 0)
    for j in range(4):
        y = TY - 1.95 + j * 1.3
        kit.cyl(f"Track{side}_Road{j}", 0.42, 0.05, (xo, y, 0.55), WHITE, r, "X", 20, 0.015)
        kit.cyl(f"Track{side}_RoadCap{j}", 0.14, 0.09, (xo, y, 0.55), BLACK, r, "X", 12, 0)
    # white track fender with orange stripe
    kit.box(f"Fender{side}", (TW + 0.25, TL - 0.6, 0.12), (x, TY, TH + 0.2), WHITE, r, 0.04)
    kit.stripe(f"FenderStripe{side}", (0.012, TL - 1.2, 0.07), (x + sx * (TW / 2 + 0.13), TY, TH + 0.2), r)

# ---- Chassis deck between and over the tracks
kit.box("Deck", (4.7, 8.2, 0.45), (0, 0.5, 1.95), BLACK, r, 0.06)
kit.box("FrontBeam", (5.0, 0.5, 0.55), (0, -3.75, 1.55), BLACK, r, 0.05)
for x in (-2.0, 0, 2.0):
    kit.lamp(f"FrontLamp{x}", (x, -4.01, 1.62), (0.35, 0.03, 0.12), r)
kit.box("RearBumper", (4.8, 0.4, 0.55), (0, 4.75, 1.5), BLACK, r, 0.05)
for x in (-2.1, 2.1):
    kit.lamp(f"TailLamp{x}", (x, 4.96, 1.5), (0.4, 0.03, 0.14), r)

# ---- Monumental hopper (47 t): tall white bin, ribbed, orange lid, black dump door at the back
HY0, HY1, HZ0, HZ1 = -0.9, 4.5, 2.15, 5.7
hy, hl, hh = (HY0 + HY1) / 2, HY1 - HY0, HZ1 - HZ0
kit.box("Hopper", (4.6, hl, hh), (0, hy, HZ0 + hh / 2), WHITE, r, 0.12)
for i, y in enumerate((HY0 + 1.2, hy, HY1 - 1.2)):
    kit.box(f"HopperRib{i}", (4.72, 0.18, hh - 0.3), (0, y, HZ0 + hh / 2), WHITE, r, 0.05)
for sx in (-1, 1):
    side = "L" if sx > 0 else "R"
    kit.stripe(f"HopperStripe{side}", (0.012, hl - 0.4, 0.22), (sx * 2.367, hy, HZ0 + 0.9), r)
    kit.stripe(f"HopperStripeThin{side}", (0.012, hl - 0.4, 0.06), (sx * 2.367, hy, HZ0 + 1.2), r)
kit.box("HopperLid", (4.3, hl - 0.3, 0.14), (0, hy + 0.1, HZ1 + 0.06), ORANGE, r, 0.05)
kit.box("HopperHatch", (1.4, 1.0, 0.12), (-0.9, HY0 + 0.75, HZ1 + 0.15), BLACK, r, 0.03)
kit.box("DumpDoor", (3.8, 0.14, hh - 0.6), (0, HY1 + 0.07, HZ0 + hh / 2 - 0.1), BLACK, r, 0.05)
for x in (-1.6, 1.6):
    kit.cyl(f"DumpHinge{x}", 0.1, 0.4, (x, HY1 + 0.12, HZ1 - 0.25), STEEL, r, "X", 12)
kit.box("LoadGauge", (0.06, 0.25, 2.2), (2.33, HY1 - 0.4, HZ0 + 1.9), BLACK, r, 0.02)
for k in range(5):
    kit.box(f"LoadGaugeBar{k}", (0.02, 0.17, 0.3), (2.37, HY1 - 0.4, HZ0 + 1.0 + k * 0.42), CYAN if k < 3 else BLACK, r, 0)

# ---- Raised cab on the front-left corner, on a black pedestal, with a ladder down the side
CX, CY, CZ = 1.2, -2.45, 3.1               # cab centre x/y, floor height
kit.box("CabPedestal", (1.6, 1.5, CZ - 2.15), (CX, CY + 0.1, (CZ + 2.15) / 2), BLACK, r, 0.05)
kit.box("CabFloor", (2.2, 2.1, 0.2), (CX, CY, CZ), BLACK, r, 0.05)
kit.box("CabBody", (2.1, 2.0, 1.85), (CX, CY + 0.05, CZ + 1.02), WHITE, r, 0.12)
kit.box("CabRoof", (2.25, 2.2, 0.16), (CX, CY + 0.05, CZ + 2.0), WHITE, r, 0.06)
kit.stripe("CabRoofStripe", (2.27, 2.22, 0.05), (CX, CY + 0.05, CZ + 1.92), r)
kit.box("CabGlassFront", (1.85, 0.03, 1.15), (CX, CY - 0.96, CZ + 1.18), GLASS, r, 0.02)
kit.box("CabGlassLow", (1.2, 0.03, 0.5), (CX, CY - 0.96, CZ + 0.38), GLASS, r, 0.02)
for sx in (-1, 1):
    kit.box(f"CabGlassSide{sx}", (0.03, 1.6, 1.0), (CX + sx * 1.056, CY - 0.05, CZ + 1.2), GLASS, r, 0.02)
kit.box("CabGlassBack", (1.6, 0.03, 0.8), (CX, CY + 1.06, CZ + 1.3), GLASS, r, 0.02)
kit.box("CabSeat", (0.6, 0.55, 0.2), (CX, CY + 0.25, CZ + 0.55), BLACK, r, 0.05)
kit.box("CabSeatBack", (0.6, 0.14, 0.7), (CX, CY + 0.55, CZ + 0.95), BLACK, r, 0.05)
kit.box("CabDash", (1.7, 0.35, 0.3), (CX, CY - 0.72, CZ + 0.6), BLACK, r, 0.04)
kit.box("CabVisor", (2.0, 0.35, 0.06), (CX, CY - 1.1, CZ + 1.85), BLACK, r, 0.02)
for x in (CX - 0.75, CX + 0.75):
    kit.lamp(f"CabHeadlight{x:.2f}", (x, CY - 1.0, CZ + 0.15), (0.3, 0.04, 0.1), r)
    kit.cyl(f"CabBeacon{x:.2f}", 0.09, 0.14, (x, CY + 0.6, CZ + 2.15), LAMP, r, "Z", 14)
kit.box("CabLightBar", (1.3, 0.1, 0.07), (CX, CY - 0.95, CZ + 2.1), BLACK, r, 0.01)
for k in range(4):
    kit.lamp(f"CabCyan{k}", (CX - 0.45 + k * 0.3, CY - 1.005, CZ + 2.1), (0.16, 0.02, 0.04), r, CYAN)
kit.rod("Antenna", (CX - 0.7, CY + 0.9, CZ + 2.08), (CX - 0.7, CY + 1.05, CZ + 3.1), 0.015, BLACK, r)
kit.cyl("AntennaTip", 0.04, 0.08, (CX - 0.7, CY + 1.05, CZ + 3.14), LAMP, r)
# railed walkway in front of the cab door and a ladder down the left side
kit.box("Walkway", (0.5, 2.0, 0.08), (2.55, CY + 0.05, CZ - 0.05), BLACK, r, 0.02)
for y in (CY - 0.9, CY + 1.0):
    kit.rod(f"Rail{y:.1f}", (2.78, y, CZ), (2.78, y, CZ + 1.0), 0.03, ORANGE, r)
kit.rod("RailTop", (2.78, CY - 0.9, CZ + 1.0), (2.78, CY + 1.0, CZ + 1.0), 0.03, ORANGE, r)
LY = CY + 1.25
for y in (LY - 0.22, LY + 0.22):
    kit.rod(f"Ladder{y:.2f}", (2.72, y, 0.3), (2.72, y, CZ), 0.03, STEEL, r)
for k in range(9):
    z = 0.45 + k * (CZ - 0.6) / 8
    kit.rod(f"Rung{k}", (2.72, LY - 0.22, z), (2.72, LY + 0.22, z), 0.022, STEEL, r)

# ---- Conveyor climbing from the pickup roller up into the front of the hopper (right side of the cab)
CVX = -0.95
a, b = (CVX, -4.55, 0.65), (CVX, HY0 + 0.4, HZ1 + 0.35)
dy, dz = b[1] - a[1], b[2] - a[2]
L, ang = math.hypot(dy, dz), math.atan2(dz, dy)
mid = (CVX, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2)
kit.box("ConveyorTrough", (1.1, L, 0.3), mid, BLACK, r, 0.04, rot=(ang, 0, 0))
nrm = (0, -math.sin(ang), math.cos(ang))   # up-normal of the incline
def along(t, off):
    return (CVX, a[1] + dy * t + nrm[1] * off, a[2] + dz * t + nrm[2] * off)
# covered belt: white tunnel over the black trough, segmented by black bands, orange stripe each side
kit.box("ConveyorCover", (1.2, L - 0.6, 0.42), along(0.52, 0.32), WHITE, r, 0.12, rot=(ang, 0, 0))
for k in range(5):
    kit.box(f"ConveyorBand{k}", (1.26, 0.1, 0.48), along(0.18 + k * 0.17, 0.32), BLACK, r, 0.02, rot=(ang, 0, 0))
for sx in (-1, 1):
    kit.stripe(f"ConveyorStripe{sx}", (0.012, L - 1.2, 0.09), (CVX + sx * 0.605, *along(0.52, 0.32)[1:]), r).rotation_euler = (ang, 0, 0)
kit.box("ConveyorMouth", (1.3, 0.6, 0.6), along(0.03, 0.15), BLACK, r, 0.08, rot=(ang, 0, 0))
kit.box("ConveyorHead", (1.4, 1.3, 0.95), (CVX, HY0 + 0.35, HZ1 + 0.3), WHITE, r, 0.14)
kit.stripe("ConveyorHeadStripe", (1.42, 1.32, 0.08), (CVX, HY0 + 0.35, HZ1 + 0.55), r)
kit.lamp("ConveyorHeadLamp", (CVX, HY0 - 0.31, HZ1 + 0.3), (0.5, 0.03, 0.1), r, CYAN)
kit.rod("ConveyorStrut", (CVX, -2.8, 2.15), (CVX, -2.8, 0.65 + (1.75) * math.tan(ang) - 0.2), 0.07, STEEL, r)

# ---- Brush gangs: centre pickup roller ahead of the conveyor mouth, staggered wing rollers on booms, corner discs
FY, WY = -5.15, -4.45                       # centre roller y, wing roller y
RR = 0.32                                    # roller radius
kit.roller_brush("BrushCentre", (0, FY, RR + 0.02), 4.2, RR, r)
kit.box("PickupHood", (4.4, 0.9, 0.35), (0, FY + 0.45, RR * 2 + 0.25), BLACK, r, 0.06)
kit.stripe("PickupStripe", (4.0, 0.012, 0.08), (0, FY - 0.01, RR * 2 + 0.25), r)
for x in (-1.6, 1.6):
    kit.rod(f"PickupArm{x}", (x, FY + 0.6, RR * 2 + 0.3), (x, -3.95, 1.4), 0.07, BLACK, r)
inner, outer = 1.95, half - 0.07            # end caps add 0.07, so housings stop exactly at the strip edge
wl = outer - inner
for sx in (-1, 1):
    side = "L" if sx > 0 else "R"
    cx = sx * (inner + wl / 2)
    kit.roller_brush(f"BrushWing{side}", (cx, WY, RR + 0.02), wl, RR, r)
    # boom: white box beam above the roller, hinged to the front beam, braced back to the deck
    kit.box(f"Boom{side}", (wl + 0.1, 0.42, 0.42), (sx * (inner + wl / 2 - 0.05), WY + 0.05, 1.45), WHITE, r, 0.08)
    kit.stripe(f"BoomStripe{side}", (wl - 0.4, 0.012, 0.12), (sx * (inner + wl / 2), WY - 0.17, 1.45), r)
    kit.cyl(f"BoomHinge{side}", 0.3, 0.6, (sx * 2.45, WY + 0.25, 1.5), BLACK, r, "Z", 16)
    r0, r1 = kit.Vector((sx * 2.35, -2.6, 2.05)), kit.Vector((sx * (outer - 0.9), WY + 0.05, 1.62))
    kit.rod(f"BoomRam{side}", r0, r1, 0.08, STEEL, r)
    kit.rod(f"BoomRamSleeve{side}", r0, r0 + (r1 - r0) * 0.55, 0.13, BLACK, r, 12)
    for k, f in enumerate((0.15, 0.5, 0.85)):
        x = sx * (inner + wl * f)
        kit.box(f"BoomHanger{side}{k}", (0.14, 0.18, 1.45 - (RR * 2 + 0.12)), (x, WY + 0.02, (1.45 + RR * 2 + 0.12) / 2), BLACK, r, 0.02)
    kit.cyl(f"BoomBeacon{side}", 0.1, 0.18, (sx * (outer - 0.3), WY + 0.05, 1.75), LAMP, r, "Z", 14)
    # corner disc brush ahead of the wing tip, rim inside the strip edge
    dr = 0.62
    dx = sx * (half - dr - 0.02)
    kit.disc_brush(f"DiscCorner{side}", (dx, WY - 0.95, 0.14), dr, r, 0.12)
    kit.rod(f"DiscArm{side}", (dx, WY - 0.95, 0.4), (dx, WY - 0.1, 1.3), 0.06, BLACK, r)

kit.save(os.path.join(HERE, "regolith_leviathan.blend"))
kit.preview(r, os.path.join(HERE, "preview"))
kit.export(r, os.path.join(HERE, "..", "regolith_leviathan.bin"))
