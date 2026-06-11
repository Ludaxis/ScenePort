# ScenePort MCP Bridge for Unity

This package runs a localhost-only Unity Editor bridge for ScenePort.

The bridge starts automatically when the editor loads and binds the first free port in
`38987–38996`. It writes the bound port, a per-project auth token, and project identity to:

```text
<YourUnityProject>/Library/ScenePort/bridge.json
```

The ScenePort MCP server reads that file to connect with zero configuration. Use
`Tools > ScenePort` to start, stop, copy the URL, or toggle the auth requirement.

## Dependencies

- `com.unity.nuget.newtonsoft-json` (official Unity package) for JSON. If your project
  ships its own loose `Newtonsoft.Json.dll`, remove it and rely on this package to avoid a
  duplicate-assembly compile error.

## Safety Model

- Binds to `127.0.0.1` only.
- Requires the `X-ScenePort-Token` header on every endpoint except `/health`.
- Rejects requests with an `Origin` header (browser-initiated) and non-loopback `Host`
  headers, before any editor work — this defeats CSRF and DNS rebinding.
- Uses Unity Editor APIs instead of direct scene YAML edits, with `Undo` for create,
  transform, component, and serialized-property operations.
- Keeps arbitrary code execution out of the default bridge.
- Non-finite numbers serialize as `null`.

Toggle the token requirement with `Tools > ScenePort > Require Auth Token` (default on).

## Implemented Endpoints

Status codes: logical errors return `200` with a `{ "status": "error" }` body; rejected
requests return `401` (missing/invalid token), `403` (Origin/Host/method), `413` (body too
large), `415` (non-JSON POST). `/health` is the only endpoint exempt from the token.

- `GET /health`
- `GET /scene`
- `GET /scene-hierarchy?limit=200&maxDepth=8`
- `GET /selection`
- `GET /console?limit=100&type=all`
- `GET /game-object`
- `GET /components`
- `POST /create-game-object`
- `POST /set-transform`
- `POST /add-component`
- `POST /set-serialized-property`
- `GET /asset-search`
- `GET /compilation-status`
- `POST /run-tests`
- `GET /tests-last`
- `POST /capture-game-view`
- `POST /play-mode`
- `GET /packages`
