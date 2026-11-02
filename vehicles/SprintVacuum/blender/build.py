# Sprint vacuum concept: Fast line tier 1, off the Mega vacuum rover. A hot-rodded compact street sweeper: wedge nose,
# low glass cab, fastback hopper, fat exposed rear wheels, twin racing stripes and vacuum side-pipes. A 4 m roller
# brush rides ahead of the nose like a race car's front wing. Run from the repo root:
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --factory-startup --python vehicles/SprintVacuum/blender/build.py
import math, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
s = kit.stats("andrew1431.sprint-vacuum")
brush = s["width"]                 # sweep strip = roller length (4.0 m)

kit.reset()
r = kit.root("SprintVacuum_Root")

FY, FR = -1.75, 0.40               # front axle y, front wheel radius
RY, RR = 1.75, 0.53                # rear axle y, rear wheel radius (bigger: speed stance)


def slab(name, size, centre, angle, mat, lift=0.012):
    """Thin plate lying on a surface tilted about X by `angle` (positive rises toward +Y), lifted along its normal."""
    n = Vector((0, -math.sin(angle), math.cos(angle)))
    o = kit.box(name, size, Vector(centre) + n * lift, mat, r, 0.003)
    o.rotation_euler = (angle, 0, 0)
    return o


# Chassis spine and skirts
kit.box("Spine", (1.2, 5.3, 0.26), (0, -0.25, 0.44), BLACK, r, 0.04)
kit.box("SkirtL", (0.08, 2.3, 0.22), (0.86, -0.25, 0.5), BLACK, r, 0.03)
kit.box("SkirtR", (0.08, 2.3, 0.22), (-0.86, -0.25, 0.5), BLACK, r, 0.03)

# Wedge nose between the front wheels
NOSE_LEN, NOSE_SLOPE = 1.8, 0.62
kit.wedge("Nose", (1.34, NOSE_LEN, 0.5), (0, -2.1, 0.72), WHITE, r, slope=NOSE_SLOPE, bevel=0.03)
nose_top_y = -3.0 + NOSE_LEN * NOSE_SLOPE
nose_ang = math.atan2(0.5, NOSE_LEN * NOSE_SLOPE)
nose_run = math.hypot(0.5, NOSE_LEN * NOSE_SLOPE)
kit.box("NoseIntake", (1.2, 0.22, 0.16), (0, -3.02, 0.44), BLACK, r, 0.03)
for x in (-0.45, 0.45):
    kit.lamp(f"Headlight{x}", (x, -3.14, 0.46), (0.26, 0.03, 0.06), r)
kit.lamp("NoseCyan", (0, -3.14, 0.46), (0.3, 0.03, 0.03), r, CYAN)

# Front fenders (black arches) over the front wheels
for x in (-0.86, 0.86):
    kit.box(f"FrontArch{x}", (0.4, 1.0, 0.09), (x, FY, FR * 2 + 0.06), BLACK, r, 0.03)
    kit.box(f"FrontArchLip{x}", (0.4, 0.09, 0.22), (x, FY - 0.47, FR * 2 - 0.04), BLACK, r, 0.02)

# Low cab: white tub, raked dark glasshouse, thin roof
kit.box("CabTub", (1.8, 1.8, 0.56), (0, -0.25, 0.88), WHITE, r, 0.06)
kit.wedge("Glasshouse", (1.66, 1.75, 0.62), (0, -0.225, 1.47), GLASS, r, slope=0.55, bevel=0.03)
kit.box("Roof", (1.72, 0.86, 0.08), (0, 0.22, 1.81), WHITE, r, 0.03)
kit.box("CowlTrim", (1.7, 0.12, 0.04), (0, -1.08, 1.17), BLACK, r, 0.01)
kit.box("Seat", (0.55, 0.5, 0.14), (0, 0.15, 1.22), BLACK, r, 0.04)
kit.box("SeatBack", (0.55, 0.1, 0.45), (0, 0.42, 1.45), BLACK, r, 0.04)
for x in (-0.95, 0.95):
    kit.rod(f"MirrorArm{x}", (x * 0.93, -0.95, 1.2), (x * 1.08, -1.0, 1.28), 0.015, BLACK, r)
    kit.box(f"Mirror{x}", (0.06, 0.04, 0.12), (x * 1.1, -1.0, 1.3), BLACK, r, 0.01)
kit.stripe("CabStripeL", (0.012, 1.6, 0.06), (0.906, -0.25, 1.02), r)
kit.stripe("CabStripeR", (0.012, 1.6, 0.06), (-0.906, -0.25, 1.02), r)

# Fastback hopper: narrow lower bin between the rear wheels, wide sloping upper shell above them
kit.box("HopperBin", (1.4, 1.95, 0.62), (0, 1.625, 0.81), WHITE, r, 0.05)
fb = kit.wedge("Fastback", (1.84, 1.95, 0.66), (0, 1.625, 1.43), WHITE, r, slope=0.72, bevel=0.03)
fb.rotation_euler = (0, 0, math.pi)                    # sloped face to the rear
fb_flat_end = 0.65 + 1.95 * (1 - 0.72)
fb_ang = -math.atan2(0.66, 2.6 - fb_flat_end)
fb_run = math.hypot(0.66, 2.6 - fb_flat_end)
kit.box("HopperDoor", (1.3, 0.05, 0.5), (0, 2.61, 0.8), BLACK, r, 0.02)
kit.box("Ducktail", (1.8, 0.22, 0.05), (0, 2.58, 1.13), BLACK, r, 0.02).rotation_euler = (0.18, 0, 0)
kit.box("RearBumper", (1.5, 0.2, 0.18), (0, 2.68, 0.45), BLACK, r, 0.04)
for x in (-0.55, 0.55):
    kit.lamp(f"TailLamp{x}", (x, 2.645, 0.98), (0.3, 0.03, 0.07), r)
