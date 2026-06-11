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

- Starts a localhost bridge on `127.0.0.1:38987`.
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

Implemented in v0.1:

- `GET /health`
- `GET /scene-hierarchy`
- `GET /selection`
- `GET /console`
- `POST /create-game-object`
- `POST /set-transform`

## Tool Contract

Implemented in v0.1:

- `unity_status`
- `unity_scene_hierarchy`
- `unity_selection`
- `unity_console_logs`
- `unity_create_game_object`
- `unity_set_transform`

## Future Architecture

- Resource templates for assets, scenes, prefabs, packages, and tests.
- Tool annotations for destructive actions.
- Optional MCP Streamable HTTP mode.
- Multi-editor discovery.
- Screenshot capture for Game view and Scene view.
- Test runner API integration.
- Build pipeline integration.
