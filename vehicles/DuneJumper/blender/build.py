# Dune jumper concept: Lunar buggy line tier 2. The buggy's wild big brother, a long-travel trophy truck for the Moon.
# Exposed A-arms and coilovers, huge balloon tyres, spare on the rear rack, roof light bar, rocket-booster pod out back,
# and a narrow roller brush tucked under the nose. Run from the repo root:
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --factory-startup --python vehicles/DuneJumper/blender/build.py
import os, sys, math
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN

HERE = os.path.dirname(os.path.abspath(__file__))
s = kit.stats("andrew1431.dune-jumper")
brush = s["width"]                 # sweep strip = roller length (1.08 m)

kit.reset()
r = kit.root("DuneJumper_Root")

WR, WW = 0.52, 0.42                # wheel radius / width
TX = 1.0                           # half track
FY, RY = -1.2, 1.15                # axle positions

# Chassis: narrow tub, long ramp nose, black skid
kit.box("Tub", (1.05, 2.3, 0.35), (0, 0.2, 0.72), WHITE, r, 0.06)
kit.box("Skid", (0.9, 3.4, 0.08), (0, -0.3, 0.5), BLACK, r, 0.03)
kit.wedge("Hood", (1.0, 1.1, 0.3), (0, -1.45, 0.78), WHITE, r, slope=0.6)
for x in (-1, 1):
    kit.stripe(f"TubStripe{x}", (0.012, 2.0, 0.07), (x * 0.53, 0.2, 0.78), r)
kit.box("NoseBumper", (0.9, 0.12, 0.16), (0, -2.02, 0.6), BLACK, r, 0.03)
kit.rod("BumperHoopL", (0.4, -2.08, 0.52), (0.3, -2.12, 0.86), 0.025, STEEL, r)
kit.rod("BumperHoopR", (-0.4, -2.08, 0.52), (-0.3, -2.12, 0.86), 0.025, STEEL, r)
kit.rod("BumperHoopTop", (-0.3, -2.12, 0.86), (0.3, -2.12, 0.86), 0.025, STEEL, r)
for x in (-0.3, 0.3):
    kit.lamp(f"Headlight{x}", (x, -1.95, 0.72), (0.2, 0.03, 0.07), r)

# Front fenders: white fibreglass shells high over the wheels for travel, orange stripe on top
for x in (-1, 1):
    f = kit.box(f"Fender{x}", (0.7, 1.2, 0.06), (x * 0.9, FY, 1.14), WHITE, r, 0.025)
    f.rotation_euler = (0, x * 0.12, 0)
    kit.box(f"FenderWall{x}", (0.05, 1.1, 0.32), (x * 0.54, FY, 1.02), WHITE, r, 0.02)
    st = kit.stripe(f"FenderStripe{x}", (0.1, 1.1, 0.012), (x * 0.95, FY, 1.17), r)
    st.rotation_euler = (0, x * 0.12, 0)
    lip = kit.box(f"FenderLip{x}", (0.05, 1.2, 0.16), (x * 1.265, FY, 1.03), BLACK, r, 0.015)
    lip.rotation_euler = (0, x * 0.12, 0)
    kit.box(f"FenderNose{x}", (0.7, 0.05, 0.14), (x * 0.9, FY - 0.61, 1.06), BLACK, r, 0.015).rotation_euler = (0, x * 0.12, 0)

# Cockpit: two black bucket seats, dash, wheel, little dark windscreen
for x in (-0.26, 0.26):
    kit.box(f"SeatBase{x}", (0.42, 0.45, 0.12), (x, -0.1, 0.96), BLACK, r, 0.04)
    kit.box(f"SeatBack{x}", (0.42, 0.1, 0.55), (x, 0.16, 1.24), BLACK, r, 0.04)
kit.box("Dash", (1.0, 0.18, 0.2), (0, -0.78, 1.0), BLACK, r, 0.04)
kit.rod("Column", (0.26, -0.72, 1.05), (0.26, -0.5, 1.2), 0.03, STEEL, r)
sw = kit.cyl("SteerWheel", 0.16, 0.04, (0.26, -0.49, 1.21), BLACK, r, "Y", 20)
sw.rotation_euler = (math.pi / 2 - 0.5, 0, 0)
ws = kit.box("Windscreen", (0.82, 0.02, 0.5), (0, -0.66, 1.33), GLASS, r, 0.01)
ws.rotation_euler = (-0.52, 0, 0)

