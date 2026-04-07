import bpy
import math
import os
import re
import sys


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)

    # Remove leftover data blocks to keep the file clean.
    for block_collection in (
        bpy.data.meshes,
        bpy.data.materials,
        bpy.data.cameras,
        bpy.data.lights,
    ):
        for block in list(block_collection):
            if block.users == 0:
                block_collection.remove(block)


def make_material(name, rgba):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = rgba
        bsdf.inputs["Roughness"].default_value = 0.85
    return mat


def assign_material(obj, mat):
    if obj.data.materials:
        obj.data.materials[0] = mat
    else:
        obj.data.materials.append(mat)


def normalize_import_name(name):
    # Strip Blender's numeric duplicate suffixes (e.g. Hair.001 -> Hair).
    return re.sub(r"\.\d{3}$", "", name)


def get_fbx_path():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    project_root = os.path.abspath(os.path.join(script_dir, "..", ".."))
    return os.path.join(project_root, "Assets", "Ressources", "Low Poly Bros", "Models", "Bro.fbx")


def import_prefab_variant():
    fbx_path = get_fbx_path()
    if not os.path.exists(fbx_path):
        raise FileNotFoundError(f"Bro.fbx introuvable: {fbx_path}")

    bpy.ops.import_scene.fbx(filepath=fbx_path)

    keep_mesh_names = {"Bro", "Bro.Face", "Hair"}
    for obj in list(bpy.context.scene.objects):
        normalized = normalize_import_name(obj.name)
        if obj.type == "ARMATURE":
            continue
        if obj.type == "MESH" and normalized in keep_mesh_names:
            obj.hide_set(False)
            obj.hide_render = False
            continue
        bpy.data.objects.remove(obj, do_unlink=True)


def setup_scene():
    # Ground for contact shadow.
    bpy.ops.mesh.primitive_plane_add(size=10.0, location=(0.0, 0.0, -0.13))
    ground = bpy.context.active_object
    ground.name = "Ground"
    ground_mat = make_material("Ground", (0.17, 0.15, 0.46, 1.0))
    assign_material(ground, ground_mat)

    # Purple studio background similar to the reference image.
    world = bpy.context.scene.world
    if world is None:
        world = bpy.data.worlds.new("World")
        bpy.context.scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.11, 0.09, 0.36, 1.0)
        bg.inputs[1].default_value = 0.9

    # Camera with a slight 3/4 view.
    bpy.ops.object.camera_add(location=(2.45, -4.20, 2.10), rotation=(math.radians(72.0), 0.0, math.radians(29.0)))
    camera = bpy.context.active_object
    camera.data.lens = 50
    bpy.context.scene.camera = camera

    # Key light
    bpy.ops.object.light_add(type='SUN', location=(2.2, -2.0, 4.8))
    sun = bpy.context.active_object
    sun.data.energy = 2.5

    # Fill light
    bpy.ops.object.light_add(type='AREA', location=(-2.2, -0.9, 2.1))
    fill = bpy.context.active_object
    fill.data.energy = 95
    fill.data.size = 2.6

    # Rim light to separate the silhouette from the background.
    bpy.ops.object.light_add(type='AREA', location=(2.2, 2.1, 2.0))
    rim = bpy.context.active_object
    rim.data.energy = 80
    rim.data.size = 2.2

    bpy.context.scene.render.engine = 'BLENDER_EEVEE'


def get_output_path():
    # Usage:
    # blender --background --python generate_lowpoly_character.py -- C:/path/to/lowpoly_character.blend
    if "--" in sys.argv:
        idx = sys.argv.index("--")
        if idx + 1 < len(sys.argv):
            return os.path.abspath(sys.argv[idx + 1])

    return os.path.abspath("lowpoly_character.blend")


def main():
    clear_scene()
    import_prefab_variant()
    setup_scene()

    out_path = get_output_path()
    out_dir = os.path.dirname(out_path)
    if out_dir and not os.path.exists(out_dir):
        os.makedirs(out_dir, exist_ok=True)

    bpy.ops.wm.save_as_mainfile(filepath=out_path)
    print(f"Saved Blender file: {out_path}")


if __name__ == "__main__":
    main()
