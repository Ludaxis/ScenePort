# `sceneport:playtest-pilot`

Drive a short, scripted playtest session — enter play mode, send a few inputs, capture
frames — and collect an agent-readable report with observations and recommendations.

## When to use it

- You want a quick "does the core loop work?" pass without manually playing.
- You want the agent to exercise a flow (menu → start → first interaction) and report back.

## Tools it drives

- `unity_start_playtest`, `unity_playtest_status`, `unity_stop_playtest` — manage the session.
- `unity_send_key`, `unity_send_click` — issue lightweight Game-view inputs.
- `unity_wait` — pace actions without blocking the editor main thread.
- `unity_capture_playtest_frame` — capture frames (real image blocks) at key moments.
- `unity_get_playtest_report` — read the consolidated report.

## Example transcript

> **You:** Use `sceneport:playtest-pilot`. Start the game, press Play on the menu, and move
> right for two seconds.

1. The agent calls `unity_start_playtest` and waits for `unity_playtest_status` to report
   running.
2. It `unity_send_click`s the menu Play button, then `unity_wait`s for the level to load.
3. It holds right with `unity_send_key`, `unity_wait`s ~2s, and calls
   `unity_capture_playtest_frame` before and after.
4. It calls `unity_stop_playtest`, then `unity_get_playtest_report`.
5. The report includes console observations, captured frames, the interactions performed, and
   recommendations ("no movement logged — check input bindings").

> **Result:** A frame-by-frame playtest report you can skim in seconds.

## Tips

- Keep scripts short — this is a smoke pilot, not a full automated test suite.
- Frame captures are asynchronous; the recipe waits before reading, so trust the report over
  raw timing.
- For assertion-based runtime checks, use
  [`sceneport:write-playmode-test`](write-playmode-test.md) instead.
