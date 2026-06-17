# ScenePort Recipe Gallery

Recipes are named workflow prompts you can invoke from your MCP client (for example
`sceneport:self-heal`). Each one orchestrates a sequence of ScenePort tools so your agent
inspects, acts, and verifies against the live Unity Editor instead of guessing from files.

Invoke a recipe the way your client surfaces prompts (Claude Code: `/sceneport:self-heal`;
Codex: by prompt name). Every page below has a realistic transcript and tips.

## Perception

See and understand the editor.

- [`sceneport:explain-scene`](explain-scene.md) — capture the views, walk the hierarchy, and
  explain what an unfamiliar scene contains and how it is wired.
- [`sceneport:inspect-scene`](inspect-scene.md) — summarize the active scene hierarchy and
  selection for a quick orientation.

## Authoring

Create and change content safely.

- [`sceneport:create-ui-from-screenshot`](create-ui-from-screenshot.md) — rebuild a UI layout
  from a reference image, then capture the Game view to compare.
- [`sceneport:create-prefab`](create-prefab.md) — assemble GameObjects and components into a
  reusable prefab under `Assets/`.

## Testing and Playtest

Prove behavior with tests and live play.

- [`sceneport:visual-regression`](visual-regression.md) — golden-frame capture plus pixel diff
  to catch unintended visual changes.
- [`sceneport:write-playmode-test`](write-playmode-test.md) — draft and run a PlayMode test
  that asserts runtime behavior.
- [`sceneport:playtest-pilot`](playtest-pilot.md) — drive a short scripted playtest session and
  collect an agent-readable report.
- [`sceneport:team-readiness-smoke`](team-readiness-smoke.md) — end-to-end readiness check that
  the bridge, perception, and safe edits all work.

## Diagnostics

Find and fix problems.

- [`sceneport:self-heal`](self-heal.md) — read console errors, apply a small reversible fix,
  recompile, and loop until the console is clean.
- [`sceneport:fix-console-errors`](fix-console-errors.md) — triage current console errors and
  propose targeted fixes.
- [`sceneport:debug-play-mode`](debug-play-mode.md) — enter play mode, observe runtime state and
  logs, and diagnose a misbehaving feature.
- [`sceneport:prepare-build`](prepare-build.md) — run pre-build checks (compilation, tests, perf
  budgets) and report blockers.
