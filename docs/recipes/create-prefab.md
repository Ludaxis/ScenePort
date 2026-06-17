# `sceneport:create-prefab`

Assemble GameObjects and components into a reusable prefab saved under `Assets/`, using
ScenePort's safe, dry-run-first authoring path.

## Copy-paste prompt

```text
> Use the sceneport:create-prefab prompt. Assemble the GameObjects/components I describe into
> a reusable prefab under Assets/, validating the authoring write as a dry run first.
```

## When to use it

- You built a GameObject in the scene and want it captured as a reusable prefab.
- You want the agent to scaffold a prefab (enemy, pickup, UI widget) from a description.

## Tools it drives

- `unity_create_game_object`, `unity_add_component`, `unity_set_transform`,
  `unity_set_serialized_property` — build the object graph.
- `unity_validate_authoring_write` / `unity_authoring_batch` — dry-run validate before
  writing.
- `unity_create_prefab` — write the prefab asset under `Assets/`.

## Example transcript

> **You:** Use `sceneport:create-prefab`. Turn my selected `Coin` object into a prefab in
> `Assets/Prefabs`.

1. The agent calls `unity_selection` to confirm `Coin` is selected and reads its components
   with `unity_get_components`.
2. It calls `unity_validate_authoring_write` to confirm the target path
   `Assets/Prefabs/Coin.prefab` is writable, under `Assets/`, and not overwriting anything.
3. It calls `unity_create_prefab`, creating the asset and linking the scene instance.
4. It reports the new asset path and that the change is Undo-able.

> **Result:** `Assets/Prefabs/Coin.prefab` created, with the scene object now a prefab
> instance.

## Tips

- Paths must be under `Assets/`, with no traversal, and must not overwrite an existing asset —
  the validate step enforces this.
- Ask for a folder explicitly; otherwise the agent proposes one and confirms.
- Authoring is Undo-backed and audited, so a wrong prefab is easy to roll back.
