"""Building kit shared by all library assets.

Everything in the library is assembled from the same few pieces (frames, legs, belts,
rollers, posts, cabinets) and the same palette, which is what keeps the assets consistent
and makes them fit together: belts are 0.8 m wide with the top at 0.8 m, roller conveyors
are 1.3 m wide with the top at 0.5 m, and lengths come in the same 2 / 4 / 6 m steps.

Axes while modelling (see docs/ASSETS.md): +X travel direction, +Z up, -Y is Godot's +Z.
"""

import math
from collections import namedtuple

import bmesh
from mathutils import Matrix, Vector

from . import base as b

# Fixed material slots: every object gets the whole palette, so a builder can refer to a
# material by index. Slots an object does not use are not exported.
(GREY, DARK, STEEL, PANEL, YELLOW, BLACK, RED, GREEN, BLUE, BELT, LENS, WHITE, ORANGE, WOOD,
 GLASS, CARD, CARD_DARK, TAPE) = range(18)

_PALETTE_KEYS = ("frame_grey", "frame_dark", "steel", "panel", "yellow", "black", "red", "green",
                 "blue", "belt", "lens", "white", "orange", "wood", "glass", "cardboard",
                 "cardboard_dark", "tape")

# Shared module sizes.
BELT_WIDTH = 0.8
BELT_TOP = 0.80
ROLLER_WIDTH = 1.3
ROLLER_TOP = 0.50
ROLLER_RADIUS = 0.046
LENGTHS = (2.0, 4.0, 6.0)

Asset = namedtuple("Asset", "id category name build behaviour origin_height params emits colliders")


def asset(id, category, name, build, behaviour=None, origin_height=0.0, **params):
    """One entry of the library.

    behaviour:      which simulator part drives it ("conveyor", "pusher", ...), or None when
                    the asset is visual only.
    origin_height:  height of the model origin above the floor when placed in a level.
    emits:          (keyword) for an emitter, the id of the library item it emits.
    colliders:      (keyword) for a passive part, the list of collider() boxes the app builds for it.
    """
    emits = params.pop("emits", None)
    colliders = params.pop("colliders", None)
    return Asset(id, category, name, build, behaviour, origin_height, params, emits, colliders)


def collider(size, position, yaw=0.0, pitch=0.0, friction=0.5):
    """One box collider of a passive part, in the frame the app uses (Godot): x along the travel,
    y up, z across, origin where the model has it. NOT Blender axes: a point at Blender (x, y, z) is
    (x, z, -y) here. yaw (degrees) turns +X towards -Z about the vertical axis; pitch lowers the +X
    end. A box is turned about one of them only."""
    assert yaw == 0.0 or pitch == 0.0, "turn a collider about one axis only"
    return dict(size=[round(v, 4) for v in size], position=[round(v, 4) for v in position],
                yaw=round(yaw, 4), pitch=round(pitch, 4), friction=friction)


def extend_materials(m):
    """Adds the library colours and lamp materials to the base palette."""
    m["red"] = b.material("FS_Red", b.srgb("#C62828"), 0.0, 0.5)
    m["green"] = b.material("FS_Green", b.srgb("#2E9E4F"), 0.0, 0.5)
    m["blue"] = b.material("FS_Blue", b.srgb("#1F6FB5"), 0.0, 0.5)
    m["white"] = b.material("FS_White", b.srgb("#E8EAEC"), 0.0, 0.6)
    m["orange"] = b.material("FS_Orange", b.srgb("#E8772E"), 0.0, 0.5)
    m["wood"] = b.material("FS_Wood", b.srgb("#B58B58"), 0.0, 0.85)
    m["glass"] = b.material("FS_Glass", b.srgb("#9CC4D8"), 0.0, 0.1, alpha=0.3)
    for key, name, colour in (("lamp_red", "FS_LampRed", "#FF2A1A"), ("lamp_yellow", "FS_LampYellow", "#FFC21A"),
                              ("lamp_green", "FS_LampGreen", "#35F06A"), ("lamp_blue", "FS_LampBlue", "#2A8CFF"),
                              ("lamp_white", "FS_LampWhite", "#FFFFFF")):
        dark = b.srgb(colour)
        dark = (dark[0] * 0.18, dark[1] * 0.18, dark[2] * 0.18, 1.0)
        m[key] = b.material(name, dark, 0.0, 0.35, emission=b.srgb(colour), emission_strength=0.0)
    return m


