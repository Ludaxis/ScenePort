# ScenePort Security Operations Playbook

## Token Handling

- Prefer automatic discovery from `Library/ScenePort/bridge.json`.
- Use `SCENEPORT_TOKEN_FILE` for CI or credential-store integrations.
- Use `sceneport auth status` to verify token storage without printing the token.
- Use `sceneport auth rotate` when a token may have leaked.

## Policy Handling

- Treat `capability.denied` as expected protection.
- Do not expand menu allowlists without a test and security review.
- Keep `Library/` out of source control and cloud sync.

## Review Triggers

Require security review for new write endpoints, route policy changes, auth changes, token storage changes, diagnostics output, and any command that can create code or execute editor actions.
