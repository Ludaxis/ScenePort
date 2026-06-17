# Tools & Resources Reference

Every ScenePort MCP tool, resource, and prompt, grouped by the task it serves. Tools marked
**(preview)** report results for an agent to reason over but are not pass/fail gates in v1.0.

## Tools

### Perceive — read the live editor (read-only)

| Tool | What it does |
| --- | --- |
| `unity_status` | Connected project, discovery source, token state, bridge health. |
| `unity_scene_hierarchy` | Active scene hierarchy tree. |
| `unity_selection` | Current editor selection. |
| `unity_console_logs` | Recent console entries (errors/warnings/logs). |
| `unity_console_stream` | New console entries since a cursor. |
| `unity_get_game_object` | A GameObject's identity and basic state. |
| `unity_get_components` | Components on a GameObject. |
| `unity_query_scene` | Structured scene queries by preset/filter. |
| `unity_query_components` | Query components by type across the scene. |
| `unity_read_serialized_properties` | Typed serialized-property reads. |
| `unity_scene_view_state` | Scene view camera/state. |
| `unity_capture_scene_view` | Capture the Scene view as an image content block. |
| `unity_capture_game_view` | Capture the Game view as an image content block. |
| `unity_runtime_status` | Play-mode runtime status. |
| `unity_query_runtime` | Query runtime objects in play mode. |
| `unity_get_runtime_object` | Read a runtime object. |
| `unity_asset_search` | Search the AssetDatabase. |
| `unity_asset_graph` | Asset dependency graph for a GUID. |
| `unity_get_compilation_status` | Whether scripts currently compile. |

### Author — small, typed, Undo-backed edits

Today: uGUI Canvas authoring plus core scene/asset authoring. Dedicated UI-authoring tools
land in v1.1.

| Tool | What it does |
| --- | --- |
| `unity_create_game_object` | Create a GameObject (Undo-backed). |
| `unity_set_transform` | Set transform values (idempotent). |
| `unity_add_component` | Add a component (Undo-backed). |
| `unity_set_serialized_property` | Set a serialized property (idempotent, guarded). |
| `unity_validate_authoring_write` | Dry-run validate an authoring write. |
| `unity_authoring_batch` | Transactional batch; rolls back created assets on failure. |
| `unity_create_script` | Create a C# script under `Assets/`. |
| `unity_create_material` | Create a material asset. |
| `unity_create_prefab` | Create a prefab asset. |
| `unity_menu_item_allowlist` | List menu items allowed for execution. |
| `unity_execute_menu_item` | Execute an exact-match allowlisted menu item. |

### Test — Unity Test Runner and assertions

| Tool | What it does |
| --- | --- |
| `unity_tests_run` | Run EditMode/PlayMode tests. **Supersedes** `unity_run_editmode_tests` / `unity_run_playmode_tests`. |
| `unity_tests_wait` | Wait for a test run to finish without blocking the editor. |
| `unity_tests_artifacts` | Fetch test result artifacts. |
| `unity_assert_state` | Assert editor/scene state. |
| `unity_capture_golden_frame` | Capture a golden reference frame. |
| `unity_compare_golden_frame` | Diff against a golden frame; returns a diff image + `pixelDiffPercent`. |
| `unity_run_editmode_tests` | Legacy EditMode test run (kept for compatibility; prefer `unity_tests_run`). |
| `unity_run_playmode_tests` | Legacy PlayMode test run (kept for compatibility; prefer `unity_tests_run`). |

### Playtest — enter play mode and interact

| Tool | What it does |
| --- | --- |
| `unity_enter_play_mode` | Enter play mode. |
| `unity_exit_play_mode` | Exit play mode. |
| `unity_wait` | MCP-side wait that does not block the editor main thread. |
| `unity_start_playtest` | Start a playtest session. |
| `unity_stop_playtest` | Stop a playtest session. |
| `unity_playtest_status` | Playtest session status. |
| `unity_send_key` | Send a key to the Game view. |
| `unity_send_click` | Send a click to the Game view. |
| `unity_capture_playtest_frame` | Capture a Game view frame during a playtest. |
| `unity_get_playtest_report` | Agent-readable playtest report. |

### Diagnose — health, audit, budgets

| Tool | What it does |
| --- | --- |
| `unity_diagnostics` | Redacted diagnostics bundle. |
| `unity_audit_log` | Read the bounded local audit log of mutating requests. |
| `unity_run_scenario` | **(preview)** Run a scenario. |
| `unity_wait_for_scenario` | **(preview)** Wait for a scenario to finish. |
| `unity_get_scenario_report` | **(preview)** Read a scenario report. |
| `unity_perf_probe` | **(preview)** Sample performance metrics. |
| `unity_check_perf_budgets` | **(preview)** Compare metrics against perf budgets. |

The capture tools (`unity_capture_game_view`, `unity_capture_scene_view`,
`unity_capture_playtest_frame`, `unity_capture_golden_frame`) return real MCP **image content
blocks** the model can see (`inline` defaults to true, `maxEdge` defaults to 1024).

## Resources

| Resource | Reads |
| --- | --- |
| `sceneport://project/status` | Project + bridge status. |
| `sceneport://bridge/capabilities` | Live bridge capability contract. |
| `sceneport://scene/active` | Active scene summary. |
| `sceneport://scene/hierarchy` | Scene hierarchy. |
| `sceneport://object/{instanceId}` | A specific object. |
| `sceneport://console/errors` | Console errors. |
| `sceneport://assets/search/{query}` | Asset search results. |
| `sceneport://tests/editmode` | EditMode test results. |
| `sceneport://tests/playmode` | PlayMode test results. |
| `sceneport://packages` | Installed packages. |
| `sceneport://playtest/status` | Playtest status. |
| `sceneport://playtest/report` | Playtest report. |
| `sceneport://audit/log` | Audit log. |
| `sceneport://diagnostics` | Redacted diagnostics. |
| `sceneport://scene/query/{preset}` | Preset scene query. |
| `sceneport://components/type/{typeName}` | Components by type. |
| `sceneport://serialized/object/{instanceId}` | Serialized object reads. |
| `sceneport://console/events/{cursor}` | Console events since a cursor. |
| `sceneport://assets/graph/{guid}` | Asset dependency graph. |
| `sceneport://scene-view/state` | Scene view state. |
| `sceneport://runtime/status` | Runtime status. |
| `sceneport://runtime/object/{instanceId}` | Runtime object. |
| `sceneport://profiler/snapshot` | Profiler snapshot. |
| `sceneport://authoring/menu-items` | Allowlisted menu items. |

## Prompts

Workflow recipes you can invoke by name. See the
[Recipe Gallery](../recipes/README.md) for transcripts and tips.

- `sceneport:self-heal`
- `sceneport:visual-regression`
- `sceneport:explain-scene`
- `sceneport:fix-console-errors`
- `sceneport:inspect-scene`
- `sceneport:create-prefab`
- `sceneport:create-ui-from-screenshot`
- `sceneport:write-playmode-test`
- `sceneport:debug-play-mode`
- `sceneport:playtest-pilot`
- `sceneport:prepare-build`
- `sceneport:team-readiness-smoke`
