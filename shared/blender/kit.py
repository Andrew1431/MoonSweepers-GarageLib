# Shared building kit for Moon Sweepers vehicle models. Run headless:
#   "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" -b --factory-startup --python vehicles/<Name>/blender/build.py
# A build script does:  import sys; sys.path.insert(0, "shared/blender"); import kit
# then kit.reset(), builds parts under kit.root(...), and calls kit.save / kit.preview / kit.export.
#
# Conventions (see vehicles/STYLE.md): metres, Z up, nose at -Y, +X is the vehicle's left. Ground at Z = 0.
# Structures (machines built by StructureLib): see shared/blender/STRUCTURES.md and smoke_structure.py (port, hopper,
# chute, footprint_guide, preview(..., structure=True)).
# Materials are named after the game materials the loader maps them to; never invent new names.
import bpy, bmesh, math, os, sys
from mathutils import Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

# Name -> (base colour linear RGB, roughness, metallic, emission strength). Names map to MoonPrototypeModels materials:
# MS_BodyWhite=shell, MS_BlackPlastic=dark, MS_OrangeMetal/MS_LampOrange=orange, MS_Glass=glass, MS_Steel=metal,
# MS_Cyan=cyan.
PALETTE = {
    "MS_BodyWhite":    ((0.80, 0.79, 0.76), 0.32, 0.35, 0.0),
    "MS_BlackPlastic": ((0.018, 0.021, 0.026), 0.55, 0.0, 0.0),
    "MS_OrangeMetal":  ((0.85, 0.22, 0.03), 0.35, 0.6, 0.0),
    "MS_LampOrange":   ((1.0, 0.45, 0.1), 0.4, 0.0, 2.0),
    "MS_Glass":        ((0.06, 0.10, 0.14), 0.08, 0.3, 0.0),
    "MS_Steel":        ((0.55, 0.56, 0.58), 0.3, 1.0, 0.0),
    "MS_Cyan":         ((0.1, 0.75, 0.9), 0.3, 0.0, 3.0),
}
WHITE, BLACK, ORANGE, LAMP, GLASS, STEEL, CYAN = PALETTE.keys()


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for name, (col, rough, metal, emit) in PALETTE.items():
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        bsdf.inputs["Base Color"].default_value = (*col, 1)
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        if emit:
            bsdf.inputs["Emission Color"].default_value = (*col, 1)
            bsdf.inputs["Emission Strength"].default_value = emit
        m.diffuse_color = (*col, 1)


def root(name):
    o = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(o)
    return o


def _link(o, parent):
    bpy.context.scene.collection.objects.link(o)
    if parent is not None:
        o.parent = parent
    return o


def _mesh_obj(name, bm, mat, parent, loc, rot):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(bpy.data.materials[mat])
    o = bpy.data.objects.new(name, me)
    o.location = loc
    o.rotation_euler = rot
    return _link(o, parent)


def _bevel(o, width, segments=2):
    if width > 0:
        m = o.modifiers.new("Bevel", "BEVEL")
        m.width = width
        m.segments = segments
        m.limit_method = "ANGLE"
    return o


def box(name, size, loc=(0, 0, 0), mat=WHITE, parent=None, bevel=0.03, rot=(0, 0, 0)):
    """Axis-aligned box: size (x, y, z) in metres, centred on loc."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    return _bevel(_mesh_obj(name, bm, mat, parent, loc, rot), min(bevel, min(size) * 0.45))


def wedge(name, size, loc=(0, 0, 0), mat=WHITE, parent=None, slope=0.5, bevel=0.02):
    """Box whose front-top edge is cut back by `slope` (fraction of length): a sloped nose or windscreen base."""
    sx, sy, sz = size
    bm = bmesh.new()
    pts = [(-sx/2, -sy/2, -sz/2), (sx/2, -sy/2, -sz/2), (sx/2, sy/2, -sz/2), (-sx/2, sy/2, -sz/2),
           (-sx/2, -sy/2 + sy*slope, sz/2), (sx/2, -sy/2 + sy*slope, sz/2), (sx/2, sy/2, sz/2), (-sx/2, sy/2, sz/2)]
    v = [bm.verts.new(p) for p in pts]
    for f in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
        bm.faces.new([v[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _bevel(_mesh_obj(name, bm, mat, parent, loc, (0, 0, 0)), bevel)


def cyl(name, radius, depth, loc=(0, 0, 0), mat=STEEL, parent=None, axis="Z", verts=24, bevel=0.01):
    """Cylinder along X, Y or Z."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=radius, radius2=radius, depth=depth)
    rot = {"X": (0, math.pi / 2, 0), "Y": (math.pi / 2, 0, 0), "Z": (0, 0, 0)}[axis]
    return _bevel(_mesh_obj(name, bm, mat, parent, loc, rot), min(bevel, radius * 0.3, depth * 0.3))


