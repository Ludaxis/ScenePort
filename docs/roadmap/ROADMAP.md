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

## v0.2: Unity Feedback Loop

- Compile status
- Unity Test Framework EditMode tests
- Unity Test Framework PlayMode tests
- Game view screenshot
- Scene view screenshot
- Asset search
- Package list

## v0.3: Authoring Workflows

- Create script
- Create material
- Create prefab
- Add component by type
- Set serialized property by path
- Menu item execution allowlist
- Prompt workflows for fixing console errors and creating prefabs

## v0.3.x: Security & Test Hardening (shipped)

- Local auth token (per-project, in `Library/ScenePort/bridge.json`) — shipped
- Origin validation + Host check (CSRF and DNS-rebinding defense) — shipped
- Multi-editor discovery (port range 38987–38996 + discovery file) — shipped
- Project identity verification — shipped
- Newtonsoft-based JSON (exponent/NaN/control-char correctness) — shipped
- EditMode test suite + MCP server vitest suite + CI test execution — shipped
- Version single-sourcing — shipped

## v0.4: Team Readiness

- MCP resources for scenes, assets, packages, tests, and logs
- Audit log of mutating requests
- `sceneport doctor` diagnostics command
- Sample Unity project
- Unity-in-CI via game-ci with a project license

## v1.0: Open-Source Launch

- Stable schema contract
- Full install docs for Codex and Claude Code
- Community contribution guide
- Marketplace submission for Claude community
- Codex local marketplace guide
- Signed releases where practical
- Maintainer governance
