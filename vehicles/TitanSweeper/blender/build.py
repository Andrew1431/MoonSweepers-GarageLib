# Titan sweeper concept: Wide line tier 1, the first step up from the Mega vacuum rover. A municipal street-sweeper
# truck with a tall hopper, three disc brushes under the nose and hydraulic outrigger arms carrying big side brushes
# so the brush span is the full sweep width. Run from the repo root:
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --factory-startup --python vehicles/TitanSweeper/blender/build.py
import math, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN

HERE = os.path.dirname(os.path.abspath(__file__))
s = kit.stats("andrew1431.titan-sweeper")
half = s["width"] / 2              # brush span = sweep strip, outer brush edges at +-half

kit.reset()
r = kit.root("TitanSweeper_Root")

# Chassis rails and side skirts
kit.box("Frame", (1.5, 5.3, 0.3), (0, -0.05, 0.7), BLACK, r, 0.04)
for sx in (-1, 1):
    t = "L" if sx > 0 else "R"
    kit.box(f"Skirt{t}", (0.1, 1.1, 0.42), (sx * 1.2, -0.25, 0.78), BLACK, r, 0.03)
    kit.box(f"Step{t}", (0.22, 0.5, 0.06), (sx * 1.2, -1.0, 0.55), BLACK, r, 0.02)

# Cab: white box, big dark glass, orange beacon bar
kit.box("CabLower", (2.36, 1.75, 0.85), (0, -1.85, 1.3), WHITE, r, 0.08)
kit.box("CabUpper", (2.26, 1.6, 1.0), (0, -1.8, 2.2), WHITE, r, 0.1)
kit.box("CabRoof", (2.3, 1.7, 0.08), (0, -1.8, 2.72), WHITE, r, 0.03)
kit.box("Windscreen", (2.0, 0.03, 0.78), (0, -2.605, 2.2), GLASS, r, 0.02)
for sx in (-1, 1):
    t = "L" if sx > 0 else "R"
    kit.box(f"SideWindow{t}", (0.03, 1.2, 0.7), (sx * 1.135, -1.85, 2.22), GLASS, r, 0.02)
    kit.box(f"DoorWindowLow{t}", (0.03, 0.5, 0.35), (sx * 1.185, -2.1, 1.45), GLASS, r, 0.02)
    kit.stripe(f"CabStripe{t}", (0.012, 1.7, 0.1), (sx * 1.185, -1.85, 1.15), r)
    kit.rod(f"MirrorArm{t}", (sx * 1.13, -2.45, 2.0), (sx * 1.45, -2.6, 2.1), 0.025, BLACK, r)
    kit.box(f"Mirror{t}", (0.08, 0.06, 0.38), (sx * 1.48, -2.6, 2.05), BLACK, r, 0.02)
kit.stripe("CabStripeF", (2.0, 0.012, 0.1), (0, -2.73, 1.15), r)
kit.box("BeaconBar", (1.4, 0.18, 0.1), (0, -2.35, 2.8), BLACK, r, 0.02)
for x in (-0.55, 0.55):
    kit.cyl(f"Beacon{x}", 0.09, 0.14, (x, -2.35, 2.9), LAMP, r, "Z", 16)
kit.lamp("BeaconCyan", (0, -2.45, 2.82), (0.4, 0.04, 0.06), r, CYAN)

# Driver seat behind the windscreen
kit.box("SeatBase", (0.55, 0.5, 0.15), (0.45, -1.7, 1.8), BLACK, r, 0.04)
kit.box("SeatBack", (0.55, 0.12, 0.6), (0.45, -1.42, 2.1), BLACK, r, 0.04)
kit.cyl("SteeringWheel", 0.2, 0.04, (0.45, -2.25, 2.05), BLACK, r, "Y", 20)

