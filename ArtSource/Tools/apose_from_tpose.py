"""Lowers a T-posed character's arms into an A-pose, so prepare_character.py can rig it (it finds the arms beside the
body at mid height, and level arms join the torso there).

blender -b --factory-startup -P ArtSource/Tools/apose_from_tpose.py -- <in.glb> <out.glb> [options]
  --down 50          degrees each arm is turned down from where it is
  --twist 80         degrees each forearm turns about the arm so the palm faces the thigh (Tripo's T-pose palms face
                     forward; lowered as they are, the hands hung palm-forward and swam palm-up)
  --axis y           the model's side-to-side axis (Tripo's decoded models face +X: the arms lie along y)
  --preview <prefix> also render the front before and after

How: each arm turns down about the front-back axis through its shoulder (the torso's edge at the arms' height), every
vertex by its own share: all of it out on the arm, none on the torso, ramping across the shoulder, and only in the
arms' height band (heat weights fail on Tripo meshes). Texture and UVs stay; the normals are worked out again.
"""
import math
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, DST = argv[0], argv[1]
opts = {'down': 50.0, 'twist': 80.0, 'axis': 'y', 'preview': ''}
i = 2
while i < len(argv):
    key = argv[i].lstrip('-')
    opts[key] = type(opts[key])(argv[i + 1]) if not isinstance(opts[key], str) else argv[i + 1]
    i += 2


def log(*a):
    print('[apose]', *a, flush=True)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if len(meshes) != 1:
    raise SystemExit(f'[apose] expected one mesh, found {len(meshes)}')
body = meshes[0]
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

side = 1 if opts['axis'] == 'y' else 0      # side-to-side axis index
fwd = 0 if side == 1 else 1                 # front-back axis index
v = np.array([tuple(p.co) for p in body.data.vertices])
zmin, zmax = float(v[:, 2].min()), float(v[:, 2].max())
H = zmax - zmin
span = float(np.abs(v[:, side]).max())
log(f'height {H:.3f}, half span {span:.3f}')


def render(path):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    if scene.world is None:
        scene.world = bpy.data.worlds.new('w')
    scene.world.color = (0.8, 0.85, 0.9)
    cam = scene.camera
    if cam is None:
        cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
        scene.collection.objects.link(cam)
        scene.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = max(H, 2 * span) * 1.1
    eye = [0.0, 0.0, zmin + H * 0.5]
    eye[fwd] = 5.0  # in front (the model faces +fwd)
    cam.location = eye
    look = Vector((0, 0, zmin + H * 0.5)) - Vector(eye)
    cam.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()
    scene.render.resolution_x, scene.render.resolution_y = 900, 700
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


if opts['preview']:
    render(opts['preview'] + '_before.png')

# The arms: everything further out than the torso. The torso's half width is measured at chest height, between the
# arms' level and the hips (a row through the middle of the body, where only the torso is).
lat = v[:, side]
torso_band = (v[:, 2] > zmin + 0.45 * H) & (v[:, 2] < zmin + 0.55 * H)
torso_half = float(np.percentile(np.abs(lat[torso_band]), 98)) if torso_band.any() else 0.15 * H
arm_pts = {}
for s, name in ((1, 'pos'), (-1, 'neg')):
    a = v[(lat * s > torso_half + 0.08 * H)]
    if len(a) < 50:
        raise SystemExit(f'[apose] no arm found on the {name} side: not a T-pose?')
    arm_pts[s] = a
arm_z = float(np.median(np.concatenate([arm_pts[1][:, 2], arm_pts[-1][:, 2]])))
log(f'torso half width {torso_half:.3f}, arms at height {arm_z - zmin:.3f} ({(arm_z - zmin) / H:.2f} H)')

