"""Library assets: items, light-load parts and heavy-load parts.

Own designs: no third-party model, texture or drawing was used.
"""

import math

from . import base as b
from .kit import (BELT, BELT_TOP, BELT_WIDTH, BLACK, BLUE, CARD, CARD_DARK, DARK, GREEN, GREY, LENGTHS, ORANGE,
                  PANEL, RED, ROLLER_RADIUS, ROLLER_TOP, ROLLER_WIDTH, STEEL, TAPE, WHITE, WOOD, YELLOW, arc_strip, asset,
                  belt_parts, box, collider, cyl, drive, frame, leg, obj, roller_parts, rotate)

# ------------------------------------------------------------------------------- items


def _carton(size, dark=False):
    sx, sy, sz = size

    def build(c, m):
        def mesh(bm):
            box(bm, (sx, sy, sz), (0, 0, 0), CARD_DARK if dark else CARD)
            box(bm, (sx + 0.002, 0.05, 0.002), (0, 0, sz / 2), TAPE)
            for side in (-1, 1):
                box(bm, (0.002, 0.05, sz * 0.4), (side * sx / 2, 0, sz / 2 - sz * 0.2), TAPE)

        obj(c, m, "Box", mesh, bevel=0.005)

    return build


def _pallet(length, width):
    def build(c, m):
        def mesh(bm):
            h = 0.144
            for i in range(5):                                   # top deck
                y = -width / 2 + 0.05 + i * (width - 0.1) / 4
                box(bm, (length, 0.1, 0.022), (0, y, h / 2 - 0.011), WOOD)
            for x in (-length / 2 + 0.07, 0, length / 2 - 0.07):  # blocks and bottom boards
                for y in (-width / 2 + 0.05, 0, width / 2 - 0.05):
                    box(bm, (0.14, 0.1, 0.078), (x, y, -0.011), WOOD)
                box(bm, (0.14, width, 0.022), (x, 0, -h / 2 + 0.011), WOOD)

        obj(c, m, "Pallet", mesh, bevel=0.003)

    return build


def _stackable_box(c, m):
    def mesh(bm):
        sx, sy, sz, t = 0.6, 0.4, 0.3, 0.015
        box(bm, (sx, sy, t), (0, 0, -sz / 2 + t / 2), BLUE)
        for side in (-1, 1):
            box(bm, (sx, t, sz), (0, side * (sy / 2 - t / 2), 0), BLUE)
            box(bm, (t, sy, sz), (side * (sx / 2 - t / 2), 0, 0), BLUE)
            box(bm, (sx + 0.02, 0.02, 0.03), (0, side * (sy / 2), sz / 2 - 0.015), BLUE)   # stacking rim

    obj(c, m, "Crate", mesh, bevel=0.004)


def _product(kind, colour):
    def build(c, m):
        def mesh(bm):
            s = 0.25
            if kind in ("raw",):
                box(bm, (s, s, 0.15), (0, 0, 0), colour)
            # Every item is centred on its origin in height, like its collider.
            if kind in ("base", "final"):
                z0 = 0.0 if kind == "base" else -0.035
                box(bm, (s, s, 0.03), (0, 0, z0 - 0.035), colour)
                for side in (-1, 1):
                    box(bm, (s, 0.03, 0.07), (0, side * (s / 2 - 0.015), z0 + 0.015), colour)
                    box(bm, (0.03, s - 0.06, 0.07), (side * (s / 2 - 0.015), 0, z0 + 0.015), colour)
            if kind in ("lid", "final"):
                z0 = -0.015 if kind == "lid" else 0.04
                box(bm, (s, s, 0.03), (0, 0, z0), colour)
                cyl(bm, 0.04, 0.03, (0, 0, z0 + 0.03), "Z", 16, colour)

        obj(c, m, "Item", mesh, bevel=0.006)

    return build