# Nose: black bumper, headlights, grille
kit.box("Bumper", (2.45, 0.22, 0.3), (0, -2.8, 0.75), BLACK, r, 0.05)
kit.box("Grille", (1.4, 0.04, 0.4), (0, -2.74, 1.4), BLACK, r, 0.02)
for x in (-0.9, 0.9):
    kit.lamp(f"Headlight{x}", (x, -2.74, 1.42), (0.3, 0.04, 0.14), r)
    kit.lamp(f"BumperLamp{x}", (x, -2.92, 0.8), (0.2, 0.02, 0.07), r, CYAN)

# Big hopper: taller than the cab, 2.5x the Mega's box
kit.box("Hopper", (2.5, 3.3, 1.95), (0, 0.95, 1.95), WHITE, r, 0.1)
kit.box("HopperLip", (2.56, 3.36, 0.06), (0, 0.95, 2.9), BLACK, r, 0.02)
kit.box("HopperLid", (2.4, 3.2, 0.08), (0, 0.95, 2.96), WHITE, r, 0.03)
for i, y in enumerate((-0.2, 0.55, 1.3, 2.05)):
    kit.box(f"HopperRib{i}", (2.58, 0.1, 0.06), (0, y, 3.03), ORANGE, r, 0.02)
for sx in (-1, 1):
    t = "L" if sx > 0 else "R"
    kit.stripe(f"HopperStripe{t}", (0.012, 3.1, 0.16), (sx * 1.255, 0.95, 1.7), r)
    kit.stripe(f"HopperStripe2{t}", (0.012, 3.1, 0.05), (sx * 1.255, 0.95, 1.5), r)
    kit.box(f"HopperRam{t}", (0.12, 0.12, 0.9), (sx * 1.0, 2.66, 1.4), STEEL, r, 0.02)
    kit.lamp(f"TailLamp{t}", (sx * 1.05, 2.62, 1.1), (0.22, 0.04, 0.12), r)
    kit.lamp(f"CornerLamp{t}", (sx * 1.2, 2.55, 2.85), (0.1, 0.1, 0.1), r)
kit.box("HopperBase", (2.3, 3.2, 0.14), (0, 0.95, 0.93), BLACK, r, 0.03)
for sx in (-1, 1):
    t = "L" if sx > 0 else "R"
    kit.box(f"HopperHatch{t}", (0.02, 0.9, 0.7), (sx * 1.255, -0.05, 2.45), BLACK, r, 0.01)
    kit.box(f"HopperHatchPanel{t}", (0.02, 0.8, 0.6), (sx * 1.262, -0.05, 2.45), WHITE, r, 0.01)
# Rear ladder up to the hopper lid
for x in (0.75, 1.1):
    kit.rod(f"LadderRail{x}", (x, 2.78, 0.9), (x, 2.78, 3.05), 0.025, STEEL, r)
for i in range(6):
    z = 1.05 + i * 0.36
    kit.rod(f"LadderRung{i}", (0.75, 2.78, z), (1.1, 2.78, z), 0.018, STEEL, r)
kit.box("RearDoor", (2.3, 0.08, 1.6), (0, 2.63, 2.0), BLACK, r, 0.04)
kit.box("RearDoorPanel", (1.6, 0.03, 1.2), (-0.1, 2.68, 2.05), WHITE, r, 0.02)
for i, x in enumerate((-0.6, -0.1, 0.4)):
    kit.stripe(f"RearChevron{i}", (0.32, 0.012, 0.32), (x, 2.70, 1.7), r).rotation_euler = (0, 0.785, 0)
kit.box("RearBumper", (2.4, 0.2, 0.25), (0, 2.68, 0.75), BLACK, r, 0.04)

