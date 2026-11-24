"""Library assets: sensors, operators, warning devices, walkways and stations.

Own designs: no third-party model, texture or drawing was used.
"""

import math

from .kit import (BELT, BELT_TOP, BLACK, BLUE, DARK, GLASS, GREEN, GREY, LENS, ORANGE, PANEL, RED, ROLLER_TOP, ROLLER_WIDTH,
                  STEEL, WHITE, YELLOW, asset, box, cabinet, circle, collider, cyl, frustum, lamp, leg, obj, rotate,
                  slab, tube_frame)

# ----------------------------------------------------------------------------- sensors
# Origin: the sensing face. All sensors look along -Y (Godot +Z).


def _barrel_sensor(tip, length=0.08, radius=0.015):
    def build(c, m):
        def static(bm):
            cyl(bm, radius, length, (0, length / 2, 0), "Y", 16, STEEL)
            cyl(bm, radius * 0.98, 0.012, (0, 0.006, 0), "Y", 16, tip)
            for y in (0.03, 0.05):
                cyl(bm, radius * 1.5, 0.006, (0, y, 0), "Y", 6, DARK)          # lock nuts
            cyl(bm, 0.005, 0.03, (0, length + 0.015, 0), "Y", 8, BLACK)        # cable

        obj(c, m, "Sensor_Static", static, bevel=0.0)
        lamp(c, m, "SensorLed", "red", lambda bm: box(bm, (0.008, 0.006, 0.008), (0, 0, 0), 0),
             location=(0, length - 0.005, radius))

    return build


def _block_sensor(body, with_beam):
    def build(c, m):
        def static(bm):
            box(bm, (0.045, 0.07, 0.045), (0, 0.037, 0), body)
            cyl(bm, 0.013, 0.006, (0, 0.0, 0), "Y", 16, LENS)
            box(bm, (0.014, 0.03, 0.06), (0, 0.086, 0), GREY)

        obj(c, m, "Sensor_Static", static, bevel=0.002)
        lamp(c, m, "SensorLed", "red", lambda bm: box(bm, (0.016, 0.016, 0.008), (0, 0, 0), 0), location=(0, 0.05, 0.0265))
        if with_beam:
            from . import base as b
            b.make_object(c, "SensorBeam", lambda bm: box(bm, (0.005, 1.0, 0.005), (0, -0.5, 0), 0), [m["beam"]], bevel=0.0)

    return build


def _reflector(c, m):
    def mesh(bm):
        box(bm, (0.08, 0.012, 0.08), (0, -0.006, 0), DARK)
        box(bm, (0.066, 0.004, 0.066), (0, 0.001, 0), ORANGE)
        box(bm, (0.014, 0.03, 0.06), (0, -0.027, 0), GREY)

    obj(c, m, "Reflector", mesh, bevel=0.002)


def _light_array(emitter):
    def build(c, m):
        def static(bm):
            box(bm, (0.05, 0.05, 0.42), (0, 0.025, 0), YELLOW)
            box(bm, (0.034, 0.004, 0.36), (0, -0.001, 0), LENS)
            box(bm, (0.07, 0.07, 0.02), (0, 0.025, -0.22), DARK)

        obj(c, m, "Array_Static", static, bevel=0.003)
        lamp(c, m, "SensorLed", "green" if emitter else "red",
             lambda bm: box(bm, (0.016, 0.006, 0.016), (0, 0, 0), 0), location=(0, -0.002, 0.195))

    return build


def _vision_sensor(c, m):
    def static(bm):
        box(bm, (0.09, 0.14, 0.09), (0, 0.09, 0), WHITE)
        cyl(bm, 0.035, 0.04, (0, 0.02, 0), "Y", 20, DARK)
        cyl(bm, 0.026, 0.006, (0, 0.0, 0), "Y", 20, LENS)
        box(bm, (0.03, 0.05, 0.09), (0, 0.185, 0), GREY)

    obj(c, m, "Sensor_Static", static, bevel=0.004)
    lamp(c, m, "SensorLed", "red", lambda bm: box(bm, (0.02, 0.02, 0.008), (0, 0, 0), 0), location=(0, 0.12, 0.049))


def _rfid_reader(c, m):
    def static(bm):
        box(bm, (0.16, 0.035, 0.16), (0, 0.0175, 0), DARK)
        box(bm, (0.12, 0.004, 0.12), (0, -0.001, 0), BLUE)
        box(bm, (0.02, 0.03, 0.08), (0, 0.05, 0), GREY)

    obj(c, m, "Reader_Static", static, bevel=0.004)
    for name, colour, x in (("Led_Read", "green", -0.05), ("Led_Busy", "yellow", 0.0), ("Led_Error", "red", 0.05)):
        lamp(c, m, name, colour, lambda bm: box(bm, (0.014, 0.004, 0.014), (0, 0, 0), 0), location=(x, -0.002, 0.066))


def _encoder(c, m):
    def static(bm):
        cyl(bm, 0.03, 0.05, (0, 0.045, 0), "Y", 18, DARK)
        box(bm, (0.02, 0.02, 0.1), (0, 0.08, 0.05), GREY)

    obj(c, m, "Encoder_Static", static, bevel=0.0)

    def wheel(bm):                   # rides on the belt or a roller
        cyl(bm, 0.04, 0.02, (0, 0, 0), "Y", 20, BLACK)
        box(bm, (0.012, 0.022, 0.012), (0.026, 0, 0), ORANGE)

    obj(c, m, "Wheel", wheel, location=(0, 0.008, 0), bevel=0.0)


SENSORS = [
    asset("diffuse_sensor", "sensors", "Diffuse sensor (photo-eye)", _block_sensor(BLACK, True),
          behaviour="photo_eye", origin_height=0.9, range=1.0),
    asset("retroreflective_sensor", "sensors", "Reflectiesensor", _block_sensor(BLUE, True),
          behaviour="retroreflective", origin_height=0.9, range=6.0),
    asset("reflector", "sensors", "Reflector", _reflector, behaviour="reflector", origin_height=0.9),
    asset("capacitive_sensor", "sensors", "Capacitieve sensor", _barrel_sensor(ORANGE),
          behaviour="photo_eye", origin_height=0.9, range=0.2),
    asset("inductive_sensor", "sensors", "Inductieve sensor", _barrel_sensor(BLUE),
          behaviour="photo_eye", origin_height=0.9, range=0.1, metalOnly=1),
    asset("light_array_emitter", "sensors", "Lichtscherm zender", _light_array(True),
          behaviour="light_emitter", origin_height=1.05),
    asset("light_array_receiver", "sensors", "Lichtscherm ontvanger", _light_array(False),
          behaviour="light_array", origin_height=1.05, range=3.0, beams=8, beamSpacing=0.05),
    asset("vision_sensor", "sensors", "Vision-sensor", _vision_sensor, behaviour="vision",
          origin_height=1.6, range=2.0),
    asset("rfid_reader", "sensors", "RFID-lezer", _rfid_reader, origin_height=0.95),
    asset("incremental_encoder", "sensors", "Incrementele encoder", _encoder, behaviour="encoder",
          origin_height=0.84, pulseDistance=0.05, wheelRadius=0.04),
]

# --------------------------------------------------------------------------- operators
# Origin: centre of the mounting face; the control points along -Y.


def _plate(bm, size=0.08, mat=GREY):
    box(bm, (size, 0.03, size), (0, 0.015, 0), mat)


