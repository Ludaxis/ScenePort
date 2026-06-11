# Changelog

## 0.5.0

- Added a bounded local audit log at `Library/ScenePort/audit.json` plus `/audit-log`.
- Added discovery schema v2 with protocol, capability, owner lease, process role,
  heartbeat, and expiry metadata.
- Added `/capabilities` for bridge contract inspection.
- Prevented AssetImportWorker processes from hosting the bridge while keeping Unity test
  batchmode supported.
- Added structured error bodies for request, auth, editor busy, and timeout failures.
- Rejected malformed or non-object JSON POST bodies with `400` before editor mutation.
- Hardened serialized-property writes by blocking internal script/prefab references,
  non-editable properties, and non-scene object targets.
- Added the `Team Readiness Demo` UPM sample.

## 0.4.0

- Added Playtest Pilot endpoints for start, stop, status, report, Game view key/click events,
  and tracked frame captures.
- Added playtest reports with interactions, captures, recent console observations, and
  recommendations.

## 0.3.0

**Breaking: update the ScenePort MCP server to 0.3+ alongside this package.** The bridge
now requires the `X-ScenePort-Token` header on every endpoint except `/health`.

- Added a per-project auth token written to `Library/ScenePort/bridge.json` and reused
  across editor restarts.
- Added a request gate that rejects browser-initiated requests (any `Origin` header) and
  non-loopback `Host` headers; removed the misleading CORS header.
- Bridge now binds the first free port in 38987–38996 and records the bound port, token,
  and project identity in the discovery file.
- `/health` now reports `projectId`, `projectName`, and `tokenRequired`.
- Added the `Tools > ScenePort > Require Auth Token` menu toggle (default on).
- Replaced the hand-rolled JSON layer with `com.unity.nuget.newtonsoft-json` (new
  dependency), fixing exponent-notation, NaN/Infinity, and control-character handling.
- Split the bridge into testable units and added an EditMode test suite (run via
  `TestProjects/BridgeHarness`).

If a consumer project ships its own loose `Newtonsoft.Json.dll`, remove it and rely on
the official `com.unity.nuget.newtonsoft-json` package to avoid a duplicate-assembly conflict.

## 0.2.0

- Added GameObject detail and Component inspection endpoints.
- Added Undo-backed component creation and SerializedProperty writes.
- Added AssetDatabase search, package manifest, compilation status, play mode, Game view capture, and Unity Test Runner endpoints.
- Added MCP resources and prompts in the shared ScenePort server.

## 0.1.0

- Initial ScenePort Unity Editor bridge.
- Added localhost health, scene hierarchy, selection, console, GameObject creation, and transform tools.