def rod(name, a, b, radius=0.03, mat=STEEL, parent=None, verts=10):
    """Round bar from point a to point b (roll cages, frames, mirrors)."""
    a, b = Vector(a), Vector(b)
    d = b - a
    o = cyl(name, radius, d.length, (a + b) / 2, mat, parent, "Z", verts, 0)
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    return o


def wheel(name, loc, radius=0.35, width=0.25, parent=None, hub=STEEL, chunky=True, spin=True):
    """Black tyre with a hub, axle along X. Tread blocks when chunky.
    The parts hang off an empty called `name` at the axle; its `spin` prop (the radius) makes GarageLib roll it with
    ground speed. Pass spin=False for wheels that shouldn't turn (a spare)."""
    piv = _link(bpy.data.objects.new(name, None), parent)
    piv.location = loc
    if spin:
        piv["spin"] = float(radius)
    o = (0, 0, 0)
    tyre = cyl(name + "_Tyre", radius, width, o, BLACK, piv, "X", 32, 0.04)
    cyl(name + "_Hub", radius * 0.55, width * 1.04, o, hub, piv, "X", 20, 0.01)
    cyl(name + "_Cap", radius * 0.18, width * 1.10, o, BLACK, piv, "X", 12, 0.005)
    if chunky:
        n = 14
        for i in range(n):
            a = 2 * math.pi * i / n
            p = Vector((0, math.sin(a) * radius, math.cos(a) * radius))
            t = box(f"{name}_Tread{i}", (width * 0.9, radius * 0.22, 0.05), p, BLACK, piv, 0)   # unbevelled: 14 per wheel adds up
            t.rotation_euler = (-a, 0, 0)
    return tyre


def disc_brush(name, loc, radius=0.3, parent=None, tilt=0.12):
    """The game's round orange gutter brush: flat bristle disc with a black hub, tilted slightly forward.
    The `brush` prop makes GarageLib turn it about its own axis while the vehicle cleans."""
    d = cyl(name, radius, 0.06, loc, ORANGE, parent, "Z", 28, 0.02)
    d.rotation_euler = (tilt, 0, 0)
    d["brush"] = 1.0
    h = cyl(name + "_Hub", radius * 0.45, 0.14, Vector(loc) + Vector((0, 0, 0.08)), BLACK, parent, "Z", 16, 0.02)
    h.rotation_euler = (tilt, 0, 0)
    return d


def roller_brush(name, loc, length, radius=0.15, parent=None):
    """Orange roller brush across the vehicle (axis X) in a black housing; turns while cleaning like disc_brush."""
    r = cyl(name, radius, length, loc, ORANGE, parent, "X", 20, 0.02)
    r["brush"] = 1.0
    box(name + "_Hood", (length + 0.08, radius * 2.0, 0.08), Vector(loc) + Vector((0, 0.02, radius + 0.04)), BLACK, parent, 0.02)
    for s in (-1, 1):
        box(f"{name}_End{'L' if s > 0 else 'R'}", (0.06, radius * 1.8, radius * 2.2), Vector(loc) + Vector((s * (length / 2 + 0.04), 0, 0)), BLACK, parent, 0.01)
    return r


def stripe(name, size, loc, parent=None, mat=ORANGE):
    """Thin livery stripe; give it 0.01 m of depth proud of the panel it sits on."""
    return box(name, size, loc, mat, parent, 0.003)


def lamp(name, loc, size=(0.18, 0.04, 0.10), parent=None, mat=LAMP):
    return box(name, size, loc, mat, parent, 0.01)


# --- Structures (see shared/blender/STRUCTURES.md) ---------------------------------------------------------------
# Objects whose name starts with GUIDE_PREFIX are build aids: previews show them, export skips them (and their children).
GUIDE_PREFIX = "Guide_"
STRUCTURE_TRIANGLES = 10000


def _heading(direction):
    """Z rotation that turns local +Y onto the horizontal part of `direction`."""
    d = Vector(direction)
    return math.atan2(-d.x, d.y)


