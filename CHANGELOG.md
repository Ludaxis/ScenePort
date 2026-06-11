# Changelog

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
