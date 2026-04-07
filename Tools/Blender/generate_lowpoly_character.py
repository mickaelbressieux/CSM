import math
import os
import re
import subprocess
import sys

try:
    import bpy
    import bmesh
    import mathutils
except ModuleNotFoundError:
    bpy = None
    bmesh = None
    mathutils = None


DEFAULT_BLENDER_EXE = r"C:\Program Files\Blender Foundation\Blender 2.91\blender.exe"
DEFAULT_LIBRARY_ROOT = r"C:\Users\fauve\Documents\GitHub\Library Model 3D"


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


def get_library_root():
    return os.environ.get("MODEL_LIBRARY_3D", DEFAULT_LIBRARY_ROOT)


def find_model_by_keywords(base_dir, required_keywords, extension=".fbx"):
    if not os.path.isdir(base_dir):
        raise FileNotFoundError(f"Bibliotheque 3D introuvable: {base_dir}")

    required = [k.lower() for k in required_keywords]
    for root, _, files in os.walk(base_dir):
        for filename in files:
            if not filename.lower().endswith(extension):
                continue
            path = os.path.join(root, filename)
            haystack = path.lower()
            if all(k in haystack for k in required):
                return path
    return None


def import_fbx_objects(fbx_path):
    if not os.path.exists(fbx_path):
        raise FileNotFoundError(f"FBX introuvable: {fbx_path}")

    before_names = {obj.name for obj in bpy.data.objects}
    bpy.ops.import_scene.fbx(filepath=fbx_path)
    imported = [obj for obj in bpy.data.objects if obj.name not in before_names]
    return imported


def get_mesh_objects(objects):
    return [obj for obj in objects if obj.type == "MESH"]


def join_meshes(mesh_objects, new_name):
    if not mesh_objects:
        raise ValueError(f"Aucun mesh a fusionner pour {new_name}")

    if len(mesh_objects) == 1:
        mesh_objects[0].name = new_name
        return mesh_objects[0]

    bpy.ops.object.select_all(action='DESELECT')
    for obj in mesh_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = mesh_objects[0]
    bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = new_name
    return joined


def get_world_bounds(obj):
    points = [obj.matrix_world @ mathutils.Vector(corner) for corner in obj.bound_box]
    min_x = min(p.x for p in points)
    min_y = min(p.y for p in points)
    min_z = min(p.z for p in points)
    max_x = max(p.x for p in points)
    max_y = max(p.y for p in points)
    max_z = max(p.z for p in points)
    return (min_x, min_y, min_z), (max_x, max_y, max_z)


def align_to_origin_and_ground(obj):
    (min_x, min_y, min_z), (max_x, max_y, _) = get_world_bounds(obj)
    center_x = (min_x + max_x) * 0.5
    center_y = (min_y + max_y) * 0.5
    obj.location.x -= center_x
    obj.location.y -= center_y

    (min_x, min_y, min_z), _ = get_world_bounds(obj)
    obj.location.z -= min_z


def scale_to_height(obj, target_height):
    (_, _, min_z), (_, _, max_z) = get_world_bounds(obj)
    height = max(max_z - min_z, 0.0001)
    factor = target_height / height
    obj.scale.x *= factor
    obj.scale.y *= factor
    obj.scale.z *= factor


def apply_transform(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def bisect_keep_half(obj, keep_positive_x):
    bm = bmesh.new()
    bm.from_mesh(obj.data)

    geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
    bmesh.ops.bisect_plane(
        bm,
        geom=geom,
        plane_co=(0.0, 0.0, 0.0),
        plane_no=(1.0, 0.0, 0.0),
        clear_outer=not keep_positive_x,
        clear_inner=keep_positive_x,
    )

    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0001)
    bm.normal_update()
    bm.to_mesh(obj.data)
    obj.data.update()
    bm.free()


def cleanup_import_objects(keep_objects):
    keep_names = {obj.name for obj in keep_objects}
    for obj in list(bpy.context.scene.objects):
        if obj.name in keep_names:
            continue
        if obj.type != "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)