# Turn each arm down about the front-back axis through its shoulder (the torso's edge at the arms' height, a little
# inside it). Blender's heat weights fail on Tripo meshes ("failed to find solution"), so each vertex gets its share of
# the turn from where it is: none on the torso, all of it on the arm, ramping across the shoulder; and only in the
# arms' height band, so the big cartoon head (wider than the shoulders) and the chest's sides stay put.
shoulder_off = torso_half * 0.85
ramp_in, ramp_out = torso_half - 0.01 * H, torso_half + 0.04 * H
top_full, top_none = arm_z + 0.05 * H, arm_z + 0.08 * H
low_full, low_none = arm_z - 0.04 * H, arm_z - 0.07 * H


def smooth(u):
    u = np.clip(u, 0.0, 1.0)
    return u * u * (3 - 2 * u)


w_lat = smooth((np.abs(lat) - ramp_in) / (ramp_out - ramp_in))
w_z = smooth((top_none - v[:, 2]) / (top_none - top_full)) * smooth((v[:, 2] - low_none) / (low_full - low_none))
# (The arm itself, far out from the body, turns whole whatever its height: the hands reach above and below the band.)
far = np.abs(lat) > ramp_out + 0.04 * H
w = np.where(far, 1.0, w_lat * w_z)
out = v.copy()
for s in (1, -1):
    on = (lat * s > 0) & (w > 0)
    pivot = np.zeros(3)
    pivot[side] = s * shoulder_off
    pivot[2] = arm_z
    angle = math.radians(opts['down'])
    # Down: the +side arm's far end goes from +side toward -z.
    c, sn = math.cos(angle), math.sin(angle)
    rel = v[on] - pivot
    turned = rel.copy()
    turned[:, side] = rel[:, side] * c + s * rel[:, 2] * sn
    turned[:, 2] = -s * rel[:, side] * sn + rel[:, 2] * c
    out[on] = (1 - w[on])[:, None] * v[on] + w[on][:, None] * (pivot + turned)
    # The forearm's twist: none at the elbow, all of it at the wrist and on the hand (as a forearm turns), about the
    # lowered arm's line. Turning +twist about the arm (pointing down and out) brings a forward-facing palm round to face
    # the body on the +side; the -side arm turns the other way.
    tip = arm_pts[s][np.argmax(np.abs(arm_pts[s][:, side]))] - pivot
    tip_low = tip.copy()
    tip_low[side] = tip[side] * c + s * tip[2] * sn
    tip_low[2] = -s * tip[side] * sn + tip[2] * c
    length = float(np.linalg.norm(tip_low))
    k = tip_low / length
    t = ((out[on] - pivot) @ k) / length
    share = smooth((t - 0.45) / (0.72 - 0.45)) * (w[on] > 0.99)
    theta = math.radians(opts['twist']) * s * share
    rel2 = out[on] - pivot
    along_k = (rel2 @ k)[:, None] * k[None, :]
    cos_t, sin_t = np.cos(theta)[:, None], np.sin(theta)[:, None]
    out[on] = pivot + rel2 * cos_t + np.cross(k[None, :], rel2) * sin_t + along_k * (1 - cos_t)
for k, p_ in enumerate(body.data.vertices):
    p_.co = Vector(out[k])
body.data.update()
if body.data.has_custom_normals:
    bpy.ops.object.select_all(action='DESELECT')
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.mesh.customdata_custom_splitnormals_clear()
log(f'forearms twisted {opts["twist"]:.0f} degrees; arms turned {opts["down"]:.0f} degrees down about shoulders {shoulder_off:.3f} out, {int((w > 0.99).sum())} vertices whole, {int(((w > 0.01) & (w <= 0.99)).sum())} blended')

v2 = np.array([tuple(p.co) for p in body.data.vertices])
log(f'after: half span {float(np.abs(v2[:, side]).max()):.3f}, lowest hand point {float(v2[np.abs(v2[:, side]) > torso_half + 0.08 * H][:, 2].min() - zmin) / H:.2f} H')
if opts['preview']:
    span = float(np.abs(v2[:, side]).max())
    render(opts['preview'] + '_after.png')

bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
bpy.ops.export_scene.gltf(filepath=DST, export_format='GLB', use_selection=True, export_apply=True)
log('wrote', DST)
