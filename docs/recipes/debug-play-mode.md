# `sceneport:debug-play-mode`

Enter play mode, observe live runtime state and logs, and diagnose a feature that misbehaves
only while the game is running.

## Copy-paste prompt

```text
> Use the sceneport:debug-play-mode prompt. Enter play mode, watch the runtime state and
> logs while I reproduce the bug, and tell me what is going wrong and why.
```

## When to use it

- A bug reproduces at runtime but not in edit mode.
- You want the agent to watch runtime values and console output as the game plays.

## Tools it drives

- `unity_enter_play_mode` / `unity_exit_play_mode` — control the play session.
- `unity_runtime_status`, `unity_query_runtime`, `unity_get_runtime_object` — read live
  object and component state.
- `unity_console_stream` — follow new log events with a cursor.
- `unity_capture_game_view` — see the running game.

## Example transcript

> **You:** Use `sceneport:debug-play-mode`. The enemy never takes damage when I shoot it.

1. The agent calls `unity_enter_play_mode`.
2. It captures the Game view with `unity_capture_game_view` to confirm the scene is running.
3. It follows `unity_console_stream` while you fire — no damage log appears.
4. It calls `unity_get_runtime_object` on the bullet and finds its collider `isTrigger` is
   true while the enemy expects a collision, not a trigger.
5. It exits with `unity_exit_play_mode` and reports the root cause plus the one serialized
   field to change.

> **Result:** A runtime-grounded diagnosis: the bullet collider was a trigger.

## Tips

- Runtime reads are observational; apply the actual fix in edit mode (then re-test).
- Use `unity_console_stream` cursors to capture only events since you started repro-ing.
- Pair with [`sceneport:write-playmode-test`](write-playmode-test.md) to lock in the fix.
