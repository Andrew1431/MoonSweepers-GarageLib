# Mare harvester concept: Wide line tier 4. A combine harvester for the Moon: a 16.36 m header of orange rollers and
# disc brushes hung from trussed booms and suspension stays, four crawler tracks, a cab perched on a pedestal, a
# towering silo hopper and a swung-out unloading auger. Run from the repo root:
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --factory-startup --python vehicles/MareHarvester/blender/build.py
import os, sys
import bmesh
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN

HERE = os.path.dirname(os.path.abspath(__file__))
s = kit.stats("andrew1431.mare-harvester")
W = s["width"]                     # sweep strip = header span, 16.36 m
H = W / 2

kit.reset()
r = kit.root("MareHarvester_Root")


def cone(name, r1, r2, depth, loc, mat, verts=24):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r1, radius2=r2, depth=depth)
    return kit._mesh_obj(name, bm, mat, r, loc, (0, 0, 0))


def lowcyl(name, radius, depth, loc, mat, axis="X", verts=12):
    return kit.cyl(name, radius, depth, loc, mat, r, axis, verts, 0)


# ---------------------------------------------------------------- chassis
kit.box("Chassis", (4.0, 10.0, 1.4), (0, 0.5, 2.0), WHITE, r, 0.08)
kit.box("Skirt", (4.2, 10.2, 0.4), (0, 0.5, 1.15), BLACK, r, 0.05)
for x in (-1, 1):
    kit.stripe(f"ChassisStripe{x}", (0.012, 9.4, 0.18), (x * 2.006, 0.5, 2.25), r)
    kit.stripe(f"ChassisStripeThin{x}", (0.012, 9.4, 0.05), (x * 2.006, 0.5, 2.02), r)

# ---------------------------------------------------------------- four crawler tracks
for ty, tag in ((-2.6, "F"), (3.4, "B")):
    for x in (-2.75, 2.75):
        side = "L" if x > 0 else "R"
        n = f"Track{tag}{side}"
        kit.box(n, (1.1, 2.6, 1.5), (x, ty, 0.75), BLACK, r, 0.04)
        for e in (-1, 1):
            lowcyl(f"{n}_End{e}", 0.75, 1.1, (x, ty + e * 1.3, 0.75), BLACK, "X", 20)
            lowcyl(f"{n}_Idler{e}", 0.5, 1.14, (x, ty + e * 1.3, 0.75), STEEL, "X", 16)
            lowcyl(f"{n}_IdlerCap{e}", 0.18, 1.18, (x, ty + e * 1.3, 0.75), BLACK, "X", 10)
        for i, dy in enumerate((-0.65, 0.0, 0.65)):
            lowcyl(f"{n}_Road{i}", 0.3, 1.14, (x, ty + dy, 0.42), STEEL, "X", 12)
        for i in range(9):   # grousers along the top run
            kit.box(f"{n}_Grouser{i}", (1.12, 0.12, 0.06), (x, ty - 1.2 + i * 0.3, 1.52), BLACK, r, 0)
        kit.box(f"{n}_Fender", (1.3, 4.1, 0.16), (x, ty, 1.72), WHITE, r, 0.04)
        kit.stripe(f"{n}_FenderStripe", (1.31, 0.25, 0.02), (x, ty - 1.6, 1.80), r)
        kit.box(f"{n}_Axle", (0.9, 0.6, 0.5), ((abs(x) - 0.75) * (1 if x > 0 else -1), ty, 1.15), BLACK, r, 0.03)

# ---------------------------------------------------------------- feeder housing and header
kit.box("Feeder", (2.8, 3.2, 1.0), (0, -5.6, 1.35), BLACK, r, 0.06, rot=(-0.28, 0, 0))
kit.box("FeederTop", (2.4, 2.6, 0.08), (0, -5.5, 1.9), WHITE, r, 0.02, rot=(-0.28, 0, 0))

HY = -7.5                                # header roller line
kit.box("HeaderBeam", (W, 0.9, 0.5), (0, HY + 0.25, 1.35), WHITE, r, 0.06)
kit.stripe("HeaderStripe", (W - 0.4, 0.012, 0.14), (0, HY - 0.206, 1.38), r)
gangs = 4
glen = W / gangs
for g in range(gangs):
    cx = -H + glen * (g + 0.5)
    kit.cyl(f"Roller{g}", 0.4, glen - 0.14, (cx, HY, 0.42), ORANGE, r, "X", 16, 0)["brush"] = 1.0
    lowcyl(f"Roller{g}_Core", 0.12, glen - 0.06, (cx, HY, 0.42), BLACK, "X", 8)
    kit.box(f"Roller{g}_Hood", (glen - 0.02, 1.05, 0.3), (cx, HY + 0.05, 0.98), BLACK, r, 0.04)
    for e in (-1, 1):
        kit.box(f"Roller{g}_End{e}", (0.06, 0.95, 0.95), (cx + e * (glen / 2 - 0.04), HY + 0.05, 0.6), BLACK, r, 0)

