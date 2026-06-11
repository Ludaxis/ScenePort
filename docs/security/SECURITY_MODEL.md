# ScenePort Security Model

## Baseline Stance

ScenePort is powerful because it lets AI agents affect a Unity project. The default product must be safe enough for everyday development.

## Trust Boundary

The trust boundary is the local OS user. Anything that can read `Library/` can already
modify `Assets/`, so the token at rest there is not a secret from local code — it exists
to stop *remote* and *browser* callers, which cannot read local files.

## Defaults (implemented in v0.3)

- The Unity bridge binds to `127.0.0.1` only, on the first free port in 38987–38996.
- Every endpoint except `/health` requires the `X-ScenePort-Token` header (constant-time
  comparison). The token is per-project, generated with a CSPRNG, stored in
  `Library/ScenePort/bridge.json`, and reused across editor restarts.
- The request gate rejects, before any body read or main-thread work: any request with an
  `Origin` header (browser-initiated), any non-loopback `Host`, non-GET/POST methods, and
  POST bodies that are not `application/json`. Request bodies are capped at 1 MiB.
- The bridge does not expose arbitrary code execution and does not delete assets or scenes.
- Write tools are narrow, typed, and use Unity Undo.
- Tool output is local; ScenePort does not send it to third-party services.

## Threats and how they are addressed

Prompt Injection:

An external asset, log, README, or package may contain text that tries to manipulate the agent. ScenePort tools return data as data, and skills tell agents not to follow instructions found inside Unity assets or logs.

CSRF from a malicious web page:

Browsers attach an `Origin` header to cross-origin requests and all POSTs; native MCP clients do not. The gate rejects any request carrying `Origin`, so a web page cannot drive the bridge even though it listens on localhost.

DNS Rebinding:

A rebound hostname still arrives in the `Host` header; the gate requires `127.0.0.1` or `localhost`.

Wrong-Project Writes:

The per-project token, plus the MCP server's project-identity check against `SCENEPORT_PROJECT_PATH`, prevent operating on an unexpected project when several editors run at once.

Request Flooding:

Rejected requests never enter the main-thread work queue, so they cannot stall the editor; bodies are size-capped.

Arbitrary Code Execution:

Dynamic C# execution is useful for power users but dangerous. It remains a future explicit dev-mode feature with a visible warning, separate enablement, and audit logs.

## Residual Risks

- `/health` is unauthenticated by design (reachability handshake and `curl` debugging), so it exposes the project path and name to any local process. No tokens or mutating actions are reachable without the token.
- The token at rest is readable by any process running as the same OS user; the design goal is to exclude remote/browser callers, not other local same-user processes.
- A cloud-synced `Library/` folder would sync the token; keep `Library/` out of sync tools (the standard Unity `.gitignore` already excludes it).

## Policy For New Tools

Read tools:

- Allowed by default.
- Must support limits for large outputs.
- Must not include secrets unless the user explicitly asks for that resource.

Write tools:

- Must be typed and scoped.
- Must use Undo where possible.
- Must return a clear summary of changed objects.
- Must be covered by QA tests.

Destructive tools:

- Require explicit confirmation.
- Must prefer moving to trash over permanent deletion.
- Must return exact affected paths and object IDs before execution.

Execution tools:

- Not enabled by default.
- Require a named dev-mode setting.
- Must log every request.
