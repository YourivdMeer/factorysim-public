"""Builds the whole FactorySim asset library.

From the repo root:

    blender --background --factory-startup --python assets/blender_scripts/build_assets.py

Options after "--":
    --only=<id>[,<id>...]   rebuild just these assets (the catalogue then lists only those)
    --no-thumbnails         skip thumbnails and contact sheets

Output:
    assets/models/*.glb                    the first MVP models (flat folder, kept for the app)
    assets/library/<category>/<id>.glb     the library
    assets/library/catalog.json            what is in the library and how each asset behaves
    src/FactorySim.App/models, library     copies Godot imports, plus 128 px palette thumbnails
    docs/assets-preview/library_*.png      one contact sheet per category
    assets/source/factorysim_assets.blend  Blender source

The generator is split into modules (assets/blender_scripts/fslib):
    base.py             scene handling, materials, geometry helpers, export, thumbnails, MVP models
    kit.py              shared building kit and module sizes
    parts_transport.py  items, light load, heavy load
    parts_plant.py      sensors, operators, warning devices, walkways, stations
"""

import json
import math
import os
import shutil
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from fslib import base, kit, parts_plant, parts_transport  # noqa: E402

CATEGORIES = [
    ("items", "Items"),
    ("light_load", "Light load"),
    ("heavy_load", "Heavy load"),
    ("sensors", "Sensors"),
    ("operators", "Operators"),
    ("stations", "Stations"),
    ("warning_devices", "Warning devices"),
    ("walkways", "Walkways"),
    ("simulation", "Simulation"),
]

# The models built for the MVP scene keep their files; this is their library metadata.
# internal = used by other parts, not offered in the palette.
MVP_META = {
    "box_low": dict(category="items", name="Doos laag (MVP)", behaviour="item"),
    "box_high": dict(category="items", name="Doos hoog (MVP)", behaviour="item"),
    "conveyor_straight_6m": dict(category="light_load", name="Bandtransporteur 6 m (MVP)",
                                 behaviour="conveyor", origin_height=0.80, params=dict(length=6.0, width=0.8, speed=0.6)),
    "conveyor_straight_1m6_wide": dict(category="light_load", name="Brede band 1,6 m (MVP)",
                                       behaviour="conveyor", origin_height=0.79, params=dict(length=1.6, width=1.2, speed=0.6)),
    "guide_rail_1m": dict(category="simulation", name="Geleiderail", behaviour="rail", origin_height=0.84,
                          params=dict(length=1.0)),
    "rail_bracket": dict(category="simulation", name="Railbeugel", internal=True),
    "pusher": dict(category="light_load", name="Pusher", behaviour="pusher", origin_height=0.945,
                   params=dict(stroke=0.88, speed=1.0)),
    "photo_eye": dict(category="sensors", name="Photo-eye (MVP)", internal=True),
    "sensor_post": dict(category="sensors", name="Sensorpaal", internal=True),
    "emitter_gantry": dict(category="simulation", name="Emitter", behaviour="emitter", origin_height=0.80,
                           params=dict(interval=2.5, maxLength=0.7, maxWidth=0.7, maxHeight=0.8)),
    "remover_hood_1m0": dict(category="simulation", name="Remover (band 0,8 m)", behaviour="remover",
                             origin_height=0.80, params=dict(width=1.2, depth=0.6)),
    "remover_hood_1m4": dict(category="simulation", name="Remover (band 1,2 m)", behaviour="remover",
                             origin_height=0.80, params=dict(width=1.6, depth=0.6)),
    "support_beam": dict(category="simulation", name="Steunbalk", internal=True),
    "floor_14x10": dict(category="simulation", name="Vloer 14 × 10 m", internal=True),
}

LIBRARY = (parts_transport.ITEMS + parts_transport.LIGHT_LOAD + parts_transport.HEAVY_LOAD + parts_plant.SENSORS
           + parts_plant.OPERATORS + parts_plant.SIMULATION + parts_plant.STATIONS + parts_plant.WARNING + parts_plant.WALKWAYS)


def bounds(collection):
    """Bounding box of an asset in Godot axes (x, y up, z = -Blender y)."""
    corners = [obj.matrix_world @ Vector(c) for obj in collection.objects for c in obj.bound_box]
    low = [min(c[i] for c in corners) for i in range(3)]
    high = [max(c[i] for c in corners) for i in range(3)]
    r = lambda v: round(v, 4)  # noqa: E731
    return ([r(low[0]), r(low[2]), r(-high[1])], [r(high[0]), r(high[2]), r(-low[1])])


def entry(asset_id, collection, category, name, behaviour, origin_height, params, internal, legacy, triangles,
          emits=None, colliders=None):
    low, high = bounds(collection)
    file = "models/{}.glb".format(asset_id) if legacy else "library/{}/{}.glb".format(category, asset_id)
    result = {
        "id": asset_id,
        "name": name,
        "category": category,
        "behaviour": behaviour,
        "internal": internal,
        "file": file,
        "thumbnail": "library/thumbs/{}.png".format(asset_id),
        "originHeight": origin_height,
        "boundsMin": low,
        "boundsMax": high,
        "size": [round(high[i] - low[i], 4) for i in range(3)],
        "parts": sorted(o["fs_part"] for o in collection.objects),
        "triangles": triangles,
        "params": params,
    }
    if emits:
        result["emits"] = emits
    if colliders:
        result["colliders"] = colliders
    return result


