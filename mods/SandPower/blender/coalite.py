# Coalite: the cargo model StructureLib shows in place of the vanilla dust capsule on near canister views (belts,
# ground, hand) for SandPower's coalite. A baked-sand brick: a rectangular block with chamfered edges and a shallow
# press line around its waist, in the kit's MS_Sand matte. It fills the capsule's footprint and height (the box the
# capsule's meshes span in view space, shared/blender/STRUCTURES.md "Canister models"), so belt spacing, pickup and the
# machine hoppers see the same size as before; the capsule's colliders are kept by the library, not modelled here.
# Run from the repo root:
#   "<blender.exe>" -b --factory-startup --python mods/SandPower/blender/coalite.py
# Writes coalite.blend and preview_coalite_{hero,side,front,rear}.png here and ../coalite.bin.
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "shared", "blender"))
import kit
from kit import SAND, BLACK

# The capsule's box in view space is X +-0.18, Y +-0.21, Z 0..0.61 in Blender axes (shared/blender/STRUCTURES.md,
# "Canister models"; the view's origin is at the capsule's base). The game's own stone block, the precedent for a
# non-capsule canister, is 0.5 x 0.5 x 0.28 m. The brick lies flat inside the capsule's footprint: long along the
# belt (X), half as tall as the capsule, so it reads as a pressed block and not as another canister.
CAP_W, CAP_D, CAP_H = 0.36, 0.42, 0.61

kit.reset()
r = kit.root("Coalite_Root")

W, D, H = 0.34, 0.40, 0.30
kit.box("Brick", (W, D, H), (0, 0, H / 2), SAND, r, bevel=0.03)
# The press seam around the waist, where the oven's die closed: a thin dark band just proud of the faces.
kit.box("Seam", (W + 0.006, D + 0.006, 0.014), (0, 0, H * 0.5), BLACK, r, bevel=0.002)
# Two shallow die marks pressed into the top face (dark recesses read as stamped lines from above).
for y in (-0.1, 0.1):
    kit.box(f"Mark{'A' if y < 0 else 'B'}", (W * 0.6, 0.02, 0.006), (0, y, H - 0.001), BLACK, r, bevel=0.001)

lo, hi = kit.bounds(r)
print(f"MODEL bounds lo=({lo.x:.3f},{lo.y:.3f},{lo.z:.3f}) hi=({hi.x:.3f},{hi.y:.3f},{hi.z:.3f})")
assert lo.z >= -0.01, f"floor below ground: {lo.z}"
assert hi.z <= CAP_H + 0.001 and hi.x - lo.x <= CAP_W + 0.01 and hi.y - lo.y <= CAP_D + 0.01, "larger than the capsule"

kit.save(os.path.join(HERE, "coalite.blend"))
kit.preview(r, os.path.join(HERE, "preview_coalite"), structure=True, figure=False)
kit.export(r, os.path.join(HERE, "..", "coalite.bin"))
