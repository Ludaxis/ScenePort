# ScenePort Architecture

## Overview

ScenePort has three layers:

```text
Codex / Claude Code / MCP Client
  -> ScenePort MCP stdio server
    -> localhost HTTP Unity bridge
      -> UnityEditor APIs on the Unity main thread
```

## Why This Architecture

Unity Editor APIs must run in the Unity process, usually on the main thread. MCP clients should not directly mutate scene YAML or prefab files. A local Unity package gives ScenePort safe access to UnityEditor APIs, while a stdio MCP server gives Codex and Claude Code a standard integration surface.

## Components

Unity Package:

- Starts a localhost bridge on `127.0.0.1`, using the first free port in `38987-38996`.
- Writes port, project identity, policy profile, token storage metadata, and a per-project auth token to `Library/ScenePort/bridge.json`.
- Rejects unsafe requests before body read or Unity main-thread work.
- Captures console logs through `Application.logMessageReceived`.
- Runs editor API work through a main-thread queue.
- Uses `Undo` for write operations.
- Rejects malformed JSON before dispatching handlers.
- Records mutating requests to a bounded local audit log.
- Exposes JSON endpoints for the MCP server.

MCP Server:

- Runs as a local Node.js stdio MCP server.
- Exposes typed tools to MCP clients.
- Calls the Unity bridge over localhost.
- Returns both text and structured content where supported.
- Provides `sceneport doctor --json`, `sceneport auth`, and `sceneport config` commands for setup, bridge readiness, and team diagnostics.

Plugin Wrappers:

- Codex wrapper uses `.codex-plugin/plugin.json` and `.mcp.json`.
- Claude wrapper uses `.claude-plugin/plugin.json` and `.mcp.json`.
- Both wrappers point at the same MCP server.

## Endpoint Contract

Protocol v3 / capabilities hash `sceneport-staged-trust-v1` preserves the v0.5 endpoints and adds the Staged Trust surface.

Baseline endpoints:

- `GET /health`
- `GET /capabilities`
- `GET /diagnostics`
- `POST /auth/rotate`
- `GET /scene`
- `GET /scene-hierarchy`
- `GET /selection`
- `GET /console`
- `GET /console-events`
- `GET /game-object`
- `GET /components`
- `POST /scene-query`
- `POST /component-query`
- `POST /serialized-read`
- `GET /scene-view`
- `POST /capture-scene-view`
- `GET /runtime-status`
- `POST /runtime-query`
- `GET /runtime-object`
- `GET /profiler-snapshot`
- `POST /asset-graph`
- `POST /create-game-object`
- `POST /set-transform`
- `POST /add-component`
- `POST /set-serialized-property`
- `POST /authoring/validate`
- `POST /authoring/batch`
- `POST /create-script`
- `POST /create-material`
- `POST /create-prefab`
- `GET /menu-item-allowlist`
- `POST /execute-menu-item`
- `GET /asset-search`
- `GET /compilation-status`
- `POST /run-tests`
- `GET /tests-last`
- `POST /tests/run`
- `GET /tests/status`
- `GET /tests/wait`
- `GET /tests/artifacts`
- `GET /assertions/catalog`
- `POST /assertions/evaluate`
- `POST /golden-frame/capture`
- `POST /golden-frame/compare`
- `POST /golden-frame/approve`
- `POST /scenario/run`
- `GET /scenario/status`
- `GET /scenario/wait`
- `GET /scenario/report`
- `GET /metrics`
- `POST /perf/probe`
- `POST /perf/check-budget`
- `POST /capture-game-view`
- `POST /play-mode`
- `GET /packages`
- `POST /playtest/start`
- `POST /playtest/stop`
- `GET /playtest/status`
- `GET /playtest/report`
- `POST /playtest/capture-frame`
- `POST /playtest/send-key`
- `POST /playtest/send-click`
- `GET /audit-log`

## Tool Contract

The MCP server keeps all v0.5 tool names stable and adds Staged Trust tools:

- `unity_status`
- `unity_scene_hierarchy`
- `unity_selection`
- `unity_console_logs`
- `unity_get_game_object`
- `unity_get_components`
- `unity_create_game_object`
- `unity_set_transform`
- `unity_add_component`
- `unity_set_serialized_property`
- `unity_asset_search`
- `unity_get_compilation_status`
- `unity_run_editmode_tests`
- `unity_run_playmode_tests`
- `unity_capture_game_view`
- `unity_enter_play_mode`
- `unity_exit_play_mode`
- `unity_start_playtest`
- `unity_stop_playtest`
- `unity_playtest_status`
- `unity_wait`
- `unity_send_key`
- `unity_send_click`
- `unity_capture_playtest_frame`
- `unity_get_playtest_report`
- `unity_audit_log`
- `unity_query_scene`
- `unity_query_components`
- `unity_read_serialized_properties`
- `unity_scene_view_state`
- `unity_capture_scene_view`
- `unity_runtime_status`
- `unity_query_runtime`
- `unity_get_runtime_object`
- `unity_console_stream`
- `unity_profiler_snapshot`
- `unity_asset_graph`
- `unity_tests_run`
- `unity_tests_wait`
- `unity_tests_artifacts`
- `unity_assert_state`
- `unity_capture_golden_frame`
- `unity_compare_golden_frame`
- `unity_run_scenario`
- `unity_wait_for_scenario`
- `unity_get_scenario_report`
- `unity_perf_probe`
- `unity_check_perf_budgets`
- `unity_diagnostics`
- `unity_validate_authoring_write`
- `unity_authoring_batch`
- `unity_create_script`
- `unity_create_material`
- `unity_create_prefab`
- `unity_menu_item_allowlist`
- `unity_execute_menu_item`

## Discovery v3

`Library/ScenePort/bridge.json` keeps v1/v2-compatible fields and adds:

- `policyProfile`
- `tokenStorage`
- `tokenRef`
- `tokenFingerprint`

The token itself remains available only to local MCP clients through discovery/env/token-file flows. Diagnostics and JSON doctor output must return redacted metadata only.

## Policy and Auditing

Endpoint metadata in the Unity router defines allowed methods, endpoint group, and whether a route mutates state. This metadata drives method rejection, scoped capability policy, and audit logging. Read-only POST endpoints such as `/scene-query` and `/serialized-read` are not logged as writes; denied mutating attempts are logged.

Structured denials use `capability.denied` with HTTP 403. Request method violations use `request.method_not_allowed` with HTTP 400.

## Future Architecture

- Optional MCP Streamable HTTP mode.
- Build pipeline integration.
