# Comet hoover concept: Fast line tier 3, prototype racer after the Streak sweeper. Long low lofted body, a jet-intake
# front scoop that is the vacuum head (orange rollers inside, span = width stat), bubble canopy, hopper fairing hump
# behind the cockpit, twin tail fins with a wing, cyan intake rim and exhaust glow. Run from the repo root:
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --factory-startup --python vehicles/CometHoover/blender/build.py
import os, sys, math
import bmesh
from mathutils import Vector
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN

HERE = os.path.dirname(os.path.abspath(__file__))
s = kit.stats("andrew1431.comet-hoover")
brush = s["width"]                 # sweep strip = roller length inside the scoop (4.66 m)


def loft(name, sections, mat, parent, bevel=0.04):
    """Lofted hull through (y, half_width, z_low, z_high) sections: flat bottom, vertical side band, chamfered top."""
    bm = bmesh.new()
    rings = []
    for y, hw, zl, zh in sections:
        h = zh - zl
        pts = [(-hw * 0.82, zl), (hw * 0.82, zl), (hw, zl + 0.25 * h), (hw, zl + 0.6 * h),
               (hw * 0.68, zh), (-hw * 0.68, zh), (-hw, zl + 0.6 * h), (-hw, zl + 0.25 * h)]
        rings.append([bm.verts.new((x, y, z)) for x, z in pts])
    n = len(rings[0])
    for a, b in zip(rings, rings[1:]):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return kit._bevel(kit._mesh_obj(name, bm, mat, parent, (0, 0, 0), (0, 0, 0)), bevel, 1)


