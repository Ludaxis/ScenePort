# `sceneport:self-heal`

A closed-loop recipe: the agent reads the Unity console, forms a hypothesis, applies one
small reversible change, recompiles, and re-checks — repeating until the console is clean or
it runs out of safe options.

## When to use it

- A scene or script throws compile or runtime errors and you want the agent to fix them
  without you copy-pasting stack traces.
- You changed something and Unity is now red, but you are not sure which edit broke it.
- You want a hands-off "make it green again" pass before a commit.

## Tools it drives

- `unity_console_logs` / `unity_console_stream` — read current errors and new ones as they
  appear.
- `unity_get_compilation_status` — confirm whether scripts compile after each edit.
- `unity_get_game_object`, `unity_get_components`, `unity_read_serialized_properties` — read
  the suspect objects before touching them.
- `unity_set_serialized_property`, `unity_set_transform`, `unity_add_component`,
  `unity_authoring_batch` — apply the minimal reversible fix (Undo-backed).
- `unity_capture_game_view` — confirm the result visually once errors clear.

## Example transcript

> **You:** Use `sceneport:self-heal`. My scene is throwing errors after I renamed a script.

1. The agent calls `unity_console_logs` and finds:
   `MissingReferenceException: PlayerController on "Player" is missing`.
2. It calls `unity_get_components` on `Player` and sees a `MonoBehaviour` slot with a missing
   script reference (the renamed file).
3. It calls `unity_get_compilation_status` — scripts compile fine, so this is a broken
   serialized reference, not a syntax error.
4. It reads the new script's type with `unity_asset_search`, then uses
   `unity_authoring_batch` (dry-run first) to re-add the renamed component and re-wire the
   one serialized field that pointed at it.
5. It re-reads `unity_console_logs`: the exception is gone.
6. It calls `unity_capture_game_view` and shows you the Game view rendering normally again.

> **Result:** Console clean, one Undo-able change, and a screenshot proving the scene renders.

## Tips

- The loop is intentionally conservative — it prefers one change at a time so each fix is
  easy to Undo if it is wrong.
- If `unity_get_compilation_status` reports compile errors, the agent fixes source first;
  serialized-reference repair only happens once scripts compile.
- Pair with [`sceneport:visual-regression`](visual-regression.md) afterward to confirm the
  fix did not move pixels you cared about.
- If the agent stalls, ask it to summarize the remaining console errors and its hypothesis so
  you can steer the next edit.
