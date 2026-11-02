# Colossus vacuum concept: Wide line tier 2, after the Titan sweeper. Three-axle hauler with a hinged full-width
# suction head (centre section + two folding wings) and a ribbed "elephant trunk" duct that arcs from the head into a
# tall white drum tank. Run from the repo root:
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --factory-startup --python vehicles/ColossusVacuum/blender/build.py
import math, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
s = kit.stats("andrew1431.colossus-vacuum")
span = s["width"]                  # sweep strip = total suction head / roller span
half = span / 2

kit.reset()
r = kit.root("ColossusVacuum_Root")

# --- Chassis: long black ladder frame with white side skirts and orange stripe
kit.box("Frame", (2.3, 7.2, 0.45), (0, 0.15, 0.95), BLACK, r, 0.05)
for sx in (-1, 1):
    t = "L" if sx > 0 else "R"
    kit.box(f"Skirt{t}", (0.12, 5.6, 0.5), (sx * 1.2, 0.55, 1.2), WHITE, r, 0.04)
    kit.stripe(f"SkirtStripe{t}", (0.012, 5.4, 0.1), (sx * 1.265, 0.55, 1.25), r)
kit.box("FrontBumper", (2.9, 0.35, 0.45), (0, -3.55, 0.85), BLACK, r, 0.08)
kit.box("RearBumper", (2.7, 0.3, 0.35), (0, 3.85, 0.85), BLACK, r, 0.06)

# --- Wheels: three axles, big chunky tyres, black arches
R_W, W_W = 0.68, 0.52
for y, tag in ((-2.35, "F"), (1.55, "M"), (3.0, "B")):
    for sx in (-1, 1):
        t = "L" if sx > 0 else "R"
        wl = (sx * 1.55, y, R_W)
        kit.wheel(f"Wheel{tag}{t}", wl, R_W, W_W, r, WHITE, chunky=False)
        for i in range(12):   # cheap unbevelled tread lugs (kit's chunky treads cost ~1.5k tris per wheel)
            a = 2 * math.pi * i / 12
            lug = kit.box(f"Wheel{tag}{t}_Lug{i}", (W_W * 0.92, R_W * 0.24, 0.07),
                          Vector(wl) + Vector((0, math.sin(a) * R_W, math.cos(a) * R_W)), BLACK, r, 0)
            lug.rotation_euler = (-a, 0, 0)
for y, ln in ((-2.35, 1.6), (2.275, 3.05)):
    for sx in (-1, 1):
        t = "L" if sx > 0 else "R"
        kit.box(f"Arch{y}{t}", (0.64, ln, 0.14), (sx * 1.55, y, 1.5), BLACK, r, 0.05)
        kit.box(f"ArchLip{y}{t}", (0.08, ln, 0.3), (sx * 1.83, y, 1.38), BLACK, r, 0.03)

# --- Cab: tall forward cab, big dark glass, seat visible through it
CY0, CY1 = -3.4, -1.55            # cab front / back
cab_cy = (CY0 + CY1) / 2
kit.box("CabBase", (2.5, CY1 - CY0, 0.7), (0, cab_cy, 1.55), WHITE, r, 0.08)
kit.box("CabUpper", (2.4, CY1 - CY0 - 0.25, 1.35), (0, cab_cy + 0.12, 2.55), WHITE, r, 0.1)
kit.box("Windscreen", (2.2, 0.06, 1.05), (0, CY0 + 0.11, 2.6), GLASS, r, 0.03)
for sx in (-1, 1):
    t = "L" if sx > 0 else "R"
    kit.box(f"SideWindow{t}", (0.06, 1.1, 0.95), (sx * 1.205, cab_cy - 0.05, 2.62), GLASS, r, 0.02)
    kit.stripe(f"CabStripe{t}", (0.012, CY1 - CY0 - 0.1, 0.12), (sx * 1.255, cab_cy, 1.75), r)
    kit.rod(f"MirrorArm{t}", (sx * 1.2, CY0 + 0.35, 2.75), (sx * 1.55, CY0 + 0.2, 2.75), 0.025, BLACK, r)
    kit.box(f"Mirror{t}", (0.08, 0.06, 0.38), (sx * 1.57, CY0 + 0.2, 2.65), BLACK, r, 0.02)
