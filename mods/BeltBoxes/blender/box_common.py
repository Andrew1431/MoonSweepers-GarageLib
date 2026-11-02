# Shared builder for BeltBoxes' two belt boxes (box_splitter.py, box_merger.py): a squat white box with steel corner
# posts on a black skid, 1.4 m square, deck 0.85 m, lid about 1.0 m. Every ported face carries a short tread stub at
# belt height running into a black belt mouth (orange lip on an input, steel lip on an output), and the black lid
# carries one orange arrow per port showing the flow: into the box for an input, away from it for an output.
# Axes: Blender (x, y, z) is Unity (-x, z, -y). A port is given by its face's outward normal in Blender XY.
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "shared", "blender"))
import kit, bpy, bmesh
from mathutils import Vector
from kit import WHITE, BLACK, ORANGE, STEEL, CYAN

HALF = 0.70          # footprint 1.4 x 1.4 (guide half size, Blender X and Y)
HEIGHT = 1.05        # height cap (guide height)
DECK = 0.85          # belt deck height: every port sits here, on the footprint edge
TRIANGLE_BUDGET = 3000

SKID = 0.10          # black skid under everything
BODY = 0.52          # body half size: the stubs run from the body face out to the footprint edge
TOP = 0.93           # body top; the lid plate and arrows sit on it
BELT_W = 0.56        # tread stub width
TREAD = 0.06         # tread thickness (top at the deck)


def _place(n, u, v):
    """A point `u` out along the face normal `n` (Blender XY unit axis) and `v` along the face, in Blender XY."""
    return (n[0] * u - n[1] * v, n[1] * u + n[0] * v)


def _face_box(name, n, u, v, z, su, sv, sz, mat, r, bevel=0.0):
    """Box centred `u` out along normal n, `v` across, at height z; su along the normal, sv across, sz up."""
    x, y = _place(n, u, v)
    size = (su, sv, sz) if n[0] != 0 else (sv, su, sz)
    return kit.box(name, size, (x, y, z), mat, r, bevel)


def _prism(name, pts, z0, z1, mat, r):
    """Flat convex polygon `pts` (Blender XY) extruded from z0 to z1."""
    bm = bmesh.new()
    lo = [bm.verts.new((x, y, z0)) for x, y in pts]
    hi = [bm.verts.new((x, y, z1)) for x, y in pts]
    bm.faces.new(lo[::-1])
    bm.faces.new(hi)
    k = len(pts)
    for i in range(k):
        j = (i + 1) % k
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return kit._mesh_obj(name, bm, mat, r, (0, 0, 0), (0, 0, 0))


def _arrow(name, n, d, r, z0, z1):
    """Orange lid arrow on the face with normal n, pointing along the flow d (n for an output, -n for an input)."""
    c = Vector((n[0] * 0.30, n[1] * 0.30))
    dv = Vector(d)
    pv = Vector((-dv.y, dv.x))
    L, SW, HW, HL = 0.32, 0.075, 0.22, 0.14     # length, shaft width, head width, head length
    tail, neck, tip = c - dv * (L / 2), c + dv * (L / 2 - HL), c + dv * (L / 2)
    shaft = [tail - pv * SW / 2, neck - pv * SW / 2, neck + pv * SW / 2, tail + pv * SW / 2]
    head = [neck - pv * HW / 2, tip, neck + pv * HW / 2]
    _prism(name + "Shaft", [tuple(p) for p in shaft], z0, z1, ORANGE, r)
    _prism(name + "Head", [tuple(p) for p in head], z0, z1, ORANGE, r)


