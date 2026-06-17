# Changelog

## v0.9.0

### Wow And Easy Setup
- Capture tools (`unity_capture_game_view`, `unity_capture_scene_view`,
  `unity_capture_playtest_frame`, `unity_capture_golden_frame`) now return real MCP image
  content blocks the model can see, with `inline` (default true) and `maxEdge` (default 1024)
  parameters.
- `unity_compare_golden_frame` now returns a per-pixel diff image plus `pixelDiffPercent` for
  visual-regression checks.
- Prepared the MCP server for npm publishing as `sceneport-mcp` so that `npx -y sceneport-mcp`
  will work without a clone or build once the package is published. Until then, use the
  bundled-local path via `Tools > ScenePort > Setup`.
- Added a `sceneport init` / `config <client> --write` setup flow that auto-writes host MCP
  config for Claude Code and Codex.
- Added a Unity Editor window at `Tools > ScenePort > Setup` for one-click bridge connection
  and discovery/token state readback.
- Added new recipe prompts `sceneport:self-heal`, `sceneport:visual-regression`, and
  `sceneport:explain-scene`.
- Added a recipe gallery under `docs/recipes/`, a MkDocs Material docs site
  (`mkdocs.yml` + `.github/workflows/docs.yml`), a `scripts/capture-demo.mjs` screenshot
  harness, and a restructured README with a hero media slot and 60-second setup.

## 0.8.0

### Staged Trust
- Added protocol v3 / discovery v3 metadata with policy profile, token storage, token
  reference, and redacted token fingerprint.
- Added route-owned method, endpoint group, mutation, policy, and audit metadata.
- Added scoped capability profiles and `capability.denied` responses.
- Added rich read-only perception tools for scene query, component query, typed serialized
  reads, Scene view state/capture, runtime status/query, console cursors, profiler
  snapshots, and asset graphs.
- Added proof-loop endpoints and MCP tools for tests, assertions, golden-frame metadata,
  scenarios, perf probes, and evidence artifact files under `Temp/ScenePort`.
- Added redacted diagnostics through `/diagnostics`, `sceneport://diagnostics`, and
  `unity_diagnostics`.
- Added safe authoring tools for dry-run/transactional batches, script/material/prefab
  creation, and exact-match menu allowlist execution.
- Added `sceneport doctor --json`, `sceneport auth status|rotate|migrate`,
  `sceneport config codex|claude`, `sceneport update-check`, `SCENEPORT_TOKEN_FILE`, and
  the `scripts/check-trust-contract.mjs` CI gate.
- Expanded Unity EditMode and MCP Vitest coverage for staged trust routes, policies,
  annotations, dry-run behavior, and tool/resource contracts.

## 0.5.0

### Team Readiness
- Added `sceneport doctor` diagnostics for Node version, bridge discovery, Unity health,
  token state, project identity, and MCP startup readiness.
- Added v2 discovery metadata with bridge protocol version, capabilities hash, owner lease,
  process role, heartbeat, and stale-owner diagnostics.
- Added `/capabilities` and `sceneport://bridge/capabilities` so agents can inspect the
  live bridge contract.
- Added a local Unity audit log for mutating ScenePort requests plus `unity_audit_log`
  and `sceneport://audit/log` MCP read surfaces.
- Hardened request safety: malformed or non-object JSON POST bodies now return `400`
  instead of falling back to defaults, and serialized-property writes block internal
  script/prefab references, non-editable properties, and non-scene object targets.
- Added structured bridge errors for bad requests, auth failures, editor busy states, and
  main-thread timeouts.
- Prevented AssetImportWorker processes from hosting the bridge while keeping test
  batchmode supported.
- Made pull requests and tagged releases require Unity EditMode tests with a configured
  Unity license, plus generated release evidence.
- Added a UPM `Team Readiness Demo` sample and `sceneport:team-readiness-smoke` prompt.

## 0.4.0

### Playtest Pilot
- Added ScenePort playtest sessions with start, stop, status, and report tools.
- Added MCP-side waiting that does not block the Unity Editor main thread.
- Added Game view key/click input helpers for lightweight playtest interactions.
- Added playtest frame capture tracking and report resources.
- Added agent-readable playtest reports with console observations, captures, interactions,
  and recommendations.

## 0.3.0

**Breaking: update the Unity package and the MCP server together.** The bridge now
requires an auth token on every endpoint except `/health`; an older server paired with a
0.3 bridge will get 401s until upgraded.

### Security
- Auth token required on all endpoints except `/health` (per-project, stored in
  `Library/ScenePort/bridge.json`, reused across restarts).
- Request gate rejects browser-initiated requests (any `Origin` header) and non-loopback
  `Host` headers, defeating CSRF and DNS rebinding before any editor work happens.
- Removed the misleading `Access-Control-Allow-Origin` header.
- `Tools > ScenePort > Require Auth Token` menu toggle (default on).

### Reliability
- Zero-config bridge discovery (`SCENEPORT_UNITY_URL` → `SCENEPORT_PROJECT_PATH` →
  cwd walk → default). `SCENEPORT_UNITY_URL` is now optional.
- Port falls back through 38987–38996 when the default is busy; the actual port and
  project identity are written to the discovery file.
- `unity_status` reports the connected project, discovery source, and token state, and
  warns about a pre-0.3 bridge. Tool calls refuse a project that does not match
  `SCENEPORT_PROJECT_PATH`.

### Correctness
- Replaced the hand-rolled JSON layer with Newtonsoft: exponent-notation numbers, NaN,
  and control characters are now handled correctly.
- `unity_capture_game_view` is no longer annotated read-only; play-mode and
  set-transform/set-serialized-property are marked idempotent.

### Engineering
- Unity bridge split into testable units with 63 EditMode tests; MCP server split with
  50 vitest unit/integration tests. Version single-sourced via `scripts/sync-versions.mjs`.

## 0.2.0

- Added GameObject and Component inspection.
- Added Undo-backed Component creation and SerializedProperty edits.
- Added AssetDatabase search, package summaries, compilation status, play mode controls, Game view capture, and Unity Test Runner integration.
- Added MCP resources and workflow prompts.
- Bundled the MCP server into a standalone `build/index.js` for plugin distribution.

## 0.1.0

- Initial ScenePort MCP server and Unity Editor bridge.
- Added status, scene hierarchy, selection, console logs, GameObject creation, and transform edits.