ITEMS = [
    asset("box_small", "items", "Doos klein", _carton((0.30, 0.30, 0.20)), behaviour="item", mass=1.0),
    asset("box_medium", "items", "Doos middel", _carton((0.40, 0.40, 0.30)), behaviour="item", mass=2.0),
    asset("box_large", "items", "Doos groot", _carton((0.50, 0.50, 0.40), dark=True), behaviour="item", mass=4.0),
    asset("pallet", "items", "Pallet", _pallet(1.2, 0.8), behaviour="item", mass=15.0),
    asset("pallet_square", "items", "Pallet vierkant", _pallet(1.0, 1.0), behaviour="item", mass=15.0),
    asset("stackable_box", "items", "Stapelbak", _stackable_box, behaviour="item", mass=3.0),
]
for _name, _label, _colour in (("blue", "blauw", BLUE), ("green", "groen", GREEN), ("metal", "metaal", STEEL)):
    ITEMS += [
        asset("raw_material_" + _name, "items", "Grondstof " + _label, _product("raw", _colour), behaviour="item", mass=2.0),
        asset("product_base_" + _name, "items", "Productbasis " + _label, _product("base", _colour), behaviour="item", mass=1.0),
        asset("product_lid_" + _name, "items", "Productdeksel " + _label, _product("lid", _colour), behaviour="item", mass=0.5),
        asset("final_product_" + _name, "items", "Eindproduct " + _label, _product("final", _colour), behaviour="item", mass=1.5),
    ]

# -------------------------------------------------------------------------- light load


def _belt_conveyor(length, width=BELT_WIDTH, height=BELT_TOP):
    def build(c, m):
        belt_parts(c, m, length, width)

        def static(bm):
            frame(bm, length, width, height)
            box(bm, (length - 0.16, width - 0.02, 0.03), (0, 0, -0.06), DARK)   # slider bed
            drive(bm, length, width)

        obj(c, m, "Conveyor_Static", static)

    return build


def _curved_conveyor(surface, width, height, radius=1.0):
    """90-degree curve around a centre on the inside of the bend. Enters along +X at
    (0, -radius), leaves along +Y... origin is the centre of the bend at transport height."""

    def build(c, m):
        r0, r1 = radius - width / 2, radius + width / 2

        def static(bm):
            arc_strip(bm, r0, r1, 90, 0.0, surface, 16)
            arc_strip(bm, r0 - 0.03, r0, 90, -0.005, GREY, 16)
            arc_strip(bm, r1, r1 + 0.03, 90, -0.005, GREY, 16)
            for a in (8, 45, 82):
                for r in (r0 - 0.015, r1 + 0.015):
                    x, y = r * math.cos(math.radians(a)), r * math.sin(math.radians(a))
                    leg(bm, x, y, -0.005, -height)
            # Skirt under the deck so it does not look paper thin.
            for r_in, r_out in ((r0 - 0.03, r0), (r1, r1 + 0.03)):
                for i in range(8):
                    a = math.radians(90 * (i + 0.5) / 8)
                    rm = (r_in + r_out) / 2
                    verts = box(bm, (0.03, 2 * rm * math.sin(math.radians(90 / 16)) + 0.01, 0.12),
                                (rm * math.cos(a), rm * math.sin(a), -0.065), GREY)
                    rotate(bm, verts, math.degrees(a), "Z", (rm * math.cos(a), rm * math.sin(a), -0.065))

        obj(c, m, "Curve_Static", static, bevel=0.0)

    return build


INCLINE_LENGTH = 4.0                 # horizontal length, in metres
INCLINE_RISE = 1.0                   # height gained over that length


