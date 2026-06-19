# Changelog

## Unreleased

- Added scene-graph editing endpoints (`safe-write` group): `/reparent`, `/rename`,
  `/reorder-sibling`, `/duplicate-game-object`, and `/delete-game-object` (destructive,
  Undo-backed). Reparent keeps world position by default.
- Added prefab-instance endpoints (`authoring` group): `/instantiate-prefab`, `/prefab-apply`,
  and `/prefab-revert`.
- Added animation authoring (new `animation` capability group, denied by team-safe/playtest/
  read-only): `/animation/create-clip` (bakes float curves from keyframes),
  `/animation/create-controller` (typed parameters), `/animation/add-state`,
  `/animation/add-transition`, and `/animation/assign-animator` (Undo-backed scene assignment).
- Activated the `shadergraph-preview` capability group (off by default): `/shadergraph/create`
  writes a `.shadergraph` asset (verbatim JSON or minimal Unlit template) with round-trip
  validation and rollback on import failure.
- New scene/prefab/animation ops are batch-composable via `/authoring/batch` with transactional
  rollback; scene-affecting ops are marked as scene mutations.

## 1.1.0

- Added mesh authoring: `/mesh/create-primitive`, `/mesh/create-procedural` (range-validated
  vertices/triangles, capped, optional normals/UVs), and `/mesh/assign` (Undo-backed MeshFilter/
  MeshRenderer assignment). New `mesh` capability group.
- Added `/create-shader` (`.shader` ShaderLab, verbatim or URP/Built-in template), `/create-folder`,
  and `/create-text-asset` (extension-allowlisted) authoring endpoints.
- Added allowlisted settings read/write: `/settings/get` and `/settings/set` over player/quality/
  time/physics keys. New `settings` capability group, denied by team-safe/playtest/read-only.
- New asset/mesh ops are batch-composable with transactional rollback; settings are excluded from
  batches by design and are not Unity-Undo reversible (previous value returned for manual revert).
- Reserved the `shadergraph-preview` capability group (off by default) for upcoming node authoring.

## 0.8.0

- Added discovery schema v3 with policy profile and redacted token metadata.
- Added route-owned method/policy/audit classification and scoped capability profiles.
- Added diagnostics, perception, proof-loop, and safe authoring endpoints.
- Added dry-run script/material/prefab creation, transactional authoring batch support,
  and an exact-match menu item allowlist.
- Added token rotation through Unity so discovery and in-memory auth stay synchronized.
- Added EditMode coverage for route registry, policy denial, read-only POST auditing,
  perception, console cursors, and authoring dry-runs.

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
