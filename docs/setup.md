# Setup

Three steps to a connected agent that can see your Unity Editor.

## 1. Add the MCP server to your client

No clone, no build — `npx` fetches the published `sceneport-mcp` package.

Claude Code:

```bash
claude mcp add-json sceneport '{
  "command": "npx",
  "args": ["-y", "sceneport-mcp"],
  "env": {
    "SCENEPORT_PROJECT_PATH": "/absolute/path/to/YourUnityProject"
  }
}'
```

Codex (`~/.codex/config.toml`):

```toml
[mcp_servers.sceneport]
command = "npx"
args = ["-y", "sceneport-mcp"]
env = { SCENEPORT_PROJECT_PATH = "/absolute/path/to/YourUnityProject" }
```

Or let ScenePort write the config for you:

```bash
npx -y sceneport-mcp init
npx -y sceneport-mcp config claude --write
```

## 2. Install the Unity bridge package

In Unity: `Window > Package Manager > + > Add package from git URL...` (or **Add package from
disk...** for a local checkout) pointed at the ScenePort UPM package.

## 3. Connect from the Setup window

Open `Tools > ScenePort > Setup` in Unity and click **Connect**. The window starts the local
bridge, shows the discovered port and token state, and confirms your MCP client is linked.

Then start a thread and ask:

```text
Use ScenePort to inspect my active Unity scene and summarize the hierarchy.
```

## Bridge discovery and ports

ScenePort starts one authoritative local editor bridge on the first free port in
`38987–38996` and writes its port, owner heartbeat, protocol metadata, and a per-project auth
token to:

```text
<YourUnityProject>/Library/ScenePort/bridge.json
```

The MCP server discovers the bridge (port and token) automatically when it runs from inside
your Unity project. If it runs elsewhere, set `SCENEPORT_PROJECT_PATH` to your Unity project
folder so it can find `Library/ScenePort/bridge.json`.

- `SCENEPORT_PROJECT_PATH` — path to your Unity project (enables zero-config discovery).
- `SCENEPORT_UNITY_URL` — optional; pin a specific bridge URL.
- `SCENEPORT_TOKEN_FILE` — optional; point at a local token file for CI or credential-store
  flows.

## Local build (developing ScenePort itself)

```bash
git clone git@github.com:Ludaxis/ScenePort.git
cd ScenePort/plugins/sceneport/server
npm ci
npm run build
node build/index.js doctor --json
```

Point the MCP `command`/`args` at
`node /absolute/path/to/ScenePort/plugins/sceneport/server/build/index.js` to use the local
build instead of `npx`.

## CLI helpers

```bash
sceneport auth status
sceneport auth rotate
sceneport config codex
sceneport config claude --write
sceneport update-check --local
```

## Troubleshooting a 401

The server and Unity package must both be v0.3+. The token is read automatically from
`Library/ScenePort/bridge.json`; set `SCENEPORT_PROJECT_PATH` if the server does not run from
inside the Unity project. You can toggle the requirement via `Tools > ScenePort > Require Auth
Token` in the editor. See the [Troubleshooting Playbook](playbooks/TROUBLESHOOTING.md) for
more.
