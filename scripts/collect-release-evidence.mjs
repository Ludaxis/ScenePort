#!/usr/bin/env node
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const root = process.cwd();
const serverPkg = JSON.parse(readFileSync(join(root, "plugins/sceneport/server/package.json"), "utf8"));
const unityPkg = JSON.parse(readFileSync(join(root, "plugins/sceneport/unity-package/package.json"), "utf8"));
const router = readFileSync(join(root, "plugins/sceneport/unity-package/Editor/ScenePortRouter.cs"), "utf8");
const protocolVersion = router.match(/internal const int Version = (\d+);/)?.[1] ?? "unknown";
const capabilitiesHash = router.match(/internal const string CapabilitiesHash = "([^"]+)";/)?.[1] ?? "unknown";
const unityVersionPath = join(root, "TestProjects/BridgeHarness/ProjectSettings/ProjectVersion.txt");
const unityVersion = existsSync(unityVersionPath)
  ? readFileSync(unityVersionPath, "utf8").match(/m_EditorVersion:\s*(.+)/)?.[1]?.trim()
  : undefined;

const tag = process.env.GITHUB_REF_NAME ?? `v${serverPkg.version}`;
const sha = process.env.GITHUB_SHA ?? "local";
const runUrl =
  process.env.GITHUB_SERVER_URL && process.env.GITHUB_REPOSITORY && process.env.GITHUB_RUN_ID
    ? `${process.env.GITHUB_SERVER_URL}/${process.env.GITHUB_REPOSITORY}/actions/runs/${process.env.GITHUB_RUN_ID}`
    : "local run";

const evidence = `# ScenePort Release Evidence

- Tag: ${tag}
- Commit: ${sha}
- Server version: ${serverPkg.version}
- Unity package version: ${unityPkg.version}
- Bridge protocol version: ${protocolVersion}
- Capabilities hash: ${capabilitiesHash}
- Unity test project version: ${unityVersion ?? "unknown"}
- CI run: ${runUrl}

## Required Gates

- Node lint, typecheck, coverage, and bundled build freshness.
- Unity EditMode tests on TestProjects/BridgeHarness with UNITY_LICENSE required.
- Version sync across server, Unity package, plugin metadata, changelog, and docs.
- Staged trust contract check for public tools, endpoints, docs, and version metadata.
- Plugin shape check for Codex, Claude, MCP metadata, bundled server, and UPM package.

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
`;

writeFileSync(join(root, "RELEASE_EVIDENCE.md"), evidence);
console.log(evidence);
