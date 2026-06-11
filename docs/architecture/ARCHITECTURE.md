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
- Writes port, project identity, and a per-project auth token to `Library/ScenePort/bridge.json`.
- Rejects unsafe requests before body read or Unity main-thread work.
- Captures console logs through `Application.logMessageReceived`.
- Runs editor API work through a main-thread queue.
- Uses `Undo` for write operations.
- Exposes JSON endpoints for the MCP server.

MCP Server:

- Runs as a local Node.js stdio MCP server.
- Exposes typed tools to MCP clients.
- Calls the Unity bridge over localhost.
- Returns both text and structured content where supported.

Plugin Wrappers:

- Codex wrapper uses `.codex-plugin/plugin.json` and `.mcp.json`.
- Claude wrapper uses `.claude-plugin/plugin.json` and `.mcp.json`.
- Both wrappers point at the same MCP server.

## Endpoint Contract

Implemented in v0.4:

- `GET /health`
- `GET /scene`
- `GET /scene-hierarchy`
- `GET /selection`
- `GET /console`
- `GET /game-object`
- `GET /components`
- `POST /create-game-object`
- `POST /set-transform`
- `POST /add-component`
- `POST /set-serialized-property`
- `GET /asset-search`
- `GET /compilation-status`
- `POST /run-tests`
- `GET /tests-last`
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

## Tool Contract

Implemented in v0.4:

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

## Future Architecture

- Optional MCP Streamable HTTP mode.
- Audit log of mutating requests.
- `sceneport doctor` diagnostics.
- Scene view screenshot capture.
- Menu item execution allowlist.
- Build pipeline integration.