def _inclined_conveyor(c, m):
    """Belt that climbs towards +X. The origin is halfway up, on the belt top: the low end meets a
    belt at BELT_TOP and the high end one at BELT_TOP + INCLINE_RISE."""
    angle = math.atan2(INCLINE_RISE, INCLINE_LENGTH)
    degrees = math.degrees(angle)
    slope = math.hypot(INCLINE_LENGTH, INCLINE_RISE)         # length along the surface
    floor = -BELT_TOP - INCLINE_RISE / 2
    belt_parts(c, m, slope, BELT_WIDTH, degrees)

    def static(bm):
        # Frame, slider bed and drive are built flat, as for a straight belt, and then tilted.
        y = BELT_WIDTH / 2 + 0.015
        for side in (-1, 1):
            box(bm, (slope - 0.08, 0.03, 0.12), (0, side * y, -0.065), GREY)
        box(bm, (slope - 0.16, BELT_WIDTH - 0.02, 0.03), (0, 0, -0.06), DARK)
        drive(bm, slope, BELT_WIDTH)
        rotate(bm, list(bm.verts), -degrees, "Y")

        # The legs stand upright under the rails, with a tie between each pair and a stretcher low down.
        xs = (-INCLINE_LENGTH / 2 + 0.3, 0.0, INCLINE_LENGTH / 2 - 0.3)
        for x in xs:
            under = x * math.tan(angle) - 0.125 / math.cos(angle) + 0.02
            for side in (-1, 1):
                leg(bm, x, side * y, under, floor)
            box(bm, (0.04, BELT_WIDTH + 0.03, 0.04), (x, 0, under - 0.04), GREY)
        for side in (-1, 1):
            box(bm, (xs[-1] - xs[0], 0.03, 0.04), ((xs[0] + xs[-1]) / 2, side * y, floor + 0.16), GREY)

    obj(c, m, "Incline_Static", static)


def _conveyor_scale(c, m):
    length = 1.0
    belt_parts(c, m, length, BELT_WIDTH)

    def static(bm):
        box(bm, (length - 0.1, BELT_WIDTH + 0.06, 0.10), (0, 0, -0.07), GREY)
        box(bm, (length - 0.3, BELT_WIDTH - 0.2, BELT_TOP - 0.14), (0, 0, -0.12 - (BELT_TOP - 0.14) / 2), PANEL)   # load cell base
        box(bm, (length - 0.2, BELT_WIDTH - 0.1, 0.02), (0, 0, -BELT_TOP + 0.01), DARK)
        box(bm, (0.04, 0.04, 0.5), (0.3, BELT_WIDTH / 2 + 0.07, 0.15), GREY)                                       # display post
        box(bm, (0.26, 0.05, 0.16), (0.3, BELT_WIDTH / 2 + 0.07, 0.45), DARK)
        box(bm, (0.2, 0.005, 0.07), (0.3, BELT_WIDTH / 2 + 0.043, 0.46), GREEN)

    obj(c, m, "Scale_Static", static)


def _spur_conveyor(c, m):
    length = 1.6
    belt_parts(c, m, length, BELT_WIDTH)

    def static(bm):
        frame(bm, length, BELT_WIDTH, BELT_TOP)
        # Angled infeed nose, so the spur can meet a main line at 45 degrees.
        verts = box(bm, (0.9, 0.03, 0.12), (-length / 2 - 0.25, 0, -0.065), YELLOW)
        rotate(bm, verts, 45, "Z", (-length / 2, 0, 0))
        drive(bm, length, BELT_WIDTH)

    obj(c, m, "Spur_Static", static)


ARM_LENGTH = 1.2                     # long enough to reach across a 0.8 m belt at 45 degrees


def _pivot_arm_sorter(c, m):
    def static(bm):
        leg(bm, 0, 0, 0.05, -BELT_TOP, size=0.09)
        cyl(bm, 0.07, 0.16, (0, 0, 0.09), "Z", 20, DARK)

    obj(c, m, "Sorter_Static", static)

    def arm(bm):                     # swings about the post; origin on the pivot axis
        box(bm, (ARM_LENGTH, 0.05, 0.16), (ARM_LENGTH / 2, 0, 0), YELLOW)
        box(bm, (ARM_LENGTH - 0.04, 0.012, 0.12), (ARM_LENGTH / 2, -0.031, 0), BELT)
        cyl(bm, 0.045, 0.17, (ARM_LENGTH - 0.02, 0, 0), "Z", 16, STEEL)

    obj(c, m, "Arm", arm, location=(0, 0, 0.11))