# front row of disc brushes, edge to edge across the strip
nd = 12
pitch = W / nd
for i in range(nd):
    x = -H + pitch * (i + 0.5)
    y = HY - 1.15
    d = kit.cyl(f"Disc{i}", pitch * 0.5, 0.08, (x, y, 0.12), ORANGE, r, "Z", 16, 0)
    d.rotation_euler = (0.12, 0, 0)
    h = kit.cyl(f"Disc{i}_Hub", pitch * 0.18, 0.2, (x, y + 0.02, 0.26), BLACK, r, "Z", 10, 0)
    h.rotation_euler = (0.12, 0, 0)
    kit.rod(f"Disc{i}_Arm", (x, y + 0.02, 0.34), (x, HY - 0.4, 0.95), 0.06, BLACK, r, 6)

# trussed booms over each wing, plus suspension stays from a central mast
BOTZ = 1.6
for sgn in (-1, 1):
    tag = "L" if sgn > 0 else "R"
    xs = [sgn * (1.4 + i * (H - 1.4) / 6) for i in range(7)]
    tops = [4.0 - 1.9 * i / 6 for i in range(7)]
    for i in range(7):
        kit.rod(f"Boom{tag}_V{i}", (xs[i], HY + 0.25, BOTZ), (xs[i], HY + 0.25, tops[i]), 0.05, STEEL, r, 6)
        if i < 6:
            kit.rod(f"Boom{tag}_T{i}", (xs[i], HY + 0.25, tops[i]), (xs[i + 1], HY + 0.25, tops[i + 1]), 0.08, WHITE, r, 6)
            kit.rod(f"Boom{tag}_D{i}", (xs[i], HY + 0.25, BOTZ), (xs[i + 1], HY + 0.25, tops[i + 1]), 0.04, STEEL, r, 6)
    kit.rod(f"Stay{tag}_Tip", (sgn * 1.7, HY + 0.6, 6.9), (xs[6], HY + 0.25, tops[6]), 0.035, STEEL, r, 6)
    kit.rod(f"Stay{tag}_Mid", (sgn * 1.7, HY + 0.6, 6.9), (xs[3], HY + 0.25, tops[3]), 0.035, STEEL, r, 6)
    kit.rod(f"Mast{tag}", (sgn * 1.7, HY + 0.6, 1.6), (sgn * 1.7, HY + 0.6, 7.0), 0.1, WHITE, r, 8)
    kit.rod(f"MastBrace{tag}", (sgn * 1.7, HY + 0.6, 5.0), (sgn * 1.0, -4.4, 3.6), 0.07, STEEL, r, 6)
    kit.cyl(f"MastBeacon{tag}", 0.14, 0.24, (sgn * 1.7, HY + 0.6, 7.15), LAMP, r, "Z", 10, 0)
    # wingtip castor wheel and beacon
    tx = sgn * (H - 0.45)
    kit.rod(f"Castor{tag}_Strut", (tx, HY + 0.9, 1.1), (tx, HY + 1.1, 0.55), 0.07, BLACK, r, 6)
    lowcyl(f"Castor{tag}", 0.32, 0.26, (tx, HY + 1.15, 0.32), BLACK, "X", 14)
    lowcyl(f"Castor{tag}_Hub", 0.17, 0.28, (tx, HY + 1.15, 0.32), WHITE, "X", 10)
    kit.box(f"Tip{tag}_Post", (0.2, 0.2, 1.2), (sgn * (H - 0.1), HY + 0.25, 2.2), BLACK, r, 0.02)
    kit.cyl(f"Tip{tag}_Beacon", 0.12, 0.2, (sgn * (H - 0.1), HY + 0.25, 2.9), LAMP, r, "Z", 10, 0)
    kit.lamp(f"Tip{tag}_Marker", (sgn * (H - 0.6), HY - 0.21, 1.15), (0.3, 0.03, 0.1), r, CYAN)