# Roll cage, roof panel, light bar
ROOF = 1.82
for x in (-1, 1):
    kit.rod(f"APillar{x}", (x * 0.48, -0.85, 0.9), (x * 0.42, -0.36, ROOF), 0.035, STEEL, r)
    kit.rod(f"BPillar{x}", (x * 0.5, 0.3, 0.9), (x * 0.42, 0.3, ROOF), 0.035, STEEL, r)
    kit.rod(f"RoofRail{x}", (x * 0.42, -0.36, ROOF), (x * 0.42, 0.3, ROOF), 0.035, STEEL, r)
    kit.rod(f"RackBrace{x}", (x * 0.42, 0.3, ROOF), (x * 0.5, 1.55, 1.55), 0.03, STEEL, r)
    kit.rod(f"RackPost{x}", (x * 0.5, 1.55, 1.55), (x * 0.5, 1.55, 0.62), 0.03, STEEL, r)
    kit.rod(f"RackFoot{x}", (x * 0.5, 1.55, 0.7), (x * 0.45, 1.3, 0.6), 0.03, STEEL, r)
kit.rod("RoofCrossF", (-0.42, -0.36, ROOF), (0.42, -0.36, ROOF), 0.035, STEEL, r)
kit.rod("RoofCrossB", (-0.42, 0.3, ROOF), (0.42, 0.3, ROOF), 0.035, STEEL, r)
kit.box("Roof", (0.88, 0.7, 0.04), (0, -0.03, ROOF + 0.04), WHITE, r, 0.015)
kit.stripe("RoofStripe", (0.16, 0.7, 0.012), (0, -0.03, ROOF + 0.065), r)
kit.box("LightBar", (1.0, 0.1, 0.1), (0, -0.36, ROOF + 0.12), BLACK, r, 0.02)
for i in range(6):
    kit.lamp(f"BarLamp{i}", (-0.4 + i * 0.16, -0.415, ROOF + 0.12), (0.12, 0.02, 0.06), r, CYAN)

# Hopper bed behind the seats (1900 kg: much bigger than the buggy's box)
kit.box("Hopper", (1.2, 0.9, 0.55), (0, 0.85, 1.17), WHITE, r, 0.05)
kit.box("HopperLid", (1.22, 0.92, 0.05), (0, 0.85, 1.47), ORANGE, r, 0.02)
for x in (-1, 1):
    kit.stripe(f"HopperStripe{x}", (0.012, 0.8, 0.06), (x * 0.605, 0.85, 1.2), r)

for x in (-0.42, 0.42):
    kit.lamp(f"TailLamp{x}", (x, 1.31, 1.32), (0.14, 0.03, 0.07), r)

# Spare tyre on the rear rack (axle along Y)
spare = kit.root("SpareMount")
spare.parent = r
spare.location = (0, 1.56, 1.2)
spare.rotation_euler = (0, 0, math.pi / 2)
kit.wheel("Spare", (0, 0, 0), 0.4, 0.28, spare, WHITE, chunky=False)
kit.rod("RackCross", (-0.5, 1.55, 1.2), (0.5, 1.55, 1.2), 0.025, STEEL, r)

# Rocket-booster pod under the spare: white tube, orange fins, glowing orange nozzle
kit.cyl("Pod", 0.2, 0.7, (0, 1.75, 0.6), WHITE, r, "Y", 20, 0.03)
kit.cyl("PodNose", 0.13, 0.12, (0, 1.36, 0.6), BLACK, r, "Y", 16, 0.02)
kit.cyl("PodBand", 0.205, 0.08, (0, 1.6, 0.6), ORANGE, r, "Y", 20, 0.01)
kit.cyl("Nozzle", 0.17, 0.16, (0, 2.17, 0.6), BLACK, r, "Y", 20, 0.02)
kit.cyl("NozzleGlow", 0.13, 0.03, (0, 2.25, 0.6), LAMP, r, "Y", 20, 0.005)
for i, a in enumerate((0.785, 2.356, 3.927, 5.498)):
    fin = kit.box(f"Fin{i}", (0.02, 0.28, 0.14), (math.cos(a) * 0.26, 1.98, 0.6 + math.sin(a) * 0.26), ORANGE, r, 0.005)
    fin.rotation_euler = (0, -a + math.pi / 2, 0)
