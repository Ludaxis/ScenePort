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

## v0.5: Team Readiness

- Audit log of mutating requests
- `sceneport doctor` diagnostics command
- Sample Unity project
- Unity-in-CI via game-ci with a project license
- Marketplace install guide for local Codex and Claude workflows

## v0.6: Authoring Workflows

- Create script
- Create material
- Create prefab
- Scene view screenshot
- Menu item execution allowlist

## v1.0: Open-Source Launch

- Stable schema contract
- Full install docs for Codex and Claude Code
- Community contribution guide
- Marketplace submission for Claude community
- Codex local marketplace guide
- Signed releases where practical
- Maintainer governance