def generate_human_elephant_hybrid():
    library_root = get_library_root()

    elephant_fbx = find_model_by_keywords(library_root, ["cartoon animal pack 01", "elephent"])
    if elephant_fbx is None:
        elephant_fbx = find_model_by_keywords(library_root, ["elephant"])

    human_fbx = find_model_by_keywords(library_root, ["low poly bros", "bro"])
    if human_fbx is None:
        human_fbx = find_model_by_keywords(library_root, ["male"])

    if elephant_fbx is None:
        raise FileNotFoundError(f"Aucun modele elephant trouve dans {library_root}")
    if human_fbx is None:
        raise FileNotFoundError(f"Aucun modele humain trouve dans {library_root}")

    imported_human = import_fbx_objects(human_fbx)
    imported_elephant = import_fbx_objects(elephant_fbx)

    human_mesh = join_meshes(get_mesh_objects(imported_human), "HumanBase")
    elephant_mesh = join_meshes(get_mesh_objects(imported_elephant), "ElephantBase")

    cleanup_import_objects([human_mesh, elephant_mesh])

    align_to_origin_and_ground(human_mesh)
    align_to_origin_and_ground(elephant_mesh)

    scale_to_height(human_mesh, 1.9)
    scale_to_height(elephant_mesh, 1.9)

    align_to_origin_and_ground(human_mesh)
    align_to_origin_and_ground(elephant_mesh)

    human_mesh.location.x += 0.03
    elephant_mesh.location.x -= 0.03

    apply_transform(human_mesh)
    apply_transform(elephant_mesh)

    bisect_keep_half(human_mesh, keep_positive_x=True)
    bisect_keep_half(elephant_mesh, keep_positive_x=False)

    hybrid = join_meshes([human_mesh, elephant_mesh], "ElephantHumanMonster")

    bpy.ops.object.select_all(action='DESELECT')
    hybrid.select_set(True)
    bpy.context.view_layer.objects.active = hybrid
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=0.02)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')

    return hybrid


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
    return parse_script_args()


def get_script_args():
    if "--" not in sys.argv:
        return []
    idx = sys.argv.index("--")
    return sys.argv[idx + 1:]


def parse_script_args():
    # Usage:
    # blender --background --python generate_lowpoly_character.py -- C:/path/to/elephant_human_monster.blend
    args = get_script_args()
    output_path = os.path.abspath("elephant_human_monster.blend")

    for arg in args:
        if arg.lower().endswith(".blend"):
            output_path = os.path.abspath(arg)

    return output_path


def get_blender_executable_path():
    # Allow overriding with an environment variable while keeping the requested default.
    return os.environ.get("BLENDER_EXE", DEFAULT_BLENDER_EXE)


def rerun_inside_blender_if_needed():
    if bpy is not None:
        return

    blender_exe = get_blender_executable_path()
    if not os.path.exists(blender_exe):
        raise FileNotFoundError(f"Blender introuvable: {blender_exe}")

    script_path = os.path.abspath(__file__)
    script_args = get_script_args()
    if not script_args:
        script_args = [get_output_path()]

    cmd = [
        blender_exe,
        "--background",
        "--python",
        script_path,
        "--",
    ]
    cmd.extend(script_args)
    print(f"Launching Blender: {' '.join(cmd)}")
    subprocess.run(cmd, check=True)
    sys.exit(0)


def main():
    rerun_inside_blender_if_needed()
    output_path = parse_script_args()

    clear_scene()
    generate_human_elephant_hybrid()
    setup_scene()

    out_path = output_path
    out_dir = os.path.dirname(out_path)
    if out_dir and not os.path.exists(out_dir):
        os.makedirs(out_dir, exist_ok=True)

    bpy.ops.wm.save_as_mainfile(filepath=out_path)
    print(f"Saved Blender file: {out_path}")


if __name__ == "__main__":
    main()