def build(root_name, ports, out_name, preview_name):
    """ports: [(name, face normal (Blender XY unit axis), output)]. Writes <out_name>.blend, preview_<preview_name>_*.png
    and ../<out_name>.bin."""
    kit.reset()
    r = kit.root(root_name)

    # Skid and body.
    kit.box("Skid", (2 * HALF - 0.04, 2 * HALF - 0.04, SKID), (0, 0, SKID / 2), BLACK, r, 0.03)
    kit.box("SkidTrim", (2 * BODY + 0.12, 2 * BODY + 0.12, 0.03), (0, 0, SKID + 0.015), ORANGE, r, 0.01)
    kit.box("Body", (2 * BODY, 2 * BODY, TOP - SKID - 0.03), (0, 0, (SKID + 0.03 + TOP) / 2), WHITE, r, 0.03)
    for sx in (-1, 1):
        for sy in (-1, 1):
            kit.box(f"Post{'P' if sx > 0 else 'N'}{'P' if sy > 0 else 'N'}", (0.07, 0.07, TOP - SKID + 0.02),
                    (sx * BODY, sy * BODY, (SKID + TOP + 0.02) / 2), STEEL, r, 0.015)

    # Lid: steel rim, black plate, steel hub with the cyan status light.
    kit.box("LidRim", (2 * BODY + 0.04, 2 * BODY + 0.04, 0.03), (0, 0, TOP + 0.005), STEEL, r)
    kit.box("Lid", (2 * BODY - 0.06, 2 * BODY - 0.06, 0.03), (0, 0, TOP + 0.02), BLACK, r, 0.01)
    LID = TOP + 0.035
    kit.cyl("Hub", 0.11, 0.03, (0, 0, LID + 0.015), STEEL, r, "Z", 16, 0.005)
    kit.cyl("StatusLight", 0.045, 0.03, (0, 0, LID + 0.045), CYAN, r, "Z", 12, 0.005)

    for name, n, output in ports:
        d = n if output else (-n[0], -n[1])
        tag = name
        # Belt mouth on the face: a black opening framed by an orange (input) or steel (output) lip.
        lip = STEEL if output else ORANGE
        MZ0, MZ1 = DECK - TREAD - 0.04, DECK + 0.05
        _face_box(f"Mouth{tag}", n, BODY + 0.005, 0, (MZ0 + MZ1) / 2, 0.02, BELT_W + 0.04, MZ1 - MZ0, BLACK, r)
        _face_box(f"LipTop{tag}", n, BODY + 0.02, 0, MZ1 + 0.02, 0.04, BELT_W + 0.14, 0.04, lip, r)
        for s in (-1, 1):
            _face_box(f"LipSide{tag}{'P' if s > 0 else 'N'}", n, BODY + 0.02, s * (BELT_W / 2 + 0.05),
                      (MZ0 + MZ1 + 0.04) / 2 - 0.02, 0.04, 0.04, MZ1 - MZ0 + 0.04, lip, r)
        # Tread stub at deck height from the mouth out to the footprint edge (where the port and the belt end are).
        u0, u1 = BODY - 0.02, HALF
        uc, ul = (u0 + u1) / 2, u1 - u0
        _face_box(f"Tread{tag}", n, uc, 0, DECK - TREAD / 2, ul, BELT_W - 0.08, TREAD, BLACK, r)
        for i in range(3):
            _face_box(f"Slat{tag}{i}", n, u0 + 0.04 + i * 0.055, 0, DECK - 0.004, 0.02, BELT_W - 0.1, 0.012, STEEL, r)
        for s in (-1, 1):
            side = 'P' if s > 0 else 'N'
            _face_box(f"Rail{tag}{side}", n, uc, s * (BELT_W / 2 - 0.02), DECK - 0.01, ul, 0.04, 0.1, WHITE, r)
            _face_box(f"RailCap{tag}{side}", n, uc + 0.01, s * (BELT_W / 2 - 0.02), DECK + 0.045, ul - 0.02, 0.045, 0.02,
                      ORANGE, r)
        # Black bracket under the stub, off the body face.
        _face_box(f"Bracket{tag}", n, uc - 0.02, 0, DECK - TREAD - 0.04, ul - 0.06, BELT_W - 0.16, 0.08, BLACK, r)
        _arrow(f"Arrow{tag}", n, d, r, LID - 0.004, LID + 0.008)
        p = kit.port(name, (n[0] * HALF, n[1] * HALF, DECK), (d[0], d[1], 0), output, None, r)
        del p["cargo"]     # cosmetic: ModelSwap.CargoOf short-circuits for routers, so a cargo prop would be ignored

    lo, hi = kit.bounds(r)
    print(f"MODEL bounds lo=({lo.x:.3f},{lo.y:.3f},{lo.z:.3f}) hi=({hi.x:.3f},{hi.y:.3f},{hi.z:.3f})")
    eps = 1e-4
    assert lo.z >= -0.01, f"floor below ground: {lo.z}"
    assert -HALF - eps <= lo.x and hi.x <= HALF + eps and -HALF - eps <= lo.y and hi.y <= HALF + eps, "outside the footprint"
    assert hi.z <= HEIGHT, f"taller than {HEIGHT}: {hi.z}"
    tris = kit.triangles(r)
    assert tris <= TRIANGLE_BUDGET, f"{tris} triangles, over the box budget of {TRIANGLE_BUDGET}"
    kit.footprint_guide(HALF, HALF, HEIGHT, r)

    def unity(v):
        return (-v[0], v[2], -v[1])

    bpy.context.view_layer.update()
    for o in sorted((c for c in r.children if c.name.startswith("Port_")), key=lambda o: o.name):
        p = o.matrix_world.translation
        dd = o.matrix_world.to_3x3().col[1]
        print("PORT %s output=%d cargo=%s unity pos=(%.2f, %.2f, %.2f) dir=(%.2f, %.2f, %.2f)" %
              (o.name, o["output"], o.get("cargo"), *[round(x, 4) + 0.0 for x in unity(p)],
               *[round(x, 4) + 0.0 for x in unity(dd)]))

    kit.save(os.path.join(HERE, out_name + ".blend"))
    kit.preview(r, os.path.join(HERE, "preview_" + preview_name), structure=True)
    kit.export(r, os.path.join(HERE, "..", out_name + ".bin"))