def _popup_wheel_sorter(c, m):
    length = 1.0

    def static(bm):
        frame(bm, length, BELT_WIDTH, BELT_TOP)
        box(bm, (length, BELT_WIDTH, 0.02), (0, 0, -0.012), PANEL)
        for i in range(6):
            x = -length / 2 + 0.1 + i * 0.16
            cyl(bm, 0.02, BELT_WIDTH * 0.94, (x, 0, -0.022), "Y", 10, STEEL)

    obj(c, m, "Sorter_Static", static)

    def wheels(bm):                  # rises 3 mm and steers the item sideways
        for ix in range(5):
            for iy in range(4):
                verts = cyl(bm, 0.05, 0.03, (0, 0, 0), "Y", 14, ORANGE)
                rotate(bm, verts, 30, "Z")
                bmesh_translate(bm, verts, (-length / 2 + 0.18 + ix * 0.16, -0.3 + iy * 0.2, -0.045))

    obj(c, m, "Wheels", wheels, bevel=0.0)


def bmesh_translate(bm, verts, vec):
    import bmesh
    bmesh.ops.translate(bm, vec=vec, verts=verts)


ALIGNER_LENGTH = 1.2                 # along the belt, in metres
ALIGNER_ENTRY = 0.52                 # inner face of a guide, from the centre line, where items come in
ALIGNER_EXIT = 0.165                 # and where they leave: a gap of 0.33 m, a 0.30 m box has 1.5 cm to spare
ALIGNER_THICKNESS = 0.03
ALIGNER_HEIGHT = 0.12
ALIGNER_Z = 0.07                     # centre of the guides above the belt
POST_Y = 0.455                       # posts stand just outside the belt frame


def _aligner_guides():
    """Two guides as (side, centre x, centre y, length, angle) in Blender axes, side +1 on +Y. A guide
    runs from the wide entry at -X to the narrow exit at +X; its inner face is the funnel."""
    theta = math.atan2(ALIGNER_ENTRY - ALIGNER_EXIT, ALIGNER_LENGTH)
    length = ALIGNER_LENGTH / math.cos(theta)
    mid = (ALIGNER_ENTRY + ALIGNER_EXIT) / 2
    guides = []
    for side in (1, -1):
        # The box centre lies half a thickness outward from the middle of the inner face.
        cx = ALIGNER_THICKNESS / 2 * math.sin(theta)
        cy = side * (mid + ALIGNER_THICKNESS / 2 * math.cos(theta))
        guides.append((side, cx, cy, length, math.degrees(theta)))
    return guides


def _inner_y(x):
    """Distance of the inner face of a guide from the centre line at x."""
    return ALIGNER_EXIT + (ALIGNER_LENGTH / 2 - x) / ALIGNER_LENGTH * (ALIGNER_ENTRY - ALIGNER_EXIT)


def _aligner(c, m):
    def mesh(bm):
        for side, cx, cy, length, degrees in _aligner_guides():
            verts = box(bm, (length, ALIGNER_THICKNESS, ALIGNER_HEIGHT), (0, 0, ALIGNER_Z), STEEL)
            rotate(bm, verts, -side * degrees, "Z")              # towards the centre line as x grows
            bmesh_translate(bm, verts, (cx, cy, 0.0))
            for x in (-0.5, 0.5):                                 # a post beside the belt and an arm to the guide
                y = side * POST_Y
                box(bm, (0.04, 0.04, 0.25), (x, y, 0.005), GREY)
                outer = side * (_inner_y(x) + ALIGNER_THICKNESS)
                if abs(y - outer) > 0.01:
                    box(bm, (0.04, abs(y - outer), 0.03), (x, (y + outer) / 2, ALIGNER_Z), GREY)

    obj(c, m, "Aligner", mesh)