def _emergency_stop(c, m):
    obj(c, m, "Stop_Static", lambda bm: (box(bm, (0.1, 0.06, 0.1), (0, 0.03, 0), YELLOW),
                                         cyl(bm, 0.022, 0.02, (0, -0.01, 0), "Y", 16, DARK)), bevel=0.004)
    obj(c, m, "Button", lambda bm: cyl(bm, 0.04, 0.025, (0, 0, 0), "Y", 20, RED), location=(0, -0.032, 0), bevel=0.0)


def _push_button(colour):
    def build(c, m):
        obj(c, m, "Button_Static", lambda bm: (_plate(bm), cyl(bm, 0.03, 0.01, (0, -0.005, 0), "Y", 20, STEEL)), bevel=0.003)
        lamp(c, m, "Button", colour, lambda bm: cyl(bm, 0.024, 0.016, (0, 0, 0), "Y", 20, 0), location=(0, -0.016, 0))

    return build


def _light_indicator(colour):
    def build(c, m):
        obj(c, m, "Indicator_Static", lambda bm: (_plate(bm), cyl(bm, 0.03, 0.012, (0, -0.006, 0), "Y", 20, STEEL)), bevel=0.003)
        lamp(c, m, "Lamp", colour, lambda bm: cyl(bm, 0.025, 0.03, (0, 0, 0), "Y", 20, 0), location=(0, -0.025, 0))

    return build


def _selector(c, m):
    obj(c, m, "Selector_Static", lambda bm: (_plate(bm), cyl(bm, 0.03, 0.012, (0, -0.006, 0), "Y", 20, STEEL)), bevel=0.003)
    obj(c, m, "Knob", lambda bm: (cyl(bm, 0.022, 0.02, (0, 0, 0), "Y", 16, BLACK),
                                  box(bm, (0.012, 0.03, 0.05), (0, -0.01, 0), BLACK),
                                  box(bm, (0.004, 0.004, 0.02), (0, -0.026, 0.012), WHITE)), location=(0, -0.022, 0), bevel=0.002)


def _potentiometer(c, m):
    obj(c, m, "Pot_Static", lambda bm: (_plate(bm), box(bm, (0.06, 0.002, 0.06), (0, -0.001, 0), WHITE)), bevel=0.003)
    obj(c, m, "Knob", lambda bm: (cyl(bm, 0.02, 0.025, (0, 0, 0), "Y", 16, BLACK),
                                  box(bm, (0.004, 0.004, 0.016), (0, -0.013, 0.01), ORANGE)), location=(0, -0.0145, 0), bevel=0.0)


def _digital_display(c, m):
    obj(c, m, "Display_Static", lambda bm: box(bm, (0.2, 0.04, 0.09), (0, 0.02, 0), DARK), bevel=0.004)
    lamp(c, m, "Screen", "green", lambda bm: box(bm, (0.17, 0.004, 0.06), (0, 0, 0), 0), location=(0, -0.002, 0))


SWITCHBOARD = (0.8, 0.3, 1.2)        # cabinet, Blender axes
SWITCHBOARD_FRONT = 0.185            # front face of the door handle, from the centre towards -Y (Godot +Z)


def _switchboard(c, m):
    def mesh(bm):                    # stands on the floor; origin at the floor, front faces -Y
        cabinet(bm, SWITCHBOARD, (0, 0, SWITCHBOARD[2] / 2))
        box(bm, (0.74, 0.01, 1.0), (0, -0.155, 0.65), WHITE)
        box(bm, (0.03, 0.03, 0.14), (0.32, -0.17, 0.65), DARK)

    obj(c, m, "Switchboard", mesh)


def _switchboard_colliders():
    """One box over the cabinet, the door and its handle, in Godot axes (the front at Blender -Y is +Z)."""
    sx, sy, sz = SWITCHBOARD
    return [collider((sx, sz, sy / 2 + SWITCHBOARD_FRONT), (0.0, sz / 2, (SWITCHBOARD_FRONT - sy / 2) / 2))]


COLUMN_FOOT, COLUMN_TOP = 0.3, 1.06  # square foot plate; top of the sloping panel


def _column(c, m):
    def mesh(bm):                    # pedestal for buttons; origin at the floor
        box(bm, (0.14, 0.14, 1.0), (0, 0, 0.5), GREY)
        box(bm, (COLUMN_FOOT, COLUMN_FOOT, 0.02), (0, 0, 0.01), DARK)
        verts = box(bm, (0.26, 0.2, 0.03), (0, -0.03, 1.02), PANEL)
        rotate(bm, verts, 25, "X", (0, 0, 1.02))

    obj(c, m, "Column", mesh)


OPERATORS = [
    asset("emergency_stop", "operators", "Noodstop", _emergency_stop, behaviour="button", origin_height=1.0, latching=1, normallyClosed=1),
    asset("push_button_green", "operators", "Drukknop groen (start)", _push_button("green"), behaviour="button", origin_height=1.0, latching=0),
    asset("push_button_red", "operators", "Drukknop rood (stop)", _push_button("red"), behaviour="button", origin_height=1.0, latching=0),
    asset("push_button_blue", "operators", "Drukknop blauw (reset)", _push_button("blue"), behaviour="button", origin_height=1.0, latching=0),
    asset("light_indicator_green", "operators", "Signaallamp groen", _light_indicator("green"), behaviour="lamp", origin_height=1.0),
    asset("light_indicator_yellow", "operators", "Signaallamp geel", _light_indicator("yellow"), behaviour="lamp", origin_height=1.0),
    asset("light_indicator_red", "operators", "Signaallamp rood", _light_indicator("red"), behaviour="lamp", origin_height=1.0),
    asset("selector", "operators", "Keuzeschakelaar", _selector, behaviour="button", origin_height=1.0, latching=1, knob=1),
    asset("potentiometer", "operators", "Potentiometer", _potentiometer, behaviour="potentiometer",
          origin_height=1.0, start=0),
    asset("digital_display", "operators", "Digitaal display", _digital_display, behaviour="display",
          origin_height=1.1, decimals=0),
    asset("electric_switchboard", "operators", "Schakelkast", _switchboard, behaviour="passive",
          colliders=_switchboard_colliders()),
    asset("column", "operators", "Bedieningszuil", _column, behaviour="passive",
          colliders=[collider((COLUMN_FOOT, COLUMN_TOP, COLUMN_FOOT), (0.0, COLUMN_TOP / 2, 0.0))]),
]

# --------------------------------------------------------------------- warning devices


def _stack_light(c, m):
    obj(c, m, "Stack_Static", lambda bm: (cyl(bm, 0.02, 0.3, (0, 0, 0.15), "Z", 12, GREY),
                                          cyl(bm, 0.06, 0.02, (0, 0, 0.01), "Z", 16, DARK),
                                          cyl(bm, 0.042, 0.02, (0, 0, 0.55), "Z", 16, DARK)), bevel=0.0)
    for i, (name, colour) in enumerate((("Lamp_Green", "green"), ("Lamp_Yellow", "yellow"), ("Lamp_Red", "red"))):
        lamp(c, m, name, colour, lambda bm: cyl(bm, 0.04, 0.075, (0, 0, 0), "Z", 16, 0), location=(0, 0, 0.34 + i * 0.08))


def _warning_light(c, m):
    obj(c, m, "Light_Static", lambda bm: cyl(bm, 0.07, 0.05, (0, 0, 0.025), "Z", 20, DARK), bevel=0.0)
    lamp(c, m, "Lamp", "yellow", lambda bm: cyl(bm, 0.055, 0.1, (0, 0, 0), "Z", 20, 0), location=(0, 0, 0.1))


