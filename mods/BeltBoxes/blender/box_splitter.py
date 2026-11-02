# Box splitter: one belt in on the -X face (Unity), three out (+X forward, +Z left, -Z right), all at the 0.85 m deck
# on the 1.4 m footprint edge. Lid: one arrow in, three out. Built by box_common.build. Run from the repo root:
#   "<blender.exe>" -b --factory-startup --python mods/BeltBoxes/blender/box_splitter.py
# Writes box_splitter.blend and preview_splitter_{hero,side,front,rear}.png here and ../box_splitter.bin.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import box_common

# (port name, face outward normal in Blender XY, output). Unity +X = Blender -X, Unity +Z = Blender -Y.
box_common.build("BoxSplitter_Root", [
    ("In",      (1, 0),  False),   # Unity -X face, flows +X into the box
    ("Forward", (-1, 0), True),    # Unity +X face, flows +X
    ("Left",    (0, -1), True),    # Unity +Z face, flows +Z
    ("Right",   (0, 1),  True),    # Unity -Z face, flows -Z
], "box_splitter", "splitter")
