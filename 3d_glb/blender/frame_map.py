# Frames the largest mesh in the scene (usually the imported field/stage) in every 3D viewport.
#
# Priston fields are exported in world units, several kilometres wide and far from the origin, so the
# default viewport (Clip End 1000 m, looking at the origin) shows nothing. Run this from the Scripting
# tab after importing a field .glb: it leaves Pose/Edit mode, selects the map and sets every workspace
# (Layout, Shading, ...) to look at it with a clip range large enough to draw it.

import bpy
from mathutils import Vector

CLIP_START = 1.0
CLIP_END = 100000.0
DISTANCE_FACTOR = 0.8

if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')

meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
if not meshes:
    raise RuntimeError("No mesh in the scene; import a field .glb first.")

stage = max(meshes, key=lambda obj: len(obj.data.vertices))
bpy.ops.object.select_all(action='DESELECT')
stage.hide_set(False)
stage.select_set(True)
bpy.context.view_layer.objects.active = stage

corners = [stage.matrix_world @ Vector(corner) for corner in stage.bound_box]
center = sum(corners, Vector()) / len(corners)
size = max((corners[6] - corners[0])[:])

for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type != 'VIEW_3D':
            continue
        space = area.spaces.active
        space.clip_start = CLIP_START
        space.clip_end = CLIP_END
        space.region_3d.view_location = center
        space.region_3d.view_distance = size * DISTANCE_FACTOR

print(f"Framed '{stage.name}' ({len(stage.data.vertices)} vertices) in every 3D viewport.")
