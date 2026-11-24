"""FactorySim asset generator.

Builds every 3D asset of the simulator from code, exports it as glTF 2.0 (.glb) and
renders a thumbnail of each. Written against Blender 5.3. Normal use, from the repo root:

    blender --background --factory-startup --python assets/blender_scripts/build_assets.py

It can also run inside an open Blender (Scripting -> Run Script, or the Blender MCP):
the first run creates and shows the scene, the second run builds in it.

Conventions (see docs/ASSETS.md):
  * 1 unit = 1 m, real dimensions that match the colliders in the app.
  * Blender +X = Godot +X (belt travel), Blender +Z = Godot +Y (up),
    Blender -Y = Godot +Z (pusher stroke, sensor view direction).
  * Visual only: no colliders are exported.
  * Moving parts are separate objects with fixed names; the app looks them up by name.

The script only touches its own scene ("FactorySim_Assets") and collections starting
with "FS_". Anything else in the open .blend file is left alone.
"""

import math
import os
import shutil

import bmesh
import bpy
from mathutils import Matrix, Vector

SCENE_NAME = "FactorySim_Assets"
PREFIX = "FS_"

try:
    REPO = FACTORYSIM_REPO  # noqa: F821 - set by the caller when run through exec()
except NameError:
    REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))

MODELS_DIR = os.path.join(REPO, "assets", "models")
SOURCE_DIR = os.path.join(REPO, "assets", "source")
ENGINE_DIR = os.path.join(REPO, "src", "FactorySim.App", "models")
PREVIEW_DIR = os.path.join(REPO, "docs", "assets-preview")
LIBRARY_DIR = os.path.join(REPO, "assets", "library")
ENGINE_LIBRARY_DIR = os.path.join(REPO, "src", "FactorySim.App", "library")

# Dimensions shared with the app (src/FactorySim.App/Main.cs and Parts/*.cs).
BELT_ROLLER_RADIUS = 0.06
BELT_STRIPE_PERIOD = 0.25      # metres of belt per texture repeat; the app scrolls 1/0.25 per metre
BOX_FOOTPRINT = 0.30
BOX_LOW_HEIGHT = 0.20
BOX_HIGH_HEIGHT = 0.35
PUSHER_PLATE = (0.60, 0.08, 0.25)   # x, y (stroke axis), z
PUSHER_CENTRE_ABOVE_FLOOR = 0.945
SENSOR_OFFSET_X = 0.10              # photo-eyes sit 10 cm past the pusher centre
SENSOR_LOW_Z = -0.045               # low eye relative to the pusher plate centre


# --------------------------------------------------------------------------- scene


def get_scene():
    """Returns this script's scene, which must be the scene Blender is working in.

    Operators such as the glTF exporter act on the active scene. Pointing them at another
    scene through a context override crashed Blender 5.3, so the script insists on being
    in its own scene instead:
      * --background (the normal way to run it): the start-up scene is taken over and
        renamed; with an unsaved start-up file its default cube, light and camera go.
      * interactive: the scene is created next to whatever is open and shown in the window;
        run the script a second time to build in it. Nothing else in the file is touched.
    """
    scene = bpy.data.scenes.get(SCENE_NAME)
    if scene is None:
        if bpy.app.background:
            scene = bpy.context.scene
            scene.name = SCENE_NAME
            if not bpy.data.filepath:
                for obj in list(scene.objects):
                    bpy.data.objects.remove(obj, do_unlink=True)
        else:
            scene = bpy.data.scenes.new(SCENE_NAME)
    if bpy.context.scene is not scene:
        if bpy.context.window is not None:
            scene["fs_previous_scene"] = bpy.context.window.scene.name
            bpy.context.window.scene = scene
        raise RuntimeError(
            "Switched to scene '{}'. Run the script again to build the assets.".format(SCENE_NAME))
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    return scene


def fresh_collection(scene, name):
    """Returns an empty collection FS_<name>, deleting what an earlier run left in it."""
    full = PREFIX + name
    old = bpy.data.collections.get(full)
    if old is not None:
        for obj in list(old.objects):
            mesh = obj.data if obj.type == "MESH" else None
            bpy.data.objects.remove(obj, do_unlink=True)
            if mesh is not None and mesh.users == 0:
                bpy.data.meshes.remove(mesh)
        bpy.data.collections.remove(old)
    collection = bpy.data.collections.new(full)
    scene.collection.children.link(collection)
    return collection