def plate(name, profile_yz, thickness, x, mat, parent, cant=0.0, bevel=0.02):
    """Flat plate (fins) from a (y, z) outline, extruded along X and canted about Y."""
    bm = bmesh.new()
    lo = [bm.verts.new((-thickness / 2, y, z)) for y, z in profile_yz]
    hi = [bm.verts.new((thickness / 2, y, z)) for y, z in profile_yz]
    n = len(lo)
    bm.faces.new(lo)
    bm.faces.new(list(reversed(hi)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return kit._bevel(kit._mesh_obj(name, bm, mat, parent, (x, 0, 0), (0, cant, 0)), bevel, 1)


def dome(name, radius_xyz, loc, mat, parent):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=22, v_segments=11, radius=1.0)
    bmesh.ops.scale(bm, vec=Vector(radius_xyz), verts=bm.verts)
    o = kit._mesh_obj(name, bm, mat, parent, loc, (0, 0, 0))
    for p in o.data.polygons:
        p.use_smooth = True
    return o


kit.reset()
r = kit.root("CometHoover_Root")

# ---- Hull: wide intake duct flaring down to a long narrow body
loft("Hull", [(-3.30, 2.40, 0.30, 0.92), (-2.70, 1.40, 0.34, 0.98), (-2.10, 1.15, 0.35, 1.02),
              (2.00, 1.15, 0.35, 1.10), (3.00, 1.05, 0.40, 1.04), (3.65, 0.88, 0.50, 0.94)], WHITE, r)
kit.box("UnderTray", (2.0, 6.6, 0.10), (0, 0.15, 0.36), BLACK, r, 0.03)
for sx in (-1, 1):
    side = "L" if sx > 0 else "R"
    kit.box(f"Skirt{side}", (0.06, 3.1, 0.16), (sx * 1.15, 0.35, 0.47), BLACK, r, 0.02)
    # racing stripe pair on the vertical side band
    kit.stripe(f"Stripe{side}A", (0.012, 3.6, 0.07), (sx * 1.157, 0.05, 0.69), r)
    kit.stripe(f"Stripe{side}B", (0.012, 3.0, 0.03), (sx * 1.157, -0.15, 0.80), r)
    # cyan running light under the stripes
    kit.lamp(f"SideGlow{side}", (sx * 1.158, -1.2, 0.58), (0.012, 1.4, 0.03), r, CYAN)
# twin orange stripes down the nose deck
for x in (-0.22, 0.22):
    kit.stripe(f"NoseStripe{x}", (0.12, 1.9, 0.012), (x, -1.95, 1.015), r)

# ---- Jet-intake scoop: the vacuum head. Hollow mouth, rollers inside, cyan rim.
mouth_hw = brush / 2 + 0.06
kit.wedge("ScoopLip", (mouth_hw * 2 + 0.2, 0.95, 0.16), (0, -3.72, 0.90), WHITE, r, slope=0.35, bevel=0.03)
kit.box("ScoopSplitter", (mouth_hw * 2 + 0.2, 1.05, 0.06), (0, -3.70, 0.05), BLACK, r, 0.02)
kit.box("ScoopBack", (mouth_hw * 2, 0.10, 0.80), (0, -3.28, 0.48), BLACK, r, 0.02)
for sx in (-1, 1):
    side = "L" if sx > 0 else "R"
    plate(f"ScoopCheek{side}", [(-4.22, 0.06), (-3.22, 0.06), (-3.22, 0.98), (-4.0, 0.98), (-4.22, 0.80)],
          0.10, sx * (mouth_hw + 0.05), WHITE, r)
    kit.lamp(f"ScoopRimSide{side}", (sx * (mouth_hw + 0.05), -4.235, 0.43), (0.11, 0.02, 0.70), r, CYAN)
    kit.stripe(f"CheekStripe{side}", (0.012, 0.75, 0.08), (sx * (mouth_hw + 0.106), -3.62, 0.62), r)
    kit.lamp(f"Headlight{side}", (sx * (mouth_hw - 0.3), -4.20, 0.92), (0.36, 0.03, 0.07), r)
kit.lamp("ScoopRimTop", (0, -4.205, 0.83), (mouth_hw * 2 + 0.1, 0.02, 0.025), r, CYAN)
kit.stripe("ScoopLipStripe", (mouth_hw * 2 + 0.21, 0.08, 0.012), (0, -3.55, 0.985), r)
# Rollers: a big ground roller and a smaller pickup roller behind it, both full sweep width
kit.cyl("RollerMain", 0.22, brush, (0, -3.80, 0.24), ORANGE, r, "X", 24, 0.02)
kit.cyl("RollerMain_Core", 0.08, brush + 0.04, (0, -3.80, 0.24), BLACK, r, "X", 12, 0.01)
kit.cyl("RollerPickup", 0.15, brush, (0, -3.50, 0.58), ORANGE, r, "X", 20, 0.02)
kit.cyl("RollerPickup_Core", 0.06, brush + 0.04, (0, -3.50, 0.58), BLACK, r, "X", 12, 0.01)
# intake guide vanes (black) between the rollers, thin so the orange still reads
for x in (-1.4, 0.0, 1.4):
    kit.box(f"Vane{x}", (0.04, 0.5, 0.78), (x, -3.62, 0.47), BLACK, r, 0.01)

# ---- Cockpit: bubble canopy over a black racing seat
kit.box("CockpitTub", (1.1, 1.8, 0.2), (0, -0.7, 1.08), BLACK, r, 0.06)
kit.box("SeatBase", (0.55, 0.55, 0.15), (0, -0.45, 1.2), BLACK, r, 0.05)
kit.box("SeatBack", (0.55, 0.14, 0.55), (0, -0.15, 1.45), BLACK, r, 0.05)
kit.cyl("SteeringWheel", 0.15, 0.04, (0, -0.95, 1.45), BLACK, r, "Y", 20)
dome("Canopy", (0.6, 1.15, 0.62), (0, -0.65, 1.12), GLASS, r)

# ---- Hopper fairing: big hump behind the cockpit (32.9 t)
loft("Hopper", [(0.35, 0.75, 0.95, 1.10), (0.95, 0.98, 0.95, 1.78), (2.45, 0.98, 0.95, 1.82),
                (3.35, 0.78, 0.90, 1.35)], WHITE, r)
kit.stripe("HopperLid", (0.42, 1.5, 0.014), (0, 1.7, 1.825), r)
for sx in (-1, 1):
    side = "L" if sx > 0 else "R"
    kit.lamp(f"FillGauge{side}", (sx * 0.985, 1.7, 1.35), (0.014, 1.3, 0.05), r, CYAN)
    kit.box(f"HopperVent{side}", (0.03, 0.5, 0.14), (sx * 0.985, 1.0, 1.15), BLACK, r, 0.01)
kit.box("DumpDoor", (1.0, 0.06, 0.32), (0, 3.36, 1.08), BLACK, r, 0.02)

# ---- Twin tail fins growing out of the hopper flanks, a wing between their tips
FIN_X, FIN_CANT = 0.80, math.radians(8)
for sx in (-1, 1):
    side = "L" if sx > 0 else "R"
    plate(f"Fin{side}", [(2.55, 0.80), (3.62, 0.80), (3.80, 2.20), (3.40, 2.20)], 0.10, sx * FIN_X, WHITE, r,
          cant=sx * FIN_CANT)
    plate(f"FinTip{side}", [(3.39, 2.06), (3.79, 2.06), (3.80, 2.21), (3.40, 2.21)], 0.115, sx * FIN_X, ORANGE, r,
          cant=sx * FIN_CANT, bevel=0.01)
    kit.lamp(f"FinLamp{side}", (sx * (FIN_X + 1.6 * math.sin(FIN_CANT)), 3.765, 1.6), (0.13, 0.03, 0.3), r, CYAN)
kit.box("Wing", (2 * (FIN_X + 2.03 * math.sin(FIN_CANT)) + 0.06, 0.42, 0.06), (0, 3.58, 2.03), BLACK, r, 0.02)

# ---- Tail: diffuser and twin cyan exhaust glows
kit.box("Diffuser", (1.6, 0.3, 0.2), (0, 3.7, 0.5), BLACK, r, 0.03)
for x in (-0.42, 0.42):
    kit.cyl(f"Exhaust{x}", 0.18, 0.22, (x, 3.67, 0.74), BLACK, r, "Y", 20)
    kit.cyl(f"ExhaustGlow{x}", 0.13, 0.24, (x, 3.68, 0.74), CYAN, r, "Y", 20)
kit.lamp("TailLamp", (0, 3.66, 0.92), (0.4, 0.03, 0.05), r)

# ---- Wheels: small fronts tucked behind the scoop, big exposed rears
for sx in (-1, 1):
    side = "L" if sx > 0 else "R"
    kit.wheel(f"WheelF{side}", (sx * 1.40, -1.75, 0.49), 0.46, 0.38, r, WHITE)
    kit.box(f"ArchF{side}", (0.52, 1.15, 0.06), (sx * 1.41, -1.75, 1.03), BLACK, r, 0.02)
    kit.box(f"ArchFWeb{side}", (0.06, 0.9, 0.36), (sx * 1.17, -1.75, 0.86), BLACK, r, 0.02)
    kit.stripe(f"ArchFStripe{side}", (0.10, 1.0, 0.012), (sx * 1.52, -1.75, 1.065), r)
    kit.wheel(f"WheelB{side}", (sx * 1.52, 2.45, 0.67), 0.64, 0.56, r, WHITE)
    kit.cyl(f"AxleF{side}", 0.07, 0.4, (sx * 1.05, -1.75, 0.49), BLACK, r, "X", 10)
    kit.cyl(f"AxleB{side}", 0.09, 0.4, (sx * 1.05, 2.45, 0.67), BLACK, r, "X", 10)

# Antenna with orange tip lamp off the hopper
kit.rod("Antenna", (-0.55, 2.9, 1.55), (-0.62, 3.15, 2.5), 0.012, BLACK, r)
kit.cyl("AntennaTip", 0.035, 0.07, (-0.62, 3.15, 2.52), LAMP, r)

kit.save(os.path.join(HERE, "comet_hoover.blend"))
kit.preview(r, os.path.join(HERE, "preview"))
kit.export(r, os.path.join(HERE, "..", "comet_hoover.bin"))