def _alarm_siren(c, m):
    from . import base as b

    # Own two-slot material list, red first: the app flashes surface 0 of the Siren part, and the glTF
    # export orders the surfaces by slot (with the shared palette the dark rim would come first).
    red, rim = 0, 1

    def mesh(bm):
        box(bm, (0.14, 0.08, 0.14), (0, 0.04, 0), red)
        cyl(bm, 0.05, 0.05, (0, -0.02, 0), "Y", 20, red)
        cyl(bm, 0.062, 0.012, (0, -0.045, 0), "Y", 20, rim)

    b.make_object(c, "Siren", mesh, [m["red"], m["frame_dark"]], bevel=0.004)


WARNING = [
    asset("stack_light", "warning_devices", "Signaalzuil", _stack_light, behaviour="lamp", origin_height=1.26, onTop=1),
    asset("warning_light", "warning_devices", "Waarschuwingslamp", _warning_light, behaviour="lamp", origin_height=1.26, onTop=1),
    asset("alarm_siren", "warning_devices", "Sirene", _alarm_siren, behaviour="siren", origin_height=2.0),
]

# ---------------------------------------------------------------------------- walkways
# Origin: on the floor (or on the platform deck for handrails), centre of the footprint.


HANDRAIL_HEIGHT = 1.1
HANDRAIL_RAILS = (0.55, 1.08)        # middle and top rail
HANDRAIL_TUBE = 0.04                 # posts and rails
HANDRAIL_KICK = (0.01, 0.1)          # kick plate: thickness, height


def _handrail_posts(length):
    return (-length / 2 + 0.03, 0.0, length / 2 - 0.03)


def _handrail(length):
    def build(c, m):
        def mesh(bm):
            for x in _handrail_posts(length):
                box(bm, (HANDRAIL_TUBE, HANDRAIL_TUBE, HANDRAIL_HEIGHT), (x, 0, HANDRAIL_HEIGHT / 2), YELLOW)
            for z in HANDRAIL_RAILS:
                cyl(bm, HANDRAIL_TUBE / 2, length, (0, 0, z), "X", 10, YELLOW)
            box(bm, (length, HANDRAIL_KICK[0], HANDRAIL_KICK[1]), (0, 0, HANDRAIL_KICK[1] / 2), YELLOW)       # kick plate

        obj(c, m, "Handrail", mesh, bevel=0.0)

    return build


def _handrail_colliders(length):
    """Posts, the two rails and the kick plate as thin boxes, in Godot axes. A small item fits between the kick
    plate and the middle rail, like on a real handrail."""
    tube, (thickness, kick) = HANDRAIL_TUBE, HANDRAIL_KICK
    result = [collider((tube, HANDRAIL_HEIGHT, tube), (x, HANDRAIL_HEIGHT / 2, 0.0)) for x in _handrail_posts(length)]
    result += [collider((length, tube, tube), (0.0, y, 0.0)) for y in HANDRAIL_RAILS]
    result.append(collider((length, kick, thickness), (0.0, kick / 2, 0.0)))
    return result


PLATFORM_SIZE = 2.0
PLATFORM_DECK = 1.0                  # deck_height: top of the deck plate; the anti-slip sheet lies 4 mm proud of it


def _platform(c, m):
    def mesh(bm):
        size, height = PLATFORM_SIZE, PLATFORM_DECK
        box(bm, (size, size, 0.04), (0, 0, height - 0.02), GREY)
        box(bm, (size - 0.1, size - 0.1, 0.004), (0, 0, height + 0.002), DARK)       # anti-slip deck
        for x in (-size / 2 + 0.05, size / 2 - 0.05):
            for y in (-size / 2 + 0.05, size / 2 - 0.05):
                leg(bm, x, y, height - 0.04, 0.0, size=0.07)
        for y in (-size / 2 + 0.05, size / 2 - 0.05):
            box(bm, (size - 0.1, 0.04, 0.04), (0, y, height * 0.4), GREY)

    obj(c, m, "Platform", mesh)


def _platform_colliders():
    """The deck plate, its top at deck_height, and the four legs, in Godot axes (symmetric about x and z)."""
    size, height = PLATFORM_SIZE, PLATFORM_DECK
    result = [collider((size, 0.04, size), (0.0, height - 0.02, 0.0))]
    for x in (-size / 2 + 0.05, size / 2 - 0.05):
        for z in (-size / 2 + 0.05, size / 2 - 0.05):
            result.append(collider((0.07, height - 0.04, 0.07), (x, (height - 0.04) / 2, z)))
    return result


STAIRS = (5, 0.2, 0.25, 0.9)         # steps; rise and run of one step; width of the treads
STAIRS_PLATE = 0.05                  # thickness of the collider plate on the treads


def _stairs(c, m):
    def mesh(bm):
        steps, rise, run, width = STAIRS
        for i in range(steps):
            box(bm, (run, width, 0.03), (-run * (steps - 1) / 2 + i * run, 0, rise * (i + 1) - 0.015), GREY)
        length = math.hypot(run * steps, rise * steps)
        for side in (-1, 1):
            verts = box(bm, (length, 0.03, 0.16), (0, side * (width / 2 + 0.015), rise * steps / 2 - 0.05), GREY)
            rotate(bm, verts, -math.degrees(math.atan2(rise, run)), "Y", (0, 0, rise * steps / 2 - 0.05))
            verts = cyl(bm, 0.02, length, (0, side * (width / 2 + 0.015), rise * steps / 2 + 0.95), "X", 10, YELLOW)
            rotate(bm, verts, -math.degrees(math.atan2(rise, run)), "Y", (0, 0, rise * steps / 2 + 0.95))
            for x, z in ((-run * steps / 2 + 0.05, 0.1), (run * steps / 2 - 0.05, rise * steps - 0.05)):
                box(bm, (0.04, 0.04, 1.0), (x, side * (width / 2 + 0.015), z + 0.5), YELLOW)

    obj(c, m, "Stairs", mesh, bevel=0.0)


def _stairs_colliders():
    """One plate over the treads and the two stringers, in Godot axes. The treads climb towards +X, so the pitch is
    negative (a positive pitch lowers the +X end). The top of the plate runs through the front edges of the treads
    over the whole run, so it is nowhere below a tread; at x = 0 it is at rise * (steps / 2 + 1)."""
    steps, rise, run, width = STAIRS
    length = math.hypot(run * steps, rise * steps)
    a = math.atan2(rise, run)
    pitch = -math.degrees(a)
    t = STAIRS_PLATE                 # the upward normal is (-sin a, cos a): the centre lies t / 2 the other way
    result = [collider((length, t, width), (t / 2 * math.sin(a), rise * (steps / 2 + 1) - t / 2 * math.cos(a), 0.0),
                       pitch=pitch)]
    for z in (-1, 1):                # stringers as modelled, below the plate
        result.append(collider((length, 0.16, 0.03), (0.0, rise * steps / 2 - 0.05, z * (width / 2 + 0.015)), pitch=pitch))
    return result


SAFEGUARD = (2.0, 2.0)               # width over the posts, height


def _safeguard(c, m):
    def mesh(bm):
        width, height = SAFEGUARD
        for x in (-width / 2 + 0.03, width / 2 - 0.03):
            box(bm, (0.06, 0.06, height), (x, 0, height / 2), YELLOW)
            box(bm, (0.16, 0.16, 0.012), (x, 0, 0.006), DARK)
        for z in (0.2, height - 0.03):
            box(bm, (width - 0.12, 0.03, 0.03), (0, 0, z), DARK)
        box(bm, (width - 0.12, 0.006, height - 0.26), (0, 0, (height + 0.17) / 2), GLASS)

    obj(c, m, "Safeguard", mesh)


