# ScenePort Team Readiness Demo Loop

Use this loop before a team adopts ScenePort on a real Unity project.

## Setup

1. Add the ScenePort Unity package to a Unity project.
2. Import the `Team Readiness Demo` sample from Package Manager.
3. Create a GameObject named `ScenePort Demo Target`.
4. Add the `ScenePortDemoTarget` component.
5. Build the MCP server:

   ```bash
   cd plugins/sceneport/server
   npm ci
   npm run build
   ```

6. From the Unity project root, run:

   ```bash
   sceneport doctor
   ```

   If the binary is not on `PATH`, run:

   ```bash
   node /absolute/path/to/ScenePort/plugins/sceneport/server/build/index.js doctor
   ```

7. Optionally run the executable smoke runner from the ScenePort checkout while Unity is open:

   ```bash
   cd plugins/sceneport/server
   SCENEPORT_PROJECT_PATH=/absolute/path/to/YourUnityProject npm run smoke:team-readiness
   ```

   The runner writes JSON evidence to `Temp/ScenePort/team-readiness-smoke.json` by default.

## Agent Smoke

Ask Codex, Claude Code, or another MCP client:

```text
Use ScenePort to run sceneport:team-readiness-smoke.
```

Expected evidence:

- `unity_status` confirms bridge, token, and project identity.
- `sceneport://bridge/capabilities` or `/capabilities` confirms protocol and capability hash.
- The scene hierarchy includes `ScenePort Demo Target`.
- Console logs and compilation status are readable.
- A small reversible edit can be made through a typed tool.
- `unity_audit_log` shows the edit.
- A short playtest or Game view capture works when the project is play-mode ready.

## Pass Bar

The team is ready to pilot ScenePort when the doctor report is clean or explainable, the
CLI smoke evidence is `ok`, the audit log captures the test mutation, and the agent can
report clear blockers without manual copy-paste from Unity.