kit.stripe("CabFrontStripe", (2.3, 0.012, 0.12), (0, CY0 - 0.006, 1.75), r)
kit.box("CabRoof", (2.5, CY1 - CY0 - 0.1, 0.12), (0, cab_cy + 0.1, 3.27), BLACK, r, 0.04)
kit.box("RoofBar", (1.9, 0.14, 0.1), (0, CY0 + 0.35, 3.38), BLACK, r, 0.02)
for x in (-0.75, 0.75):
    kit.cyl(f"Beacon{x}", 0.1, 0.16, (x, CY0 + 0.35, 3.5), LAMP, r, "Z", 16)
for x in (-0.3, 0, 0.3):
    kit.lamp(f"RoofCyan{x}", (x, CY0 + 0.27, 3.38), (0.18, 0.03, 0.06), r, CYAN)
kit.box("SeatBase", (0.6, 0.55, 0.18), (0.35, cab_cy + 0.25, 2.05), BLACK, r, 0.05)
kit.box("SeatBack", (0.6, 0.14, 0.75), (0.35, cab_cy + 0.55, 2.45), BLACK, r, 0.05)
kit.box("Dash", (2.1, 0.35, 0.3), (0, CY0 + 0.35, 2.15), BLACK, r, 0.04)
kit.cyl("SteeringWheel", 0.2, 0.05, (0.35, CY0 + 0.6, 2.4), BLACK, r, "Y", 20)
for x in (-0.85, 0.85):
    kit.lamp(f"Headlight{x}", (x, CY0 - 0.02, 1.45), (0.4, 0.04, 0.14), r)
kit.box("Grille", (0.9, 0.04, 0.35), (0, CY0 - 0.02, 1.45), BLACK, r, 0.02)

# Ladder up the left side of the cab
for sy in (-0.12, 0.12):
    kit.rod(f"LadderRail{sy}", (1.32, CY1 - 0.2 + sy, 0.6), (1.32, CY1 - 0.2 + sy, 2.1), 0.022, STEEL, r)
for i in range(5):
    z = 0.75 + i * 0.3
    kit.rod(f"LadderRung{i}", (1.32, CY1 - 0.32, z), (1.32, CY1 - 0.08, z), 0.018, STEEL, r)

# --- Tank: tall horizontal-drum hopper on a cradle
TY0, TY1, TR = -1.3, 3.75, 1.4
ty = (TY0 + TY1) / 2
TZ = 1.25 + TR + 0.15
kit.box("TankCradle", (2.4, TY1 - TY0 - 0.2, 0.3), (0, ty, 1.35), BLACK, r, 0.04)
for y in (TY0 + 0.6, ty, TY1 - 0.6):
    kit.box(f"Saddle{y:.1f}", (2.6, 0.3, 0.9), (0, y, 1.75), BLACK, r, 0.05)
kit.cyl("Tank", TR, TY1 - TY0, (0, ty, TZ), WHITE, r, "Y", 36, 0.08)
for y in (TY0 + 1.2, TY1 - 1.2):
    kit.cyl(f"TankBand{y:.1f}", TR + 0.02, 0.22, (0, y, TZ), ORANGE, r, "Y", 28, 0)