def _safety_door(c, m):
    def static(bm):
        for x in (-0.53, 0.53):
            box(bm, (0.06, 0.06, 2.0), (x, 0, 1.0), YELLOW)
            box(bm, (0.16, 0.16, 0.012), (x, 0, 0.006), DARK)
        box(bm, (1.12, 0.06, 0.06), (0, 0, 2.0), YELLOW)
        box(bm, (0.08, 0.08, 0.12), (0.53, -0.05, 1.05), DARK)       # door switch

    obj(c, m, "Door_Static", static)

    def leaf(bm):                    # hinged on the left post; origin on the hinge line
        box(bm, (1.0, 0.03, 0.03), (0.5, 0, 0.2), DARK)
        box(bm, (1.0, 0.03, 0.03), (0.5, 0, 1.9), DARK)
        box(bm, (0.03, 0.03, 1.7), (0.985, 0, 1.05), DARK)
        box(bm, (1.0, 0.006, 1.7), (0.5, 0, 1.05), GLASS)
        box(bm, (0.03, 0.06, 0.12), (0.93, -0.03, 1.05), STEEL)

    obj(c, m, "Leaf", leaf, location=(-0.5, 0, 0))


# Signs: the app draws the text itself (a Label3D); these models only give it a place and a thumbnail.
FLOOR_TEXT = (1.0, 0.3)              # text_width along x, text_depth across (Godot z) of the text area on the floor
FLOOR_TEXT_LINE = (0.02, 0.002)      # the dark outline round that area: width of a strip, height above the floor


def _floor_text(c, m):
    def mesh(bm):                    # a thin outline lying on the floor, open inside; origin at the floor, in the middle
        width, depth = FLOOR_TEXT
        line, height = FLOOR_TEXT_LINE
        for side in (-1, 1):
            box(bm, (width, line, height), (0, side * (depth - line) / 2, height / 2), DARK)             # long sides
            box(bm, (line, depth - 2 * line, height), (side * (width - line) / 2, 0, height / 2), DARK)   # ends, between them

    obj(c, m, "FloorText", mesh, bevel=0.0)


SIGN_PANEL = (0.6, 0.4, 0.02)        # panel_width (x), panel_height, panel_thickness from face to face (the light faces)
SIGN_PANEL_CENTRE = 1.0              # panel_center_height: the plate runs from 0.8 to 1.2 m above the floor
SIGN_POST = (0.05, 0.8)              # square post: side, and post_height = from the floor to the lower edge of the plate
SIGN_RIM = 0.015                     # dark rim round each light face, 2 mm below it
SIGN_FOOT = (0.24, 0.012)            # foot plate: side, thickness
SIGN_CLAMP = (0.5, 0.028, 0.012)     # bar that holds the lower edge of the plate: length, depth, how far it reaches up


def _sign_post(c, m):
    width, height, thickness = SIGN_PANEL
    side, post_top = SIGN_POST
    foot, foot_t = SIGN_FOOT
    clamp_l, clamp_d, clamp_up = SIGN_CLAMP

    def static(bm):                  # post on a foot plate, ending in a clamp under the plate; origin at the floor
        box(bm, (foot, foot, foot_t), (0, 0, foot_t / 2), DARK)
        box(bm, (side, side, post_top - foot_t), (0, 0, (post_top + foot_t) / 2), GREY)
        box(bm, (clamp_l, clamp_d, 2 * clamp_up), (0, 0, post_top), DARK)

    obj(c, m, "Sign_Static", static, bevel=0.003)

    def panel(bm):                   # origin in the middle of the plate; the front faces -Y (Godot +Z)
        box(bm, (width, thickness - 0.004, height), (0, 0, 0), DARK)                                # rim and edges
        box(bm, (width - 2 * SIGN_RIM, thickness, height - 2 * SIGN_RIM), (0, 0, 0), WHITE)         # both faces

    obj(c, m, "Panel", panel, location=(0, 0, SIGN_PANEL_CENTRE), bevel=0.001)


def _sign_post_colliders():
    """A thin box round the post and one round the plate, in Godot axes; both on the centre line."""
    width, height, thickness = SIGN_PANEL
    side, post_top = SIGN_POST
    return [collider((side, post_top, side), (0.0, post_top / 2, 0.0)),
            collider((width, height, thickness), (0.0, SIGN_PANEL_CENTRE, 0.0))]


WALKWAYS = [
    asset("handrail_2m", "walkways", "Leuning 2 m", _handrail(2.0), behaviour="passive", length=2.0,
          colliders=_handrail_colliders(2.0)),
    asset("platform_2x2", "walkways", "Bordes 2 × 2 m", _platform, behaviour="passive",
          deck_height=PLATFORM_DECK, colliders=_platform_colliders()),
    asset("stairs", "walkways", "Trap", _stairs, behaviour="passive", rise=1.0, colliders=_stairs_colliders()),
    # One thin box from post to post and from the floor to the top, as deep as the rails; no door.
    asset("safeguard_2m", "walkways", "Veiligheidshek 2 m", _safeguard, behaviour="passive", length=2.0,
          colliders=[collider((SAFEGUARD[0], SAFEGUARD[1], 0.03), (0.0, SAFEGUARD[1] / 2, 0.0))]),
    # A latching control with a normally closed contact that swings open when clicked: TRUE while the door is shut.
    asset("safety_door", "walkways", "Veiligheidsdeur", _safety_door, behaviour="button",
          latching=1, normallyClosed=1, door=1, swing=90, pickHeight=1.05, post=0),
    # Text in the scene, no tags: the app lays a Label3D in the outline, or on both faces of Panel.
    asset("floor_text", "walkways", "Vloertekst", _floor_text, behaviour="sign",
          text_width=FLOOR_TEXT[0], text_depth=FLOOR_TEXT[1]),
    asset("sign_post", "walkways", "Bordje", _sign_post, behaviour="sign", panel_width=SIGN_PANEL[0],
          panel_height=SIGN_PANEL[1], panel_thickness=SIGN_PANEL[2], panel_center_height=SIGN_PANEL_CENTRE,
          post_height=SIGN_POST[1], colliders=_sign_post_colliders()),
]

# ---------------------------------------------------------------------------- stations
# Origin: on the floor, centre of the footprint. Simplified: enough to read what it is and
# with the moving assemblies as separate objects, but no behaviour in the simulator yet.


# Machining center, own design. Seen from above (x, y; Godot z = -y): the open side is -x.
#   - parts ride in on a driven deck at y = -lane and stop against a blade at the pick position;
#   - the robot stands between the two decks and turns about a vertical axis; its gripper is
#     `reach` from that axis, so pick position, machine table and drop position lie on one circle;
#   - the machine table is at angle 0 (+x), under the spindle;
#   - finished parts are set down on the deck at y = +lane, which runs outwards.
MACHINING = dict(surface=BELT_TOP, base_x=-0.55, reach=0.76, lane=0.6, lane_width=0.8, lift=0.15, tip=BELT_TOP + 0.30,
                 spindle=BELT_TOP + 0.55)
MACHINING["pick_x"] = MACHINING["base_x"] - math.sqrt(MACHINING["reach"] ** 2 - MACHINING["lane"] ** 2)
MACHINING["entry_angle"] = 180.0 - math.degrees(math.atan2(MACHINING["lane"], MACHINING["base_x"] - MACHINING["pick_x"]))
MACHINING["stop_x"] = MACHINING["pick_x"] + 0.125 + 0.02      # centre of the stop blade; parts are 0.25 m long


