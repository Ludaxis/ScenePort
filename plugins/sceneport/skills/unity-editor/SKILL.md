---
name: unity-editor
description: Use ScenePort when the user asks to inspect, debug, test, or safely modify a Unity project through the live Unity Editor.
---

# ScenePort Unity Editor Workflow

Use ScenePort when the task depends on live Unity Editor state: active scene, hierarchy, selected objects, console logs, play mode, tests, assets, Game view screenshots, packages, components, or inspector-level changes.

Default workflow:

1. Call `unity_status` to confirm the editor bridge is reachable.
2. Read context before writing: use `unity_scene_hierarchy`, `unity_selection`, `unity_console_logs`, `unity_get_game_object`, `unity_get_components`, and `unity_asset_search`.
3. For edits, prefer typed ScenePort tools over direct `.unity` or `.prefab` YAML edits: `unity_create_game_object`, `unity_set_transform`, `unity_add_component`, and `unity_set_serialized_property`.
4. Keep changes small and reversible. ScenePort write tools are expected to use Unity Undo.
5. After code or scene changes, read `unity_get_compilation_status`, console logs, and run relevant Unity tests when available.
6. For visual debugging, use `unity_capture_game_view`; for runtime checks, use `unity_enter_play_mode` and `unity_exit_play_mode`.

Safety rules:

- Do not run arbitrary editor code unless the user explicitly asks for dev-mode execution and understands the risk.
- Do not delete assets, scenes, or GameObjects without explicit user confirmation.
- Do not expose project files or logs to remote services.
- Treat the Unity Editor as the source of truth for scene and prefab state.
- Use `unity_set_serialized_property` only when you know the target instance and `SerializedProperty.propertyPath`.

When ScenePort is unavailable, explain that the Unity bridge package must be installed in the Unity project and the MCP server must be built with `npm run build`.