def obj(collection, m, name, build, location=(0.0, 0.0, 0.0), bevel=0.004):
    """Mesh object with the whole palette; build(bm) uses the slot constants above."""
    return b.make_object(collection, name, build, [m[k] for k in _PALETTE_KEYS], location, bevel)


def lamp(collection, m, name, colour, build, location=(0.0, 0.0, 0.0)):
    """Separate object with an emissive material the app can switch on."""
    return b.make_object(collection, name, build, [m["lamp_" + colour]], location, 0.0)


box = b.add_box
cyl = b.add_cylinder


def rotate(bm, verts, angle_degrees, axis, centre=(0.0, 0.0, 0.0)):
    bmesh.ops.rotate(bm, cent=Vector(centre), matrix=Matrix.Rotation(math.radians(angle_degrees), 3, axis), verts=verts)


def frustum(bm, r0, r1, a0, a1, axis="Z", centre=(0.0, 0.0, 0.0), segments=20, mat=0):
    """Truncated cone along axis, radius r0 at coordinate a0 and r1 at a1 on that axis; centre gives the
    other two coordinates (its value on the axis is ignored). Smooth side, flat caps, like cyl()."""
    if a0 > a1:
        a0, a1, r0, r1 = a1, a0, r1, r0
    verts = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=r0, radius2=r1,
                                  depth=a1 - a0, calc_uvs=True)["verts"]     # radius1 at -z, radius2 at +z
    if axis == "X":
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, "Y"), verts=verts)
    elif axis == "Y":
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(-90), 3, "X"), verts=verts)
    middle = list(centre)
    middle["XYZ".index(axis)] = (a0 + a1) / 2
    bmesh.ops.translate(bm, vec=Vector(middle), verts=verts)
    for face in {f for v in verts for f in v.link_faces}:
        face.material_index = mat
        face.smooth = len(face.verts) == 4
    return verts


def circle(u, v, radius, segments=24):
    """Points on a circle around (u, v), for the outline of a slab()."""
    return [(u + radius * math.cos(2 * math.pi * i / segments), v + radius * math.sin(2 * math.pi * i / segments))
            for i in range(segments)]


def _hull(points):
    """Convex hull of 2D points, counter-clockwise (monotone chain)."""
    pts = sorted(set((round(u, 6), round(v, 6)) for u, v in points))
    if len(pts) < 3:
        return pts

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 1e-12:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 1e-12:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def slab(bm, outline, axis, a0, a1, mat=0, sharp=35.0):
    """Plate along axis from a0 to a1 whose cross-section is the convex hull of outline: 2D points in
    the plane across the axis, (y, z) for X, (x, z) for Y, (x, y) for Z. The hull of a few circle()s
    and corner points gives rounded link shapes. The side is smooth, except at corners sharper than
    `sharp` degrees; the caps are flat."""
    ring = _hull(outline)
    if a0 > a1:
        a0, a1 = a1, a0

    def point(u, v, w):
        return {"X": (w, u, v), "Y": (u, w, v), "Z": (u, v, w)}[axis]

    low = [bm.verts.new(point(u, v, a0)) for u, v in ring]
    high = [bm.verts.new(point(u, v, a1)) for u, v in ring]
    count = len(ring)
    caps = [bm.faces.new(low[::-1]), bm.faces.new(high)]
    sides = [bm.faces.new((low[i], low[(i + 1) % count], high[(i + 1) % count], high[i])) for i in range(count)]
    for face in caps + sides:
        face.material_index = mat
    for face in sides:
        face.smooth = True
    for i in range(count):            # the side edge at corner i, between sides i-1 and i
        (u0, v0), (u1, v1), (u2, v2) = ring[i - 1], ring[i], ring[(i + 1) % count]
        turn = math.degrees(abs(math.atan2(u1 - u0, v1 - v0) - math.atan2(u2 - u1, v2 - v1))) % 360.0
        if min(turn, 360.0 - turn) > sharp:
            edge = bm.edges.get((low[i], high[i]))
            if edge is not None:
                edge.smooth = False
    bmesh.ops.recalc_face_normals(bm, faces=caps + sides)
    return low + high


