# ScenePort Roadmap

## v0.1: Trustworthy First Loop

- Unity bridge health
- Scene hierarchy
- Selection
- Console logs
- Create GameObject
- Set transform
- Codex and Claude plugin metadata
- Product, architecture, QA, and security docs

## v0.2: Unity Feedback Loop (shipped)

- GameObject and Component inspection
- Add component by type
- Set serialized property by path
- Asset search
- Package list
- Compile status
- Unity Test Framework EditMode tests
- Unity Test Framework PlayMode tests
- Game view screenshot
- Play mode enter/exit
- MCP resources and workflow prompts

## v0.3: Security & Test Hardening (shipped)

- Local auth token (per-project, in `Library/ScenePort/bridge.json`)
- Origin validation + Host check (CSRF and DNS-rebinding defense)
- Multi-editor discovery (port range 38987–38996 + discovery file)
- Project identity verification
- Newtonsoft-based JSON (exponent/NaN/control-char correctness)
- EditMode test suite + MCP server vitest suite + CI test execution
- Version single-sourcing
- Release workflow and Unity package tarball

## v0.4: Playtest Pilot (shipped)

- Playtest start/stop/status/report tools
- MCP-side wait without blocking Unity's main thread
- Game view key/click input helpers
- Tracked playtest frame captures
- Agent-readable report with console observations and recommendations

## v0.5: Team Readiness (shipped)

- Audit log of mutating requests
- `sceneport doctor` diagnostics command
- Team Readiness Demo UPM sample
- Unity-in-CI via game-ci with a project license
- Marketplace install guide for local Codex and Claude workflows
- Malformed JSON and write-surface hardening

## v0.5.1: Team Readiness Seal

- Release evidence reads protocol/capability values from code
- Trust-contract script checks public surface and docs
- README/architecture/security docs stay in sync with capabilities
- `sceneport doctor --json` supports automation and redaction checks

## v0.6: Legible Proof Loop

- Rich read-only editor perception
- Typed serialized reads
- Console event cursor streaming
- Runtime status/query snapshots
- Scene view state and screenshot capture
- Profiler snapshots and asset graph reads
- Structured tests, assertions, scenarios, golden-frame metadata, perf probes
- PR/release evidence includes Staged Trust proof artifacts

## v0.7: Team Operations

- Redacted diagnostics endpoint and MCP resource
- Scoped capability profiles: `read-only`, `team-safe`, `playtest`, `full-safe-local`
- Policy fields in discovery and `/capabilities`
- Token status/rotation CLI
- Codex/Claude config helpers
- Token-file fallback for CI and credential-store flows

## v0.8: Safe Authoring

- Create script
- Create material
- Create prefab
- Transactional/dry-run authoring batch
- Menu item execution allowlist
- Assets-only path validation
- Audit metadata for dry-run, operation count, and authoring paths

## v0.9: Launch Candidate

- Unity 2022.3 / 2023.2 / Unity 6 matrix
- macOS / Windows / Linux smoke where licensing permits
- Protocol/schema freeze
- Governance and rollback playbooks

## v1.0: Open-Source Launch

- Stable schema contract
- Full install docs for Codex and Claude Code
- Community contribution guide
- Marketplace submission for Claude community
- Codex local marketplace guide
- Signed releases where practical
- Maintainer governance

## v1.1: Verified Authoring — Geometry, Shaders, Settings (in progress)

The mission tightens to "the Unity MCP layer where every edit is verified, reversible, and
audited." All new capabilities route through the existing authoring spine (dry-run, Undo,
transactional batch rollback, audit log, capability-group policy).

- Folder creation and inert text/source-asset authoring (extension-allowlisted)
- `.shader` (ShaderLab) authoring + the author → `wait_for_idle` → `get_compile_errors` verify loop
- Mesh authoring: built-in primitives, validated procedural geometry, and Undo-backed assignment
  (new `mesh` capability group)
- Allowlisted project/quality/time/physics settings read and write (new `settings` capability group)

## Phase 2: Scene Graph and Prefab Completeness (in this release)

Round out scene-graph editing and prefab-instance management on the existing `safe-write` and
`authoring` spines, all dry-run-first and Undo-backed.

- Reparent (world-position preserving by default), rename, and reorder siblings (`safe-write`)
- Duplicate and destructive delete of GameObjects (`safe-write`, Undo-backed)
- Prefab instantiation into the active scene and apply/revert of instance overrides (`authoring`)
- Scene-affecting ops are batch-composable via `unity_authoring_batch` with transactional rollback

## v1.2: Animation Systems (in this release)

- AnimationClip authoring from keyframe curves
- AnimatorController creation, parameters, states, and transitions (state-machine graphs)
- Undo-backed Animator assignment (new `animation` capability group)

## v1.3: Shader Graph Node Authoring (preview, in this release)

- Programmatic `.shadergraph` node/slot authoring via the referenceable-JSON format
- Version-pinned and capability-gated (`shadergraph-preview`, off by default)
- Mandatory import round-trip validation with auto-rollback on mangling