# ----------------------------------------------------------------------- materials


def srgb(hex_colour):
    """'#RRGGBB' -> linear RGBA, which is what the Principled BSDF expects."""
    hex_colour = hex_colour.lstrip("#")
    channels = [int(hex_colour[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    linear = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in channels]
    return (linear[0], linear[1], linear[2], 1.0)


def make_image(name, width, height, pixel):
    """Small generated texture. pixel(x, y) returns sRGB (r, g, b) in 0..1."""
    old = bpy.data.images.get(name)
    if old is not None:
        bpy.data.images.remove(old)
    image = bpy.data.images.new(name, width, height, alpha=False)
    data = []
    for y in range(height):
        for x in range(width):
            r, g, b = pixel(x, y)
            data.extend((r, g, b, 1.0))
    image.pixels.foreach_set(data)
    image.pack()
    return image


def material(name, colour, metallic=0.0, roughness=0.5, emission=None, emission_strength=1.0,
             alpha=1.0, image=None):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    if hasattr(mat, "use_nodes") and mat.node_tree is None:
        mat.use_nodes = True
    tree = mat.node_tree
    tree.nodes.clear()
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
    tree.links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])
    bsdf.inputs["Base Color"].default_value = colour
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Alpha"].default_value = alpha
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = emission
        bsdf.inputs["Emission Strength"].default_value = emission_strength
    if image is not None:
        texture = tree.nodes.new("ShaderNodeTexImage")
        texture.image = image
        tree.links.new(texture.outputs["Color"], bsdf.inputs["Base Color"])
    if alpha < 1.0:
        if hasattr(mat, "surface_render_method"):
            mat.surface_render_method = "BLENDED"
        elif hasattr(mat, "blend_method"):
            mat.blend_method = "BLEND"
    mat.diffuse_color = colour[:3] + (alpha,)
    return mat


def _noise(seed):
    """Tiny deterministic pseudo-random generator, so textures are identical on every run."""
    state = [seed & 0x7FFFFFFF or 1]

    def next_value():
        state[0] = (state[0] * 1103515245 + 12345) & 0x7FFFFFFF
        return state[0] / 0x7FFFFFFF

    return next_value


def build_materials():
    rand = _noise(7)

    def belt_pixel(x, _y):
        base = 0.10 if x < 32 else 0.16
        return (base, base, base + 0.01)

    def cardboard(base, seed):
        r = _noise(seed)

        def pixel(_x, y):
            ridge = 0.02 if y % 4 == 0 else 0.0     # faint corrugation lines
            n = (r() - 0.5) * 0.05
            return tuple(max(0.0, min(1.0, c + n - ridge)) for c in base)

        return pixel

    def concrete_pixel(_x, _y):
        v = 0.36 + (rand() - 0.5) * 0.05
        return (v, v + 0.005, v + 0.012)

    m = {}
    m["frame_grey"] = material("FS_FrameGrey", srgb("#6B7178"), 0.6, 0.45)
    m["frame_dark"] = material("FS_FrameDark", srgb("#2A2D31"), 0.5, 0.5)
    m["steel"] = material("FS_Steel", srgb("#C0C3C7"), 1.0, 0.3)
    m["panel"] = material("FS_PanelLight", srgb("#C9CDD1"), 0.3, 0.55)
    m["yellow"] = material("FS_SafetyYellow", srgb("#F2B705"), 0.1, 0.5)
    m["black"] = material("FS_Black", srgb("#141517"), 0.0, 0.7)
    m["lens"] = material("FS_Lens", srgb("#0A0C10"), 0.0, 0.1)
    m["tape"] = material("FS_Tape", srgb("#B79A6B"), 0.0, 0.6)
    m["led"] = material("FS_Led", srgb("#300808"), 0.0, 0.4, emission=srgb("#FF2A1A"), emission_strength=0.0)
    m["lamp"] = material("FS_Lamp", srgb("#0B2A12"), 0.0, 0.4, emission=srgb("#35F06A"), emission_strength=0.0)
    m["beam"] = material("FS_Beam", srgb("#FF1A1A"), 0.0, 0.5, emission=srgb("#FF1A1A"), emission_strength=1.0, alpha=0.35)
    m["belt"] = material("FS_BeltRubber", srgb("#1E1F21"), 0.0, 0.9,
                         image=make_image("FS_BeltStripes", 64, 8, belt_pixel))
    m["cardboard"] = material("FS_Cardboard", srgb("#C8A473"), 0.0, 0.85,
                              image=make_image("FS_CardboardLight", 64, 64, cardboard((0.78, 0.64, 0.45), 11)))
    m["cardboard_dark"] = material("FS_CardboardDark", srgb("#8C6239"), 0.0, 0.85,
                                   image=make_image("FS_CardboardKraft", 64, 64, cardboard((0.55, 0.38, 0.22), 23)))
    m["concrete"] = material("FS_Concrete", srgb("#5C5E61"), 0.0, 0.95,
                             image=make_image("FS_ConcreteNoise", 128, 128, concrete_pixel))
    return m