def _machining_center(c, m):
    surface, base_x, reach = MACHINING["surface"], MACHINING["base_x"], MACHINING["reach"]
    lane, width = MACHINING["lane"], MACHINING["lane_width"]
    table_x = base_x + reach

    def static(bm):
        tube_frame(bm, -1.5, 1.5, -1.2, 1.2, 0.0, 2.3, size=0.08, mat=YELLOW)
        for y in (-1.2, 1.2):
            box(bm, (2.84, 0.01, 1.9), (0, y, 1.2), GLASS)
        box(bm, (0.01, 2.24, 1.9), (1.5, 0, 1.2), GLASS)
        cabinet(bm, (1.0, 1.6, 2.1), (0.9, 0, 1.05))                        # CNC machine
        box(bm, (0.5, 0.34, 0.6), (0.3, 0, 1.85), PANEL)                    # spindle head
        box(bm, (0.5, 0.5, surface - 0.02), (table_x - 0.01, 0, (surface - 0.02) / 2), DARK)    # machine table
        box(bm, (0.42, 0.42, 0.02), (table_x - 0.01, 0, surface - 0.01), STEEL)
        box(bm, (0.02, 0.4, 0.3), (0.39, -0.55, 1.4), DARK)                 # operator panel
        box(bm, (0.004, 0.3, 0.18), (0.378, -0.55, 1.43), WHITE)
        for y, mat in ((-lane, GREEN), (lane, BLUE)):                       # entry and exit decks
            box(bm, (0.65, width, 0.04), (-1.175, y, surface - 0.02), STEEL)
            box(bm, (0.65, 0.03, 0.06), (-1.175, y - width / 2 + 0.015, surface - 0.03), mat)
            box(bm, (0.65, 0.03, 0.06), (-1.175, y + width / 2 - 0.015, surface - 0.03), mat)
            for x in (-1.44, -0.91):
                for side in (-1, 1):
                    leg(bm, x, y + side * (width / 2 - 0.06), surface - 0.04, 0.0)
        box(bm, (0.04, width, 0.1), (MACHINING["stop_x"], -lane, surface + 0.05), YELLOW)    # stop at the pick position
        cyl(bm, 0.18, 0.35, (base_x, 0, 0.175), "Z", 20, DARK)              # robot base

    obj(c, m, "Machining_Static", static)

    def arm(bm):                     # turns about its origin; the gripper pad is `reach` out, underside at z = 0.75
        cyl(bm, 0.1, 1.13, (0, 0, 0.515), "Z", 16, ORANGE)
        box(bm, (reach + 0.1, 0.1, 0.1), (reach / 2, 0, 1.03), ORANGE)
        box(bm, (0.06, 0.06, 0.2), (reach, 0, 0.88), STEEL)
        box(bm, (0.2, 0.2, 0.03), (reach, 0, 0.765), BLACK)

    obj(c, m, "RobotArm", arm, location=(base_x, 0, MACHINING["tip"] - 0.75))

    def spindle(bm):                 # origin at the tool tip; the app moves it down onto the part
        cyl(bm, 0.05, 0.65, (0, 0, 0.425), "Z", 16, STEEL)
        cyl(bm, 0.015, 0.1, (0, 0, 0.05), "Z", 10, DARK)
        box(bm, (0.012, 0.03, 0.02), (0, 0.022, 0.01), ORANGE)              # cutter; shows the rotation

    obj(c, m, "Spindle", spindle, location=(table_x, 0, MACHINING["spindle"]), bevel=0.0)
    for name, colour, x in (("Lamp_Green", "green", 1.3), ("Lamp_Yellow", "yellow", 1.15), ("Lamp_Red", "red", 1.0)):
        lamp(c, m, name, colour, lambda bm: cyl(bm, 0.05, 0.1, (0, 0, 0), "Z", 14, 0), location=(x, -1.1, 2.38))


def _elevator(c, m):
    height = 4.0

    def static(bm):
        tube_frame(bm, -0.9, 0.9, -0.85, 0.85, 0.0, height, size=0.1, mat=GREY)
        box(bm, (0.5, 0.4, 0.35), (0, 0, height + 0.15), DARK)              # hoist
        for x in (-0.9, 0.9):
            box(bm, (0.04, 0.2, height - 0.2), (x * 0.94, 0, height / 2), YELLOW)   # guides

    obj(c, m, "Elevator_Static", static)

    def platform(bm):                # travels up and down; carries a short roller bed
        box(bm, (1.6, ROLLER_WIDTH + 0.1, 0.1), (0, 0, -0.1), GREY)
        for i in range(11):
            cyl(bm, 0.045, ROLLER_WIDTH * 0.94, (-0.65 + i * 0.13, 0, -0.045), "Y", 12, STEEL)

    obj(c, m, "Platform", platform, location=(0, 0, ROLLER_TOP), bevel=0.0)


# Sized for two belt conveyors side by side under the frame, running across it (along Y):
# the picker rests above the one at x = -rest and carries to the one at x = +rest.
GANTRY_2 = dict(sx=2.2, sy=0.9, top=2.0, rest=0.5)
GANTRY_3 = dict(sx=2.6, sy=1.8, top=2.1, rest=0.75)
PICKER_REACH = 0.78                  # from the picker origin down to the suction cups


def _gantry(two_axis):
    def build(c, m):
        size = GANTRY_2 if two_axis else GANTRY_3
        sx, sy, top, rest = size["sx"], size["sy"], size["top"], size["rest"]

        def static(bm):
            tube_frame(bm, -sx / 2, sx / 2, -sy / 2, sy / 2, 0.0, top, size=0.08, mat=GREY)
            for y in (-sy / 2, sy / 2):
                box(bm, (sx, 0.05, 0.03), (0, y, top + 0.015), STEEL)       # linear rails

        obj(c, m, "Gantry_Static", static)

        def beam(bm):                # X axis: runs along the frame
            box(bm, (0.14, sy + 0.1, 0.12), (0, 0, 0), YELLOW)

        obj(c, m, "Beam", beam, location=(-rest, 0, top + 0.09))

        def carriage(bm):            # Y axis: runs along the beam
            box(bm, (0.2, 0.2, 0.18), (0, 0, 0), DARK)

        obj(c, m, "Carriage", carriage, location=(-rest, 0, top + 0.09))

        def picker(bm):              # Z axis with a vacuum gripper
            box(bm, (0.07, 0.07, 0.9), (0, 0, -0.45), STEEL)
            box(bm, (0.22, 0.22, 0.04), (0, 0, -0.92), BLACK)
            for x in (-0.07, 0.07):
                for y in (-0.07, 0.07):
                    cyl(bm, 0.025, 0.04, (x, y, -0.96), "Z", 10, BLUE)

        obj(c, m, "Picker", picker, location=(-rest, 0, top + 0.2))

    return build


# Stacker crane and rack, own design. Seen from above (x = along the aisle, y = across; Godot z = -y):
#   - the rack stands on +y (Godot -z), centred rack_offset (on the 0.25 m editor grid) from the rail; the in and out roller
#     conveyors reach the aisle from -y (Godot +z), their pallets centred rack_offset from the rail too;
#   - pallets lie with their long side (1.2 m) across the aisle, 0.8 m along it, on two beams per slot;
#   - the forks reach fork_stroke to either side, so a pallet that sits on them ends up centred on a slot.
# The moving parts have their origin on the top face of the lowered forks (Cart: on the floor); the app
# moves them to the positions the crane logic reports.
STACKER = dict(column_spacing=1.5, level_height=1.2, first_level_top=0.6, clearance=0.03, fork_stroke=1.5,
               fork_length=1.7, fork_spacing=0.5, fork_width=0.1, fork_thickness=0.04, platform_depth=1.4,
               platform_width=0.94, mast_gap=1.0, rack_offset=1.5, lift_stroke=0.10, mast_height=5.3,
               beam_spacing=0.68, beam_width=0.05, beam_height=0.1, rack_depth=1.3, rack_posts=0.08)


