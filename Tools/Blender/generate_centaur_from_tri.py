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
DEFAULT_TRI_ROOT = r"C:\Users\fauve\Documents\GitHub\Library Model 3D\Tri"

SUPPORTED_IMPORT_EXTS = {".fbx", ".obj", ".gltf", ".glb", ".dae", ".blend"}


def get_script_args():
    if "--" not in sys.argv:
        return []
    idx = sys.argv.index("--")
    return sys.argv[idx + 1:]


def parse_script_args():
    # Usage:
    # blender --background --python generate_centaur_from_tri.py -- C:/path/out.blend
    output_path = os.path.abspath("centaure_tri.blend")
    tri_root = os.environ.get("TRI_LIBRARY_ROOT", DEFAULT_TRI_ROOT)

    for arg in get_script_args():
        lower = arg.lower()
        if lower.endswith(".blend"):
            output_path = os.path.abspath(arg)
        elif os.path.isdir(arg):
            tri_root = os.path.abspath(arg)

    return output_path, tri_root


def get_blender_executable_path():
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
        default_out, default_tri = parse_script_args()
        script_args = [default_out, default_tri]

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


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

    for block_collection in (
        bpy.data.meshes,
        bpy.data.materials,
        bpy.data.cameras,
        bpy.data.lights,
    ):
        for block in list(block_collection):
            if block.users == 0:
                block_collection.remove(block)


def get_mesh_objects(objects):
    return [obj for obj in objects if obj.type == "MESH"]


def join_meshes(mesh_objects, new_name):
    if not mesh_objects:
        raise ValueError(f"Aucun mesh a fusionner pour {new_name}")

    if len(mesh_objects) == 1:
        mesh_objects[0].name = new_name
        return mesh_objects[0]

    bpy.ops.object.select_all(action="DESELECT")
    for obj in mesh_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = mesh_objects[0]
    bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = new_name
    return joined