def build_library(only=None):
    scene = base.get_scene()
    materials = kit.extend_materials(base.build_materials())
    catalogue = []

    for asset_id, builder in base.ASSETS:
        if only and asset_id not in only:
            continue
        meta = MVP_META[asset_id]
        collection = base.fresh_collection(scene, asset_id)
        builder(collection, materials)
        bpy.context.view_layer.update()
        triangles = base.triangle_count(collection)
        base.export_asset(scene, collection, asset_id)
        catalogue.append(entry(asset_id, collection, meta["category"], meta["name"], meta.get("behaviour"),
                               meta.get("origin_height", 0.0), meta.get("params", {}), meta.get("internal", False),
                               True, triangles))

    for a in LIBRARY:
        if only and a.id not in only:
            continue
        collection = base.fresh_collection(scene, a.id)
        a.build(collection, materials)
        bpy.context.view_layer.update()
        triangles = base.triangle_count(collection)
        base.export_asset(scene, collection, a.id, category=a.category)
        catalogue.append(entry(a.id, collection, a.category, a.name, a.behaviour, a.origin_height,
                               dict(a.params), False, False, triangles, a.emits, a.colliders))
    return scene, catalogue


def write_catalogue(catalogue):
    data = {
        "format": "factorysim-catalog",
        "version": 1,
        "categories": [{"id": cid, "name": cname} for cid, cname in CATEGORIES],
        "assets": catalogue,
    }
    text = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
    for folder in (base.LIBRARY_DIR, base.ENGINE_LIBRARY_DIR):
        os.makedirs(folder, exist_ok=True)
        with open(os.path.join(folder, "catalog.json"), "w", encoding="utf-8") as handle:
            handle.write(text)


def render_thumbnails(catalogue):
    folder = os.path.join(base.ENGINE_LIBRARY_DIR, "thumbs")
    for item in catalogue:
        base.render_thumbnail(item["id"], size=128, path=os.path.join(folder, item["id"] + ".png"))


def render_contact_sheets(scene, catalogue):
    """One picture per category with its assets side by side, for reviewing the library."""
    camera = bpy.data.objects["FS_ThumbCamera"]
    for category, _name in CATEGORIES:
        items = [i for i in catalogue if i["category"] == category]
        if not items:
            continue
        moved = []
        visible = set()
        cursor = 0.0
        row_back = 0.0          # Blender +Y: further away from the camera
        row_depth = 0.0
        for item in items:
            collection = bpy.data.collections[base.PREFIX + item["id"]]
            width, depth = item["size"][0], item["size"][2]
            if cursor > 0.0 and cursor + width > 13.0:      # start a new row behind the previous one
                cursor = 0.0
                row_back += row_depth + 1.0
                row_depth = 0.0
            shift_x = cursor - item["boundsMin"][0]
            shift_y = row_back + item["boundsMax"][2]       # Godot z = -Blender y
            lift = item["originHeight"]
            for obj in collection.objects:
                obj.location.x += shift_x
                obj.location.y += shift_y
                obj.location.z += lift
                moved.append((obj, shift_x, shift_y, lift))
                visible.add(obj.name)
            cursor += width + max(0.3, width * 0.2)
            row_depth = max(row_depth, depth)
        for obj in scene.objects:
            if "fs_part" in obj:
                obj.hide_render = obj.name not in visible
        bpy.context.view_layer.update()

        corners = [o.matrix_world @ Vector(c) for o, _x, _y, _z in moved for c in o.bound_box]
        low = Vector([min(c[i] for c in corners) for i in range(3)])
        high = Vector([max(c[i] for c in corners) for i in range(3)])
        centre = (low + high) / 2
        radius = (high - low).length / 2
        aspect = 1920 / 1080
        vertical_fov = 2 * math.atan(math.tan(camera.data.angle / 2) / aspect)
        distance = radius / math.sin(vertical_fov / 2) * 0.92
        direction = Vector((0.35, -1.0, 0.7)).normalized()
        camera.location = centre + direction * distance
        camera.rotation_euler = (centre - camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.resolution_x = 1920
        scene.render.resolution_y = 1080
        scene.render.filepath = os.path.join(base.PREVIEW_DIR, "library_{}.png".format(category))
        bpy.ops.render.render(write_still=True)

        for obj, shift_x, shift_y, lift in moved:
            obj.location.x -= shift_x
            obj.location.y -= shift_y
            obj.location.z -= lift
    for obj in scene.objects:
        obj.hide_render = False


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    only = None
    for arg in args:
        if arg.startswith("--only="):
            only = set(arg[len("--only="):].split(","))

    scene, catalogue = build_library(only)
    if only is None:
        write_catalogue(catalogue)
    if "--no-thumbnails" not in args:
        render_thumbnails(catalogue)
        render_contact_sheets(scene, catalogue)
    if only is None:
        base.save_source(scene)

    summary = {
        "assets": len(catalogue),
        "triangles_max": max((i["triangles"], i["id"]) for i in catalogue),
        "over_budget": [i["id"] for i in catalogue if i["triangles"] > 5000],
        "by_category": {c: sum(1 for i in catalogue if i["category"] == c) for c, _n in CATEGORIES},
    }
    print("FACTORYSIM_LIBRARY " + json.dumps(summary, sort_keys=True))


if __name__ == "__main__":
    main()
