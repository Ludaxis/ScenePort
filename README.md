# ScenePort

ScenePort is an open-source MCP bridge for Unity. It lets Codex, Claude Code, and other MCP clients inspect and safely operate a live Unity Editor through typed tools instead of guessing from files alone.

Tagline: The safe port into the Unity Editor for AI coding agents.

## What You Get

- A TypeScript MCP stdio server in `plugins/sceneport/server`
- A Unity Package Manager editor bridge in `plugins/sceneport/unity-package`
- Claude Code plugin metadata in `plugins/sceneport/.claude-plugin`
- Codex plugin metadata in `plugins/sceneport/.codex-plugin`
- Local marketplace files for Claude and Codex
- Product, architecture, security, QA, data, and roadmap docs

## Quick Start

Clone ScenePort:

```bash
git clone git@github.com:Ludaxis/ScenePort.git
cd ScenePort
```

1. Add the Unity bridge package to your Unity project.

   In Unity: `Window > Package Manager > + > Add package from disk...`

   Select:

   ```text
   plugins/sceneport/unity-package/package.json
   ```

2. Open your Unity project. ScenePort starts a local editor bridge on the first free port
   in `38987–38996` and writes its port and a per-project auth token to:

   ```text
   <YourUnityProject>/Library/ScenePort/bridge.json
   ```

3. Build the MCP server when developing locally.

   The repository includes a bundled `plugins/sceneport/server/build/index.js` for plugin installs. Rebuild it after changing TypeScript sources:

   ```bash
   cd plugins/sceneport/server
   npm ci
   npm run build
   ```

4. Connect Claude Code directly from your project. ScenePort discovers the bridge (port
   and token) automatically when the MCP server runs from inside your Unity project. If it
   runs elsewhere, set `SCENEPORT_PROJECT_PATH` to your Unity project folder so it can find
   `Library/ScenePort/bridge.json`:

   ```bash
   claude mcp add-json sceneport '{
     "command": "node",
     "args": ["/absolute/path/to/ScenePort/plugins/sceneport/server/build/index.js"],
     "env": {
       "SCENEPORT_PROJECT_PATH": "/absolute/path/to/YourUnityProject"
     }
   }'
   ```

   `SCENEPORT_UNITY_URL` is optional and only needed to pin a specific bridge URL.

   **Troubleshooting a 401:** the server and Unity package must both be v0.3+. The token is
   read automatically from `Library/ScenePort/bridge.json`; set `SCENEPORT_PROJECT_PATH` if
   the server does not run from inside the Unity project. You can toggle the requirement via
   `Tools > ScenePort > Require Auth Token` in the editor.

5. For Codex, install from the local marketplace after replacing the path with your local checkout:

   ```bash
   codex plugin marketplace add /absolute/path/to/ScenePort
   codex plugin add sceneport@sceneport-local
   ```

6. Start a new Codex or Claude Code thread and ask:

   ```text
   Use ScenePort to inspect my active Unity scene and summarize the hierarchy.
   ```

## Tools

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

## Resources

- `sceneport://project/status`
- `sceneport://scene/active`
- `sceneport://scene/hierarchy`
- `sceneport://object/{instanceId}`
- `sceneport://console/errors`
- `sceneport://assets/search/{query}`
- `sceneport://tests/editmode`
- `sceneport://tests/playmode`
- `sceneport://packages`
- `sceneport://playtest/status`
- `sceneport://playtest/report`

## Prompts

- `sceneport:fix-console-errors`
- `sceneport:inspect-scene`
- `sceneport:create-prefab`
- `sceneport:create-ui-from-screenshot`
- `sceneport:write-playmode-test`
- `sceneport:debug-play-mode`
- `sceneport:playtest-pilot`
- `sceneport:prepare-build`

## Design Principles

- Read before write.
- Prefer UnityEditor APIs over serialized YAML edits.
- Use Undo for editor mutations.
- Keep all network access bound to localhost by default.
- Make arbitrary code execution a future opt-in feature, not a default capability.
- Ship the MCP server as the shared core, with thin Codex and Claude wrappers.

## Project Docs

- [Product Brief](docs/product/PRODUCT_BRIEF.md)
- [Team Charter](docs/product/TEAM_CHARTER.md)
- [Architecture](docs/architecture/ARCHITECTURE.md)
- [Security Model](docs/security/SECURITY_MODEL.md)
- [QA Plan](docs/qa/QA_PLAN.md)
- [Observability](docs/data/OBSERVABILITY.md)
- [Roadmap](docs/roadmap/ROADMAP.md)

## License

MIT. See [LICENSE](LICENSE).
