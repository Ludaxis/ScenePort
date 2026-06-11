# ScenePort Troubleshooting Playbook

## Bridge Not Found

Run `sceneport doctor --json`. If discovery is stale, restart Unity or delete `Library/ScenePort/bridge.json`.

## 401 Unauthorized

Set `SCENEPORT_PROJECT_PATH` to the Unity project root or run `sceneport auth status` to verify token discovery.

## Wrong Project

Set `SCENEPORT_PROJECT_PATH` and rerun `unity_status`. The status report should show `identityMatch: true`.

## `capability.denied`

The active policy blocks the requested endpoint group. Use a read-only tool or change policy only after team approval.

## Authoring Rejected

Check that paths are under `Assets/`, do not contain traversal, do not overwrite existing assets, and use `dryRun: true` before mutation.