for x in (-0.92, 0.92):
    kit.box(f"RearArch{x}", (0.5, 1.2, 0.08), (x * 1.03, RY, RR * 2 + 0.05), BLACK, r, 0.03)
kit.stripe("FastbackStripeL", (0.012, 1.45, 0.06), (0.926, 1.45, 1.2), r)
kit.stripe("FastbackStripeR", (0.012, 1.45, 0.06), (-0.926, 1.45, 1.2), r)

# Twin racing stripes: nose slope, nose deck, roof, fastback deck and slope
for x in (-0.15, 0.15):
    t = f"{'L' if x > 0 else 'R'}"
    slab(f"RaceNose{t}", (0.16, nose_run - 0.06, 0.01), (x, (-3.0 + nose_top_y) / 2, 0.72), nose_ang, ORANGE)
    kit.stripe(f"RaceDeck{t}", (0.16, -1.2 - nose_top_y, 0.01), (x, (nose_top_y - 1.2) / 2, 0.975), r)
    kit.stripe(f"RaceRoof{t}", (0.16, 0.8, 0.01), (x, 0.22, 1.855), r)
    kit.stripe(f"RaceFbDeck{t}", (0.16, fb_flat_end - 0.67, 0.01), (x, (0.67 + fb_flat_end) / 2, 1.765), r)
    slab(f"RaceFb{t}", (0.16, fb_run - 0.08, 0.01), (x, (fb_flat_end + 2.6) / 2, 1.43), fb_ang, ORANGE)

# Vacuum side-pipes: fat hot-rod tubes from the brush intake back into the hopper
for x in (-1.0, 1.0):
    t = f"{'L' if x > 0 else 'R'}"
    kit.rod(f"SidePipe{t}", (x * 0.98, -1.3, 0.62), (x * 0.98, 1.0, 0.62), 0.075, STEEL, r, 16)
    kit.rod(f"SidePipeIn{t}", (x * 0.98, 1.0, 0.62), (x * 0.72, 1.12, 0.78), 0.075, STEEL, r, 16)
    kit.rod(f"SidePipeFront{t}", (x * 0.98, -1.3, 0.62), (x * 0.62, -1.38, 0.55), 0.075, STEEL, r, 16)
    for y in (-0.9, 0.0, 0.8):
        kit.cyl(f"PipeClamp{t}{y}", 0.09, 0.06, (x * 0.98, y, 0.62), BLACK, r, "Y", 16)

# Roof beacon and antenna
kit.box("BeaconBase", (0.3, 0.16, 0.05), (0, 0.5, 1.87), BLACK, r, 0.01)
kit.cyl("Beacon", 0.07, 0.1, (0, 0.5, 1.94), LAMP, r, "Z", 16)
kit.rod("Antenna", (0.7, 0.62, 1.84), (0.7, 0.8, 2.5), 0.012, BLACK, r)
kit.cyl("AntennaTip", 0.03, 0.06, (0.7, 0.8, 2.52), LAMP, r)

# Wheels: modest fronts, fat exposed rears
for x in (-0.86, 0.86):
    kit.wheel(f"WheelF{'L' if x > 0 else 'R'}", (x, FY, FR), FR, 0.3, r, WHITE)
for x in (-0.96, 0.96):
    kit.wheel(f"WheelB{'L' if x > 0 else 'R'}", (x, RY, RR), RR, 0.44, r, WHITE)

# Front-wing brush bar: 4 m orange roller in a black hood, white endplates, struts back to the nose
BY = -3.35
kit.roller_brush("Brush", (0, BY, 0.17), brush, 0.17, r)
for x in (-1, 1):
    t = f"{'L' if x > 0 else 'R'}"
    kit.box(f"Endplate{t}", (0.05, 0.6, 0.42), (x * (brush / 2 + 0.11), BY + 0.02, 0.26), WHITE, r, 0.02)
    kit.stripe(f"EndplateStripe{t}", (0.012, 0.5, 0.06), (x * (brush / 2 + 0.141), BY + 0.02, 0.36), r)
    kit.lamp(f"EndplateLamp{t}", (x * (brush / 2 + 0.11), BY - 0.2, 0.5), (0.06, 0.08, 0.06), r)
    kit.rod(f"BrushArm{t}", (x * 0.45, -2.95, 0.5), (x * 0.45, BY + 0.05, 0.4), 0.035, BLACK, r)
    kit.rod(f"BrushStrut{t}", (x * 0.62, -2.6, 0.55), (x * 1.35, BY + 0.05, 0.42), 0.03, BLACK, r)
kit.box("BrushDuct", (0.5, 0.35, 0.16), (0, BY + 0.25, 0.38), BLACK, r, 0.04)

kit.save(os.path.join(HERE, "sprint_vacuum.blend"))
kit.preview(r, os.path.join(HERE, "preview"))
kit.export(r, os.path.join(HERE, "..", "sprint_vacuum.bin"))