def _stacker_crane(c, m):
    s = STACKER
    mast_x = s["mast_gap"] / 2 + 0.07

    def static(bm):                  # floor rail the cart runs on, with end stops
        box(bm, (6.0, 0.12, 0.06), (0, 0, 0.03), STEEL)
        for x in (-2.95, 2.95):
            box(bm, (0.1, 0.3, 0.2), (x, 0, 0.1), YELLOW)

    obj(c, m, "Crane_Static", static)

    def cart(bm):                    # travels along the aisle with the two masts
        box(bm, (1.5, 0.6, 0.2), (0, 0, 0.16), YELLOW)
        for x in (-mast_x, mast_x):
            box(bm, (0.14, 0.14, s["mast_height"]), (x, 0, 0.26 + s["mast_height"] / 2), GREY)
        box(bm, (2 * mast_x + 0.14, 0.2, 0.14), (0, 0, 0.33 + s["mast_height"]), GREY)
        box(bm, (0.4, 0.5, 0.5), (0.0, -0.05, 0.5), DARK)

    obj(c, m, "Cart", cart)

    def platform(bm):                # lifts along the mast; the forks run on it
        box(bm, (s["platform_width"], s["platform_depth"], 0.08), (0, 0, -s["fork_thickness"] - 0.04), ORANGE)

    obj(c, m, "Platform", platform)

    def forks(bm):                   # two fork bars, longer than the platform, top face at z = 0
        for x in (-s["fork_spacing"] / 2, s["fork_spacing"] / 2):
            box(bm, (s["fork_width"], s["fork_length"], s["fork_thickness"]), (x, 0, -s["fork_thickness"] / 2), STEEL)

    obj(c, m, "Forks", forks, bevel=0.002)


def _rack(c, m):
    s = STACKER
    width = 3 * s["column_spacing"]
    depth = s["rack_depth"]

    def mesh(bm):                    # one bay: 3 slots wide, 4 levels, two beams under every slot
        top = s["first_level_top"] + 3 * s["level_height"] + 1.1
        for i in range(4):
            x = -width / 2 + i * s["column_spacing"]
            for y in (-depth / 2, depth / 2):
                box(bm, (s["rack_posts"], s["rack_posts"], top), (x, y, top / 2), BLUE)
                box(bm, (0.18, 0.18, 0.012), (x, y, 0.006), DARK)
        for level in range(4):
            support = s["first_level_top"] + level * s["level_height"]
            for y in (-depth / 2, depth / 2):        # cross beams, lower than the supports so the forks pass over them
                box(bm, (width, 0.05, s["beam_height"]), (0, y, support - 1.5 * s["beam_height"]), ORANGE)
            for column in range(3):
                cx = -width / 2 + (column + 0.5) * s["column_spacing"]
                for side in (-1, 1):
                    box(bm, (s["beam_width"], depth, s["beam_height"]),
                        (cx + side * s["beam_spacing"] / 2, 0, support - s["beam_height"] / 2), ORANGE)

    obj(c, m, "Rack", mesh)


# Palletizer, own design. Seen from above (x = travel, y = across; Godot z = -y):
#   - boxes arrive on a driven lane at y = +0.5 and queue against the end stop at x = +0.46;
#   - the pusher shoves each row of three off the lane onto the layer plate (two rows: y -0.3..0.3);
#   - the plate is two leaves that slide apart along x, dropping the layer between the fences
#     onto the pallet on the lifting table underneath.
PALLETIZER = dict(deck=1.9, table_length=2.5, table_width=1.0, lane=0.5, push_stroke=0.37, leaf_stroke=0.5)


def _palletizer(c, m):
    deck = PALLETIZER["deck"]

    def static(bm):
        tube_frame(bm, -1.3, 1.3, -1.2, 1.2, 0.0, 3.0, size=0.1, mat=GREY)
        box(bm, (2.6, 0.01, 1.0), (0, -1.2, 2.45), GLASS)
        box(bm, (0.5, 0.4, 0.3), (0, 0, 3.15), DARK)                        # hoist
        for y in (0.72, -0.42):                                             # beams under the deck
            box(bm, (2.5, 0.05, 0.04), (0, y, deck - 0.06), GREY)
        for x in (-1.25, 1.25):
            box(bm, (0.05, 2.3, 0.04), (x, 0, deck - 0.06), GREY)
        box(bm, (1.71, 0.365, 0.04), (-0.395, 0.5075, deck - 0.02), STEEL)  # infeed lane
        box(bm, (0.04, 1.05, 0.145), (0.48, 0.165, deck + 0.0775), YELLOW)  # end stop
        box(bm, (0.04, 0.685, 0.145), (-0.52, -0.0175, deck + 0.0775), YELLOW)   # back fence
        box(bm, (1.04, 0.04, 0.145), (-0.02, -0.34, deck + 0.0775), YELLOW)      # side fence
        box(bm, (0.71, 0.02, 0.1), (-0.895, 0.33, deck + 0.055), GREY)      # lane guides
        box(bm, (0.79, 0.02, 0.1), (-0.855, 0.68, deck + 0.055), GREY)

    obj(c, m, "Palletizer_Static", static)
    obj(c, m, "Pusher", lambda bm: (box(bm, (0.88, 0.04, 0.15), (0, 0, 0), YELLOW),
                                    cyl(bm, 0.03, 0.45, (0, 0.245, 0), "Y", 12, STEEL)),
        location=(0, 0.69, deck + 0.085))
    for name, x in (("Leaf_A", -0.25), ("Leaf_B", 0.25)):
        obj(c, m, name, lambda bm: box(bm, (0.5, 0.685, 0.03), (0, 0, 0), STEEL), location=(x, -0.0175, deck - 0.015))

    def table(bm):                   # lifting table with a roller bed; the pallet rides in on it
        box(bm, (PALLETIZER["table_length"], PALLETIZER["table_width"], 0.1), (0, 0, -0.1), ORANGE)
        for i in range(19):
            cyl(bm, 0.045, PALLETIZER["table_width"] * 0.94, (-1.17 + i * 0.13, 0, -0.045), "Y", 12, STEEL)

    obj(c, m, "ElevatorTable", table, location=(0, 0, ROLLER_TOP), bevel=0.0)


def _pallet_emitter(c, m):
    """Portal over a roller conveyor where pallets appear. Origin: on the rollers, under the hopper."""
    floor = -ROLLER_TOP

    def static(bm):
        for x in (-0.75, 0.75):
            for y in (-0.8, 0.8):
                leg(bm, x, y, 0.9, floor, size=0.06)
        box(bm, (1.62, 1.72, 0.3), (0, 0, 1.05), PANEL)                     # hopper
        box(bm, (1.63, 1.73, 0.05), (0, 0, 0.925), YELLOW)                  # safety band
        box(bm, (1.3, 0.9, 0.02), (0, 0, 0.895), BLACK)                     # dark outlet underneath
        box(bm, (0.1, 0.1, 0.02), (0.65, 0.7, 1.21), DARK)                  # lamp base

    obj(c, m, "Emitter_Static", static)
    lamp(c, m, "EmitterLamp", "green", lambda bm: cyl(bm, 0.04, 0.07, (0, 0, 0), "Z", 16, 0),
         location=(0.65, 0.7, 1.255))


def _item_emitter(band):
    """The box emitter portal with a band in the colour of the item it emits."""
    def build(c, m):
        from . import base as b
        b.build_emitter_gantry(c, m)
        obj(c, m, "Band", lambda bm: box(bm, (0.76, 1.24, 0.08), (0, 0, 0.98), band), bevel=0.002)

    return build


