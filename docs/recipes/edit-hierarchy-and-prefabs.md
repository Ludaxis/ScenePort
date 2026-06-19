# Edit hierarchy and prefabs

Reshape the scene graph and manage prefab instances through ScenePort's safe, Undo-backed,
dry-run-first scene-editing path.

## Copy-paste prompt

```text
> Reorganize my scene hierarchy and prefab instances with ScenePort. Reparent, rename,
> reorder, duplicate, or delete the objects I describe, and instantiate / apply / revert
> prefabs as needed. Validate each mutation as a dry run before applying it.
```

## When to use it

- You want to tidy a messy hierarchy: reparent objects, rename them, fix sibling order.
- You need to clone or remove GameObjects in bulk from a description.
- You are working with prefabs: dropping instances into the scene, or pushing / discarding
  instance overrides.

## Tools it drives

- `unity_scene_hierarchy`, `unity_get_game_object`, `unity_get_components` — read the current
  scene graph before changing it.
- `unity_reparent_game_object` — move an object under a new parent (or to the scene root),
  keeping world position by default.
- `unity_rename_game_object` — rename a GameObject (idempotent).
- `unity_reorder_sibling` — set a GameObject's sibling index among its parent's children.
- `unity_duplicate_game_object` — clone an object under the same parent.
- `unity_delete_game_object` — destroy an object and its children (destructive, Undo-backed).
- `unity_instantiate_prefab` — instantiate a prefab asset into the active scene.
- `unity_apply_prefab_overrides` / `unity_revert_prefab_overrides` — push or discard an
  instance's overrides against the source prefab asset.

## Example transcript

> **You:** Move my three `Spawn_*` markers under a new empty called `Spawns`, then drop a
> `Coin` prefab at each one.

1. The agent calls `unity_scene_hierarchy` to locate the markers and confirm there is no
   existing `Spawns` parent.
2. It creates the `Spawns` container (`unity_create_game_object`) and reparents each marker
   with `unity_reparent_game_object`, running a `dryRun` first to confirm the targets resolve.
3. It calls `unity_instantiate_prefab` once per marker, parenting the instance and setting its
   local position.
4. It reports the new hierarchy and notes every step is a single Undo group.

> **Result:** A `Spawns` container holding the three markers, each with a `Coin` prefab
> instance — fully Undo-able.

## Tips

- Every mutation is dry-run aware: the agent previews the change set before applying it.
- Reparenting keeps world position by default; ask for "keep local position" if you want the
  local transform preserved instead.
- `unity_delete_game_object` is destructive but Undo-backed and audited, so an accidental
  delete is easy to roll back.
- Prefer `unity_apply_prefab_overrides` only after you have inspected the instance — it writes
  back to the shared prefab asset and affects every other instance.
