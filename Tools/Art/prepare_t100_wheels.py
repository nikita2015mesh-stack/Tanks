"""Run with Blender: blender -b --factory-startup --python Tools/Art/prepare_t100_wheels.py.

Asset authoring only. Creates independent, centered wheel meshes; does not touch
the vendor FBX, Unity scenes, importer settings, or gameplay code.
"""

import argparse
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Euler, Matrix, Vector


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Assets/ASEED/Stylized Tactical Vehicle/Source/T100_FREE.fbx"
DEFAULT_OUTPUT = ROOT / "Assets/_ThirdPartyLocal/T100/Wheels"
WHEELS = (
    ("SM_Wheel_1", "Wheel_FL"),
    ("SM_Wheel_2", "Wheel_FR"),
    ("SM_Wheel_3", "Wheel_RL"),
    ("SM_Wheel_4", "Wheel_RR"),
)
META_GUIDS = {
    "Wheel_FL": "936925b9e1739fb89b4d7b27f8415504",
    "Wheel_FR": "2e1a9bf4cce2abd0e972261c1d2ab792",
    "Wheel_RL": "da0af4ba6473f5774a4b5d7a567ec435",
    "Wheel_RR": "96f60d5829d9bbb059e77c10786c4853",
}


def bounds(mesh):
    low = Vector([min(v.co[i] for v in mesh.vertices) for i in range(3)])
    high = Vector([max(v.co[i] for v in mesh.vertices) for i in range(3)])
    return (low + high) * 0.5, high - low


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--overwrite", action="store_true",
                        help="Replace previously authored outputs, including the editable blend file.")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    output = args.output.resolve()
    blend_path = output / "Source~" / "T100_Wheels.blend"
    targets = [output / (name + ".fbx") for _, name in WHEELS] + [blend_path]
    if not args.overwrite and any(path.exists() for path in targets):
        raise FileExistsError("Output already exists. Choose a different --output to preserve manual edits.")

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(SOURCE))
    imported = {name: bpy.data.objects[name] for name, _ in WHEELS}
    for obj in list(bpy.data.objects):
        if obj not in imported.values():
            bpy.data.objects.remove(obj, do_unlink=True)
    output.mkdir(parents=True, exist_ok=True)
    blend_path.parent.mkdir(parents=True, exist_ok=True)
    # Seed canonical GUIDs before writing FBX files so Unity's refresh cannot assign new ones.
    # Alternate output folders receive their own importer identities to avoid duplicate GUIDs.
    if output == DEFAULT_OUTPUT.resolve():
        for name, guid in META_GUIDS.items():
            meta_path = output / (name + ".fbx.meta")
            if meta_path.exists():
                if "guid: " + guid not in meta_path.read_text(encoding="utf-8"):
                    raise ValueError("Importer GUID differs from the Playground reference: " + str(meta_path))
            else:
                meta_path.write_text("fileFormatVersion: 2\nguid: " + guid + "\n", encoding="utf-8")
    report = {"source": str(SOURCE.relative_to(ROOT)), "blender": bpy.app.version_string, "wheels": []}
    scenes = []

    for source_name, name in WHEELS:
        obj = imported[source_name]
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world
        # Bake the original centimetre scale and axis conversion into vertices and normals.
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        vertices_before = len(obj.data.vertices)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.delete_loose(use_verts=True, use_edges=True, use_faces=False)
        bpy.ops.object.mode_set(mode="OBJECT")
        center, dimensions = bounds(obj.data)
        obj.data.transform(Matrix.Translation(-center))
        obj.data.update()
        obj.location = (0, 0, 0)
        obj.name = name
        obj.data.name = name
        obj["source_object"] = source_name
        obj["axle"] = "X"
        obj["preparation"] = "Loose geometry removed; bounds center at origin; rotation and scale baked."

        bpy.ops.export_scene.fbx(
            filepath=str(output / (name + ".fbx")), use_selection=True,
            object_types={"MESH"}, axis_forward="-Z", axis_up="Y",
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
            bake_space_transform=True, use_mesh_modifiers=False,
            # Preserve editable n-gons; Unity calculates tangents after its import triangulation.
            mesh_smooth_type="OFF", use_tspace=False, bake_anim=False,
            add_leaf_bones=False, use_custom_props=False, path_mode="AUTO",
        )
        report["wheels"].append({
            "source_object": source_name, "output": name + ".fbx",
            "removed_loose_vertices": vertices_before - len(obj.data.vertices),
            "removed_center_offset_m": list(center), "dimensions_m": list(dimensions),
            "radius_m": max(dimensions.y, dimensions.z) * 0.5,
        })
        scene = bpy.data.scenes.new(name)
        scene.unit_settings.system = "METRIC"
        scene.unit_settings.scale_length = 1
        scene.collection.objects.link(obj)
        scenes.append(scene)

    # One centered wheel per scene: no display offsets or overlapping models in the editable source.
    old_scene = bpy.context.scene
    bpy.context.window.scene = scenes[0]
    bpy.data.scenes.remove(old_scene)
    for obj in imported.values():
        for collection in list(obj.users_collection):
            if collection not in [scene.collection for scene in scenes]:
                collection.objects.unlink(obj)
    bpy.context.view_layer.objects.active = imported[WHEELS[0][0]]
    imported[WHEELS[0][0]].select_set(True)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == "VIEW_3D":
                area.spaces.active.region_3d.view_location = (0, 0, 0)
                area.spaces.active.region_3d.view_distance = 2.2
                area.spaces.active.region_3d.view_rotation = Euler(
                    (math.radians(70), 0, math.radians(60)), "XYZ").to_quaternion()
    text = bpy.data.texts.new("READ_ME")
    text.write("Четыре сцены: Wheel_FL, Wheel_FR, Wheel_RL, Wheel_RR.\n"
               "В каждой одно колесо: центр в (0,0,0), ось X, масштаб 1.\n"
               "Исходный размер сохранён: радиус около 0,5461545 м.\n"
               "Редактируйте геометрию в Edit Mode; экспортируйте выбранное колесо в соответствующий FBX.\n"
               "В Unity заменяется Mesh у существующего MeshFilter, материал остаётся прежним.\n")
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
    (blend_path.parent / "preparation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("Prepared wheel assets: " + str(output))


if __name__ == "__main__":
    main()
