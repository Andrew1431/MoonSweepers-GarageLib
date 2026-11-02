import bpy, mathutils
for m in bpy.data.materials:
    n=next((x for x in (m.node_tree.nodes if m.use_nodes else []) if x.type=="BSDF_PRINCIPLED"),None)
    c=tuple(round(v,3) for v in (n.inputs["Base Color"].default_value if n else m.diffuse_color))
    print("MAT",m.name,c, round(n.inputs["Roughness"].default_value,2) if n else "", round(n.inputs["Metallic"].default_value,2) if n else "")
mins=mathutils.Vector((9e9,)*3); maxs=-mins
for o in bpy.data.objects:
    if o.type=="MESH":
        for v in o.bound_box:
            w=o.matrix_world@mathutils.Vector(v); mins=mathutils.Vector(map(min,mins,w)); maxs=mathutils.Vector(map(max,maxs,w))
print("BOUNDS",mins,maxs, "objs",len([o for o in bpy.data.objects if o.type=="MESH"]), "engine",bpy.context.scene.render.engine)
print("ROOTS",[o.name for o in bpy.data.objects if o.parent is None])