# Suction hose from the nose deck up into the hopper, exhaust stack and antenna
kit.rod("HoseA", (-0.9, -2.75, 0.45), (-1.32, -1.2, 0.95), 0.12, BLACK, r)
kit.rod("HoseB", (-1.32, -1.2, 0.95), (-1.32, -0.55, 1.5), 0.12, BLACK, r)
kit.cyl("HoseCollar", 0.16, 0.12, (-1.32, -0.55, 1.5), ORANGE, r, "Z", 16)
kit.cyl("Exhaust", 0.07, 1.1, (1.0, -0.82, 2.75), STEEL, r, "Z", 12)
kit.rod("Antenna", (-0.9, -1.2, 2.76), (-0.9, -1.05, 3.6), 0.012, BLACK, r)
kit.cyl("AntennaTip", 0.035, 0.07, (-0.9, -1.05, 3.62), LAMP, r)

def wheel(name, loc, rad=0.52, w=0.42):
    # kit.wheel with bevel-free tread blocks: six big wheels with bevelled treads blow the 20k triangle budget
    kit.wheel(name, loc, rad, w, r, WHITE, chunky=False)
    n = 14
    for i in range(n):
        a = 2 * math.pi * i / n
        p = (loc[0], loc[1] + math.sin(a) * rad, loc[2] + math.cos(a) * rad)
        kit.box(f"{name}_Tread{i}", (w * 0.9, rad * 0.22, 0.06), p, BLACK, r, 0).rotation_euler = (-a, 0, 0)


# Wheels: front axle plus a tandem rear (heavy, slow, lots of rubber)
for sx in (-1, 1):
    t = "L" if sx > 0 else "R"
    for y, tag in ((-1.45, "F"), (1.15, "B1"), (2.15, "B2")):
        wheel(f"Wheel{tag}{t}", (sx * 1.02, y, 0.52))
    kit.box(f"FrontArch{t}", (0.5, 1.25, 0.08), (sx * 1.05, -1.45, 1.12), BLACK, r, 0.03)
    kit.box(f"RearArch{t}", (0.5, 2.25, 0.08), (sx * 1.05, 1.65, 1.12), BLACK, r, 0.03)
    kit.stripe(f"RearArchStripe{t}", (0.5, 2.2, 0.02), (sx * 1.05, 1.65, 1.17), r)

# Brush deck: three disc brushes under the nose (Mega heritage)
cr = 0.5
kit.box("BrushDeck", (2.6, 0.9, 0.12), (0, -2.75, 0.42), BLACK, r, 0.03)
for x in (-1.0, 0.0, 1.0):
    kit.disc_brush(f"NoseBrush{x}", (x, -2.75, 0.12), cr, r)
    kit.cyl(f"NoseMotor{x}", 0.14, 0.22, (x, -2.75, 0.3), BLACK, r, "Z", 12)

# Outrigger arms: hydraulic booms reaching to big side brushes; outer brush edges sit at +-width/2
orad = 0.6
ox = half - orad
for sx in (-1, 1):
    t = "L" if sx > 0 else "R"
    hub = (sx * ox, -3.0, 0.42)
    kit.box(f"ArmMount{t}", (0.25, 0.4, 0.35), (sx * 1.2, -2.55, 0.62), BLACK, r, 0.04)
    kit.rod(f"Arm{t}", (sx * 1.25, -2.55, 0.62), hub, 0.07, WHITE, r)
    kit.rod(f"ArmRam{t}", (sx * 1.2, -2.45, 0.85), (sx * (ox - 0.25), -2.9, 0.5), 0.035, STEEL, r)
    kit.stripe(f"ArmStripe{t}", (0.1, 0.1, 0.1), (sx * 1.3, -2.58, 0.82), r)
    kit.box(f"OutMotor{t}", (0.36, 0.36, 0.3), (sx * ox, -3.0, 0.35), BLACK, r, 0.06)
    kit.lamp(f"OutLamp{t}", (sx * ox, -3.0, 0.53), (0.12, 0.12, 0.06), r)
    kit.disc_brush(f"OutBrush{t}", (sx * ox, -3.0, 0.13), orad, r)

kit.save(os.path.join(HERE, "titan_sweeper.blend"))
kit.preview(r, os.path.join(HERE, "preview"))
kit.export(r, os.path.join(HERE, "..", "titan_sweeper.bin"))
