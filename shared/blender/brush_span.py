# Prints the X span (metres) covered by objects with a `brush` prop in the open .blend.
import bpy
xs = []
for o in bpy.data.objects:
    if "brush" in o.keys() and o.type == "MESH":
        for c in o.bound_box:
            xs.append((o.matrix_world @ __import__("mathutils").Vector(c)).x)
print("BRUSHSPAN", bpy.data.filepath.split("\\")[-1], f"{(max(xs)-min(xs)) if xs else 0:.2f}", len([o for o in bpy.data.objects if 'brush' in o.keys()]))