def _aligner_colliders():
    """The guides again as the app builds them (Godot axes: Blender +Y is -Z there)."""
    result = []
    for side, cx, cy, length, degrees in _aligner_guides():
        result.append(collider((length, ALIGNER_HEIGHT, ALIGNER_THICKNESS), (cx, ALIGNER_Z, -cy),
                               yaw=-side * degrees, friction=0.02))
    return result


CHUTE_FRICTION = 0.02                # a box has 0.7; even the shallow chute (11 degrees) must make it slide


def _chute_colliders(length, width, drop):
    """Slab and two side walls of a chute as the app builds them, in Godot axes. The slope runs down
    towards +X; its upper surface passes through the origin."""
    slope = math.hypot(length, drop)
    pitch = math.degrees(math.atan2(drop, length))
    a = math.radians(pitch)

    def at(normal):                                  # a point `normal` above the surface, along the normal
        return (normal * math.sin(a), normal * math.cos(a))

    x, y = at(-0.05)
    result = [collider((slope, 0.10, width), (x, y, 0.0), pitch=pitch, friction=CHUTE_FRICTION)]
    x, y = at(0.04)
    for side in (-1, 1):
        result.append(collider((slope, 0.10, 0.02), (x, y, side * (width / 2 + 0.006)), pitch=pitch, friction=CHUTE_FRICTION))
    return result


def _chute(length, width, drop, name):
    def build(c, m):
        angle = math.degrees(math.atan2(drop, length))

        def mesh(bm):
            slope = math.hypot(length, drop)
            verts = box(bm, (slope, width, 0.012), (0, 0, -0.006), STEEL)
            for side in (-1, 1):
                verts += box(bm, (slope, 0.012, 0.10), (0, side * (width / 2 + 0.006), 0.04), GREY)
            rotate(bm, verts, angle, "Y")
            top_height = BELT_TOP if name == "chute" else ROLLER_TOP
            for x, z in ((-length / 2 + 0.1, drop / 2 - 0.05), (length / 2 - 0.1, -drop / 2 + 0.03)):
                for side in (-1, 1):
                    leg(bm, x, side * (width / 2 + 0.006), z - 0.02, -top_height + drop / 2, size=0.04)

        obj(c, m, "Chute", mesh)

    return build


def _stop_blade(c, m):
    def static(bm):
        box(bm, (0.12, BELT_WIDTH + 0.12, 0.06), (0, 0, -0.17), GREY)
        cyl(bm, 0.035, 0.14, (0, 0, -0.10), "Z", 14, DARK)

    obj(c, m, "Stop_Static", static)

    def blade(bm):                   # rises 0.12 m through the gap between two conveyors
        box(bm, (0.012, BELT_WIDTH * 0.9, 0.14), (0, 0, 0), YELLOW)

    obj(c, m, "Blade", blade, location=(0, 0, -0.075))


def _metal_corner(c, m):
    def mesh(bm):
        box(bm, (0.5, 0.02, 0.12), (0.25, 0, 0.06), STEEL)
        box(bm, (0.02, 0.5, 0.12), (0, -0.25, 0.06), STEEL)
        box(bm, (0.06, 0.06, 0.14), (0.03, -0.03, -0.01), GREY)

    obj(c, m, "Corner", mesh)


CLAMP_ORIGIN = 0.55                  # a clamp part's origin lies this far from the centre line
CLAMP_BLADE_LENGTH = 0.5             # along the belt
CLAMP_BLADE_HEIGHT = 0.16
CLAMP_BLADE_THICKNESS = 0.02         # of the model; the app's collider is 0.04 thick on the same inner face
CLAMP_BLADE_SHIFT = 0.02             # the blade sits this far from the clamp origin towards the centre line
CLAMP_HEIGHT = 0.10                  # of the clamp origin above the belt
CLAMP_OPEN_HALF_GAP = CLAMP_ORIGIN - CLAMP_BLADE_SHIFT - CLAMP_BLADE_THICKNESS / 2    # inner face when open: 0.52
CLAMP_STROKE = 0.42


