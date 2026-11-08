# Battery bank: PowerGrid's 20 kWh store (base SolarPostKind, footprint 1.2 x 1.2 m). A 1.2 m white cabinet of cells:
# two rows of cell packs behind a black window on the front and back, a cyan charge gauge, black vent slats on the
# sides, terminal posts and an orange lamp on top. Run from the repo root:
#   "<blender.exe>" -b --factory-startup --python mods/PowerGrid/blender/battery_bank.py
# Writes battery_bank.blend and preview_bank_{hero,side,front,rear}.png here and ../battery_bank.bin.
import os, sys, math
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "shared", "blender"))
import kit
from kit import WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN

HALF_X, HALF_Y = 0.6, 0.6                     # Footprint (1.2, 1.2) / 2; Blender X, Y = Unity -x, -z
BASE_T = 0.08
BOX = 1.10                                    # cabinet width and depth
TOP = 1.2                                     # cabinet top: the 1.2 m cube
CHARGE = 4                                    # lit gauge bars out of 5

kit.reset()
r = kit.root("BatteryBank_Root")

kit.box("Base", (1.18, 1.18, BASE_T), (0, 0, BASE_T / 2), BLACK, r, 0.02)
kit.box("Cabinet", (BOX, BOX, TOP - BASE_T), (0, 0, (BASE_T + TOP) / 2), WHITE, r, 0.05)
kit.stripe("Stripe", (BOX + 0.01, BOX + 0.01, 0.06), (0, 0, TOP - 0.1), r)
kit.box("Lid", (BOX - 0.12, BOX - 0.12, 0.04), (0, 0, TOP + 0.02), BLACK, r, 0.015)

# Front (-Y) and back (+Y): a black window with two rows of three cell packs, each with an orange terminal cap.
for face, sy in (("F", -1), ("B", 1)):
    fy = sy * BOX / 2
    kit.box(f"Window{face}", (0.7, 0.03, 0.8), (-0.14, fy + sy * 0.005, 0.6), BLACK, r, 0.01)
    for row, z in enumerate((0.41, 0.79)):
        for col, x in enumerate((-0.36, -0.14, 0.08)):
            kit.box(f"Cell{face}{row}{col}", (0.18, 0.04, 0.32), (x, fy + sy * 0.02, z), WHITE, r, 0.02)
            kit.box(f"CellCap{face}{row}{col}", (0.08, 0.05, 0.04), (x, fy + sy * 0.025, z + 0.14), ORANGE, r, 0.008)
            kit.box(f"CellLed{face}{row}{col}", (0.05, 0.05, 0.03), (x, fy + sy * 0.025, z - 0.1), CYAN, r, 0.005)
    # Charge gauge: five bars in a black slot, the lit ones cyan.
    kit.box(f"Gauge{face}", (0.16, 0.03, 0.8), (0.37, fy + sy * 0.005, 0.6), BLACK, r, 0.01)
    for k in range(5):
        kit.box(f"GaugeBar{face}{k}", (0.11, 0.04, 0.11), (0.37, fy + sy * 0.012, 0.32 + k * 0.14),
                CYAN if k < CHARGE else GLASS, r, 0.008)

# Sides (+X, -X): black vent slats.
for face, sx in (("L", 1), ("R", -1)):
    for k in range(5):
        kit.box(f"Vent{face}{k}", (0.03, 0.8, 0.05), (sx * (BOX / 2 + 0.005), 0, 0.35 + k * 0.12), BLACK, r, 0.01)

# Top: two terminal posts (steel, orange + and black - caps) on a black bus bar, and the orange lamp.
kit.box("BusBar", (0.6, 0.12, 0.04), (-0.12, 0.2, TOP + 0.06), BLACK, r, 0.01)
for name, x, cap in (("Pos", -0.34, ORANGE), ("Neg", 0.1, BLACK)):
    kit.cyl(f"Terminal{name}", 0.05, 0.1, (x, 0.2, TOP + 0.13), STEEL, r, "Z", 14, 0.01)
    kit.cyl(f"TerminalCap{name}", 0.065, 0.05, (x, 0.2, TOP + 0.2), cap, r, "Z", 14, 0.015)
kit.cyl("LampBase", 0.1, 0.06, (0.3, -0.28, TOP + 0.07), BLACK, r, "Z", 16, 0.01)
kit.cyl("Lamp", 0.075, 0.14, (0.3, -0.28, TOP + 0.17), LAMP, r, "Z", 16, 0.03)
kit.cyl("LampCap", 0.08, 0.03, (0.3, -0.28, TOP + 0.255), BLACK, r, "Z", 16, 0.01)

lo, hi = kit.bounds(r)
print(f"MODEL bounds lo=({lo.x:.2f},{lo.y:.2f},{lo.z:.2f}) hi=({hi.x:.2f},{hi.y:.2f},{hi.z:.2f})")
assert lo.z >= -0.01, f"floor below ground: {lo.z}"
assert -HALF_X <= lo.x and hi.x <= HALF_X and -HALF_Y <= lo.y and hi.y <= HALF_Y, "outside the footprint"
kit.footprint_guide(HALF_X, HALF_Y, hi.z, r)

kit.save(os.path.join(HERE, "battery_bank.blend"))
kit.preview(r, os.path.join(HERE, "preview_bank"), structure=True)
kit.export(r, os.path.join(HERE, "..", "battery_bank.bin"))