CZ = 1.2                                 # cab lift: perched above the header
# ---------------------------------------------------------------- cab on a pedestal
kit.box("CabPedestal", (2.4, 2.2, 1.3 + CZ), (0, -3.4, 3.35 + CZ / 2), BLACK, r, 0.06)
kit.box("Cab", (3.0, 2.6, 2.2), (0, -3.4, 5.1 + CZ), WHITE, r, 0.1)
kit.box("CabWindscreen", (2.8, 0.08, 1.75), (0, -4.72, 5.15 + CZ), GLASS, r, 0.02, rot=(0.18, 0, 0))
for x in (-1, 1):
    kit.box(f"CabSideGlass{x}", (0.04, 1.8, 1.3), (x * 1.51, -3.6, 5.3 + CZ), GLASS, r, 0.02)
    kit.stripe(f"CabStripe{x}", (0.012, 2.4, 0.12), (x * 1.506, -3.4, 4.4 + CZ), r)
kit.box("CabRoof", (3.3, 3.0, 0.2), (0, -3.5, 6.3 + CZ), WHITE, r, 0.06)
kit.box("CabLightBar", (2.6, 0.12, 0.12), (0, -5.0, 6.3 + CZ), BLACK, r, 0.02)
for x in (-0.9, -0.3, 0.3, 0.9):
    kit.lamp(f"CabWorkLamp{x}", (x, -5.07, 6.3 + CZ), (0.36, 0.02, 0.08), r)
for x in (-1.45, 1.45):
    kit.cyl(f"CabBeacon{x}", 0.13, 0.26, (x, -2.3, 6.53 + CZ), LAMP, r, "Z", 10, 0)
kit.rod("CabAntenna", (-1.2, -2.3, 6.4 + CZ), (-1.2, -2.2, 7.6 + CZ), 0.015, BLACK, r, 6)
kit.box("Seat", (0.6, 0.6, 0.2), (0, -3.3, 4.45 + CZ), BLACK, r, 0.04)
kit.box("SeatBack", (0.6, 0.14, 0.7), (0, -3.0, 4.85 + CZ), BLACK, r, 0.04)
kit.box("Console", (1.4, 0.4, 0.6), (0, -4.2, 4.5 + CZ), BLACK, r, 0.03)
# ladder and landing on the left side
for dx in (0, 0.5):
    kit.rod(f"LadderRail{dx}", (1.6 + dx, -4.75, 0.4), (1.6 + dx, -4.75, 4.0 + CZ), 0.03, STEEL, r, 6)
for i in range(12):
    z = 0.6 + i * 0.4
    kit.rod(f"LadderRung{i}", (1.6, -4.75, z), (2.1, -4.75, z), 0.02, STEEL, r, 6)
kit.box("Landing", (1.0, 1.4, 0.08), (1.75, -4.1, 4.0 + CZ), BLACK, r, 0.01)
kit.rod("LandingRail", (2.25, -4.75, 4.0 + CZ), (2.25, -4.75, 4.9 + CZ), 0.03, ORANGE, r, 6)

# ---------------------------------------------------------------- hopper base and towering silo
kit.box("HopperBase", (4.0, 5.0, 3.0), (0, 1.5, 4.2), WHITE, r, 0.1)
for x in (-1, 1):
    kit.stripe(f"HopperStripe{x}", (0.012, 4.6, 0.25), (x * 2.006, 1.5, 4.9), r)
    kit.box(f"HopperGauge{x}", (0.02, 0.18, 2.0), (x * 2.01, 3.4, 4.2), CYAN, r, 0)
SY = 1.5
kit.cyl("Silo", 2.0, 5.4, (0, SY, 8.4), WHITE, r, "Z", 28, 0.04)
kit.cyl("SiloCollar", 2.1, 0.35, (0, SY, 5.85), BLACK, r, "Z", 28, 0.02)
for i, z in enumerate((7.2, 9.6)):
    kit.cyl(f"SiloBand{i}", 2.03, 0.3, (0, SY, z), ORANGE, r, "Z", 28, 0)
cone("SiloCap", 2.05, 0.7, 1.0, (0, SY, 11.6), WHITE, 28)
kit.cyl("SiloHatch", 0.7, 0.2, (0, SY, 12.2), BLACK, r, "Z", 16, 0.02)
kit.cyl("SiloBeacon", 0.25, 0.4, (0, SY, 12.5), LAMP, r, "Z", 12, 0)
kit.rod("SiloAntenna", (0.5, SY, 12.2), (0.5, SY, 13.6), 0.02, BLACK, r, 6)
kit.box("SiloWindow", (0.5, 0.04, 3.4), (0, SY - 2.0, 8.4), GLASS, r, 0.02)
for x in (-0.6, 0.6):
    kit.lamp(f"SiloCyan{x}", (x, SY - 2.0, 10.4), (0.2, 0.04, 0.2), r, CYAN)
