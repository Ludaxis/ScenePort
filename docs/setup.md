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

## Multiple Unity projects at once

Each MCP server process talks to exactly one Unity bridge, pinned by `SCENEPORT_PROJECT_PATH`.
To drive several projects open at the same time, register **one MCP server per project**, each
with a distinct name and its own `SCENEPORT_PROJECT_PATH`. Without distinct names the
registrations collide on the default `sceneport` key, and the zero-config fallback would
otherwise resolve every server to whichever editor happened to grab port `38987`.

**Easiest — from inside Unity:** open each project and use **Tools ▸ ScenePort ▸ Setup**. The
window pre-fills a unique **Registration name** derived from the project folder (e.g.
`sceneport-my-game`); click **Connect Claude (local)** in each project and they register side by
side without collisions. Edit the name field first if you want a different key.

**From the CLI**, use the `--name` flag (`--name auto` derives a slug from the project folder):

```bash
npx -y sceneport-mcp config claude --name auto --write   # run once per project, from each project
# or pin the name yourself:
npx -y sceneport-mcp config claude --name sceneport-alpha --write
```

Or write the entries by hand — for example a Claude `.mcp.json` for four projects:

```json
{
  "mcpServers": {
    "sceneport-alpha":   { "command": "npx", "args": ["-y", "sceneport-mcp"], "env": { "SCENEPORT_PROJECT_PATH": "/abs/path/Alpha" } },
    "sceneport-bravo":   { "command": "npx", "args": ["-y", "sceneport-mcp"], "env": { "SCENEPORT_PROJECT_PATH": "/abs/path/Bravo" } },
    "sceneport-charlie": { "command": "npx", "args": ["-y", "sceneport-mcp"], "env": { "SCENEPORT_PROJECT_PATH": "/abs/path/Charlie" } },
    "sceneport-delta":   { "command": "npx", "args": ["-y", "sceneport-mcp"], "env": { "SCENEPORT_PROJECT_PATH": "/abs/path/Delta" } }
  }
}
```

Each entry is its own stdio process with its own per-project token, and the editors coexist on
their own ports in the `38987–38996` range. Tools are then addressed per server (e.g.
`sceneport-alpha`'s `unity_scene_graph` vs `sceneport-delta`'s).

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
