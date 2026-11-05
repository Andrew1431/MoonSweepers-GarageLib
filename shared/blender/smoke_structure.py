# Smoke test and worked example for the kit's structure helpers (ports, hopper, chute, footprint guide, structure
# preview stage, export checks). Run from the repo root:
#   "<blender.exe>" -b --factory-startup --python shared/blender/smoke_structure.py
# Writes shared/blender/_smoke/ (git-ignored): preview_{hero,side,front,rear}.png and smoke.bin, then reads the .bin
# back with check_bin.py and asserts the ports, their Unity-space flow directions, the guide's absence and the budget.
import os, sys, math
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kit
from kit import WHITE, BLACK, ORANGE, STEEL, CYAN
from mathutils import Vector

OUT = os.path.join(HERE, "_smoke")

kit.reset()

# kit.port must turn the empty's local +Y onto `direction`, for any direction (not only the trivial +Y).
probe = kit.root("Probe_Root")
for d in ((0, 1, 0), (0, -1, 0), (1, 0, 0), (-1, 0, 0), (0.6, 0.8, 0), (0, 0, 1)):
    p = kit.port("Probe", (0, 0, 0), d, False, "Sand", probe)
    import bpy
    bpy.context.view_layer.update()
    got = p.matrix_world.to_3x3() @ Vector((0, 1, 0))
    assert (got - Vector(d).normalized()).length < 1e-4, f"port +Y {tuple(got)} != {d}"
    bpy.data.objects.remove(p, do_unlink=True)
bpy.data.objects.remove(probe, do_unlink=True)

root = kit.root("Smoke_Root")
kit.box("Skid", (2.2, 2.2, 0.1), (0, 0, 0.05), BLACK, root, 0.02)
kit.box("Body", (2.0, 2.0, 1.5), (0, 0, 0.85), WHITE, root, 0.05)
kit.stripe("BodyStripe", (2.02, 2.02, 0.08), (0, 0, 1.35), root)
kit.cyl("Status", 0.05, 0.04, (0.7, -0.7, 1.62), CYAN, root, "Z", 12)
kit.hopper("Intake", (0, -1.1, 0.8), 0.8, 0.5, 0.45, root)
kit.chute("Outlet", (0, 1.1, 0.4), 0.6, 0.5, root)
kit.port("In", (0, -1.1, 0.8), (0, 1, 0), False, "Sand", root)
kit.port("Out", (0, 1.1, 0.4), (0, 1, 0), True, "ryanhartwig.coalite", root)
kit.footprint_guide(1.2, 1.2, 1.6, root)

lo, hi = kit.bounds(root)
assert lo.z >= -0.01, f"floor below ground: {lo.z}"

kit.preview(root, os.path.join(OUT, "preview"), structure=True)
kit.export(root, os.path.join(OUT, "smoke.bin"))

import check_bin
mats, nodes = check_bin.read(os.path.join(OUT, "smoke.bin"))
check_bin.dump(mats, nodes)
byname = {n["name"]: n for n in nodes}
assert not any(n["name"].startswith("Guide_") for n in nodes), "guide was exported"
assert byname["Port_In"]["props"] == {"output": 0.0, "cargo": "Sand"}, byname["Port_In"]["props"]
assert byname["Port_Out"]["props"] == {"output": 1.0, "cargo": "ryanhartwig.coalite"}, byname["Port_Out"]["props"]
# Blender (x, y, z) -> Unity (-x, z, -y): flow +Y in Blender is -Z in Unity; positions map the same way.
for n, pos in (("Port_In", (0, 0.8, 1.1)), ("Port_Out", (0, 0.4, -1.1))):
    d, p = byname[n]["dir"], byname[n]["rootpos"]
    assert all(abs(a - b) < 1e-4 for a, b in zip(d, (0, 0, -1))), f"{n} dir {d}"
    assert all(abs(a - b) < 1e-4 for a, b in zip(p, pos)), f"{n} pos {p}"
total = sum(n["tris"] for n in nodes)
assert 0 < total < 10000, total
assert set(mats) <= set(kit.PALETTE), mats

# The export must refuse a material outside the palette.
bad = bpy.data.materials.new("NotAGameMaterial")
o = kit.box("Bad", (0.1, 0.1, 0.1), (0, 0, 2), WHITE, root)
o.data.materials[0] = bad
try:
    kit.export(root, os.path.join(OUT, "bad.bin"))
    raise AssertionError("export accepted a non-palette material")
except ValueError as e:
    print("refused as expected:", e)
assert not os.path.exists(os.path.join(OUT, "bad.bin"))
print("SMOKE OK")
