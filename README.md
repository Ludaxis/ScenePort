# ScenePort

ScenePort is an open-source MCP bridge for Unity. It lets Codex, Claude Code, and other MCP clients inspect and safely operate a live Unity Editor through typed tools instead of guessing from files alone.

Tagline: The safe port into the Unity Editor for AI coding agents.

## What You Get

- A TypeScript MCP stdio server in `plugins/sceneport/server`
- A Unity Package Manager editor bridge in `plugins/sceneport/unity-package`
- Claude Code plugin metadata in `plugins/sceneport/.claude-plugin`
- Codex plugin metadata in `plugins/sceneport/.codex-plugin`
- Local marketplace files for Claude and Codex
- `sceneport doctor --json` diagnostics, bridge capability readback, policy diagnostics, and local audit-log readback
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

2. Open your Unity project. ScenePort starts one authoritative local editor bridge on the
   first free port in `38987–38996` and writes its port, owner heartbeat, protocol metadata,
   and a per-project auth token to:

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

   Run diagnostics from your Unity project root or with `SCENEPORT_PROJECT_PATH` set:

   ```bash
   node /absolute/path/to/ScenePort/plugins/sceneport/server/build/index.js doctor
   node /absolute/path/to/ScenePort/plugins/sceneport/server/build/index.js doctor --json
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
   `SCENEPORT_TOKEN_FILE` can point at a local token file for CI or credential-store flows.

   Useful CLI helpers:

   ```bash
   sceneport auth status
   sceneport auth rotate
   sceneport config codex
   sceneport config claude
   sceneport update-check --local
   ```

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

   For the v0.5 readiness loop, import the `Team Readiness Demo` sample from Unity
   Package Manager and ask for `sceneport:team-readiness-smoke`, or run
   `SCENEPORT_PROJECT_PATH=/path/to/project npm run smoke:team-readiness` from
   `plugins/sceneport/server`.

## Tools

Core v0.5 tools:

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

Staged Trust perception, proof, diagnostics, and safe authoring tools:

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

## Resources

- `sceneport://project/status`
- `sceneport://bridge/capabilities`
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
- `sceneport://audit/log`
- `sceneport://diagnostics`
- `sceneport://scene/query/{preset}`
- `sceneport://components/type/{typeName}`
- `sceneport://serialized/object/{instanceId}`
- `sceneport://console/events/{cursor}`
- `sceneport://assets/graph/{guid}`
- `sceneport://scene-view/state`
- `sceneport://runtime/status`
- `sceneport://runtime/object/{instanceId}`
- `sceneport://profiler/snapshot`
- `sceneport://authoring/menu-items`

## Prompts

- `sceneport:fix-console-errors`
- `sceneport:inspect-scene`
- `sceneport:create-prefab`
- `sceneport:create-ui-from-screenshot`
- `sceneport:write-playmode-test`
- `sceneport:debug-play-mode`
- `sceneport:playtest-pilot`
- `sceneport:prepare-build`
- `sceneport:team-readiness-smoke`

## Design Principles

- Read before write.
- Prefer UnityEditor APIs over serialized YAML edits.
- Use Undo for editor mutations.
- Keep all network access bound to localhost by default.
- Reject malformed JSON before it can mutate editor state.
- Record mutating requests in a bounded local audit log.
- Enforce scoped capability policy at the bridge; `capability.denied` means the current team profile blocks an endpoint group.
- Treat authoring as dry-run first, Assets-only, allowlist-only for menu execution, and auditable when it writes.
- Make arbitrary code execution a future opt-in feature, not a default capability.
- Ship the MCP server as the shared core, with thin Codex and Claude wrappers.

## Project Docs

- [Product Brief](docs/product/PRODUCT_BRIEF.md)
- [Team Charter](docs/product/TEAM_CHARTER.md)
- [Architecture](docs/architecture/ARCHITECTURE.md)
- [Security Model](docs/security/SECURITY_MODEL.md)
- [QA Plan](docs/qa/QA_PLAN.md)
- [Observability](docs/data/OBSERVABILITY.md)
- [Team Readiness Demo](docs/demo/TEAM_READINESS_DEMO.md)
- [Roadmap](docs/roadmap/ROADMAP.md)
- [Team Adoption Playbook](docs/playbooks/TEAM_ADOPTION.md)
- [Security Operations Playbook](docs/playbooks/SECURITY_OPERATIONS.md)
- [CI And Release Playbook](docs/playbooks/CI_RELEASE.md)
- [Troubleshooting Playbook](docs/playbooks/TROUBLESHOOTING.md)

## License

MIT. See [LICENSE](LICENSE).
