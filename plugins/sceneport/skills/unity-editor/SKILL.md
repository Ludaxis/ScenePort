---
name: unity-editor
description: Use ScenePort when the user asks to inspect, debug, test, or safely modify a Unity project through the live Unity Editor.
---

# ScenePort Unity Editor Workflow

Use ScenePort when the task depends on live Unity Editor state: active scene, hierarchy, selected objects, console logs, play mode, playtest sessions, tests, assets, Game view screenshots, packages, components, audit log, or inspector-level changes.

Default workflow:

1. Call `unity_status` to confirm the editor bridge is reachable.
2. Read diagnostics and context before writing: use `unity_diagnostics`, `unity_query_scene`, `unity_scene_hierarchy`, `unity_selection`, `unity_console_stream`, `unity_get_game_object`, `unity_get_components`, `unity_read_serialized_properties`, and `unity_asset_search`.
3. For edits, prefer typed ScenePort tools over direct `.unity` or `.prefab` YAML edits: `unity_create_game_object`, `unity_set_transform`, `unity_add_component`, `unity_set_serialized_property`, and dry-run authoring tools such as `unity_authoring_batch`, `unity_create_script`, `unity_create_material`, and `unity_create_prefab`.
4. Keep changes small and reversible. ScenePort write tools are expected to use Unity Undo.
5. After code or scene changes, read `unity_audit_log`, `unity_get_compilation_status`, console logs, `unity_assert_state`, and run relevant Unity tests with `unity_tests_run` when available.
6. For visual debugging, use `unity_capture_game_view`, `unity_capture_scene_view`, and `unity_compare_golden_frame`; for runtime checks, use `unity_enter_play_mode`, `unity_exit_play_mode`, and `unity_runtime_status`.
7. For playable flows, prefer the playtest loop: `unity_start_playtest`, `unity_wait`, `unity_capture_playtest_frame`, optional `unity_send_key`/`unity_send_click`, then `unity_stop_playtest` or `unity_get_playtest_report`.
8. For team adoption checks, use `sceneport:team-readiness-smoke` after running `sceneport doctor`.

Safety rules:

- Do not run arbitrary editor code unless the user explicitly asks for dev-mode execution and understands the risk.
- Do not delete assets, scenes, or GameObjects without explicit user confirmation.
- Do not expose project files or logs to remote services.
- Treat the Unity Editor as the source of truth for scene and prefab state.
- Treat `capability.denied` as a policy boundary, not a transient failure.
- Keep authoring operations dry-run first unless the user has clearly asked to mutate.
- Do not use arbitrary script source; ScenePort script creation is template-only.
- Use `unity_set_serialized_property` only when you know the target instance and `SerializedProperty.propertyPath`.

When ScenePort is unavailable, explain that the Unity bridge package must be installed in the Unity project, the MCP server must be built with `npm run build`, and `sceneport doctor --json` should be run from the Unity project root or with `SCENEPORT_PROJECT_PATH` set.
