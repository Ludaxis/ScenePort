# ScenePort Troubleshooting Playbook

Start with `sceneport doctor --json` (or **Run doctor** in `Tools > ScenePort > Setup`). It
checks Node version, bridge discovery, Unity health, token state, project identity, and MCP
startup readiness, and emits a `warn`/`fail` per check. The tables below map common symptoms
and each doctor finding to a fix.

## Symptom → diagnosis → fix

### Bridge won't start / no port

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| No `Library/ScenePort/bridge.json` after the editor loads | Bridge never started; an AssetImportWorker or batch process is suppressing it, or scripts failed to compile | Confirm the editor finished compiling; reopen the project so the `[InitializeOnLoad]` bridge runs in the main editor process (not an import worker). |
| `bridge won't start` / port unavailable | All ports in `38987–38996` are occupied (orphaned editors, other tools) | Close stale Unity editors; check the range with `lsof -nP -iTCP:38987-38996` (macOS/Linux). Delete `Library/ScenePort/bridge.json` and reopen to force a clean bind. |
| Port range `38987–38996` exhausted | Ten or more bridges/holders on the loopback range | Free a port in the range or quit other ScenePort editors. The bridge binds the **first free** port in the range and records it in `bridge.json`. |
| Client connects to the wrong port | Stale `bridge.json` from a previous run | Delete `Library/ScenePort/bridge.json`, reopen Unity, rerun `sceneport doctor --json`. |

### Unity busy: compiling or paused mid-call

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| Tool call returns an "editor busy" / main-thread timeout error | Unity is recompiling scripts or importing assets when the call lands | Wait for compilation to finish, then retry. Have the agent call `unity_wait_for_idle` (landing in v1.0) before mutating calls so it blocks on the MCP side instead of failing. |
| Calls hang after entering play mode then pausing | Editor paused; main-thread work cannot complete | Resume or exit play mode. Use `unity_wait` / `unity_playtest_status` to coordinate; do not issue authoring writes while paused. |
| Authoring batch fails midway during a domain reload | A recompile/domain reload interrupted the editor | Retry after compilation settles; `unity_authoring_batch` rolls back assets it created in a failed batch. Gate with `unity_wait_for_idle`. |

### Auth: 401 token mismatch

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| Every endpoint except `/health` returns `401` | The server is not sending the per-project token, or is reading the wrong project's token | Ensure the server runs from inside the Unity project, or set `SCENEPORT_PROJECT_PATH` to the project root so it can read `Library/ScenePort/bridge.json`. Run `sceneport auth status`. |
| `401` after rotating credentials | Server cached an old token | Run `sceneport auth rotate`, then restart the MCP client so it re-reads `bridge.json`. |
| Intermittent `401` | Auth requirement toggled off/on, or two projects share a path | Confirm `Tools > ScenePort > Require Auth Token` matches expectations and that `SCENEPORT_PROJECT_PATH` points at exactly one project. |

### Version mismatch

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| `unity_status` warns about a pre-0.3 bridge, or handshake fails | Server and Unity package are on incompatible versions | Update the **Unity package and the MCP server together**. The bundled server in the UPM package is always matched to that package — prefer the bundled-local path. |
| Tools missing that the docs describe | Older bundled server than the package | Reinstall/refresh the UPM package; rerun `sceneport doctor --json` to confirm the protocol version. |

### Project path: wrong or missing `SCENEPORT_PROJECT_PATH`

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| Server cannot discover the bridge when run outside the project | `SCENEPORT_PROJECT_PATH` unset and cwd is not the Unity project | Set `SCENEPORT_PROJECT_PATH=/absolute/path/to/YourUnityProject` in the MCP `env`. |
| Tool calls refused with an identity mismatch | The path points at a different Unity project than the one running | Point `SCENEPORT_PROJECT_PATH` at the running project; `unity_status` should report `identityMatch: true`. |
| `SCENEPORT_PROJECT_PATH` set but still not found | Wrong folder level (e.g. `Assets/` instead of the project root) | Use the project root — the folder that contains `Assets/`, `Packages/`, and `Library/`. |

### Doctor timeout / `bridge.timeout`

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| `sceneport doctor` times out, or the Setup window reports "Doctor timed out" | Unity busy/compiling, or no bridge running | Let Unity settle, confirm the bridge is up (`bridge.json` present), then rerun. The Setup window's doctor has a bounded timeout and prints the manual command to retry in a terminal. |
| `bridge.timeout` on tool calls | Main-thread work exceeded the bridge timeout | Retry after the editor is idle; raise the client `timeout` (the `.mcp.json` ships `600000` ms) if long compiles are expected; precede writes with `unity_wait_for_idle`. |

## `sceneport doctor` findings → fix

| Doctor check | Result | Fix |
| --- | --- | --- |
| Node version | `fail` (Node < 18) | Install Node 18+ (the bundled server and npx path require it). |
| Bridge discovery | `fail` (no bridge) | Open the Unity project so the bridge auto-starts; delete a stale `bridge.json` and reopen. |
| Bridge discovery | `warn` (stale owner) | Another editor owned the bridge and exited; reopen Unity or delete `bridge.json`. |
| Unity health | `warn` (busy/compiling) | Wait for compilation; retry. Use `unity_wait_for_idle` from the agent. |
| Token state | `fail` (token unreadable) | Set `SCENEPORT_PROJECT_PATH`; ensure the user can read `Library/ScenePort/bridge.json`. |
| Token state | `warn` (auth disabled) | Re-enable `Tools > ScenePort > Require Auth Token` unless you intentionally disabled it. |
| Project identity | `fail` (mismatch) | Point `SCENEPORT_PROJECT_PATH` at the running project root. |
| MCP startup readiness | `fail` | Check the client config (`command`/`args`/`env`); for the bundled-local path confirm `node` resolves and the bundled `index.js` exists. |

## `capability.denied`

The active policy profile blocks the requested endpoint group. Use a read-only tool, or
change the policy only after team approval. See the
[Security Model](../security/SECURITY_MODEL.md) and
[Trust & Safety](../security/TRUST_AND_SAFETY.md).

## Authoring rejected

Authoring writes must target paths under `Assets/`, must not contain traversal, must not
overwrite existing assets, and should run with `dryRun: true` first. Re-run
`unity_validate_authoring_write` to see exactly which rule failed.