# silo ladder (right side)
for dy in (-0.25, 0.25):
    kit.rod(f"SiloLadder{dy}", (-2.15, SY + dy, 5.7), (-2.15, SY + dy, 11.0), 0.025, STEEL, r, 6)
for i in range(13):
    z = 6.0 + i * 0.4
    kit.rod(f"SiloRung{i}", (-2.15, SY - 0.25, z), (-2.15, SY + 0.25, z), 0.02, STEEL, r, 6)

# catwalk ring with railing near the top of the silo
kit.cyl("SiloCatwalk", 2.5, 0.1, (0, SY, 10.9), BLACK, r, "Z", 28, 0)
import math
ring = [(2.45 * math.cos(2 * math.pi * k / 14), SY + 2.45 * math.sin(2 * math.pi * k / 14)) for k in range(14)]
for k, (px, py) in enumerate(ring):
    qx, qy = ring[(k + 1) % 14]
    kit.rod(f"CatwalkPost{k}", (px, py, 10.95), (px, py, 11.75), 0.025, STEEL, r, 6)
    kit.rod(f"CatwalkRail{k}", (px, py, 11.75), (qx, qy, 11.75), 0.03, ORANGE, r, 6)

# ---------------------------------------------------------------- unloading auger, swung out to the left
AX, AY = 1.75, SY + 1.75
kit.cyl("AugerRiser", 0.32, 6.4, (AX, AY, 7.0), WHITE, r, "Z", 14, 0)
for z in (5.0, 7.5, 9.5):
    kit.rod(f"AugerBracket{z}", (AX, AY, z), (1.2, SY + 1.3, z), 0.08, BLACK, r, 6)
kit.cyl("AugerElbow", 0.42, 0.9, (AX, AY, 10.3), BLACK, r, "Z", 14, 0.02)
tip = (11.2, AY - 0.3, 9.6)
kit.rod("AugerTube", (AX, AY, 10.3), tip, 0.32, WHITE, r, 14)
for t in (0.3, 0.55, 0.8):
    p = [AX + (tip[0] - AX) * t, AY + (tip[1] - AY) * t, 10.3 + (tip[2] - 10.3) * t]
    kit.rod(f"AugerBand{t}", (p[0] - 0.15, p[1], p[2] + 0.011), (p[0] + 0.15, p[1], p[2] - 0.011), 0.34, ORANGE, r, 14)
kit.rod("AugerStay", (0, SY, 12.0), (7.0, AY - 0.15, 9.9), 0.04, STEEL, r, 6)
kit.rod("AugerSpout", tip, (tip[0] + 0.2, tip[1], tip[2] - 1.1), 0.36, BLACK, r, 12)
kit.cyl("AugerTipLamp", 0.1, 0.16, (tip[0], tip[1], tip[2] + 0.38), LAMP, r, "Z", 10, 0)

# ---------------------------------------------------------------- engine pod at the rear
kit.box("EnginePod", (3.6, 1.6, 1.8), (0, 4.9, 3.6), WHITE, r, 0.08)
kit.box("Grille", (3.0, 0.06, 1.2), (0, 5.72, 3.5), BLACK, r, 0.02)
for i in range(5):
    kit.box(f"GrilleSlat{i}", (2.9, 0.04, 0.05), (0, 5.76, 3.05 + i * 0.22), STEEL, r, 0)
for x in (-1.3, 1.3):
    kit.rod(f"Exhaust{x}", (x * 0.8, 5.0, 4.5), (x * 0.8, 5.0, 7.2), 0.12, STEEL, r, 10)
    kit.cyl(f"ExhaustCap{x}", 0.15, 0.2, (x * 0.8, 5.0, 7.3), BLACK, r, "Z", 10, 0)
    kit.lamp(f"TailLamp{x}", (x, 5.62, 1.8), (0.4, 0.04, 0.16), r)
    kit.lamp(f"RearCyan{x}", (x * 1.2, 5.72, 4.3), (0.3, 0.03, 0.08), r, CYAN)
kit.box("RearBumper", (4.2, 0.4, 0.4), (0, 5.7, 1.2), BLACK, r, 0.04)
# walkway handrails along the chassis deck
for x in (-1.9, 1.9):
    kit.rod(f"Handrail{x}", (x, -1.6, 3.6), (x, 4.0, 3.6), 0.03, ORANGE, r, 6)
    for y in (-1.6, 1.2, 4.0):
        kit.rod(f"HandPost{x}{y}", (x, y, 2.7), (x, y, 3.6), 0.025, STEEL, r, 6)

kit.save(os.path.join(HERE, "mare_harvester.blend"))
kit.preview(r, os.path.join(HERE, "preview"))
kit.export(r, os.path.join(HERE, "..", "mare_harvester.bin"))
