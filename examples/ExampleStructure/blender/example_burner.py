# Throwaway test model for StructureLib's Model support: the example burner (donor solar post 93). A 1.2 x 1.0 x 1.2 m
# white box on a black skid with a cyan lamp, and one belt port empty exactly at the burner's declared port.
# Run from the repo root:
#   "<blender.exe>" -b --factory-startup --python examples/ExampleStructure/blender/example_burner.py
# Writes blender/example_burner.blend, blender/preview_burner_{hero,side,front,rear}.png and ../example_burner.bin.
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, os.path.join(REPO, "shared", "blender"))
import kit
from kit import WHITE, BLACK, CYAN

kit.reset()
root = kit.root("ExampleBurner_Root")
kit.box("Skid", (1.3, 1.1, 0.1), (0, 0, 0.05), BLACK, root, 0.02)
kit.box("Body", (1.2, 1.0, 1.2), (0, 0, 0.7), WHITE, root, 0.04)
kit.cyl("Lamp", 0.08, 0.06, (-0.35, -0.3, 1.33), CYAN, root, "Z", 16)
# Unity (-0.90, 0.85, 0) flowing (1, 0, 0) is Blender (0.90, 0, 0.85) flowing (-1, 0, 0): into the machine.
kit.port("Grit in", (0.90, 0, 0.85), (-1, 0, 0), False, "ryanhartwig.example-grit", root)
# Solar post: half footprint (0.91, 0.64), height 1.44.
kit.footprint_guide(0.91, 0.64, 1.44, root)

lo, hi = kit.bounds(root)
assert lo.z >= -0.01, f"floor below ground: {lo.z}"

kit.save(os.path.join(HERE, "example_burner.blend"))
kit.preview(root, os.path.join(HERE, "preview_burner"), structure=True)
out = os.path.join(HERE, "..", "example_burner.bin")
kit.export(root, out)

import check_bin
mats, nodes = check_bin.read(out)
check_bin.dump(mats, nodes)
port = next(n for n in nodes if n["name"] == "Port_Grit in")
assert port["props"] == {"output": 0.0, "cargo": "ryanhartwig.example-grit"}, port["props"]
assert all(abs(a - b) < 1e-4 for a, b in zip(port["rootpos"], (-0.90, 0.85, 0))), port["rootpos"]
assert all(abs(a - b) < 1e-4 for a, b in zip(port["dir"], (1, 0, 0))), port["dir"]
print("BURNER OK")
