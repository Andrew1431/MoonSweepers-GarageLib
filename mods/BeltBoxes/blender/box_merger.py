# Box merger: three belts in (Unity +Z left, -X back, -Z right), one out on +X, all at the 0.85 m deck on the 1.4 m
# footprint edge. Lid: three arrows in, one out. Built by box_common.build. Run from the repo root:
#   "<blender.exe>" -b --factory-startup --python mods/BeltBoxes/blender/box_merger.py
# Writes box_merger.blend and preview_merger_{hero,side,front,rear}.png here and ../box_merger.bin.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import box_common

# (port name, face outward normal in Blender XY, output). Unity +X = Blender -X, Unity +Z = Blender -Y.
box_common.build("BoxMerger_Root", [
    ("Left",  (0, -1), False),   # Unity +Z face, flows -Z into the box
    ("Back",  (1, 0),  False),   # Unity -X face, flows +X into the box
    ("Right", (0, 1),  False),   # Unity -Z face, flows +Z into the box
    ("Out",   (-1, 0), True),    # Unity +X face, flows +X
], "box_merger", "merger")