# ------------------------------------------------------------------------ geometry


def add_box(bm, size, centre, mat=0):
    """Axis-aligned box of the given size (x, y, z) around centre."""
    verts = bmesh.ops.create_cube(bm, size=1.0, calc_uvs=True)["verts"]
    bmesh.ops.scale(bm, vec=Vector(size), verts=verts)
    bmesh.ops.translate(bm, vec=Vector(centre), verts=verts)
    for face in {f for v in verts for f in v.link_faces}:
        face.material_index = mat
    return verts


def add_cylinder(bm, radius, depth, centre, axis="Z", segments=20, mat=0):
    """Cylinder along the given axis; the curved side is smooth-shaded, the caps flat."""
    verts = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments,
                                  radius1=radius, radius2=radius, depth=depth, calc_uvs=True)["verts"]
    if axis == "Y":
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, "X"), verts=verts)
    elif axis == "X":
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, "Y"), verts=verts)
    bmesh.ops.translate(bm, vec=Vector(centre), verts=verts)
    for face in {f for v in verts for f in v.link_faces}:
        face.material_index = mat
        face.smooth = len(face.verts) == 4
    return verts


def make_object(collection, name, build, materials, location=(0.0, 0.0, 0.0), bevel=0.004):
    """Creates a mesh object. build(bm) fills the mesh; materials are the slots it indexes."""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    build(bm)
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    for mat in materials:
        mesh.materials.append(mat)
    obj = bpy.data.objects.new(name, mesh)
    obj.location = location
    obj["fs_part"] = name
    collection.objects.link(obj)
    if bevel:
        modifier = obj.modifiers.new("Bevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        modifier.limit_method = "ANGLE"
        modifier.angle_limit = math.radians(40)
    return obj


# -------------------------------------------------------------------------- assets


def build_box(collection, m, height, dark):
    """Cardboard box, origin at its centre. Matches the box collider exactly."""
    f = BOX_FOOTPRINT

    def build(bm):
        add_box(bm, (f, f, height), (0, 0, 0), 0)
        # Tape over the top seam and a little way down both ends; 1 mm proud of the surface.
        add_box(bm, (f + 0.002, 0.05, 0.002), (0, 0, height / 2), 1)
        for side in (-1, 1):
            add_box(bm, (0.002, 0.05, height * 0.4), (side * f / 2, 0, height / 2 - height * 0.2), 1)

    make_object(collection, "Box", build, [m["cardboard_dark"] if dark else m["cardboard"], m["tape"]], bevel=0.005)


def build_conveyor(collection, m, length, width, height):
    """Belt conveyor. Origin: centre of the belt top. Travel direction +X.

    height is the belt top above the floor. Objects: Belt (UV runs along the travel
    direction, one repeat per BELT_STRIPE_PERIOD metres), Roller_Head, Roller_Tail,
    Conveyor_Static.
    """
    r = BELT_ROLLER_RADIUS
    half = length / 2
    belt_width = width * 0.96

    def build_belt(bm):
        # Loop around both end rollers, starting underneath so the UV seam is out of sight.
        path = [(half - r, -2 * r), (-half + r, -2 * r)]
        steps = 8
        for i in range(1, steps + 1):          # tail roller, bottom -> top
            a = math.radians(-90 - 180 * i / steps)
            path.append((-half + r + r * math.cos(a), -r + r * math.sin(a)))
        path.append((half - r, 0.0))
        for i in range(1, steps + 1):          # head roller, top -> bottom
            a = math.radians(90 - 180 * i / steps)
            path.append((half - r + r * math.cos(a), -r + r * math.sin(a)))

        uv_layer = bm.loops.layers.uv.new("UVMap")
        distance = 0.0
        for i in range(len(path) - 1):
            (x0, z0), (x1, z1) = path[i], path[i + 1]
            step = math.hypot(x1 - x0, z1 - z0)
            # Winding chosen so the normal points away from the rollers.
            corners = [(x0, -belt_width / 2, z0), (x1, -belt_width / 2, z1),
                       (x1, belt_width / 2, z1), (x0, belt_width / 2, z0)]
            us = [distance, distance + step, distance + step, distance]
            vs = [0.0, 0.0, 1.0, 1.0]
            face = bm.faces.new([bm.verts.new(c) for c in corners])
            face.smooth = True
            for loop, u, v in zip(face.loops, us, vs):
                loop[uv_layer].uv = (u / BELT_STRIPE_PERIOD, v)
            distance += step
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    make_object(collection, "Belt", build_belt, [m["belt"]], bevel=0.0)

    def build_roller(bm):
        add_cylinder(bm, r - 0.004, belt_width, (0, 0, 0), "Y", 24, 0)
        for side in (-1, 1):
            y = side * (belt_width / 2 + 0.012)
            add_cylinder(bm, 0.03, 0.024, (0, y, 0), "Y", 16, 1)     # bearing cap
            add_box(bm, (0.016, 0.012, 0.016), (0.018, y + side * 0.012, 0), 0)   # off-centre key: shows rotation

    for name, x in (("Roller_Head", half - r), ("Roller_Tail", -half + r)):
        make_object(collection, name, build_roller, [m["steel"], m["frame_dark"]], location=(x, 0, -r), bevel=0.0)

    legs_x = [-(half - 0.35) + i * (length - 0.7) / (max(2, int(length // 2) + 1) - 1)
              for i in range(max(2, int(length // 2) + 1))]
    beam_y = width / 2 + 0.015

    def build_static(bm):
        for side in (-1, 1):
            # Side beam, top just below the belt surface so nothing sticks up into the box path.
            add_box(bm, (length - 0.08, 0.03, 0.12), (0, side * beam_y, -0.065), 0)
            # Lower stretcher between the first and last leg.
            add_box(bm, (legs_x[-1] - legs_x[0], 0.03, 0.04), ((legs_x[0] + legs_x[-1]) / 2, side * beam_y, -height + 0.16), 0)
            for x in legs_x:
                leg = height - 0.125
                add_box(bm, (0.05, 0.05, leg), (x, side * beam_y, -0.125 - leg / 2), 0)
                add_box(bm, (0.11, 0.11, 0.012), (x, side * beam_y, -height + 0.006), 1)
        # Slider bed inside the belt loop.
        add_box(bm, (length - 2 * r - 0.04, width - 0.02, 0.03), (0, 0, -r), 1)
        for x in legs_x:
            add_box(bm, (0.04, width + 0.03, 0.04), (x, 0, -height * 0.55), 0)
        # Drive at the head end, on the +Y side: gearbox, motor and a yellow guard over the shaft.
        drive_x = half - 0.30
        drive_y = beam_y + 0.015 + 0.075
        add_box(bm, (0.16, 0.15, 0.16), (drive_x, drive_y, -0.15), 1)
        add_cylinder(bm, 0.06, 0.24, (drive_x - 0.20, drive_y, -0.15), "X", 20, 1)
        add_box(bm, (0.30, 0.025, 0.15), (half - 0.16, beam_y + 0.03, -r), 2)

    make_object(collection, "Conveyor_Static", build_static, [m["frame_grey"], m["frame_dark"], m["yellow"]])


def build_guide_rail(collection, m):
    """1 m of guide rail along X, scaled in length by the app. Origin: centre of the rail collider."""

    def build(bm):
        add_box(bm, (1.0, 0.016, 0.06), (0, 0, -0.005), 0)
        add_cylinder(bm, 0.012, 1.0, (0, 0, 0.028), "X", 12, 0)

    make_object(collection, "Rail", build, [m["steel"]], bevel=0.0)


def build_rail_bracket(collection, m):
    """Bracket that carries a guide rail from the conveyor side beam. Outer side is +Y."""

    def build(bm):
        add_box(bm, (0.022, 0.022, 0.21), (0, 0.021, -0.075), 0)
        add_box(bm, (0.04, 0.03, 0.035), (0, 0.012, 0.0), 1)
        add_box(bm, (0.06, 0.008, 0.06), (0, 0.034, -0.14), 1)

    make_object(collection, "Bracket", build, [m["frame_grey"], m["frame_dark"]], bevel=0.002)


def build_support_beam(collection, m):
    """Square beam of 1 m standing on its origin; the app scales it to length for support frames."""

    def build(bm):
        add_box(bm, (0.06, 0.06, 1.0), (0, 0, 0.5), 0)

    make_object(collection, "Beam", build, [m["frame_grey"]], bevel=0.0)


def build_pusher(collection, m):
    """Pneumatic pusher. Origin: centre of the plate when retracted. Stroke direction -Y.

    PusherPlate and PusherRod move together; PusherBody stays where it is.
    """
    px, py, pz = PUSHER_PLATE
    wx, wz, w = SENSOR_OFFSET_X, SENSOR_LOW_Z, 0.05     # window the low photo-eye looks through

    def plate(bm, y0, y1, mat):
        depth, cy = y1 - y0, (y0 + y1) / 2
        left = wx - w / 2 + px / 2
        right = px / 2 - (wx + w / 2)
        add_box(bm, (left, depth, pz), (-px / 2 + left / 2, cy, 0), mat)
        add_box(bm, (right, depth, pz), (px / 2 - right / 2, cy, 0), mat)
        below = (wz - w / 2) + pz / 2
        above = pz / 2 - (wz + w / 2)
        add_box(bm, (w, depth, below), (wx, cy, -pz / 2 + below / 2), mat)
        add_box(bm, (w, depth, above), (wx, cy, pz / 2 - above / 2), mat)

    def build_plate(bm):
        plate(bm, -py / 2 + 0.015, py / 2, 0)           # yellow plate
        plate(bm, -py / 2, -py / 2 + 0.015, 1)          # rubber face, flush with the collider
        add_box(bm, (0.12, 0.035, 0.12), (0, py / 2 + 0.0175, 0), 2)   # rod eye

    make_object(collection, "PusherPlate", build_plate, [m["yellow"], m["black"], m["frame_dark"]], bevel=0.003)

    def build_rod(bm):
        add_cylinder(bm, 0.018, 0.98, (0, 0.56, 0), "Y", 16, 0)

    make_object(collection, "PusherRod", build_rod, [m["steel"]], bevel=0.0)

    floor = -PUSHER_CENTRE_ABOVE_FLOOR

    def build_body(bm):
        add_cylinder(bm, 0.045, 0.96, (0, 0.62, 0), "Y", 20, 0)
        for y in (0.14, 1.10):
            add_box(bm, (0.11, 0.03, 0.11), (0, y, 0), 1)
        for y in (0.30, 0.95):
            post = -0.055 - floor
            add_box(bm, (0.12, 0.04, 0.03), (0, y, -0.055), 1)
            add_box(bm, (0.05, 0.05, post), (0, y, -0.055 - post / 2), 0)
            add_box(bm, (0.14, 0.14, 0.012), (0, y, floor + 0.006), 1)
        add_box(bm, (0.03, 0.65, 0.04), (0, 0.625, floor + 0.20), 0)

    make_object(collection, "PusherBody", build_body, [m["frame_grey"], m["frame_dark"]])


def build_photo_eye(collection, m):
    """Photo-eye head. Origin: the lens, where the beam starts. Looks along -Y."""

    def build_static(bm):
        add_box(bm, (0.045, 0.07, 0.045), (0, 0.037, 0), 0)
        add_cylinder(bm, 0.013, 0.006, (0, 0.0, 0), "Y", 16, 1)
        add_box(bm, (0.014, 0.03, 0.06), (0, 0.086, 0), 2)
        add_cylinder(bm, 0.006, 0.022, (0, 0.055, -0.032), "Z", 10, 0)

    make_object(collection, "Sensor_Static", build_static, [m["black"], m["lens"], m["frame_grey"]], bevel=0.002)

    def build_led(bm):
        add_box(bm, (0.016, 0.016, 0.008), (0, 0, 0), 0)

    make_object(collection, "SensorLed", build_led, [m["led"]], location=(0, 0.05, 0.0265), bevel=0.0)

    def build_beam(bm):
        add_box(bm, (0.005, 1.0, 0.005), (0, -0.5, 0), 0)      # 1 m long; the app scales it to the range

    make_object(collection, "SensorBeam", build_beam, [m["beam"]], bevel=0.0)


def build_sensor_post(collection, m):
    """Post for the two photo-eyes. Origin: on the floor."""

    def build(bm):
        add_box(bm, (0.03, 0.03, 1.25), (0, 0, 0.625), 0)
        add_box(bm, (0.14, 0.14, 0.012), (0, 0, 0.006), 1)
        add_box(bm, (0.036, 0.036, 0.01), (0, 0, 1.255), 1)

    make_object(collection, "Post", build, [m["frame_grey"], m["frame_dark"]], bevel=0.002)


def build_emitter_gantry(collection, m):
    """Portal with a hopper over the belt where boxes appear. Origin: on the belt, under the hopper."""
    floor = -0.80

    def build_static(bm):
        for x in (-0.32, 0.32):
            for y in (-0.56, 0.56):
                add_box(bm, (0.05, 0.05, 0.76 - floor), (x, y, (0.76 + floor) / 2), 0)
                add_box(bm, (0.12, 0.12, 0.012), (x, y, floor + 0.006), 1)
        add_box(bm, (0.74, 1.22, 0.32), (0, 0, 0.92), 2)            # hopper
        add_box(bm, (0.75, 1.23, 0.05), (0, 0, 0.785), 3)           # safety band
        add_box(bm, (0.46, 0.46, 0.02), (0, 0, 0.752), 4)           # dark outlet underneath
        add_box(bm, (0.10, 0.10, 0.02), (0.25, 0.45, 1.09), 1)      # lamp base

    make_object(collection, "Emitter_Static", build_static,
                [m["frame_grey"], m["frame_dark"], m["panel"], m["yellow"], m["black"]])

    def build_lamp(bm):
        add_cylinder(bm, 0.04, 0.07, (0, 0, 0), "Z", 16, 0)

    make_object(collection, "EmitterLamp", build_lamp, [m["lamp"]], location=(0.25, 0.45, 1.135), bevel=0.0)


def build_remover_hood(collection, m, inner_width):
    """Discharge cabinet with a tunnel over the last part of the belt.

    Origin: centre of the remover volume (0.6 m deep), level with the belt top. The entrance
    faces -X. Boxes are removed when they reach x = -0.3; the tunnel and its strip curtain
    reach 0.42 m further out, so that happens out of sight.
    """
    floor = -0.80
    top = 0.55
    wall_y = inner_width / 2 + 0.015
    outer = inner_width + 0.06
    tunnel = 0.42

    def build(bm):
        for side in (-1, 1):
            add_box(bm, (0.6, 0.03, top - floor), (0, side * wall_y, (top + floor) / 2), 0)
            # Tunnel side walls start above the guide rails.
            add_box(bm, (tunnel, 0.03, top - 0.10), (-0.3 - tunnel / 2, side * wall_y, (top + 0.10) / 2), 0)
        add_box(bm, (0.6 + tunnel, outer, 0.03), (-tunnel / 2, 0, top + 0.015), 0)       # roof
        add_box(bm, (0.03, outer, top - floor), (0.285, 0, (top + floor) / 2), 0)        # back
        add_box(bm, (0.03, inner_width, 0.55), (-0.285, 0, floor + 0.275), 0)            # front, below the belt
        add_box(bm, (0.02, outer + 0.01, 0.07), (-0.3 - tunnel - 0.005, 0, top - 0.02), 1)   # hazard band
        # Strip curtain across the tunnel mouth.
        strips = max(4, int(round(inner_width / 0.11)))
        pitch = inner_width / strips
        for i in range(strips):
            y = -inner_width / 2 + pitch * (i + 0.5)
            add_box(bm, (0.004, pitch - 0.012, top - 0.07 - 0.015), (-0.3 - tunnel + 0.01, y, (top - 0.07 + 0.015) / 2), 2)

    make_object(collection, "Hood", build, [m["panel"], m["yellow"], m["black"]], bevel=0.003)


def build_floor(collection, m):
    """Concrete floor slab with a yellow safety line around the line. Origin: floor surface, centre."""

    def build_slab(bm):
        verts = add_box(bm, (14.0, 10.0, 0.1), (0, 0, -0.05), 0)
        uv_layer = bm.loops.layers.uv.verify()
        for face in {f for v in verts for f in v.link_faces}:
            for loop in face.loops:
                loop[uv_layer].uv = (loop.vert.co.x / 2.0, loop.vert.co.y / 2.0)     # texture repeats every 2 m

    make_object(collection, "Slab", build_slab, [m["concrete"]], bevel=0.0)

    x0, x1, y0, y1, line = -3.7, 4.3, -3.4, 2.3, 0.08

    def build_lines(bm):
        for y in (y0, y1):
            add_box(bm, (x1 - x0 + line, line, 0.002), ((x0 + x1) / 2, y, 0.001), 0)
        for x in (x0, x1):
            add_box(bm, (line, y1 - y0 - line, 0.002), (x, (y0 + y1) / 2, 0.001), 0)

    make_object(collection, "SafetyLines", build_lines, [m["yellow"]], bevel=0.0)


ASSETS = [
    ("box_low", lambda c, m: build_box(c, m, BOX_LOW_HEIGHT, dark=False)),
    ("box_high", lambda c, m: build_box(c, m, BOX_HIGH_HEIGHT, dark=True)),
    ("conveyor_straight_6m", lambda c, m: build_conveyor(c, m, 6.0, 0.8, 0.80)),
    ("conveyor_straight_1m6_wide", lambda c, m: build_conveyor(c, m, 1.6, 1.2, 0.79)),
    ("guide_rail_1m", build_guide_rail),
    ("rail_bracket", build_rail_bracket),
    ("pusher", build_pusher),
    ("photo_eye", build_photo_eye),
    ("sensor_post", build_sensor_post),
    ("emitter_gantry", build_emitter_gantry),
    ("remover_hood_1m0", lambda c, m: build_remover_hood(c, m, 1.0)),
    ("remover_hood_1m4", lambda c, m: build_remover_hood(c, m, 1.4)),
    ("floor_14x10", build_floor),
    ("support_beam", build_support_beam),
]


# -------------------------------------------------------------------------- export


def triangle_count(collection):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    total = 0
    for obj in collection.objects:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        mesh.calc_loop_triangles()
        total += len(mesh.loop_triangles)
        evaluated.to_mesh_clear()
    return total


def export_asset(scene, collection, name, category=None):
    """Exports one collection as <name>.glb, with the short part names the app looks up."""
    all_objects = [o for o in scene.objects if "fs_part" in o]
    # Part names such as "Belt" occur in several assets, and Blender names must be unique:
    # park everything under a long name, then give only this asset its short names.
    for obj in all_objects:
        obj.name = "{}::{}".format(obj.users_collection[0].name, obj["fs_part"])
    for obj in collection.objects:
        obj.name = obj["fs_part"]

    for obj in scene.objects:
        obj.select_set(False)
    for obj in collection.objects:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = collection.objects[0]

    # Library assets go into a folder per category; the first MVP models keep their flat folder.
    out_dir = os.path.join(LIBRARY_DIR, category) if category else MODELS_DIR
    engine_dir = os.path.join(ENGINE_LIBRARY_DIR, category) if category else ENGINE_DIR
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name + ".glb")
    # get_scene() guarantees this scene is the active one, so the selection the exporter
    # reads is ours and nothing from another scene can end up in the file.
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=True,          # bake the bevel modifiers
        export_yup=True,            # Blender Z-up -> glTF/Godot Y-up
        export_animations=False,
        export_cameras=False,
        export_lights=False,
        export_extras=False,
    )

    for obj in all_objects:
        obj.name = "{}::{}".format(obj.users_collection[0].name, obj["fs_part"])

    os.makedirs(engine_dir, exist_ok=True)
    shutil.copyfile(path, os.path.join(engine_dir, name + ".glb"))
    return os.path.getsize(path)



def save_source(scene):
    """Writes only this script's scene to assets/source/, not the rest of the open file.

    Thumbnails and contact sheets leave an absolute output path in scene.render.filepath;
    it is reset to the relative "//" first, so that output path does not end up in the file."""
    os.makedirs(SOURCE_DIR, exist_ok=True)
    path = os.path.join(SOURCE_DIR, "factorysim_assets.blend")
    # The path is a fixed buffer that goes into the file whole, and a shorter value only
    # overwrites its start: fill the buffer completely first, so nothing of the old path remains.
    render = scene.render
    render.filepath = " " * (render.bl_rna.properties["filepath"].length_max - 1)
    render.filepath = "//"
    if os.path.normcase(os.path.abspath(bpy.data.filepath or "")) == os.path.normcase(os.path.abspath(path)):
        bpy.ops.wm.save_mainfile(compress=True)     # the source file itself is open
    else:
        bpy.data.libraries.write(path, {scene}, fake_user=True, compress=True)
    return os.path.getsize(path)


def build_all(only=None):
    scene = get_scene()
    materials = build_materials()
    report = {}
    for name, builder in ASSETS:
        if only and name not in only:
            continue
        collection = fresh_collection(scene, name)
        builder(collection, materials)
        bpy.context.view_layer.update()
        triangles = triangle_count(collection)
        size = export_asset(scene, collection, name)
        report[name] = {"triangles": triangles, "bytes": size,
                        "objects": sorted(o["fs_part"] for o in collection.objects)}
    report["_source_bytes"] = save_source(scene)
    return report


# ----------------------------------------------------------------------- thumbnails


def frame_asset(name):
    """Shows only FS_<name> to the renderer and points the thumbnail camera at it."""
    scene = get_scene()
    target = bpy.data.collections[PREFIX + name]
    for obj in scene.objects:
        if "fs_part" in obj:
            obj.hide_render = obj.name not in target.objects

    corners = [obj.matrix_world @ Vector(c) for obj in target.objects for c in obj.bound_box]
    low = Vector([min(c[i] for c in corners) for i in range(3)])
    high = Vector([max(c[i] for c in corners) for i in range(3)])
    centre = (low + high) / 2
    radius = max((high - low).length / 2, 0.05)

    camera = bpy.data.objects.get("FS_ThumbCamera")
    if camera is None:
        camera = bpy.data.objects.new("FS_ThumbCamera", bpy.data.cameras.new("FS_ThumbCamera"))
        scene.collection.objects.link(camera)
    camera.data.lens = 60
    camera.data.clip_start = 0.01
    direction = Vector((0.9, -1.3, 0.75)).normalized()      # from the front-right, slightly above
    distance = radius / math.tan(camera.data.angle / 2) * 1.15
    camera.location = centre + direction * distance
    camera.rotation_euler = (centre - camera.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = camera

    sun = bpy.data.objects.get("FS_ThumbSun")
    if sun is None:
        sun = bpy.data.objects.new("FS_ThumbSun", bpy.data.lights.new("FS_ThumbSun", "SUN"))
        scene.collection.objects.link(sun)
        sun.data.energy = 3.0
        sun.rotation_euler = (math.radians(50), 0, math.radians(35))

    world = bpy.data.worlds.get("FS_ThumbWorld") or bpy.data.worlds.new("FS_ThumbWorld")
    if hasattr(world, "use_nodes") and world.node_tree is None:
        world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs["Color"].default_value = (0.32, 0.34, 0.37, 1.0)
        background.inputs["Strength"].default_value = 1.2
    scene.world = world
    bpy.context.view_layer.update()
    return {"asset": name, "radius": round(radius, 3)}


def render_thumbnail(name, size=512, path=None):
    """Renders docs/assets-preview/<name>.png. Used when the script runs in the background;
    in an interactive session the Blender MCP tool render_thumbnail_to_path does the same."""
    frame_asset(name)
    scene = get_scene()
    path = path or os.path.join(PREVIEW_DIR, name + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    render = scene.render
    render.resolution_x = size
    render.resolution_y = size
    render.resolution_percentage = 100
    render.image_settings.file_format = "PNG"
    render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def restore_previous_scene():
    """Puts the window back on the scene that was active before this script ran."""
    scene = get_scene()
    previous = bpy.data.scenes.get(scene.get("fs_previous_scene", ""))
    if previous is not None and bpy.context.window is not None:
        bpy.context.window.scene = previous
        return previous.name
    return None


if __name__ == "__main__":
    import json

    summary = build_all()
    thumbnails = "--no-thumbnails" not in os.sys.argv
    if thumbnails:
        for asset_name, _builder in ASSETS:
            render_thumbnail(asset_name)
        for obj in get_scene().objects:
            obj.hide_render = False
        save_source(get_scene())
    print("FACTORYSIM_ASSETS " + json.dumps(summary, sort_keys=True))
