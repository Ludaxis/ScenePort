# ScenePort Release Evidence

- Tag: v0.8.0
- Commit: local
- Server version: 0.8.0
- Unity package version: 0.8.0
- Bridge protocol version: 3
- Capabilities hash: sceneport-staged-trust-v1
- Unity test project version: 2022.3.62f3
- CI run: local run

## Required Gates

- Node lint, typecheck, coverage, and bundled build freshness.
- Unity EditMode tests on TestProjects/BridgeHarness with UNITY_LICENSE required.
- Version sync across server, Unity package, plugin metadata, changelog, and docs.
- Staged trust contract check for public tools, endpoints, docs, and version metadata.
- Plugin shape check for Codex, Claude, MCP metadata, bundled server, and UPM package.

## Local Validation

- `npm run lint`: pass.
- `npm run typecheck`: pass.
- `npm test`: pass, 66 tests.
- `npm run test:coverage`: pass outside the sandbox, 66 tests, 90.02% statement coverage.
- `npm run build`: pass.
- `node scripts/sync-versions.mjs --check`: pass.
- `node scripts/check-trust-contract.mjs`: pass.
- `node plugins/sceneport/server/build/index.js doctor --json`: structured JSON returned; non-zero exit expected without a running Unity bridge.
- Unity 2022.3.62f3 BridgeHarness compile smoke: pass, exit code 0; `io.sceneport.mcpbridge.EditorTests.dll` contains the staged-trust tests. Local `-runTests` invocations did not emit a fresh XML result in this macOS batch environment, so CI/human release promotion must verify EditMode XML before tagging.

## Team Readiness Demo Evidence

- Demo script: docs/demo/TEAM_READINESS_DEMO.md
- Sample: plugins/sceneport/unity-package/Samples~/TeamReadinessDemo
- Expected proof: doctor report, unity_status, scene hierarchy, safe typed edit, unity_audit_log, and optional playtest/capture when the project is play-mode ready.

## Known Risks

- Unity matrix beyond 2022.3 remains a follow-up gate unless manually run.
- Game view capture is asynchronous in Unity; smoke scripts must wait before asserting file presence.
- Team-readiness demo proof is still agent-run/manual until a live Unity MCP smoke runner is added.

## Rollback

- Revert the release tag and install the previous UPM package tarball.
- MCP clients can pin the previous bundled server path or plugin version.
- ScenePort stores local discovery in Library/ScenePort; deleting bridge.json forces rediscovery on restart.