def import_model_file(model_path):
    if not os.path.exists(model_path):
        raise FileNotFoundError(f"Modele introuvable: {model_path}")

    ext = os.path.splitext(model_path)[1].lower()
    before_names = {obj.name for obj in bpy.data.objects}

    if ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=model_path)
    elif ext == ".obj":
        bpy.ops.import_scene.obj(filepath=model_path)
    elif ext in {".gltf", ".glb"}:
        bpy.ops.import_scene.gltf(filepath=model_path)
    elif ext == ".dae":
        bpy.ops.wm.collada_import(filepath=model_path)
    elif ext == ".blend":
        raise ValueError("Import direct .blend non implemente dans ce script")
    else:
        raise ValueError(f"Format non supporte: {ext}")

    return [obj for obj in bpy.data.objects if obj.name not in before_names]


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
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def bisect_keep_half_z(obj, keep_upper, cut_z):
    bm = bmesh.new()
    bm.from_mesh(obj.data)

    geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
    bmesh.ops.bisect_plane(
        bm,
        geom=geom,
        plane_co=(0.0, 0.0, cut_z),
        plane_no=(0.0, 0.0, 1.0),
        clear_outer=not keep_upper,
        clear_inner=keep_upper,
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


def find_prefab(tri_root, relative_folder, name_keywords):
    folder = os.path.join(tri_root, relative_folder)
    if not os.path.isdir(folder):
        raise FileNotFoundError(f"Dossier introuvable: {folder}")

    candidates = []
    for filename in os.listdir(folder):
        if not filename.lower().endswith(".prefab"):
            continue
        lower = filename.lower()
        if all(k.lower() in lower for k in name_keywords):
            candidates.append(os.path.join(folder, filename))

    if not candidates:
        raise FileNotFoundError(
            f"Aucun prefab trouve dans {folder} avec les mots-clefs: {name_keywords}"
        )

    candidates.sort()
    return candidates[0]


def extract_guids_from_prefab(prefab_path):
    guid_re = re.compile(r"guid:\s*([0-9a-fA-F]{32})")
    with open(prefab_path, "r", encoding="utf-8", errors="ignore") as handle:
        text = handle.read()
    return list(dict.fromkeys(guid_re.findall(text)))


def build_guid_to_asset_index(search_root):
    index = {}
    for root, _, files in os.walk(search_root):
        for name in files:
            if not name.endswith(".meta"):
                continue
            meta_path = os.path.join(root, name)
            guid = None
            try:
                with open(meta_path, "r", encoding="utf-8", errors="ignore") as handle:
                    for _ in range(25):
                        line = handle.readline()
                        if not line:
                            break
                        if line.startswith("guid:"):
                            guid = line.split(":", 1)[1].strip()
                            break
            except OSError:
                continue

            if not guid:
                continue

            asset_path = meta_path[:-5]
            if os.path.exists(asset_path):
                index[guid.lower()] = asset_path

    return index


def choose_importable_asset(guid_list, guid_index):
    for guid in guid_list:
        asset_path = guid_index.get(guid.lower())
        if not asset_path:
            continue
        ext = os.path.splitext(asset_path)[1].lower()
        if ext in SUPPORTED_IMPORT_EXTS:
            return asset_path
    return None


def resolve_prefab_to_model(prefab_path, guid_index):
    guid_list = extract_guids_from_prefab(prefab_path)
    model_path = choose_importable_asset(guid_list, guid_index)
    if model_path is None:
        raise FileNotFoundError(
            f"Impossible de trouver un modele importable depuis {prefab_path}. "
            "Verifie que les FBX/OBJ existent avec leurs .meta."
        )
    return model_path


def generate_centaur(tri_root):
    library_root = os.path.abspath(os.path.join(tri_root, os.pardir))
    guid_index = build_guid_to_asset_index(library_root)

    horse_prefab = find_prefab(tri_root, "Animal", ["horse"])

    # Priorite a un prefab homme standard; fallback sur le premier male trouvé.
    try:
        human_prefab = find_prefab(tri_root, "Homme", ["male", "young", "guy"])
    except FileNotFoundError:
        human_prefab = find_prefab(tri_root, "Homme", ["male"])

    horse_model = resolve_prefab_to_model(horse_prefab, guid_index)
    human_model = resolve_prefab_to_model(human_prefab, guid_index)

    print(f"Cheval source : {horse_model}")
    print(f"Humain source : {human_model}")

    horse_imported = import_model_file(horse_model)
    human_imported = import_model_file(human_model)

    horse_mesh = join_meshes(get_mesh_objects(horse_imported), "HorseBase")
    human_mesh = join_meshes(get_mesh_objects(human_imported), "HumanBase")

    cleanup_import_objects([horse_mesh, human_mesh])

    align_to_origin_and_ground(horse_mesh)
    align_to_origin_and_ground(human_mesh)

    scale_to_height(horse_mesh, 1.55)
    scale_to_height(human_mesh, 1.75)
    human_mesh.scale *= 0.82

    align_to_origin_and_ground(horse_mesh)
    align_to_origin_and_ground(human_mesh)

    apply_transform(horse_mesh)
    apply_transform(human_mesh)

    (_, _, horse_min_z), (_, _, horse_max_z) = get_world_bounds(horse_mesh)
    (_, _, human_min_z), (_, _, human_max_z) = get_world_bounds(human_mesh)
    horse_height = horse_max_z - horse_min_z
    human_height = human_max_z - human_min_z

    horse_cut_z = horse_min_z + horse_height * 0.62
    human_cut_z = human_min_z + human_height * 0.44

    bisect_keep_half_z(horse_mesh, keep_upper=False, cut_z=horse_cut_z)
    bisect_keep_half_z(human_mesh, keep_upper=True, cut_z=human_cut_z)

    (_, _, horse_cut_min), (horse_max_x, horse_max_y, horse_cut_max) = get_world_bounds(horse_mesh)
    (human_min_x, human_min_y, human_cut_min), (human_max_x, human_max_y, human_cut_max) = get_world_bounds(human_mesh)

    horse_center_x = (0.0 + horse_max_x) * 0.5 if horse_max_x < 0.0 else horse_max_x * 0.15
    horse_center_y = (0.0 + horse_max_y) * 0.5 if horse_max_y < 0.0 else 0.0
    human_center_x = (human_min_x + human_max_x) * 0.5
    human_center_y = (human_min_y + human_max_y) * 0.5

    human_mesh.location.x += horse_center_x - human_center_x
    human_mesh.location.y += horse_center_y - human_center_y
    human_mesh.location.z += horse_cut_max - human_cut_min + 0.01

    apply_transform(human_mesh)

    centaur = join_meshes([horse_mesh, human_mesh], "Centaure_Tri")

    bpy.ops.object.select_all(action="DESELECT")
    centaur.select_set(True)
    bpy.context.view_layer.objects.active = centaur
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.02)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")

    centaur.location = (0.0, 0.0, 0.0)
    apply_transform(centaur)

    return centaur


def setup_scene():
    bpy.ops.object.camera_add(location=(2.7, -4.4, 2.2), rotation=(1.22, 0.0, 0.58))
    camera = bpy.context.active_object
    camera.data.lens = 50
    bpy.context.scene.camera = camera

    bpy.ops.object.light_add(type="SUN", location=(2.0, -2.2, 5.0))
    sun = bpy.context.active_object
    sun.data.energy = 2.3

    bpy.ops.mesh.primitive_plane_add(size=10.0, location=(0.0, 0.0, -0.02))
    ground = bpy.context.active_object
    ground.name = "Ground"

    bpy.context.scene.render.engine = "BLENDER_EEVEE"


def main():
    rerun_inside_blender_if_needed()
    output_path, tri_root = parse_script_args()

    if not os.path.isdir(tri_root):
        raise FileNotFoundError(f"Dossier Tri introuvable: {tri_root}")

    clear_scene()
    generate_centaur(tri_root)
    setup_scene()

    out_dir = os.path.dirname(output_path)
    if out_dir and not os.path.exists(out_dir):
        os.makedirs(out_dir, exist_ok=True)

    bpy.ops.wm.save_as_mainfile(filepath=output_path)
    print(f"Saved Blender file: {output_path}")


if __name__ == "__main__":
    main()