def _positioning_bars(c, m):
    def static(bm):
        for side in (-1, 1):
            leg(bm, 0, side * 0.75, 0.55, -BELT_TOP, size=0.06)
        box(bm, (0.08, 1.56, 0.08), (0, 0, 0.55), GREY)

    obj(c, m, "Bars_Static", static)
    for name, side in (("Clamp_Left", 1), ("Clamp_Right", -1)):
        def clamp(bm, side=side):    # the rod and the blade close towards the centre line together
            box(bm, (0.05, 0.05, 0.42), (0, 0, 0.21), STEEL)
            box(bm, (CLAMP_BLADE_LENGTH, CLAMP_BLADE_THICKNESS, CLAMP_BLADE_HEIGHT), (0, -side * CLAMP_BLADE_SHIFT, 0.02), YELLOW)

        obj(c, m, name, clamp, location=(0, side * CLAMP_ORIGIN, CLAMP_HEIGHT))


def _gate_conveyor(c, m):
    length = 2.0
    belt_parts(c, m, length, BELT_WIDTH)

    def static(bm):
        frame(bm, length, BELT_WIDTH, BELT_TOP)
        cyl(bm, 0.05, BELT_WIDTH + 0.1, (-length / 2 + 0.05, 0, -0.07), "Y", 16, YELLOW)     # hinge
        box(bm, (0.1, 0.06, 0.5), (-length / 2 - 0.02, BELT_WIDTH / 2 + 0.06, 0.2), YELLOW)   # gas spring post
        drive(bm, length, BELT_WIDTH)

    obj(c, m, "Gate_Static", static)


LIGHT_LOAD = [
    asset("belt_conveyor_{}m".format(int(n)), "light_load", "Bandtransporteur {} m".format(int(n)),
          _belt_conveyor(n), behaviour="conveyor", origin_height=BELT_TOP, length=n, width=BELT_WIDTH, speed=0.6)
    for n in LENGTHS
] + [
    asset("belt_conveyor_curved", "light_load", "Bochtband 90°",
          _curved_conveyor(BELT, BELT_WIDTH, BELT_TOP), behaviour="curve", origin_height=BELT_TOP, radius=1.0,
          width=BELT_WIDTH, speed=0.6),
    asset("belt_conveyor_inclined", "light_load", "Hellende band", _inclined_conveyor,
          behaviour="conveyor", origin_height=BELT_TOP + INCLINE_RISE / 2, length=INCLINE_LENGTH, rise=INCLINE_RISE,
          width=BELT_WIDTH, speed=0.6),
    asset("belt_conveyor_gate", "light_load", "Bandtransporteur met doorgang", _gate_conveyor,
          behaviour="conveyor", origin_height=BELT_TOP, length=2.0, width=BELT_WIDTH, speed=0.6),
    asset("conveyor_scale", "light_load", "Weegband", _conveyor_scale, behaviour="conveyor",
          origin_height=BELT_TOP, length=1.0, width=BELT_WIDTH, speed=0.6, weighs=1),
    asset("spur_conveyor", "light_load", "Aftakband", _spur_conveyor, behaviour="conveyor",
          origin_height=BELT_TOP, length=1.6, width=BELT_WIDTH, speed=0.6),
    asset("pivot_arm_sorter", "light_load", "Zwenkarm-sorteerder", _pivot_arm_sorter,
          behaviour="arm_sorter", origin_height=BELT_TOP, length=ARM_LENGTH, angle=45.0, swingSpeed=120.0, speed=0.6),
    asset("popup_wheel_sorter", "light_load", "Wielsorteerder", _popup_wheel_sorter,
          behaviour="wheel_sorter", origin_height=BELT_TOP, length=1.0, width=BELT_WIDTH, speed=0.6, angle=45.0, lift=0.012),
    asset("aligner", "light_load", "Uitlijner", _aligner, behaviour="passive", origin_height=BELT_TOP,
          colliders=_aligner_colliders()),
    asset("chute", "light_load", "Glijgoot", _chute(1.5, BELT_WIDTH, 0.5, "chute"), behaviour="passive",
          origin_height=BELT_TOP - 0.25, colliders=_chute_colliders(1.5, BELT_WIDTH, 0.5)),
    asset("stop_blade", "light_load", "Stopblad", _stop_blade, behaviour="stop", origin_height=BELT_TOP,
          stroke=0.12, bladeWidth=BELT_WIDTH * 0.9, bladeHeight=0.14, bladeCentre=-0.075),
    asset("metal_corner", "light_load", "Hoekaanslag", _metal_corner, behaviour="passive",
          origin_height=BELT_TOP,
          colliders=[collider((0.5, 0.12, 0.02), (0.25, 0.06, 0.0), friction=0.3),       # along the travel
                     collider((0.02, 0.12, 0.5), (0.0, 0.06, 0.25), friction=0.3)]),     # across it (Blender -Y is +Z)
    asset("positioning_bars", "light_load", "Positioneerbalken", _positioning_bars, behaviour="clamp",
          origin_height=BELT_TOP, openHalfGap=CLAMP_OPEN_HALF_GAP, stroke=CLAMP_STROKE, speed=0.3,
          bladeLength=CLAMP_BLADE_LENGTH, bladeHeight=CLAMP_BLADE_HEIGHT, bladeThickness=0.04,
          bladeCentre=CLAMP_HEIGHT + 0.02),
]