def port(name, loc, direction, output, cargo, parent):
    """Belt port `Port_<name>`: an empty at `loc` (the exact point a belt end snaps to) whose local +Y is the flow
    direction (into the machine for an input, out of it for an output; a unit vector in Blender space). Its ID
    properties `output` (0.0 / 1.0) and `cargo` ("Sand", "Dust", "Stone" or a cargo id) are written by the exporter as
    node props, which StructureLib reads. Put it at the hopper mouth (input) or chute lip (output)."""
    o = _link(bpy.data.objects.new("Port_" + name, None), parent)
    o.empty_display_type = "ARROWS"
    o.empty_display_size = 0.35
    o.location = loc
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 1, 0)).rotation_difference(Vector(direction).normalized())
    o["output"] = 1.0 if output else 0.0
    o["cargo"] = str(cargo)
    return o


def _guide_material():
    m = bpy.data.materials.get(GUIDE_PREFIX + "Wire")
    if m is None:
        m = bpy.data.materials.new(GUIDE_PREFIX + "Wire")
        m.use_nodes = True
        b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        b.inputs["Base Color"].default_value = (1.0, 0.85, 0.1, 1)
        b.inputs["Emission Color"].default_value = (1.0, 0.85, 0.1, 1)
        b.inputs["Emission Strength"].default_value = 1.5
        m.diffuse_color = (1.0, 0.85, 0.1, 1)
    return m


def footprint_guide(half_x, half_y, height, parent):
    """The donor's footprint and height as a yellow wireframe box (`Guide_Footprint`) from Z = 0, centred on the root.
    Previews show it; export skips it. Take the numbers from the donor's `halfFootprint` (x, z) and `height`."""
    _guide_material()
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector((half_x * 2, half_y * 2, height)), verts=bm.verts)
    o = _mesh_obj(GUIDE_PREFIX + "Footprint", bm, GUIDE_PREFIX + "Wire", parent, (0, 0, height / 2), (0, 0, 0))
    o.display_type = "WIRE"
    w = o.modifiers.new("Wire", "WIREFRAME")
    w.thickness = 0.025
    w.use_replace = True
    return o


def hopper(name, loc, width, depth, height, parent, mat=BLACK, lip=ORANGE, throat=0.45, wall=0.04):
    """Open-top tapered intake: the mouth (width along X, depth along Y) is centred on `loc` at the top and the walls
    taper down `height` to a throat of `throat` x the mouth. Put `loc` at the input port so the belt end sits on the
    rim. The rim faces take `lip` (orange intake lip; None for the body material)."""
    hw, hd, tw, td = width / 2, depth / 2, width * throat / 2, depth * throat / 2
    t = wall
    bm = bmesh.new()

    def ring(x, y, z):
        return [bm.verts.new(p) for p in ((-x, -y, z), (x, -y, z), (x, y, z), (-x, y, z))]

    ot, ob = ring(hw, hd, 0), ring(tw, td, -height)                    # outer top / bottom
    it, ib = ring(hw - t, hd - t, 0), ring(max(tw - t, t), max(td - t, t), -height + t)
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((ob[i], ob[j], ot[j], ot[i]))                     # outer walls
        bm.faces.new((ib[j], ib[i], it[i], it[j]))                     # inner walls
        bm.faces.new((ot[i], ot[j], it[j], it[i])).material_index = 1  # rim
    bm.faces.new(list(reversed(ob)))
    bm.faces.new(ib)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = _mesh_obj(name, bm, mat, parent, loc, (0, 0, 0))
    o.data.materials.append(bpy.data.materials[lip or mat])
    return o