for x in (-1, 1):
    kit.rod(f"PodStrut{x}", (x * 0.5, 1.55, 0.64), (x * 0.18, 1.62, 0.62), 0.025, BLACK, r)

# Antenna with orange tip lamp, a nod to the buggy
kit.rod("Antenna", (0.5, 1.55, 1.55), (0.5, 1.62, 2.4), 0.012, BLACK, r)
kit.cyl("AntennaTip", 0.035, 0.07, (0.5, 1.62, 2.42), LAMP, r)

# Wheels: even bigger balloon tyres, wide stance
for x in (-1, 1):
    tag = "L" if x > 0 else "R"
    kit.wheel(f"WheelF{tag}", (x * TX, FY, WR), WR, WW, r, WHITE)
    kit.wheel(f"WheelB{tag}", (x * TX, RY, WR), WR, WW, r, WHITE)

# Front long-travel suspension: exposed double A-arms, upright, orange coilover through the hood
for x in (-1, 1):
    kit.box(f"UprightF{x}", (0.08, 0.16, 0.36), (x * 0.74, FY, WR), BLACK, r, 0.02)
    for dy in (-0.2, 0.2):
        kit.rod(f"LowerArm{x}{dy}", (x * 0.4, FY + dy, 0.46), (x * 0.72, FY, 0.4), 0.025, BLACK, r)
        kit.rod(f"UpperArm{x}{dy}", (x * 0.45, FY + dy * 0.8, 0.78), (x * 0.72, FY, 0.66), 0.022, BLACK, r)
    a, b = (x * 0.68, FY + 0.06, 0.42), (x * 0.36, FY + 0.06, 1.1)
    kit.rod(f"ShockF{x}", a, b, 0.022, STEEL, r)
    kit.rod(f"SpringF{x}", (x * 0.6, FY + 0.06, 0.59), (x * 0.42, FY + 0.06, 0.97), 0.055, ORANGE, r, 12)
    kit.cyl(f"ShockCapF{x}", 0.06, 0.06, b, BLACK, r, "Z", 12)

# Rear: solid axle on trailing arms, coilovers to the hopper sides, mud flaps
kit.cyl("RearAxle", 0.05, 1.6, (0, RY, WR), STEEL, r, "X", 12)
kit.cyl("Diff", 0.14, 0.22, (0, RY, WR), BLACK, r, "Y", 16, 0.03)
for x in (-1, 1):
    kit.rod(f"TrailArm{x}", (x * 0.45, 0.25, 0.55), (x * 0.66, RY, 0.48), 0.03, BLACK, r)
    kit.rod(f"ShockR{x}", (x * 0.7, RY - 0.05, 0.56), (x * 0.66, 0.62, 1.3), 0.022, STEEL, r)
    kit.rod(f"SpringR{x}", (x * 0.695, RY - 0.12, 0.66), (x * 0.67, 0.72, 1.16), 0.055, ORANGE, r, 12)
    kit.box(f"ShockMountR{x}", (0.08, 0.12, 0.1), (x * 0.64, 0.62, 1.3), BLACK, r, 0.02)
    kit.box(f"MudFlap{x}", (0.4, 0.03, 0.4), (x * TX, RY + WR + 0.1, 0.4), BLACK, r, 0.01)
    kit.rod(f"FlapArm{x}", (x * 0.5, 1.3, 0.62), (x * TX, RY + WR + 0.1, 0.6), 0.02, BLACK, r)

# Front roller brush, exactly the sweep width, tucked under the nose on two short arms
kit.roller_brush("Brush", (0, -1.85, 0.14), brush, 0.13, r)
for x in (-0.3, 0.3):
    kit.rod(f"BrushArm{x}", (x, -1.6, 0.5), (x, -1.82, 0.3), 0.03, BLACK, r)

kit.save(os.path.join(HERE, "dune_jumper.blend"))
kit.preview(r, os.path.join(HERE, "preview"))
kit.export(r, os.path.join(HERE, "..", "dune_jumper.bin"))
