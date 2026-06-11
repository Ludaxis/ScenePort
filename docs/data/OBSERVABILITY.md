# ScenePort Observability

## Principle

ScenePort should be private by default. Observability starts with local diagnostics, not cloud telemetry.

## Local Signals

Bridge health:

- Unity version
- Active scene name
- Project path
- Play mode status
- Bridge version
- Port

Tool metrics:

- Tool name
- Start time
- Duration
- Success or failure
- Error category
- Response size

Local audit log:

- UTC timestamp
- HTTP method and endpoint
- Mutating action summary
- Target path or instance ID where available
- Success or error status

Error taxonomy:

- Bridge offline
- Unity main-thread timeout
- Invalid tool input
- Object not found
- Unity API exception
- MCP transport failure

## Future Optional Metrics

If contributors add telemetry, it must be:

- Off by default.
- Explicitly documented.
- Anonymous.
- Free of project paths, asset names, source code, logs, and scene data.

## Debug Artifacts

`sceneport doctor` collects:

- Node version
- MCP server version
- Unity bridge health
- Port availability
- Package version
- Token discovery state
- Project identity match state
- Suggested MCP command/config reminders

Future diagnostics should add the last 20 ScenePort bridge errors and recent audit-log
summaries.