# -------------------------------------------------------------------------- heavy load


def _roller_conveyor(length, end_stop=False, driven=True):
    def build(c, m):
        roller_parts(c, m, length, ROLLER_WIDTH)

        def static(bm):
            frame(bm, length, ROLLER_WIDTH, ROLLER_TOP, beam=0.14)
            if driven:
                drive(bm, length, ROLLER_WIDTH, z=-0.2)
            if end_stop:
                box(bm, (0.03, ROLLER_WIDTH, 0.18), (length / 2 - 0.015, 0, 0.09), YELLOW)

        obj(c, m, "Conveyor_Static", static)

    return build


def _roller_stop(c, m):
    def static(bm):
        box(bm, (0.14, ROLLER_WIDTH + 0.1, 0.08), (0, 0, -0.2), GREY)
        for side in (-1, 1):
            cyl(bm, 0.04, 0.16, (0, side * 0.35, -0.12), "Z", 14, DARK)

    obj(c, m, "Stop_Static", static)

    def blade(bm):                   # rises 0.1 m between two rollers
        box(bm, (0.02, ROLLER_WIDTH * 0.85, 0.12), (0, 0, 0), YELLOW)

    obj(c, m, "Blade", blade, location=(0, 0, -0.07))


def _chain_transfer(c, m):
    def static(bm):
        frame(bm, 1.5, ROLLER_WIDTH, ROLLER_TOP, beam=0.14)
        box(bm, (1.3, ROLLER_WIDTH - 0.1, 0.03), (0, 0, -0.10), DARK)
        for i in range(11):          # rollers carry straight on; none where the chain tracks run
            x = -0.65 + i * 0.13
            if min(abs(x - 0.4), abs(x + 0.4)) > 0.09:
                cyl(bm, ROLLER_RADIUS, ROLLER_WIDTH - 0.24, (x, 0, -ROLLER_RADIUS), "Y", 12, STEEL)

    obj(c, m, "Transfer_Static", static)

    def chains(bm):                  # two chain tracks that lift 0.02 m and carry the load sideways
        for x in (-0.4, 0.4):
            box(bm, (0.08, ROLLER_WIDTH + 0.3, 0.05), (x, 0, 0), DARK)
            box(bm, (0.04, ROLLER_WIDTH + 0.3, 0.012), (x, 0, 0.031), YELLOW)

    obj(c, m, "Chains", chains, location=(0, 0, -0.055))