SIMULATION = [
    asset("emitter_pallet", "simulation", "Emitter (pallets)", _pallet_emitter, behaviour="emitter",
          origin_height=ROLLER_TOP, emits="pallet", interval=10.0, itemMass=15.0, volumeLength=1.3, volumeWidth=0.9,
          maxLength=1.3, maxWidth=1.1, maxHeight=1.3),
]
for _name, _label, _colour in (("blue", "blauw", BLUE), ("green", "groen", GREEN), ("metal", "metaal", STEEL)):
    SIMULATION.append(asset("emitter_raw_" + _name, "simulation", "Emitter (grondstof " + _label + ")",
                            _item_emitter(_colour), behaviour="emitter", origin_height=BELT_TOP,
                            emits="raw_material_" + _name, interval=6.0, itemMass=2.0, volumeLength=0.5, volumeWidth=0.5,
                            maxLength=0.7, maxWidth=0.7, maxHeight=0.8))


def _tank(c, m):
    def static(bm):
        cyl(bm, 1.0, 3.0, (0, 0, 1.9), "Z", 32, PANEL)
        cyl(bm, 1.03, 0.08, (0, 0, 3.4), "Z", 32, GREY)
        for a in range(0, 360, 90):
            x, y = 0.8 * math.cos(math.radians(a + 45)), 0.8 * math.sin(math.radians(a + 45))
            leg(bm, x, y, 0.4, 0.0, size=0.1)
        cyl(bm, 0.125, 1.2, (-1.5, 0, 3.0), "X", 16, BLUE)                  # fill pipe
        cyl(bm, 0.125, 1.0, (1.4, 0, 0.6), "X", 16, BLUE)                   # discharge pipe
        for x, z in ((-1.2, 3.0), (1.2, 0.6)):                              # valves
            box(bm, (0.2, 0.3, 0.3), (x, 0, z), DARK)
            cyl(bm, 0.12, 0.03, (x, -0.2, z), "Y", 14, RED)
        box(bm, (0.12, 0.02, 2.6), (0, -1.0, 1.9), DARK)                    # sight glass frame

    obj(c, m, "Tank_Static", static, bevel=0.0)

    def level(bm):                   # liquid column in the sight glass; the app scales it in height
        box(bm, (0.08, 0.012, 1.0), (0, 0, 0.5), BLUE)

    obj(c, m, "Level", level, location=(0, -1.012, 0.6), bevel=0.0)


# Six-axis robot arm, own design in the style of the library, sized like a small arm with 0.58 m reach (the
# source of the sizes is in docs/ASSETS.md). Modelled in the home pose: upper arm straight up, forearm forward
# along +x, wrist bent so the tool points down. Every part is its own object, NOT parented into a chain, with
# its origin on the joint it turns about and its axes along the world axes. The app places each part every
# tick from the kinematics (RobotKinematics.Forward), so only the home pose and the origins matter:
#   Base, Link_1   the foot (axis 1, vertical)              Link_4   the forearm axis, elbow_offset above axis 3
#   Link_2         axis 2, base_height above the foot       Link_5   the wrist centre, forearm ahead of Link_4
#   Link_3         axis 3, upper_arm above axis 2           Link_6   the flange, wrist_to_flange below the wrist
# Axes 2, 3 and 5 run across the arm (Blender y, Godot -z), axis 4 along it (x), axes 1 and 6 vertically.
# The rim of the suction cup is tool_length below the flange face. The foot stands on the pedestal; the app
# stretches Pedestal in height to the pedestal setting, so it is a plain column.
# Seen from the front (-y), the upper arm runs beside the shoulder on the -y side; from the elbow on everything
# stays at y >= -ROBOT_CORE (bar the 4 mm axis 5 caps), so the forearm and the wrist swing past the upper arm.
ROBOT = dict(base_height=0.290, upper_arm=0.270, elbow_offset=0.070, forearm=0.302, wrist_to_flange=0.072,
             tool_length=0.050, pedestal=0.75)
ROBOT_ARM_Y = (-0.100, -0.040)       # the upper arm, across the arm
ROBOT_CORE = 0.035                   # half-width of the shoulder side, elbow, forearm and wrist


def robot_joints():
    """Origin of each part of robot_6axis in the home pose, in Blender axes (foot on the pedestal)."""
    r = ROBOT
    foot = r["pedestal"]
    axis_2 = foot + r["base_height"]
    axis_3 = axis_2 + r["upper_arm"]
    axis_4 = axis_3 + r["elbow_offset"]
    return {"Pedestal": (0.0, 0.0, 0.0), "Base": (0.0, 0.0, foot), "Link_1": (0.0, 0.0, foot),
            "Link_2": (0.0, 0.0, axis_2), "Link_3": (0.0, 0.0, axis_3), "Link_4": (0.0, 0.0, axis_4),
            "Link_5": (r["forearm"], 0.0, axis_4), "Link_6": (r["forearm"], 0.0, axis_4 - r["wrist_to_flange"])}


