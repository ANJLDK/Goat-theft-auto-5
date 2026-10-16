"""
Batch-import the extracted Goat Simulator models into Blender and prepare them for GTA V.

Requirements (install once in Blender):
  * "Unreal PSK/PSA importer" add-on (io_import_scene_unreal_psa_psk, Befzz fork or the
    newer 'io_scene_psk_psa' by DarklightGames). Either provides bpy.ops.import_scene.psk
    or bpy.ops.psk.import_file.
  * Sollumz (GTA V add-on) for the final export to .ydd/.yft.

Run from Blender's Scripting tab, or:
  blender --python import_goats.py -- <exports_dir> <output_dir>

What it does for each rigged goat (*.psk):
  1. imports the mesh + armature
  2. scales it from Unreal centimetres to metres (x0.01)
  3. builds a basic material from the matching *.png texture next to the export
  4. saves <output_dir>/<name>.blend and <name>.fbx

What it does NOT do (needs a person in Blender):
  * re-rig onto the GTA ped skeleton (see Docs/README.md, "Making the models work")
  * animations: .psa files import as actions but must be retargeted to GTA's skeleton
"""

import os
import sys
import glob
import bpy

UE_TO_M = 0.01


def _args():
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = []
    exports = argv[0] if len(argv) > 0 else os.path.join(os.path.dirname(bpy.data.filepath) or ".", "exports")
    out = argv[1] if len(argv) > 1 else os.path.join(exports, "..", "blend")
    return os.path.abspath(exports), os.path.abspath(out)


def _import_psk(path):
    if hasattr(bpy.ops.import_scene, "psk"):
        bpy.ops.import_scene.psk(filepath=path)
    elif hasattr(bpy.ops, "psk") and hasattr(bpy.ops.psk, "import_file"):
        bpy.ops.psk.import_file(filepath=path)
    else:
        raise RuntimeError("No PSK importer add-on is enabled in Blender.")


def _find_texture(psk_path):
    """Pick a diffuse PNG that belongs to this mesh (matched by name, then by package)."""
    pkg_dir = os.path.dirname(os.path.dirname(psk_path))          # .../<Package>/
    tex_dir = os.path.join(pkg_dir, "Texture2D")
    if not os.path.isdir(tex_dir):
        return None
    stem = os.path.splitext(os.path.basename(psk_path))[0].lower().replace("_skele", "")
    pngs = glob.glob(os.path.join(tex_dir, "*.png"))
    # prefer "<stem>... dif", then any dif, then any non-normal map
    def score(p):
        n = os.path.basename(p).lower()
        s = 0
        if stem.split("_")[0] in n:
            s += 4
        if "dif" in n or "diff" in n:
            s += 2
        if "nor" in n or "norm" in n:
            s -= 5
        if "angel" in n or "demon" in n or "black" in n or "brown" in n or "white" in n:
            s -= 1                                      # variants, not the default skin
        return s
    pngs.sort(key=score, reverse=True)
    return pngs[0] if pngs else None


def _apply_material(objs, tex_path):
    mat = bpy.data.materials.new("GS_" + os.path.splitext(os.path.basename(tex_path))[0])
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    img = bpy.data.images.load(tex_path)
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = img
    mat.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    for o in objs:
        if o.type == "MESH":
            o.data.materials.clear()
            o.data.materials.append(mat)


def process(psk_path, out_dir):
    name = os.path.splitext(os.path.basename(psk_path))[0]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    before = set(bpy.data.objects)
    _import_psk(psk_path)
    new = [o for o in bpy.data.objects if o not in before]

    # parent everything under one empty so scale/rotation is applied once
    root = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(root)
    for o in new:
        if o.parent is None:
            o.parent = root
    root.scale = (UE_TO_M, UE_TO_M, UE_TO_M)
    bpy.context.view_layer.update()

    tex = _find_texture(psk_path)
    if tex:
        _apply_material(new, tex)

    os.makedirs(out_dir, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, name + ".blend"))
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, name + ".fbx"),
                             use_selection=True, global_scale=1.0, apply_scale_options="FBX_SCALE_ALL")
    print("[goat] done:", name, "texture:", tex)


def main():
    exports, out = _args()
    psks = sorted(glob.glob(os.path.join(exports, "**", "SkeletalMesh3", "*.psk"), recursive=True))
    print("[goat] found", len(psks), "rigged meshes in", exports)
    for p in psks:
        try:
            process(p, out)
        except Exception as e:                                   # keep going on a bad file
            print("[goat] FAILED:", p, e)


if __name__ == "__main__":
    main()
