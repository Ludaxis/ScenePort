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
- Scene hierarchy reads active scene.
- Selection endpoint reports selected GameObjects.
- Console ring buffer stores bounded log entries.
- Create GameObject registers Undo.
- Set transform registers Undo.

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
5. Connect Claude Code or Codex.
6. Ask for active scene hierarchy.

Regression smoke:

1. Open a project with compile errors.
2. Read console logs.
3. Fix code manually or with an agent.
4. Confirm console errors update after Unity recompiles.

## Release Gates

- No default remote network binding.
- No arbitrary execution in default toolset.
- No direct scene YAML writes in docs or skills.
- All JSON manifests validate.
- README quick start works from a clean checkout.