kit.cyl("TankFrontCap", TR * 0.9, 0.08, (0, TY0 - 0.02, TZ), WHITE, r, "Y", 36, 0.03)
# Rear dump door: black ring, white door, steel hinge, orange lamps
kit.cyl("DumpRing", TR * 0.95, 0.1, (0, TY1 + 0.04, TZ), BLACK, r, "Y", 36, 0.03)
kit.cyl("DumpDoor", TR * 0.82, 0.14, (0, TY1 + 0.08, TZ), WHITE, r, "Y", 36, 0.04)
kit.box("DumpHinge", (1.6, 0.2, 0.18), (0, TY1 + 0.12, TZ + TR * 0.9), STEEL, r, 0.03)
for sx in (-1, 1):
    kit.box(f"DumpLatch{sx}", (0.18, 0.12, 0.25), (sx * 0.95, TY1 + 0.15, TZ - 0.75), BLACK, r, 0.03)
    kit.lamp(f"TailLamp{sx}", (sx * 1.1, 3.98, 0.95), (0.35, 0.04, 0.14), r)
    kit.rod(f"DumpRam{sx}", (sx * 1.2, TY1 - 0.4, 1.6), (sx * 1.15, TY1, TZ + 0.9), 0.06, STEEL, r)
# Walkway rail and hatch on top
kit.box("Walkway", (0.8, 3.6, 0.06), (-0.4, ty, TZ + TR + 0.02), BLACK, r, 0.02)
kit.cyl("TopHatch", 0.4, 0.14, (-0.4, ty + 0.8, TZ + TR + 0.08), WHITE, r, "Z", 20, 0.03)
for y in (ty - 1.6, ty, ty + 1.6):
    kit.rod(f"RailPost{y:.1f}", (-0.85, y, TZ + TR - 0.05), (-0.85, y, TZ + TR + 0.75), 0.022, STEEL, r)
kit.rod("RailTop", (-0.85, ty - 1.6, TZ + TR + 0.75), (-0.85, ty + 1.6, TZ + TR + 0.75), 0.022, STEEL, r)
# Exhaust / blower stacks behind the cab
for sx in (-1, 1):
    kit.cyl(f"Stack{sx}", 0.13, 1.6, (sx * 1.05, TY0 - 0.12, 3.25), BLACK, r, "Z", 12, 0)
    kit.cyl(f"StackCap{sx}", 0.16, 0.12, (sx * 1.05, TY0 - 0.12, 4.1), STEEL, r, "Z", 16)
kit.rod("Antenna", (-0.95, TY1 - 0.5, TZ + TR - 0.2), (-0.95, TY1 - 0.3, TZ + TR + 1.4), 0.014, BLACK, r)
kit.cyl("AntennaTip", 0.04, 0.08, (-0.95, TY1 - 0.3, TZ + TR + 1.42), LAMP, r)
kit.stripe("TankDecal", (0.012, 1.6, 0.18), (TR + 0.005, ty, TZ + 0.2), r)
kit.lamp("TankCyanL", (TR + 0.01, ty - 1.4, TZ - 0.3), (0.03, 0.4, 0.06), r, CYAN)
kit.lamp("TankCyanR", (-TR - 0.01, ty - 1.4, TZ - 0.3), (0.03, 0.4, 0.06), r, CYAN)

