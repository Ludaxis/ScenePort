# `sceneport:explain-scene`

Point the agent at an unfamiliar scene; it captures the Scene and Game views, walks the
hierarchy and key components, and explains in plain language what the scene contains and how
it is wired together.

## When to use it

- You opened someone else's scene (or your own from months ago) and need orientation fast.
- You are onboarding to a project and want a guided tour of a level or screen.
- You want a written summary of a scene's structure before changing it.

## Tools it drives

- `unity_scene_hierarchy` / `unity_query_scene` — enumerate the object tree.
- `unity_capture_scene_view` and `unity_capture_game_view` — see the scene from the editor and
  from the player's viewpoint (real image blocks the model reads).
- `unity_scene_view_state` — read the current Scene view camera framing.
- `unity_get_components`, `unity_read_serialized_properties`, `unity_query_components` —
  inspect what is attached and how it is configured.
- `unity_asset_graph` — trace how key objects reference prefabs, materials, and scripts.

## Example transcript

> **You:** Use `sceneport:explain-scene`. I just opened `Level_03` and have no idea what's
> going on.

1. The agent calls `unity_capture_scene_view` and `unity_capture_game_view` so it can see the
   layout and the in-game framing.
2. It calls `unity_scene_hierarchy` and groups the tree: an `Environment` root, a `Gameplay`
   root with spawners, a `UI` Canvas, and a `Managers` object.
3. For each group it calls `unity_get_components` / `unity_query_components` — it finds a
   `GameManager`, an `EnemySpawner` with a `waveCount` of 5, and a `NavMesh` setup.
4. It uses `unity_asset_graph` on the spawner to show it instantiates an `Enemy` prefab.
5. It writes a tour: "This is a wave-survival level. `GameManager` drives state; `EnemySpawner`
   emits 5 waves of the `Enemy` prefab onto the baked NavMesh; the `UI` Canvas shows score and
   health. Start here: `GameManager.OnWaveComplete`."

> **Result:** Annotated screenshots plus a plain-language map of the scene and where to begin.

## Tips

- Ask for a specific lens ("explain just the UI", "what controls difficulty?") to keep the
  tour focused.
- The Scene + Game captures matter: they let the agent ground its explanation in what is
  actually visible, not just object names.
- Follow up with [`sceneport:inspect-scene`](inspect-scene.md) for a terser
  hierarchy/selection summary, or [`sceneport:self-heal`](self-heal.md) if the tour surfaces
  errors.
- It is read-only by design — nothing here mutates the scene.
