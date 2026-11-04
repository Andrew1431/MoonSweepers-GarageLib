# Throwaway test model for StructureLib's CanisterModel support: the example grit lump, shown in place of the vanilla
# capsule on near canister views (belt, ground, hand). A low steel block with chamfered edges, sized like the capsule
# (shared/blender/STRUCTURES.md, "Canister models"). Run from the repo root:
#   "<blender.exe>" -b --factory-startup --python examples/ExampleStructure/blender/example_grit.py
# Writes blender/example_grit.blend, blender/preview_grit_{hero,side,front,rear}.png and ../example_grit.bin.
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, os.path.join(REPO, "shared", "blender"))
import kit
from kit import STEEL, BLACK

# The capsule's box in view space is X +-0.18, Y +-0.21, Z 0..0.61 in Blender axes (STRUCTURES.md "Canister models");
# the lump is a squat block inside it, floored at Z = 0 like every model.
W, D, H = 0.36, 0.40, 0.40

kit.reset()
root = kit.root("ExampleGrit_Root")
kit.box("Lump", (W, D, H * 0.8), (0, 0, H * 0.4), STEEL, root, 0.05)
kit.box("Cap", (W * 0.6, D * 0.6, H * 0.2), (0, 0, H * 0.9), BLACK, root, 0.03)

lo, hi = kit.bounds(root)
assert lo.z >= -0.01, f"floor below ground: {lo.z}"

kit.save(os.path.join(HERE, "example_grit.blend"))
kit.preview(root, os.path.join(HERE, "preview_grit"), structure=True, figure=False)
kit.export(root, os.path.join(HERE, "..", "example_grit.bin"))
