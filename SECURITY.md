# Security Policy

ScenePort connects MCP clients to a live Unity Editor over localhost. Please treat security reports seriously, especially issues involving remote access, arbitrary code execution, destructive editor actions, or data exposure.

## Supported Versions

The `main` branch is the supported development line until the project begins tagged stable releases.

## Reporting a Vulnerability

Please do not open a public issue for a vulnerability. Report privately to the repository maintainers with:

- affected version or commit
- operating system and Unity version
- reproduction steps
- expected impact
- any suggested mitigation

We will acknowledge valid reports as quickly as possible and coordinate a fix before public disclosure when appropriate.

## Security Principles

- Bind editor bridges to `127.0.0.1` by default.
- Prefer typed tools over arbitrary code execution.
- Use Unity Undo for editor mutations.
- Require explicit user intent for destructive actions.
- Keep project logs and assets local unless the user deliberately exports them.
