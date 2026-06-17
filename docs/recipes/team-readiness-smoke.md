# `sceneport:team-readiness-smoke`

An end-to-end readiness check that the ScenePort bridge, perception, and safe authoring all
work against a project — the smoke test a team runs to confirm the integration is healthy.

## Copy-paste prompt

```text
> Use the sceneport:team-readiness-smoke prompt. Run an end-to-end readiness check — bridge
> health, perception, and a safe authoring dry run — and tell me whether the integration is
> healthy.
```

## When to use it

- A teammate just set ScenePort up and wants to confirm it works end to end.
- You are validating a new project, machine, or CI runner before relying on ScenePort.

## Tools it drives

- `unity_status` — confirm the connected project, discovery source, and token state.
- `unity_scene_hierarchy` — prove read perception works.
- `unity_set_serialized_property` / `unity_authoring_batch` — prove a safe, Undo-able edit
  works.
- `unity_audit_log` — confirm the mutating request was recorded.
- `unity_capture_game_view` — optional visual proof when the project is play-mode ready.

## Example transcript

> **You:** Use `sceneport:team-readiness-smoke` on my project.

1. The agent runs `unity_status` and reports `identityMatch: true` with a valid token.
2. It reads `unity_scene_hierarchy` to prove perception.
3. It makes one tiny safe edit via `unity_set_serialized_property` (and Undoes it), then reads
   `unity_audit_log` to confirm the edit was recorded.
4. If the project is play-mode ready, it captures the Game view as visual proof.
5. It returns a checklist: discovery OK, auth OK, read OK, safe write + audit OK, capture OK.

> **Result:** A green readiness checklist proving the whole loop end to end.

## Tips

- This mirrors the [Team Readiness Demo](../demo/TEAM_READINESS_DEMO.md); import the UPM
  `Team Readiness Demo` sample for a known-good scene.
- You can also run it headless from `plugins/sceneport/server` via the smoke script described
  in that demo.
- A failing step points straight at the layer to fix — see the
  [Troubleshooting Playbook](../playbooks/TROUBLESHOOTING.md).