# ----------------------------------------------------------------------------- pieces


def leg(bm, x, y, top, floor, size=0.05, mat=GREY):
    """Square leg from top down to the floor, with a foot plate."""
    box(bm, (size, size, top - floor), (x, y, (top + floor) / 2), mat)
    box(bm, (size * 2.2, size * 2.2, 0.012), (x, y, floor + 0.006), DARK)


def frame(bm, length, width, height, beam=0.12, inset=0.08):
    """Conveyor frame: two side beams just below the transport surface (z = 0), legs to the
    floor at z = -height, cross ties and lower stretchers."""
    count = max(2, int(length // 2) + 1)
    xs = [-(length / 2 - 0.35) + i * (length - 0.7) / (count - 1) for i in range(count)]
    y = width / 2 + 0.015
    for side in (-1, 1):
        box(bm, (length - inset, 0.03, beam), (0, side * y, -0.005 - beam / 2), GREY)
        if height > 0.35:
            box(bm, (xs[-1] - xs[0], 0.03, 0.04), ((xs[0] + xs[-1]) / 2, side * y, -height + 0.16), GREY)
        for x in xs:
            leg(bm, x, side * y, -0.005 - beam, -height)
    for x in xs:
        box(bm, (0.04, width + 0.03, 0.04), (x, 0, -height * 0.55), GREY)
    return xs


def drive(bm, length, width, z=-0.15):
    """Gear motor at the head end on the +Y side, with a yellow guard."""
    y = width / 2 + 0.03 + 0.075
    x = length / 2 - 0.30
    box(bm, (0.16, 0.15, 0.16), (x, y, z), DARK)
    cyl(bm, 0.06, 0.24, (x - 0.20, y, z), "X", 20, DARK)
    box(bm, (0.30, 0.025, 0.15), (length / 2 - 0.16, width / 2 + 0.045, -0.06), YELLOW)


def belt_loop(bm, length, width, r=b.BELT_ROLLER_RADIUS):
    """Endless belt around two end rollers, top at z = 0. UV runs along the travel
    direction with one repeat per BELT_STRIPE_PERIOD metres, seam underneath."""
    half = length / 2
    path = [(half - r, -2 * r), (-half + r, -2 * r)]
    steps = 8
    for i in range(1, steps + 1):
        a = math.radians(-90 - 180 * i / steps)
        path.append((-half + r + r * math.cos(a), -r + r * math.sin(a)))
    path.append((half - r, 0.0))
    for i in range(1, steps + 1):
        a = math.radians(90 - 180 * i / steps)
        path.append((half - r + r * math.cos(a), -r + r * math.sin(a)))
    uv_layer = bm.loops.layers.uv.verify()
    distance = 0.0
    for i in range(len(path) - 1):
        (x0, z0), (x1, z1) = path[i], path[i + 1]
        step = math.hypot(x1 - x0, z1 - z0)
        corners = [(x0, -width / 2, z0), (x1, -width / 2, z1), (x1, width / 2, z1), (x0, width / 2, z0)]
        face = bm.faces.new([bm.verts.new(c) for c in corners])
        face.smooth = True
        face.material_index = BELT
        for loop, u, v in zip(face.loops, (distance, distance + step, distance + step, distance), (0, 0, 1, 1)):
            loop[uv_layer].uv = (u / b.BELT_STRIPE_PERIOD, v)
        distance += step
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


def belt_parts(collection, m, length, width, angle=0.0):
    """The moving parts of a belt conveyor: Belt, Roller_Head, Roller_Tail.

    For a belt that climbs, length is the length along the surface and angle (degrees) the slope
    towards +X; the belt top then passes through the origin, as it does for a flat belt."""
    r = b.BELT_ROLLER_RADIUS
    belt_width = width * 0.96

    def belt(bm):
        belt_loop(bm, length, belt_width)
        if angle:
            rotate(bm, list(bm.verts), -angle, "Y")

    obj(collection, m, "Belt", belt, bevel=0.0)

    def roller(bm):
        cyl(bm, r - 0.004, belt_width, (0, 0, 0), "Y", 24, STEEL)
        for side in (-1, 1):
            y = side * (belt_width / 2 + 0.012)
            cyl(bm, 0.03, 0.024, (0, y, 0), "Y", 16, DARK)
            box(bm, (0.016, 0.012, 0.016), (0.018, y + side * 0.012, 0), STEEL)

    # A roller sits one radius below the surface, measured along the normal of the slope.
    sin, cos = math.sin(math.radians(angle)), math.cos(math.radians(angle))
    for name, t in (("Roller_Head", length / 2 - r), ("Roller_Tail", -length / 2 + r)):
        obj(collection, m, name, roller, location=(t * cos + r * sin, 0, t * sin - r * cos), bevel=0.0)


def roller_parts(collection, m, length, width, pitch=0.125, radius=ROLLER_RADIUS):
    """A bed of driven rollers, tops at z = 0; each roller is its own object (Roller_NN)
    so the app can spin it."""
    count = max(2, int(round((length - 0.1) / pitch)))
    start = -(count - 1) * pitch / 2

    def roller(bm):
        cyl(bm, radius, width * 0.94, (0, 0, 0), "Y", 14, STEEL)
        box(bm, (0.012, 0.01, 0.012), (radius * 0.6, width * 0.47 + 0.005, 0), DARK)

    for i in range(count):
        obj(collection, m, "Roller_{:02d}".format(i), roller, location=(start + i * pitch, 0, -radius), bevel=0.0)


def arc_strip(bm, r_inner, r_outer, angle_degrees, z, mat, segments=12, thickness=0.0):
    """Flat ring sector around the origin, from angle 0 towards +angle (counter-clockwise seen
    from above), top face at z. With thickness it becomes a solid slab."""
    verts = []
    for i in range(segments + 1):
        a = math.radians(angle_degrees * i / segments)
        verts.append((bm.verts.new((r_inner * math.cos(a), r_inner * math.sin(a), z)),
                      bm.verts.new((r_outer * math.cos(a), r_outer * math.sin(a), z))))
    faces = []
    for i in range(segments):
        (a0, b0), (a1, b1) = verts[i], verts[i + 1]
        face = bm.faces.new((a0, b0, b1, a1))
        face.material_index = mat
        faces.append(face)
    if thickness:
        extruded = bmesh.ops.extrude_face_region(bm, geom=faces)
        moved = [e for e in extruded["geom"] if isinstance(e, bmesh.types.BMVert)]
        bmesh.ops.translate(bm, vec=(0, 0, -thickness), verts=moved)
        for face in bm.faces:
            if face.material_index == 0 and mat != 0 and all(v in moved or any(v in pair for pair in verts) for v in face.verts):
                face.material_index = mat
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


def cabinet(bm, size, centre, mat=PANEL, plinth=0.08):
    """Sheet-metal cabinet with a dark plinth."""
    sx, sy, sz = size
    cx, cy, cz = centre
    box(bm, (sx, sy, sz - plinth), (cx, cy, cz + plinth / 2), mat)
    box(bm, (sx - 0.04, sy - 0.04, plinth), (cx, cy, cz - sz / 2 + plinth / 2), DARK)


def tube_frame(bm, x0, x1, y0, y1, z0, z1, size=0.06, mat=GREY, feet=True):
    """Box frame from square tube: four posts and top and bottom rectangles."""
    for x in (x0, x1):
        for y in (y0, y1):
            box(bm, (size, size, z1 - z0), (x, y, (z0 + z1) / 2), mat)
            if feet:
                box(bm, (size * 2, size * 2, 0.012), (x, y, z0 + 0.006), DARK)
    for z in (z1 - size / 2,):
        for y in (y0, y1):
            box(bm, (x1 - x0, size, size), ((x0 + x1) / 2, y, z), mat)
        for x in (x0, x1):
            box(bm, (size, y1 - y0, size), (x, (y0 + y1) / 2, z), mat)
