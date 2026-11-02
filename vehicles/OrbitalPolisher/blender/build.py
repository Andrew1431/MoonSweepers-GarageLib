# Orbital polisher concept: Fast line tier 4. A land-speed-record needle that happens to vacuum: full-width front
# vacuum blade with an orange roller, needle fuselage, bubble canopy, dorsal hopper spine into a tall stabiliser,
# huge exposed rear wheels, three rocket nozzles glowing orange, cyan underglow. Run from the repo root:
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --factory-startup --python vehicles/OrbitalPolisher/blender/build.py
import os, sys, math
import bmesh
from mathutils import Vector
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN

HERE = os.path.dirname(os.path.abspath(__file__))
s = kit.stats("andrew1431.orbital-polisher")
brush = s["width"]                 # sweep strip = front roller length (5.03 m)


# --- Local shape helpers (built on the kit's mesh/material plumbing) ------------------------------------------------
def _finish(name, bm, mat, parent, smooth, extra_mats=()):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = kit._mesh_obj(name, bm, mat, parent, (0, 0, 0), (0, 0, 0))
    for m in extra_mats:
        o.data.materials.append(kit.bpy.data.materials[m])
    if smooth:
        for p in o.data.polygons:
            p.use_smooth = len(p.vertices) == 4
    return o


def section(y, rx, rz, zc, a0=0.0, a1=2 * math.pi, n=32, p=2.6, scale=1.0, x0=0.0):
    """Superellipse cross-section in the XZ plane at Y. Full ring when a1-a0 == 2pi, else an open arc."""
    full = abs(a1 - a0 - 2 * math.pi) < 1e-6
    count = n if full else n + 1
    pts = []
    for i in range(count):
        a = a0 + (a1 - a0) * i / n
        c, sn = math.cos(a), math.sin(a)
        x = rx * scale * math.copysign(abs(c) ** (2 / p), c)
        z = rz * scale * math.copysign(abs(sn) ** (2 / p), sn)
        pts.append(Vector((x0 + x, y, zc + z)))
    return pts


def loft(name, rings, mat, parent=None, smooth=True):
    """Skin a list of equal-length closed rings (point lists) and cap both ends."""
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in ring] for ring in rings]
    n = len(rings[0])
    for a, b in zip(vs, vs[1:]):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(vs[0])
    bm.faces.new(list(reversed(vs[-1])))
    return _finish(name, bm, mat, parent, smooth)


def band(name, stations, a0, a1, mat, parent, outer=1.012, inner=0.99, n=6, p=2.6):
    """Thin shell strip that follows a lofted surface between angles a0..a1 (livery stripes, skirts, glow strips)."""
    rings = []
    for (y, rx, rz, zc) in stations:
        o = section(y, rx, rz, zc, a0, a1, n, p, outer)
        i = section(y, rx, rz, zc, a0, a1, n, p, inner)
        rings.append(o + list(reversed(i)))
    return loft(name, rings, mat, parent, smooth=False)


