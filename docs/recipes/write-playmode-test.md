# `sceneport:write-playmode-test`

Draft a PlayMode test that asserts runtime behavior, then run it through the Unity Test Runner
and report results.

## Copy-paste prompt

```text
> Use the sceneport:write-playmode-test prompt. Draft a PlayMode test for the behavior I
> describe, run it with unity_tests_run, and report pass/fail with the relevant artifacts.
```

## When to use it

- You fixed a bug and want a regression test that exercises play-mode behavior.
- You want coverage for a gameplay interaction that only manifests at runtime.

## Tools it drives

- `unity_query_scene` / `unity_get_components` — understand the behavior under test.
- `unity_create_script` — write the test file under `Assets/` (dry-run validated).
- `unity_run_playmode_tests` / `unity_tests_run` — execute the PlayMode suite.
- `unity_tests_wait`, `unity_tests_artifacts` — wait without blocking the editor and read
  results.

## Example transcript

> **You:** Use `sceneport:write-playmode-test`. Add a test that the player dies when health
> hits zero.

1. The agent inspects `PlayerController` via `unity_get_components` to find the `health` field
   and the death path.
2. It uses `unity_create_script` (dry-run first) to add
   `Assets/Tests/PlayMode/PlayerDeathTests.cs` with a `UnityTest` coroutine that sets health
   to zero and asserts the death event fires.
3. It calls `unity_run_playmode_tests`, then `unity_tests_wait` so the editor stays
   responsive.
4. It reads `unity_tests_artifacts`: 1 passed. It reports the result and the file path.

> **Result:** A passing PlayMode test committed under `Assets/Tests`, with a run report.

## Tips

- PlayMode tests need an assembly definition referencing the test framework; ask the agent to
  set one up if your project lacks one.
- Waiting is done MCP-side so the Unity main thread never blocks.
- If the test fails, hand off to [`sceneport:debug-play-mode`](debug-play-mode.md) to diagnose.
