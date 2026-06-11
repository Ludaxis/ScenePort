# ScenePort QA Plan

## Quality Bar

ScenePort must feel boringly reliable. Agents should not get mysterious bridge errors, stale editor state, or half-applied scene edits.

## Test Matrix

Unity versions:

- 2022.3 LTS
- 2023.2
- Unity 6

Operating systems:

- macOS Apple Silicon
- macOS Intel
- Windows
- Linux Editor where supported

MCP clients:

- Claude Code local stdio
- Codex plugin MCP
- MCP Inspector

## Automated Tests

Unity EditMode:

- Bridge starts and stops.
- Health endpoint schema.
- Capabilities endpoint schema.
- Discovery v2 owner lease, heartbeat, owner-safe delete, and v1 token compatibility.
- AssetImportWorker process classification never hosts the bridge.
- Scene hierarchy reads active scene.
- Selection endpoint reports selected GameObjects.
- Console ring buffer stores bounded log entries.
- Create GameObject registers Undo.
- Set transform registers Undo.
- Malformed JSON POSTs return `400` and do not mutate.
- Mutating requests are recorded in the local audit log.
- Serialized-property writes reject internal/non-editable/non-scene targets.
- 1,000-request health stress does not hang the editor bridge.

MCP Server:

- Tool list loads.
- Each tool handles bridge offline errors cleanly.
- Tool schemas reject invalid input.
- Tool outputs are valid JSON text.
- Large outputs remain bounded.

End-to-End:

- Install UPM package in sample Unity project.
- Build MCP server.
- Call `unity_status`.
- Create object.
- Set transform.
- Verify object exists in hierarchy.
- Undo the change in Unity.

## Manual QA Scripts

First-run smoke:

1. Clone repo.
2. Add package from disk in Unity.
3. Run `curl http://127.0.0.1:38987/health`.
4. Build server with `npm install && npm run build`.
5. Run `sceneport doctor` or `node <ScenePort>/plugins/sceneport/server/build/index.js doctor`.
6. Run `SCENEPORT_PROJECT_PATH=<project> npm run smoke:team-readiness` from
   `plugins/sceneport/server`.
7. Connect Claude Code or Codex.
8. Ask for active scene hierarchy.

Team-readiness smoke:

1. Import the `Team Readiness Demo` sample from Unity Package Manager.
2. Add `ScenePortDemoTarget` to a GameObject.
3. Ask an MCP client to run `sceneport:team-readiness-smoke`.
4. Confirm status, hierarchy, console, tests/playtest when safe, and `unity_audit_log`
   all return useful results.
5. Confirm `Temp/ScenePort/team-readiness-smoke.json` or equivalent CI artifact is attached
   to release evidence when the smoke runner is used.

Regression smoke:

1. Open a project with compile errors.
2. Read console logs.
3. Fix code manually or with an agent.
4. Confirm console errors update after Unity recompiles.

## Manual Security Checks

CSRF defense:

1. Open any local HTML page and run, from its console:
   `fetch("http://127.0.0.1:38987/play-mode", { method: "POST", body: '{"action":"enter"}' })`.
2. Confirm the request is rejected (403) and play mode does not change.

Two-project isolation:

1. Open two Unity projects at once.
2. Confirm the second bridge binds `38988` and each project's `Library/ScenePort/bridge.json`
   has a distinct port and token.
3. With `SCENEPORT_PROJECT_PATH` set, confirm the MCP server talks to the matching project
   and `unity_status` reports `identityMatch: true`.

These are covered automatically by the EditMode suite (CSRF simulation, gate matrix) and the
vitest suite (identity guard), but should be spot-checked manually before a release.

## Release Gates

- No default remote network binding.
- No arbitrary execution in default toolset.
- No direct scene YAML writes in docs or skills.
- All JSON manifests validate.
- Auth token required on all endpoints except `/health`.
- `npm test` and the EditMode suite pass; versions in sync; committed bundle is fresh.
- Pull requests and tagged releases must run Unity EditMode tests with `UNITY_LICENSE`;
  skipping is not allowed.
- Releases must generate `RELEASE_EVIDENCE.md` with test gates, versions, demo evidence
  pointers, risks, and rollback notes.
- README quick start works from a clean checkout.