def prism(name, poly_yz, thickness, x, mat, parent=None):
    """Flat plate: polygon in the YZ plane extruded along X (fins, fenders)."""
    bm = bmesh.new()
    l = [bm.verts.new((x - thickness / 2, y, z)) for y, z in poly_yz]
    r = [bm.verts.new((x + thickness / 2, y, z)) for y, z in poly_yz]
    n = len(poly_yz)
    bm.faces.new(l)
    bm.faces.new(list(reversed(r)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((l[i], l[j], r[j], r[i]))
    return _finish(name, bm, mat, parent, False)


def lathe(name, profile, x, z, mats, parent=None, n=24):
    """Revolve (radius, y) points around a Y-parallel axis at (x, z). mats[i] colours segment i (point i -> i+1)."""
    bm = bmesh.new()
    rings = []
    for rad, y in profile:
        if rad < 1e-6:
            rings.append([bm.verts.new((x, y, z))])
        else:
            rings.append([bm.verts.new((x + rad * math.cos(2 * math.pi * k / n), y,
                                        z + rad * math.sin(2 * math.pi * k / n))) for k in range(n)])
    uniq = list(dict.fromkeys(mats))
    for si, (a, b) in enumerate(zip(rings, rings[1:])):
        idx = uniq.index(mats[si])
        for k in range(n):
            k2 = (k + 1) % n
            if len(a) == 1:
                f = bm.faces.new((a[0], b[k], b[k2]))
            elif len(b) == 1:
                f = bm.faces.new((a[k], a[k2], b[0]))
            else:
                f = bm.faces.new((a[k], a[k2], b[k2], b[k]))
            f.material_index = idx
    return _finish(name, bm, uniq[0], parent, False, uniq[1:])


def arc_poly(yc, zc, r_in, r_out, a0, a1, n=12):
    """Arc band polygon in YZ around (yc, zc); angles measured from +Y towards +Z."""
    out = [(yc + r_out * math.cos(a0 + (a1 - a0) * i / n), zc + r_out * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]
    inn = [(yc + r_in * math.cos(a1 - (a1 - a0) * i / n), zc + r_in * math.sin(a1 - (a1 - a0) * i / n)) for i in range(n + 1)]
    return out + inn


# --- Fuselage profile: (y, rx, rz, zc). Needle nose at -Y, flat tail at +Y. -----------------------------------------
BODY = [(-4.10, 0.05, 0.04, 0.52), (-3.85, 0.18, 0.12, 0.54), (-3.50, 0.30, 0.19, 0.57), (-3.00, 0.43, 0.26, 0.61),
        (-2.40, 0.54, 0.33, 0.66), (-1.70, 0.63, 0.39, 0.71), (-0.90, 0.69, 0.43, 0.75), (0.00, 0.73, 0.46, 0.78),
        (1.00, 0.75, 0.47, 0.80), (2.00, 0.75, 0.47, 0.80), (2.90, 0.71, 0.45, 0.80), (3.50, 0.64, 0.43, 0.80),
        (3.95, 0.58, 0.41, 0.80)]


def body_at(y):
    for a, b in zip(BODY, BODY[1:]):
        if a[0] <= y <= b[0]:
            t = (y - a[0]) / (b[0] - a[0])
            return tuple(a[k] + (b[k] - a[k]) * t for k in range(4))[1:]
    return BODY[-1][1:] if y > 0 else BODY[0][1:]


def stations(y0, y1, steps=10):
    return [(y, *body_at(y)) for y in [y0 + (y1 - y0) * i / steps for i in range(steps + 1)]]


def top_z(y):
    rx, rz, zc = body_at(y)
    return zc + rz


kit.reset()
r = kit.root("OrbitalPolisher_Root")

fus = loft("Fuselage", [section(*st, n=40) for st in BODY], WHITE, r)

# Black lower hull (functional low parts) with cyan underglow strips on each flank
band("LowerHull", stations(-3.9, 3.95, 16), -math.pi + 0.45, -0.45, BLACK, r, 1.008, 0.985, 14)
for side, (a0, a1) in (("L", (-0.62, -0.50)), ("R", (-math.pi + 0.50, -math.pi + 0.62))):
    band(f"Underglow{side}", stations(-2.0, 2.0, 8), a0, a1, CYAN, r, 1.022, 1.0, 2)

# Orange livery: twin racing stripes over the nose, a flank stripe down each side
for side, (a0, a1) in (("L", (math.pi / 2 - 0.30, math.pi / 2 - 0.16)), ("R", (math.pi / 2 + 0.16, math.pi / 2 + 0.30))):
    band(f"NoseStripe{side}", stations(-3.95, -1.9, 10), a0, a1, ORANGE, r, 1.012, 0.99, 3)
for side, (a0, a1) in (("L", (0.10, 0.24)), ("R", (math.pi - 0.24, math.pi - 0.10))):
    band(f"FlankStripe{side}", stations(-3.4, 3.95, 14), a0, a1, ORANGE, r, 1.012, 0.99, 3)

# Cockpit: bubble canopy sunk into the spine, black seat and yoke inside
CANOPY = [(-2.15, 0.06, 0.03), (-1.85, 0.26, 0.22), (-1.45, 0.38, 0.38), (-1.0, 0.41, 0.46), (-0.6, 0.38, 0.45),
          (-0.3, 0.30, 0.38)]
can = []
for y, rx, h in CANOPY:
    sink = 0.14
    can.append(section(y, rx, h + sink, top_z(y) - sink, 0.0, math.pi, 16, 2.2))
loft("Canopy", can, GLASS, r)
kit.box("Seat", (0.48, 0.5, 0.16), (0, -0.9, 0.86), BLACK, r, 0.04)
kit.box("SeatBack", (0.48, 0.12, 0.55), (0, -0.62, 1.12), BLACK, r, 0.04)
kit.box("Yoke", (0.32, 0.04, 0.1), (0, -1.45, 1.16), BLACK, r, 0.02)

# Dorsal hopper spine: rises from behind the canopy and runs into the fin; orange lid with black seams
SPINE = [(-0.75, 0.10, 0.05), (-0.45, 0.30, 0.30), (0.3, 0.40, 0.38), (1.5, 0.42, 0.38), (2.6, 0.38, 0.32),
         (3.4, 0.30, 0.22), (3.95, 0.24, 0.15)]       # (y, half width, height above the body top)
sp_st = [(y, rx, h + 0.2, top_z(y) - 0.2) for y, rx, h in SPINE]
loft("HopperSpine", [section(*st, n=28, p=2.4) for st in sp_st], WHITE, r)


def spine_at(y):
    for a, b in zip(sp_st, sp_st[1:]):
        if a[0] <= y <= b[0]:
            t = (y - a[0]) / (b[0] - a[0])
            return (y, *[a[k] + (b[k] - a[k]) * t for k in range(1, 4)])


lid = [spine_at(0.2 + 2.2 * i / 8) for i in range(9)]
band("HopperLid", lid, math.pi / 2 - 0.55, math.pi / 2 + 0.55, ORANGE, r, 1.015, 0.99, 8, 2.4)
for y in (0.15, 2.45):
    band(f"LidSeam{y}", [spine_at(y - 0.03), spine_at(y + 0.03)], 0.25, math.pi - 0.25, BLACK, r, 1.02, 0.99, 10, 2.4)

# Vertical stabiliser with an orange tip and a lamp
FIN = [(2.2, 1.25), (3.95, 1.15), (4.30, 2.85), (3.72, 2.85)]
prism("Fin", FIN, 0.09, 0, WHITE, r)


def edge_y(p, q, z):
    return p[0] + (q[0] - p[0]) * (z - p[1]) / (q[1] - p[1])


zc0 = 2.55
prism("FinTip", [(edge_y(FIN[0], FIN[3], zc0) - 0.01, zc0), (edge_y(FIN[1], FIN[2], zc0) + 0.01, zc0),
                 (4.31, 2.86), (3.71, 2.86)], 0.1, 0, ORANGE, r)
prism("FinStripe", [(edge_y(FIN[0], FIN[3], 2.2), 2.2), (edge_y(FIN[1], FIN[2], 2.2), 2.2),
                    (edge_y(FIN[1], FIN[2], 2.3), 2.3), (edge_y(FIN[0], FIN[3], 2.3), 2.3)], 0.1, 0, BLACK, r)
kit.lamp("FinLamp", (0, 4.22, 2.8), (0.11, 0.08, 0.07), r)

# Rocket nozzles: steel bells, black throats, orange lamp glowing inside
def nozzle(name, x, z, rt, re, length):
    y0, y1 = 3.82, 3.95 + length
    lathe(name, [(0, y0), (rt, y0), (rt, 3.98), (re, y1), (re - 0.035, y1 + 0.01), (re - 0.06, y1 - length * 0.3),
                 (0, y1 - length * 0.3)], x, z, [STEEL, STEEL, STEEL, BLACK, BLACK, LAMP], r)


nozzle("NozzleL", 0.27, 0.70, 0.19, 0.24, 0.42)
nozzle("NozzleR", -0.27, 0.70, 0.19, 0.24, 0.42)
nozzle("NozzleTop", 0.0, 1.06, 0.12, 0.15, 0.32)
kit.box("TailPlate", (1.0, 0.04, 0.62), (0, 3.955, 0.84), BLACK, r, 0.02)

# Front vacuum blade: full-width orange roller in a black hood, white wing above, endplates with lamps
BY = -4.25
kit.roller_brush("VacuumRoller", (0, BY, 0.15), brush, 0.14, r)
kit.box("BladeMouth", (brush - 0.1, 0.30, 0.14), (0, BY + 0.24, 0.17), BLACK, r, 0.03)
wing = kit.wedge("BladeWing", (brush + 0.02, 0.62, 0.11), (0, BY + 0.12, 0.395), WHITE, r, slope=0.55, bevel=0.02)
kit.stripe("BladeStripe", (brush - 0.4, 0.08, 0.012), (0, BY + 0.30, 0.456), r)
kit.box("BladeGlow", (brush - 0.6, 0.03, 0.04), (0, BY + 0.40, 0.12), CYAN, r, 0.005)
for sx in (-1, 1):
    ex = sx * (brush / 2 + 0.1)
    prism(f"Endplate{'L' if sx > 0 else 'R'}", [(BY - 0.34, 0.04), (BY + 0.55, 0.04), (BY + 0.55, 0.44),
                                                 (BY - 0.04, 0.52)], 0.05, ex, BLACK, r)
    kit.lamp(f"EndLamp{'L' if sx > 0 else 'R'}", (ex, BY - 0.25, 0.2), (0.07, 0.04, 0.08), r)
    kit.lamp(f"EndGlow{'L' if sx > 0 else 'R'}", (ex + sx * 0.03, BY + 0.1, 0.12), (0.012, 0.6, 0.03), r, CYAN)
    kit.box(f"Pylon{'L' if sx > 0 else 'R'}", (0.06, 0.45, 0.26), (sx * 0.22, BY + 0.38, 0.48), BLACK, r, 0.02)
# Intake duct from the blade mouth into the nose
kit.box("IntakeDuct", (0.7, 0.9, 0.16), (0, -3.85, 0.33), BLACK, r, 0.05)

# Front wheels in white spats
FY, FR, FW, FX = -2.45, 0.40, 0.26, 0.88
for sx in (-1, 1):
    t = 'L' if sx > 0 else 'R'
    kit.wheel(f"WheelF{t}", (sx * FX, FY, FR), FR, FW, r, WHITE)
    prism(f"SpatF{t}", arc_poly(FY, FR, FR + 0.05, FR + 0.13, math.radians(-10), math.radians(190), 14),
          FW + 0.1, sx * FX, WHITE, r)
    prism(f"SpatStripeF{t}", arc_poly(FY, FR, FR + 0.13, FR + 0.16, math.radians(40), math.radians(140), 8),
          FW + 0.04, sx * FX, ORANGE, r)
    for dy, z in ((-0.14, 0.62), (0.14, 0.50)):
        kit.rod(f"ArmF{t}{dy}", (sx * 0.4, FY + dy, z), (sx * (FX - FW / 2), FY, FR + 0.02), 0.035, BLACK, r)

# Huge exposed rear wheels on faired arms
RY, RR, RW, RX = 2.75, 0.85, 0.52, 1.48
for sx in (-1, 1):
    t = 'L' if sx > 0 else 'R'
    kit.wheel(f"WheelR{t}", (sx * RX, RY, RR), RR, RW, r, WHITE)
    kit.box(f"ArmFairing{t}", (RX - RW / 2 - 0.5, 0.5, 0.08), (sx * (0.5 + (RX - RW / 2 - 0.5) / 2), RY, RR + 0.05),
            WHITE, r, 0.03)
    kit.stripe(f"ArmStripe{t}", (RX - RW / 2 - 0.6, 0.1, 0.01), (sx * (0.55 + (RX - RW / 2 - 0.6) / 2), RY, RR + 0.095), r)
    kit.rod(f"ArmLow{t}", (sx * 0.55, RY, 0.5), (sx * (RX - RW / 2), RY, RR - 0.05), 0.04, BLACK, r)

# Antenna with an orange tip lamp on the spine
kit.rod("Antenna", (0.22, 1.4, top_z(1.4) + 0.3), (0.22, 1.7, top_z(1.4) + 1.05), 0.012, BLACK, r)
kit.cyl("AntennaTip", 0.03, 0.06, (0.22, 1.7, top_z(1.4) + 1.07), LAMP, r)

kit.save(os.path.join(HERE, "orbital_polisher.blend"))
kit.preview(r, os.path.join(HERE, "preview"))
kit.export(r, os.path.join(HERE, "..", "orbital_polisher.bin"))
