# ScenePort Team Readiness Demo

Import this sample from Unity Package Manager after adding the ScenePort package.

## Demo Loop

1. Add `ScenePortDemoTarget` to an empty GameObject named `ScenePort Demo Target`.
2. Start the ScenePort MCP server and run `sceneport doctor` from the Unity project root.
3. Optionally run `SCENEPORT_PROJECT_PATH=/path/to/project npm run smoke:team-readiness`
   from `plugins/sceneport/server` to write JSON evidence.
4. Ask the agent to run `sceneport:team-readiness-smoke`.
5. Confirm the agent can read bridge capabilities, scene hierarchy, selection, console state, EditMode tests,
   playtest status, and the local audit log.
6. Have the agent make one small reversible change, such as setting the target transform
   or changing `readinessLabel`, then review `unity_audit_log`.

The sample is intentionally tiny: it gives agents a safe object to inspect and edit while
teams verify install, auth, identity, diagnostics, audit, and test readiness.
