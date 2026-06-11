# ScenePort Team Adoption Playbook

## First Run

1. Install the Unity package from `plugins/sceneport/unity-package/package.json`.
2. Open Unity and confirm `Library/ScenePort/bridge.json` exists.
3. Run `sceneport doctor --json` from the Unity project root.
4. Connect Codex with `sceneport config codex` or Claude with `sceneport config claude`.
5. Run `unity_diagnostics`, `unity_query_scene`, and `unity_audit_log`.

## Recommended Policy

- Use `read-only` for reviewers and exploratory agents.
- Use `playtest` for QA agents.
- Use `team-safe` for agents that can inspect and proof but not author.
- Use `full-safe-local` only for trusted local development.

## Demo Loop

1. Import the Team Readiness Demo sample.
2. Run `sceneport:team-readiness-smoke`.
3. Capture doctor output, diagnostics, audit log, assertion proof, and Unity test result.
