# ScenePort

<!-- Hero media: renders as a static image until the recorded clip is dropped in. -->
<!-- TODO: drop 45s hero video/gif here; see docs/demo/HERO_STORYBOARD.md -->
<p align="center">
  <a href="docs/demo/HERO_STORYBOARD.md">
    <img src="docs/media/hero.gif" alt="ScenePort: an AI agent connects to the Unity Editor, sees the Game view, and fixes a console error live." width="860">
  </a>
</p>
<!--
  MP4 fallback once recorded (GitHub renders <video> in README on supported themes):
  <p align="center">
    <video src="docs/media/hero.mp4" controls muted loop width="860"></video>
  </p>
-->

ScenePort is the safe MCP port into the Unity Editor for AI coding agents. It lets Claude
Code, Codex, and other MCP clients **see** your live Unity Editor — Game view, Scene view,
hierarchy, inspector values, console, and tests — and make small, reversible, audited edits
through typed tools instead of guessing from files alone. Your agent stops flying blind.

[![CI](https://github.com/Ludaxis/ScenePort/actions/workflows/ci.yml/badge.svg)](https://github.com/Ludaxis/ScenePort/actions/workflows/ci.yml)
[![Docs](https://github.com/Ludaxis/ScenePort/actions/workflows/docs.yml/badge.svg)](https://github.com/Ludaxis/ScenePort/actions/workflows/docs.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## 60-Second Setup

The fastest path needs **no npm publish, no clone, and no build** — the MCP server is
bundled inside the Unity package and the Setup window wires it up for you.

**1. Install the Unity bridge package.** In Unity:
`Window > Package Manager > + > Add package from git URL...` (or **Add package from
disk...** for a local checkout) and point it at the ScenePort UPM package. The bridge
**auto-starts** when the editor loads (it is `[InitializeOnLoad]`), picking the first free
port in `38987–38996` and writing its port and per-project token to
`Library/ScenePort/bridge.json`.

**2. Open `Tools > ScenePort > Setup` in Unity and click "Connect Claude (local)"** (or
**"Connect Codex (local)"**). When the bundled server is found, those two buttons are the
first thing you see; the window resolves `claude`/`node` for you, registers the bundled
server, and shows the discovered port and token state. (The Setup window does **not** start
the bridge — the bridge is already running on its own.)

**3. Start a Claude Code or Codex session and ask:**

```text
Use ScenePort to inspect my active Unity scene and summarize the hierarchy.
```

### Alternative: npx (after the package is published to npm)

Once `sceneport-mcp` is published to npm, you can skip the bundled-local path and point your
client at it directly with `npx`. This path is **not available until the package is
published.**

Claude Code:

```bash
claude mcp add-json sceneport '{
  "command": "npx",
  "args": ["-y", "sceneport-mcp"],
  "env": {
    "SCENEPORT_PROJECT_PATH": "/absolute/path/to/YourUnityProject"
  }
}'
```

Codex (`~/.codex/config.toml`):

```toml
[mcp_servers.sceneport]
command = "npx"
args = ["-y", "sceneport-mcp"]
env = { SCENEPORT_PROJECT_PATH = "/absolute/path/to/YourUnityProject" }
```

See [Detailed Setup](#detailed-setup) below for the bridge port range, `SCENEPORT_PROJECT_PATH`,
auth tokens, and local-build flows.

## What You Can Do

Real capabilities, each driven by ScenePort tools your agent can call.

### Build UI from a screenshot

Hand the agent a mockup; it recreates the layout as real `RectTransform`/`Canvas` objects,
then captures the Game view to compare against your reference.

<!-- TODO: add docs/media/create-ui-from-screenshot.png (generate via scripts/capture-demo.mjs) -->
![Agent rebuilding a UI from a screenshot](docs/media/create-ui-from-screenshot.png)

Recipe: [`sceneport:create-ui-from-screenshot`](docs/recipes/create-ui-from-screenshot.md)

### Self-heal a broken scene

The agent reads console errors, forms a hypothesis, applies a small reversible edit,
recompiles, and re-checks the console — looping until the errors clear.

<!-- TODO: add docs/media/self-heal.png -->
![Self-heal loop clearing console errors](docs/media/self-heal.png)

Recipe: [`sceneport:self-heal`](docs/recipes/self-heal.md)

### Catch visual regressions

Capture a golden frame, change something, then diff pixel-for-pixel. ScenePort returns a
real diff image and a `pixelDiffPercent` so the agent can judge whether a change was intended.

<!-- TODO: add docs/media/visual-regression.png -->
![Pixel diff between a golden frame and the current Game view](docs/media/visual-regression.png)

Recipe: [`sceneport:visual-regression`](docs/recipes/visual-regression.md)

### Explain a scene

Point the agent at an unfamiliar scene; it captures the Scene/Game views, walks the
hierarchy, and explains what is there and how it is wired.

<!-- TODO: add docs/media/explain-scene.png -->
![Agent explaining an unfamiliar scene](docs/media/explain-scene.png)

Recipe: [`sceneport:explain-scene`](docs/recipes/explain-scene.md)

Browse all workflows in the **[Recipe Gallery](docs/recipes/README.md)**.

## What You Get

- A TypeScript MCP stdio server, bundled inside the Unity package (and at
  `plugins/sceneport/server/build/index.js` for plugin installs) and packaged for npm
  publishing as `sceneport-mcp`
- A Unity Package Manager editor bridge with a `Tools > ScenePort > Setup` window
- Vision tools that return real MCP image content blocks the model can see
- Pixel-diff golden frames with a per-pixel diff image and `pixelDiffPercent`
- Claude Code and Codex plugin metadata plus local marketplace files
- `sceneport init` / `config <client> --write` setup helpers and `sceneport doctor --json`
  diagnostics
- Product, architecture, security, QA, data, and roadmap docs, plus a recipe gallery

## Detailed Setup

The 60-second path above is enough for most users. This section covers the specifics.

### Bridge discovery and ports

Open your Unity project. ScenePort starts one authoritative local editor bridge on the first
free port in `38987–38996` and writes its port, owner heartbeat, protocol metadata, and a
per-project auth token to:

```text
<YourUnityProject>/Library/ScenePort/bridge.json
```

The MCP server discovers the bridge (port and token) automatically when it runs from inside
your Unity project. If it runs elsewhere, set `SCENEPORT_PROJECT_PATH` to your Unity project
folder so it can find `Library/ScenePort/bridge.json`.

- `SCENEPORT_PROJECT_PATH` — path to your Unity project (enables zero-config discovery).
- `SCENEPORT_UNITY_URL` — optional; pin a specific bridge URL.
- `SCENEPORT_TOKEN_FILE` — optional; point at a local token file for CI or credential-store
  flows.

### Local build (developing ScenePort itself)

Clone, then rebuild the bundled server after changing TypeScript sources:

```bash
git clone git@github.com:Ludaxis/ScenePort.git
cd ScenePort/plugins/sceneport/server
npm ci
npm run build
```

Run diagnostics from your Unity project root or with `SCENEPORT_PROJECT_PATH` set:

```bash
node /absolute/path/to/ScenePort/plugins/sceneport/server/build/index.js doctor --json
```

To use a local build instead of `npx`, point the MCP `command`/`args` at
`node /absolute/path/to/ScenePort/plugins/sceneport/server/build/index.js`.

### Useful CLI helpers

```bash
sceneport auth status
sceneport auth rotate
sceneport config codex
sceneport config claude --write
sceneport update-check --local
```

### Codex marketplace install

```bash
codex plugin marketplace add /absolute/path/to/ScenePort
codex plugin add sceneport@sceneport-local
```

### Troubleshooting a 401

The server and Unity package must both be v0.3+. The token is read automatically from
`Library/ScenePort/bridge.json`; set `SCENEPORT_PROJECT_PATH` if the server does not run from
inside the Unity project. You can toggle the requirement via `Tools > ScenePort > Require
Auth Token` in the editor. See the
[Troubleshooting Playbook](docs/playbooks/TROUBLESHOOTING.md) for more.

## Tools

Tools are grouped by the task they serve. The full reference, with one line per tool, lives
in [Tools & Resources reference](docs/reference/tools.md).

**Perceive** — read the live editor (read-only):

- `unity_status`, `unity_scene_hierarchy`, `unity_selection`, `unity_console_logs`,
  `unity_console_stream`
- `unity_get_game_object`, `unity_get_components`, `unity_query_scene`,
  `unity_query_components`, `unity_read_serialized_properties`
- `unity_scene_view_state`, `unity_capture_scene_view`, `unity_capture_game_view`
- `unity_runtime_status`, `unity_query_runtime`, `unity_get_runtime_object`
- `unity_asset_search`, `unity_asset_graph`, `unity_get_compilation_status`

**Author** — make small, typed, Undo-backed edits (today: uGUI Canvas + core scene/asset
authoring):

- `unity_create_game_object`, `unity_set_transform`, `unity_add_component`,
  `unity_set_serialized_property`
- `unity_validate_authoring_write`, `unity_authoring_batch` (dry-run first; transactional)
- `unity_create_script`, `unity_create_material`, `unity_create_prefab`
- `unity_menu_item_allowlist`, `unity_execute_menu_item` (exact-match allowlist only)

**Test** — drive the Unity Test Runner and assert state:

- `unity_tests_run`, `unity_tests_wait`, `unity_tests_artifacts`, `unity_assert_state`
- `unity_capture_golden_frame`, `unity_compare_golden_frame`

> `unity_tests_run` (with `unity_tests_wait` / `unity_tests_artifacts`) **supersedes** the
> older `unity_run_editmode_tests` / `unity_run_playmode_tests` pair, which remain for
> backward compatibility. Prefer `unity_tests_run` for new workflows.

**Playtest** — enter play mode and interact:

- `unity_enter_play_mode`, `unity_exit_play_mode`, `unity_wait`
- `unity_start_playtest`, `unity_stop_playtest`, `unity_playtest_status`
- `unity_send_key`, `unity_send_click`, `unity_capture_playtest_frame`,
  `unity_get_playtest_report`

**Diagnose** — health, audit, and budgets:

- `unity_diagnostics`, `unity_audit_log`
- `unity_run_scenario`, `unity_wait_for_scenario`, `unity_get_scenario_report` (preview)
- `unity_perf_probe`, `unity_check_perf_budgets` (preview)

> **Preview in v1.0:** the scenario tools (`unity_run_scenario`, `unity_wait_for_scenario`,
> `unity_get_scenario_report`) and the perf-budget tools (`unity_perf_probe`,
> `unity_check_perf_budgets`) are **preview** surfaces — they report results for an agent to
> reason over, but are **not pass/fail CI gates** yet.

The capture tools (`unity_capture_game_view`, `unity_capture_scene_view`,
`unity_capture_playtest_frame`, `unity_capture_golden_frame`) return real MCP **image content
blocks** the model can see (`inline` defaults to true, `maxEdge` defaults to 1024).
`unity_compare_golden_frame` returns a per-pixel diff image plus `pixelDiffPercent`.

### Scope today

ScenePort authors **uGUI Canvas UIs today**; a perf profiler suite and dedicated
UI-authoring tools land in **v1.1**. The scenario and perf-budget tools above are previews,
not gates, in v1.0.

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

Workflow recipes you can invoke by name. See the **[Recipe Gallery](docs/recipes/README.md)**
for example transcripts and tips.

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

## Design Principles

- Read before write.
- Prefer UnityEditor APIs over serialized YAML edits.
- Use Undo for editor mutations.
- Keep all network access bound to localhost by default.
- Reject malformed JSON before it can mutate editor state.
- Record mutating requests in a bounded local audit log.
- Enforce scoped capability policy at the bridge; `capability.denied` means the current team
  profile blocks an endpoint group.
- Treat authoring as dry-run first, Assets-only, allowlist-only for menu execution, and
  auditable when it writes.
- Make arbitrary code execution a future opt-in feature, not a default capability.
- Ship the MCP server as the shared core, with thin Codex and Claude wrappers.

## Project Docs

- [Recipe Gallery](docs/recipes/README.md)
- [Tools & Resources Reference](docs/reference/tools.md)
- [Trust & Safety](docs/security/TRUST_AND_SAFETY.md)
- [Product Brief](docs/product/PRODUCT_BRIEF.md)
- [Team Charter](docs/product/TEAM_CHARTER.md)
- [Architecture](docs/architecture/ARCHITECTURE.md)
- [Security Model](docs/security/SECURITY_MODEL.md)
- [QA Plan](docs/qa/QA_PLAN.md)
- [Observability](docs/data/OBSERVABILITY.md)
- [Team Readiness Demo](docs/demo/TEAM_READINESS_DEMO.md)
- [Hero Video Storyboard](docs/demo/HERO_STORYBOARD.md)
- [Roadmap](docs/roadmap/ROADMAP.md)
- [Team Adoption Playbook](docs/playbooks/TEAM_ADOPTION.md)
- [Security Operations Playbook](docs/playbooks/SECURITY_OPERATIONS.md)
- [CI And Release Playbook](docs/playbooks/CI_RELEASE.md)
- [Troubleshooting Playbook](docs/playbooks/TROUBLESHOOTING.md)

## License

MIT. See [LICENSE](LICENSE).
