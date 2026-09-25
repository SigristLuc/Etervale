# Plays one exported Priston clip on an armature and fits the timeline to it.
#
# Workflow: import a class's bodies/<armor>.glb (and optionally a heads/<head>.glb), then that class's
# _animations.glb. Blender stores every clip as an action; set CLIP below (or leave it empty to list the
# available clips), select the armor's armature and run this from the Scripting tab. Exports made with
# --t-pose share the same rest pose, so any clip of the class applies to any of its bodies and heads.

import bpy

CLIP = "Walk"

actions = sorted(action.name for action in bpy.data.actions)
if not CLIP:
    print("Available clips:", ", ".join(actions))
    raise SystemExit

action = bpy.data.actions.get(CLIP)
if action is None:
    raise RuntimeError(f"Clip '{CLIP}' not found. Available clips: {', '.join(actions)}")

targets = [obj for obj in bpy.context.selected_objects if obj.type == 'ARMATURE']
if not targets:
    active = bpy.context.object
    if active is None or active.type != 'ARMATURE':
        raise RuntimeError("Select the armature of the imported armor (or head) first.")
    targets = [active]

for armature in targets:
    if armature.animation_data is None:
        armature.animation_data_create()
    armature.animation_data.action = action
    # Blender 4.4+ keeps animation in action slots; bind the first slot that fits this armature.
    slots = getattr(armature.animation_data, "action_suitable_slots", None)
    if slots:
        armature.animation_data.action_slot = slots[0]

start, end = action.frame_range
scene = bpy.context.scene
scene.frame_start = int(start)
scene.frame_end = max(int(start) + 1, int(round(end)))
scene.frame_set(scene.frame_start)
print(f"Playing '{CLIP}' on {', '.join(obj.name for obj in targets)} (frames {scene.frame_start}-{scene.frame_end}).")