def _robot_6axis(c, m):
    r = ROBOT
    joint = robot_joints()
    arm_y0, arm_y1 = ROBOT_ARM_Y
    core = ROBOT_CORE
    axis_2, upper, kink = r["base_height"], r["upper_arm"], r["elbow_offset"]
    forearm, flange, tool = r["forearm"], r["wrist_to_flange"], r["tool_length"]
    assert tool > 0.04 and flange > 0.06, "the wrist and the cup below are drawn for these sizes"

    def pedestal(bm):                # floor to the foot: foot plate, column, top plate with its face at the foot
        height = r["pedestal"]
        box(bm, (0.34, 0.34, 0.02), (0, 0, 0.01), DARK)
        box(bm, (0.22, 0.22, height - 0.04), (0, 0, height / 2), GREY)
        box(bm, (0.30, 0.30, 0.02), (0, 0, height - 0.01), DARK)

    # Cylinders here have at least 10 segments: with fewer, the bevel (40 degrees) also cuts the side edges.
    def base(bm):                    # fixed: foot plate, housing, bearing of axis 1, cable connectors at the back
        corners = [(x, y) for x in (-0.075, 0.075) for y in (-0.075, 0.075)]
        slab(bm, [p for x, y in corners for p in circle(x, y, 0.02, 12)], "Z", 0.0, 0.018, ORANGE)
        cyl(bm, 0.085, 0.077, (0, 0, 0.0565), "Z", 20, ORANGE)         # housing, up to 0.095
        cyl(bm, 0.078, 0.009, (0, 0, 0.0995), "Z", 20, DARK)           # bearing ring, 0.095 .. 0.104
        box(bm, (0.035, 0.08, 0.05), (-0.0875, 0, 0.05), DARK)
        for y in (-0.018, 0.018):
            cyl(bm, 0.011, 0.014, (-0.112, y, 0.05), "X", 10, BLACK)

    def link_1(bm):                  # turns about the vertical axis: turret, shoulder around axis 2, axis 2 motor
        cyl(bm, 0.085, 0.07, (0, 0, 0.14), "Z", 20, ORANGE)            # 0.105 .. 0.175
        slab(bm, circle(0, axis_2, 0.065) + [(-0.068, 0.15), (0.068, 0.15)], "Y", -core, 0.05, ORANGE)
        cyl(bm, 0.048, 0.035, (0, 0.0675, axis_2), "Y", 16, DARK)      # on the +y side, 0.05 .. 0.085

    def link_2(bm):                  # upper arm beside the shoulder, axis 2 up to axis 3; caps on the outer face
        slab(bm, circle(0, 0, 0.062) + circle(0, upper, 0.048), "Y", arm_y0, arm_y1, ORANGE)
        cyl(bm, 0.042, 0.008, (0, arm_y0 - 0.004, 0), "Y", 16, DARK)
        cyl(bm, 0.032, 0.008, (0, arm_y0 - 0.004, upper), "Y", 16, DARK)

    def link_3(bm):                  # elbow housing around axis 3, up to the forearm root; axis 4 motor at the back
        slab(bm, circle(0, 0, 0.05) + [(-0.072, kink + 0.044), (0.052, kink + 0.044), (-0.072, 0.02)],
             "Y", -core, 0.045, ORANGE)
        cyl(bm, 0.03, 0.016, (-0.08, 0, kink), "X", 16, DARK)
        cyl(bm, 0.034, 0.007, (0, 0.0485, 0), "Y", 16, DARK)           # cap of axis 3 on the +y face

    def link_4(bm):                  # forearm: turns about its length (x) and ends in a fork around axis 5
        cyl(bm, 0.036, 0.008, (0.058, 0, 0), "X", 16, DARK)            # 0.054 .. 0.062, clear of the elbow
        frustum(bm, 0.034, 0.027, 0.062, forearm - 0.087, "X", segments=16, mat=ORANGE)
        box(bm, (0.04, 2 * core - 0.002, 0.054), (forearm - 0.077, 0, 0), ORANGE)
        for side in (-1, 1):         # the prongs leave 2 mm on either side of the wrist
            slab(bm, circle(forearm, 0, 0.03, 16) + [(forearm - 0.067, -0.027), (forearm - 0.067, 0.027)], "Y",
                 side * 0.022, side * (core - 0.001), ORANGE)
            cyl(bm, 0.018, 0.005, (forearm, side * (core + 0.0015), 0), "Y", 12, DARK)

    def link_5(bm):                  # wrist: turns about axis 5 between the prongs; neck down towards the flange
        cyl(bm, 0.027, 0.04, (0, 0, 0), "Y", 16, ORANGE)
        cyl(bm, 0.02, flange - 0.018, (0, 0, -(flange - 0.018) / 2), "Z", 16, ORANGE)
        cyl(bm, 0.023, 0.008, (0, 0, -(flange - 0.014)), "Z", 16, DARK)   # bearing of axis 6, 2 mm above Link_6

    def link_6(bm):                  # flange disc (its face at the origin), adapter, bellows and suction cup
        cyl(bm, 0.02, 0.008, (0, 0, 0.004), "Z", 16, STEEL)
        cyl(bm, 0.015, 0.012, (0, 0, -0.006), "Z", 12, DARK)
        for i, radius in enumerate((0.012, 0.0145, 0.012, 0.0145)):
            cyl(bm, radius, 0.006, (0, 0, -0.015 - 0.006 * i), "Z", 12, BLUE)
        frustum(bm, 0.011, 0.021, -0.036, -tool, "Z", segments=12, mat=BLUE)    # the rim ends exactly at -tool

    for name, build, bevel in (("Pedestal", pedestal, 0.004), ("Base", base, 0.003), ("Link_1", link_1, 0.004),
                               ("Link_2", link_2, 0.004), ("Link_3", link_3, 0.004), ("Link_4", link_4, 0.003),
                               ("Link_5", link_5, 0.0), ("Link_6", link_6, 0.0)):
        obj(c, m, name, build, location=joint[name], bevel=bevel)


STATIONS = [
    asset("machining_center", "stations", "Bewerkingscentrum", _machining_center, behaviour="machining",
          surface=MACHINING["surface"], baseX=MACHINING["base_x"], reach=MACHINING["reach"], lane=MACHINING["lane"],
          laneWidth=MACHINING["lane_width"], pickX=round(MACHINING["pick_x"], 4), stopX=round(MACHINING["stop_x"], 4),
          entryAngle=round(MACHINING["entry_angle"], 3), tipHeight=MACHINING["tip"], lift=MACHINING["lift"],
          spindleHeight=MACHINING["spindle"], swingSpeed=120.0, liftTime=0.4, machineTime=4.0, deckSpeed=0.4),
    asset("elevator", "stations", "Lift", _elevator, behaviour="elevator", travel=3.0, speed=0.5,
          rollSpeed=0.45, base=ROLLER_TOP, length=1.6, width=ROLLER_WIDTH + 0.1),
    asset("pick_and_place", "stations", "Pick & place (3 assen)", _gantry(False), behaviour="pick_place",
          restX=-GANTRY_3["rest"], tipHeight=GANTRY_3["top"] + 0.2 - 0.98, strokeX=2 * GANTRY_3["rest"], strokeY=0.5,
          strokeZ=PICKER_REACH, speed=1.0, speedZ=0.6),
    asset("two_axis_pick_and_place", "stations", "Pick & place (2 assen)", _gantry(True),
          behaviour="pick_place", restX=-GANTRY_2["rest"], tipHeight=GANTRY_2["top"] + 0.2 - 0.98,
          strokeX=2 * GANTRY_2["rest"], strokeY=0.0, strokeZ=0.7, speed=1.0, speedZ=0.6),
    asset("stacker_crane", "stations", "Magazijnkraan", _stacker_crane, behaviour="stacker_crane",
          columnSpacing=STACKER["column_spacing"], levelHeight=STACKER["level_height"],
          firstLevelTop=STACKER["first_level_top"], transferTop=ROLLER_TOP, clearance=STACKER["clearance"],
          forkStroke=STACKER["fork_stroke"], forkLength=STACKER["fork_length"], forkSpacing=STACKER["fork_spacing"],
          forkWidth=STACKER["fork_width"], forkThickness=STACKER["fork_thickness"],
          platformDepth=STACKER["platform_depth"], platformWidth=STACKER["platform_width"],
          mastGap=STACKER["mast_gap"], rackOffset=STACKER["rack_offset"], liftStroke=STACKER["lift_stroke"],
          driveSpeed=0.5, hoistSpeed=0.4, forkSpeed=0.7, liftSpeed=0.1),
    asset("rack", "stations", "Magazijnstelling", _rack, behaviour="rack",
          columnSpacing=STACKER["column_spacing"], levelHeight=STACKER["level_height"],
          firstLevelTop=STACKER["first_level_top"], beamSpacing=STACKER["beam_spacing"],
          beamWidth=STACKER["beam_width"], beamHeight=STACKER["beam_height"], depth=STACKER["rack_depth"]),
    asset("palletizer", "stations", "Palletiseerder", _palletizer, behaviour="palletizer",
          deck=PALLETIZER["deck"], base=ROLLER_TOP, tableTravel=1.25, tableSpeed=0.3, rollSpeed=0.45,
          tableLength=PALLETIZER["table_length"], tableWidth=PALLETIZER["table_width"], feedSpeed=0.4,
          pushStroke=PALLETIZER["push_stroke"], pushSpeed=0.4, leafStroke=PALLETIZER["leaf_stroke"], leafSpeed=1.0),
    asset("tank", "stations", "Tank", _tank, behaviour="tank", height=3.0, diameter=2.0, maxInflow=0.2,
          maxOutflow=0.2),
    asset("robot_6axis", "stations", "Robotarm 6 assen", _robot_6axis, behaviour="robot",
          baseHeight=ROBOT["base_height"], upperArm=ROBOT["upper_arm"], elbowOffset=ROBOT["elbow_offset"],
          forearm=ROBOT["forearm"], wristToFlange=ROBOT["wrist_to_flange"], toolLength=ROBOT["tool_length"],
          speed=40.0, pedestal=ROBOT["pedestal"]),
]
