# Lunar buggy concept: open-frame runabout off the Ride-on sweeper. Narrow roller brush up front, tiny hopper behind
# the seat, roll cage, big balloon tyres. Run from the repo root:
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --factory-startup --python vehicles/LunarBuggy/blender/build.py
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN

HERE = os.path.dirname(os.path.abspath(__file__))
s = kit.stats("andrew1431.lunar-buggy")
brush = s["width"]                 # sweep strip = roller length

kit.reset()
r = kit.root("LunarBuggy_Root")

# Chassis: low tub, black skid plate
kit.box("Tub", (1.15, 2.1, 0.32), (0, 0.05, 0.62), WHITE, r, 0.06)
kit.box("Skid", (1.0, 2.2, 0.08), (0, 0.05, 0.42), BLACK, r, 0.03)
kit.stripe("TubStripeL", (0.012, 1.7, 0.07), (0.58, 0.05, 0.66), r)
kit.stripe("TubStripeR", (0.012, 1.7, 0.07), (-0.58, 0.05, 0.66), r)
kit.wedge("Nose", (1.05, 0.55, 0.26), (0, -1.12, 0.66), WHITE, r, slope=0.55)

# Seat and controls
kit.box("SeatBase", (0.55, 0.5, 0.18), (0, 0.2, 0.87), BLACK, r, 0.05)
kit.box("SeatBack", (0.55, 0.12, 0.5), (0, 0.46, 1.12), BLACK, r, 0.05)
kit.rod("Column", (0, -0.35, 0.78), (0, -0.2, 1.12), 0.03, STEEL, r)
kit.cyl("Wheel", 0.17, 0.04, (0, -0.18, 1.14), BLACK, r, "Y", 20)

# Roll cage
for x in (-0.5, 0.5):
    kit.rod(f"CageRear{x}", (x, 0.58, 0.78), (x * 0.8, 0.5, 1.6), 0.035, STEEL, r)
    kit.rod(f"CageFront{x}", (x, -0.45, 0.78), (x * 0.8, 0.0, 1.6), 0.035, STEEL, r)
    kit.rod(f"CageTop{x}", (x * 0.8, 0.0, 1.6), (x * 0.8, 0.5, 1.6), 0.035, STEEL, r)
kit.rod("CageCross", (-0.4, 0.5, 1.6), (0.4, 0.5, 1.6), 0.035, STEEL, r)
kit.box("RoofLightBar", (0.7, 0.08, 0.06), (0, 0.0, 1.63), BLACK, r, 0.01)
for x in (-0.24, 0, 0.24):
    kit.lamp(f"RoofLamp{x}", (x, -0.045, 1.63), (0.12, 0.02, 0.05), r, CYAN)

# Hopper behind the seat
kit.box("Hopper", (0.9, 0.55, 0.45), (0, 0.92, 0.95), WHITE, r, 0.05)
kit.box("HopperLid", (0.92, 0.57, 0.05), (0, 0.92, 1.19), ORANGE, r, 0.02)
kit.box("HopperChute", (0.25, 0.2, 0.25), (0, 1.25, 0.82), BLACK, r, 0.03)

# Antenna with orange tip lamp
kit.rod("Antenna", (0.38, 1.1, 1.18), (0.38, 1.25, 2.1), 0.012, BLACK, r)
kit.cyl("AntennaTip", 0.035, 0.07, (0.38, 1.25, 2.12), LAMP, r)

# Balloon tyres, wide stance
for x in (-0.72, 0.72):
    for y, tag in ((-0.72, "F"), (0.78, "B")):
        kit.wheel(f"Wheel{tag}{'L' if x > 0 else 'R'}", (x, y, 0.38), 0.38, 0.32, r, WHITE)
    kit.box(f"Fender{x}", (0.36, 0.8, 0.06), (x, -0.72, 0.82), ORANGE, r, 0.02)

# Front roller brush, exactly the sweep width, on two black arms
kit.roller_brush("Brush", (0, -1.55, 0.16), brush, 0.15, r)
for x in (-0.35, 0.35):
    kit.rod(f"BrushArm{x}", (x, -1.2, 0.5), (x, -1.5, 0.28), 0.03, BLACK, r)

# Headlights
for x in (-0.38, 0.38):
    kit.lamp(f"Headlight{x}", (x, -1.4, 0.72), (0.2, 0.03, 0.08), r)

kit.save(os.path.join(HERE, "lunar_buggy.blend"))
kit.preview(r, os.path.join(HERE, "preview"))
kit.export(r, os.path.join(HERE, "..", "lunar_buggy.bin"))
