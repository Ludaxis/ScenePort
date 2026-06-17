# `sceneport:inspect-scene`

A quick orientation: summarize the active scene's hierarchy and current selection so you (and
the agent) start with shared context.

## Copy-paste prompt

```text
> Use the sceneport:inspect-scene prompt. Summarize my active scene's hierarchy and current
> selection so we start with shared context.
```

## When to use it

- You want a fast, terse snapshot of what is in the scene right now.
- You are about to ask for an edit and want the agent grounded in the real hierarchy first.

## Tools it drives

- `unity_scene_hierarchy` / `unity_query_scene` — enumerate the object tree.
- `unity_selection` — report what is currently selected in the editor.
- `unity_get_components` — list components on selected or notable objects.

## Example transcript

> **You:** Use `sceneport:inspect-scene` and summarize what I've got open.

1. The agent calls `unity_scene_hierarchy` and lists the top-level roots and their notable
   children.
2. It calls `unity_selection` and reports that `Player` is currently selected.
3. It calls `unity_get_components` on `Player` and lists `Rigidbody`, `PlayerController`, and
   a `CapsuleCollider`.
4. It returns a compact summary: scene roots, selection, and the selected object's components.

> **Result:** A one-screen orientation you can build the next request on.

## Tips

- This is read-only and cheap — a good first call before any authoring request.
- For a deeper, narrated tour with screenshots, use
  [`sceneport:explain-scene`](explain-scene.md) instead.