def chute(name, loc, length, width, parent, mat=STEEL, direction=(0, 1, 0), angle=30, depth=0.14, wall=0.03):
    """Angled outlet trough: a U channel whose lip (open end) is centred on `loc` with its floor at `loc`, rising
    `length` (horizontal) back against `direction` at `angle` degrees into the machine. Put `loc` at the output port."""
    rise = length * math.tan(math.radians(angle))
    hw, t = width / 2, wall
    prof = [(-hw, 0), (hw, 0), (hw, depth), (hw - t, depth), (hw - t, t), (-hw + t, t), (-hw + t, depth), (-hw, depth)]
    bm = bmesh.new()
    lip = [bm.verts.new((x, 0, z - t)) for x, z in prof]
    back = [bm.verts.new((x, -length, z - t + rise)) for x, z in prof]
    n = len(prof)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((lip[i], lip[j], back[j], back[i]))
    bm.faces.new(lip)
    bm.faces.new(list(reversed(back)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _mesh_obj(name, bm, mat, parent, loc, (0, 0, _heading(direction)))


def _is_guide(o):
    while o is not None:
        if o.name.startswith(GUIDE_PREFIX):
            return True
        o = o.parent
    return False


def _parts(obj):
    """Mesh/curve objects under `obj` that export writes (guides excluded)."""
    return [o for o in [obj, *obj.children_recursive] if o.type in ("MESH", "CURVE") and not _is_guide(o)]


def triangles(obj):
    """Triangles export would write for `obj` (evaluated: bevels and other modifiers included)."""
    dg = bpy.context.evaluated_depsgraph_get()
    n = 0
    for o in _parts(obj):
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        me.calc_loop_triangles()
        n += len(me.loop_triangles)
        ev.to_mesh_clear()
    return n


def bounds(obj):
    bpy.context.view_layer.update()   # matrix_world is stale until the scene is evaluated
    pts = []
    for o in [obj, *obj.children_recursive]:
        if o.type in ("MESH", "CURVE"):
            pts += [o.matrix_world @ Vector(c) for c in o.bound_box]
    if not pts:
        return Vector((-1, -1, 0)), Vector((1, 1, 1))
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def bounds_union(lo, hi, lo2, hi2):
    return (Vector((min(lo.x, lo2.x), min(lo.y, lo2.y), min(lo.z, lo2.z))),
            Vector((max(hi.x, hi2.x), max(hi.y, hi2.y), max(hi.z, hi2.z))))


def scale_figure(x=None):
    """A 1.8 m astronaut stand-in beside the vehicle for preview renders (not exported: it has no parent root)."""
    fig = root("ScaleFigure")
    box("Fig_Legs", (0.42, 0.28, 0.85), (0, 0, 0.43), WHITE, fig, 0.08)
    box("Fig_Torso", (0.55, 0.36, 0.6), (0, 0, 1.15), WHITE, fig, 0.1)
    box("Fig_Pack", (0.45, 0.22, 0.55), (0, 0.27, 1.15), WHITE, fig, 0.06)
    cyl("Fig_Helmet", 0.2, 0.36, (0, 0, 1.62), WHITE, fig, "Z", 20, 0.08)
    box("Fig_Visor", (0.3, 0.05, 0.18), (0, -0.19, 1.63), BLACK, fig, 0.03)
    if x is not None:
        fig.location.x = x
    return fig


def _stage():
    sc = bpy.context.scene
    try:
        sc.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = 1280, 800
    sc.render.film_transparent = False
    world = bpy.data.worlds.new("Space")
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (0.05, 0.055, 0.07, 1)
    bg.inputs["Strength"].default_value = 1.0
    sc.world = world
    if "Regolith" not in bpy.data.materials:
        g = bpy.data.materials.new("Regolith")
        g.use_nodes = True
        b = next(n for n in g.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        b.inputs["Base Color"].default_value = (0.42, 0.42, 0.45, 1)
        b.inputs["Roughness"].default_value = 0.95
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=60)
        _mesh_obj("Ground", bm, "Regolith", None, (0, 0, 0), (0, 0, 0))
        sun = bpy.data.lights.new("Sun", "SUN")
        sun.energy = 4.0
        sun.angle = math.radians(1.5)
        so = bpy.data.objects.new("Sun", sun)
        so.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
        sc.collection.objects.link(so)
        fill = bpy.data.lights.new("Fill", "SUN")
        fill.energy = 1.4
        fo = bpy.data.objects.new("Fill", fill)
        fo.rotation_euler = (math.radians(60), 0, math.radians(100))
        sc.collection.objects.link(fo)
    return sc


def stage_structure(obj=None):
    """Preview stage for structures: the usual lit ground plane at Z = 0, and the 1.8 m figure standing beside the
    model's +X side (level with its middle, 0.8 m out) so the footprint and height read against a person.
    Returns (scene, figure); the figure is None when there is no `obj` or one already exists."""
    sc = _stage()
    fig = None
    if obj is not None and "ScaleFigure" not in bpy.data.objects:
        lo, hi = bounds(obj)
        fig = scale_figure()
        fig.location = (hi.x + 0.8, (lo.y + hi.y) / 2, 0)
    return sc, fig


def _camera(sc, lo, hi, view, structure=False):
    centre = (lo + hi) / 2
    size = hi - lo
    cam = bpy.data.objects.get("PreviewCam") or bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    cd = cam.data
    if view in ("hero", "rear"):
        cd.type = "PERSP"
        cd.lens = 50
        direction = Vector((1.0, -1.35, 0.75) if view == "hero" else (-1.0, 1.35, 0.75)).normalized()
        dist = max(size.length, 2.0) * 1.9
    else:
        cd.type = "ORTHO"
        up = 0.12 if structure else 0.0001   # structures: look slightly down so the ground plane shows as a floor
        direction = {"side": Vector((1, 0, up)), "front": Vector((0, -1, up)), "top": Vector((0.0001, -0.0001, 1))}[view].normalized()
        dist = max(size.length, 2.0) * 3
        span = {"side": max(size.y, size.z * 1.6), "front": max(size.x, size.z * 1.6), "top": max(size.x, size.y)}[view]
        cd.ortho_scale = span * 1.25
    cd.clip_end = 500
    cam.location = centre + direction * dist
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam


def preview(obj, path_base, views=("hero", "side", "front", "rear"), figure=True, structure=False):
    """Render PNGs of `obj` to <path_base>_<view>.png. Look at them with Read before calling the model done.
    Prints `TRIANGLES <n>` (what export would write). With structure=True: the structure stage (figure beside the +X
    side, ground visible in every view) and a RuntimeError, after rendering, above STRUCTURE_TRIANGLES."""
    tris = triangles(obj)
    if structure:
        sc, fig = stage_structure(obj if figure else None)
    else:
        sc = _stage()
        fig = None
    lo, hi = bounds(obj)
    if figure and not structure and "ScaleFigure" not in bpy.data.objects:
        fig = scale_figure()
        fig.location = ((lo.x + hi.x) / 2 + (hi.x - lo.x) / 2 + 0.6, hi.y + 0.5, 0)   # behind the left rear corner
    out = []
    for v in views:
        flo, fhi = (lo, hi)
        # Vehicles: the figure stands behind the rear corner and the front view is framed without it. Structures: it
        # stands beside the +X side, in every view but the side view, where it would hide the +X face.
        show_fig = fig is not None and (v != "side" if structure else v != "front")
        if fig and structure:
            for o in [fig, *fig.children_recursive]:
                o.hide_render = not show_fig
        if show_fig:
            flo, fhi = bounds_union(lo, hi, *bounds(fig))
        _camera(sc, flo, fhi, v, structure)
        sc.render.filepath = os.path.abspath(f"{path_base}_{v}.png")
        bpy.ops.render.render(write_still=True)
        out.append(sc.render.filepath)
    if fig:
        for o in [fig, *fig.children_recursive]:
            bpy.data.objects.remove(o, do_unlink=True)
    print("PREVIEW", *out)
    print("TRIANGLES", tris)
    if structure and tris > STRUCTURE_TRIANGLES:
        raise RuntimeError(f"{obj.name}: {tris} triangles, over the structure budget of {STRUCTURE_TRIANGLES}")
    return out


def save(path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(path))
    print("SAVED", path)


def export(obj, out_path):
    """Write the mesh model (.bin) the loader reads, via the shared exporter.
    Raises ValueError, writing nothing, if a part uses a material outside PALETTE (or an empty slot): the game would show
    it as a flat fallback colour. `Guide_*` objects (and their children) are left out of the file."""
    bad = sorted({f"{o.name}: {s.material.name if s.material else '<empty slot>'}"
                  for o in _parts(obj) for s in (o.material_slots or [None])
                  if s is None or s.material is None or s.material.name not in PALETTE})
    if bad:
        raise ValueError("materials outside the kit palette: " + "; ".join(bad))
    guides = [o for o in obj.children_recursive if o.name.startswith(GUIDE_PREFIX) and not _is_guide(o.parent)]
    held = [(g, g.parent, g.matrix_parent_inverse.copy()) for g in guides]
    for g, _, _ in held:
        g.parent = None
    bpy.context.view_layer.update()
    exporter = os.path.join(REPO, "shared", "blender", "export_mesh_model.py")
    argv = sys.argv
    sys.argv = [argv[0]]
    try:
        code = compile(open(exporter, encoding="utf-8").read(), exporter, "exec")
        exec(code, {"__name__": "export_mesh_model", "ROOT_NAME": obj.name, "OUT_PATH": os.path.abspath(out_path)})
    finally:
        sys.argv = argv
        for g, p, inv in held:
            g.parent = p
            g.matrix_parent_inverse = inv
        bpy.context.view_layer.update()


def stats(vehicle_id):
    """Numbers for `vehicle_id` from data/lines.vehicles.json (width = sweep strip, the brush span to model)."""
    import json
    d = json.load(open(os.path.join(REPO, "data", "lines.vehicles.json"), encoding="utf-8"))
    return d["vehicles"][vehicle_id]