def _turntable(c, m):
    def static(bm):
        cyl(bm, 0.95, 0.10, (0, 0, -ROLLER_TOP + 0.05), "Z", 32, DARK)
        cyl(bm, 0.25, ROLLER_TOP - 0.26, (0, 0, -0.16 - (ROLLER_TOP - 0.26) / 2), "Z", 20, GREY)

    obj(c, m, "Turntable_Static", static)

    def deck(bm):                    # turns about the vertical axis
        cyl(bm, 0.9, 0.04, (0, 0, -0.14), "Z", 32, GREY)
        for side in (-1, 1):
            box(bm, (1.5, 0.03, 0.12), (0, side * (ROLLER_WIDTH / 2 - 0.1), -0.06), GREY)
        for i in range(11):
            cyl(bm, 0.045, ROLLER_WIDTH - 0.24, (-0.65 + i * 0.13, 0, -0.045), "Y", 12, STEEL)

    obj(c, m, "Deck", deck, bevel=0.0)


FREE_ROLLER_LENGTH = 1.0
FREE_ROLLER_FRICTION = 0.02          # a pallet carries on at the speed it came in with


def _free_roller(c, m):
    roller_parts(c, m, FREE_ROLLER_LENGTH, ROLLER_WIDTH)
    obj(c, m, "Conveyor_Static", lambda bm: frame(bm, FREE_ROLLER_LENGTH, ROLLER_WIDTH, ROLLER_TOP, beam=0.14))


HEAVY_LOAD = [
    asset("roller_conveyor_{}m".format(int(n)), "heavy_load", "Rollenbaan {} m".format(int(n)),
          _roller_conveyor(n), behaviour="conveyor", origin_height=ROLLER_TOP, length=n, width=ROLLER_WIDTH, speed=0.45)
    for n in LENGTHS
] + [
    asset("roller_conveyor_curved", "heavy_load", "Rollenbocht 90°",
          _curved_conveyor(STEEL, ROLLER_WIDTH, ROLLER_TOP, radius=1.5), behaviour="curve", origin_height=ROLLER_TOP,
          radius=1.5, width=ROLLER_WIDTH, speed=0.45),
    asset("loading_conveyor", "heavy_load", "Laadbaan", _roller_conveyor(2.0, end_stop=True),
          behaviour="conveyor", origin_height=ROLLER_TOP, length=2.0, width=ROLLER_WIDTH, speed=0.45),
    asset("roller_stop", "heavy_load", "Rollenstop", _roller_stop, behaviour="stop", origin_height=ROLLER_TOP,
          stroke=0.1, bladeWidth=ROLLER_WIDTH * 0.85, bladeHeight=0.12, bladeCentre=-0.07),
    asset("free_roller", "heavy_load", "Vrijlopende rollenbaan", _free_roller, behaviour="passive",
          origin_height=ROLLER_TOP, length=FREE_ROLLER_LENGTH, width=ROLLER_WIDTH,
          colliders=[collider((FREE_ROLLER_LENGTH, 0.2, ROLLER_WIDTH * 0.94), (0.0, -0.1, 0.0), friction=FREE_ROLLER_FRICTION)]),
    asset("chain_transfer", "heavy_load", "Kettingovername", _chain_transfer, behaviour="wheel_sorter",
          origin_height=ROLLER_TOP, length=1.5, width=ROLLER_WIDTH, speed=0.45, angle=90.0, lift=0.02),
    asset("turntable", "heavy_load", "Draaitafel", _turntable, behaviour="turntable", origin_height=ROLLER_TOP,
          radius=0.9, speed=0.45, angle=90.0, turnSpeed=60.0),
    asset("chute_low", "heavy_load", "Lage glijgoot", _chute(1.5, ROLLER_WIDTH, 0.3, "chute_low"),
          behaviour="passive", origin_height=ROLLER_TOP - 0.15, colliders=_chute_colliders(1.5, ROLLER_WIDTH, 0.3)),
]