# --- Suction head: centre section + two hinged wings, rollers span exactly `span`
HY, HZ, RR = -4.55, 0.28, 0.26
centre_w = 3.0
wing_w = half - centre_w / 2
kit.roller_brush("RollerC", (0, HY, HZ), centre_w - 0.1, RR, r)
kit.box("HeadC", (centre_w, 0.95, 0.42), (0, HY + 0.1, HZ + 0.5), BLACK, r, 0.06)
kit.wedge("PlenumC", (1.5, 0.9, 0.6), (-0.7, HY + 0.15, HZ + 1.0), BLACK, r, slope=0.4, bevel=0.05)
kit.stripe("HeadStripeC", (centre_w - 0.2, 0.012, 0.1), (0, HY - 0.38, HZ + 0.55), r)
for sx in (-1, 1):
    t = "L" if sx > 0 else "R"
    cx = sx * (centre_w / 2 + wing_w / 2)
    kit.roller_brush(f"Roller{t}", (cx, HY, HZ), wing_w - 0.1, RR, r)
    kit.box(f"Head{t}", (wing_w, 0.85, 0.34), (cx, HY + 0.1, HZ + 0.46), BLACK, r, 0.05)
    kit.stripe(f"HeadStripe{t}", (wing_w - 0.2, 0.012, 0.1), (cx, HY - 0.33, HZ + 0.48), r)
    kit.cyl(f"Hinge{t}", 0.12, 0.5, (sx * centre_w / 2, HY + 0.1, HZ + 0.75), STEEL, r, "Y", 16)
    # tip disc brush, outer edge on the span line
    kit.disc_brush(f"TipDisc{t}", (sx * (half - 0.38), HY - 0.55, 0.1), 0.38, r)
    kit.box(f"TipArm{t}", (0.14, 0.5, 0.12), (sx * (half - 0.38), HY - 0.32, 0.42), BLACK, r, 0.03)
    kit.box(f"TipSkid{t}", (0.1, 0.9, 0.5), (sx * (half - 0.05), HY + 0.05, HZ + 0.18), BLACK, r, 0.03)
    kit.lamp(f"WingLamp{t}", (sx * (half - 0.2), HY + 0.1, HZ + 0.68), (0.2, 0.2, 0.08), r)
    # hydraulic ram from cab corner to wing (folds the wing up for transport)
    kit.rod(f"WingRam{t}", (sx * 1.1, -3.6, 1.25), (cx * 0.85, HY + 0.15, HZ + 0.65), 0.05, STEEL, r)
    kit.rod(f"WingRamBody{t}", (sx * 1.1, -3.6, 1.25), (sx * 1.9, -4.1, 0.97), 0.08, BLACK, r)
    # lift arms from chassis to head centre
    kit.rod(f"LiftArm{t}", (sx * 0.8, -3.4, 0.75), (sx * 0.9, HY + 0.4, HZ + 0.45), 0.07, BLACK, r)

# --- The trunk: ribbed suction duct arcing from the plenum up the right side of the cab into the tank
P0 = Vector((-1.0, HY + 0.3, HZ + 1.25))
P1 = Vector((-1.85, HY + 0.3, 3.3))
P2 = Vector((-1.8, -2.4, 4.7))
P3 = Vector((-0.75, TY0 - 0.05, TZ + 0.75))


def bez(t):
    u = 1 - t
    return P0 * u**3 + P1 * 3 * u * u * t + P2 * 3 * u * t * t + P3 * t**3


N = 18
pts = [bez(i / N) for i in range(N + 1)]
for i in range(N):
    kit.rod(f"Duct{i}", pts[i], pts[i + 1], 0.34, BLACK, r, 16)
for i in range(1, N, 2):
    d = (pts[i + 1] - pts[i - 1]) * 0.06
    kit.rod(f"DuctRib{i}", pts[i] - d, pts[i] + d, 0.39, ORANGE if i in (5, 13) else WHITE, r, 16)
kit.cyl("DuctCollar", 0.45, 0.25, (P3.x, TY0 - 0.08, P3.z), ORANGE, r, "Y", 20)
kit.cyl("PlenumCollar", 0.42, 0.2, (P0.x, P0.y, P0.z), ORANGE, r, "Z", 20)
kit.rod("DuctStrut", (-1.15, CY0 + 0.9, 3.25), pts[7], 0.04, STEEL, r)

# Budget: tiny bevels only need one segment (keeps the model under ~20k triangles)
for o in r.children:
    for m in o.modifiers:
        if m.type == "BEVEL" and m.width < 0.035:
            m.segments = 1

kit.save(os.path.join(HERE, "colossus_vacuum.blend"))
kit.preview(r, os.path.join(HERE, "preview"))
kit.export(r, os.path.join(HERE, "..", "colossus_vacuum.bin"))
