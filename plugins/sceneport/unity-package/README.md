# ScenePort MCP Bridge for Unity

This package runs a localhost-only Unity Editor bridge for ScenePort.

Default URL:

```text
http://127.0.0.1:38987
```

The bridge starts automatically when the editor loads. Use `Tools > ScenePort` to start, stop, or inspect the bridge.

## Safety Model

- Binds to `127.0.0.1` only.
- Uses Unity Editor APIs instead of direct scene YAML edits.
- Uses `Undo` for create, transform, component, and serialized property operations.
- Keeps arbitrary code execution out of the default bridge.

## Implemented Endpoints

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
