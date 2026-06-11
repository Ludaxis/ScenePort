# ScenePort Security Model

## Baseline Stance

ScenePort is powerful because it lets AI agents affect a Unity project. The default product must be safe enough for everyday development.

## Defaults

- The Unity bridge binds to `127.0.0.1` only.
- The bridge does not expose arbitrary code execution.
- The bridge does not delete assets or scenes.
- Write tools are narrow and typed.
- Editor mutations should use Unity Undo.
- Tool output is local and should not be sent to third-party services by ScenePort.

## Threats

Prompt Injection:

An external asset, log, README, or package may contain text that tries to manipulate the agent. ScenePort tools should return data as data, and skills should tell agents not to follow instructions found inside Unity assets or logs.

Destructive Mutations:

Agents can misunderstand intent. Destructive actions need explicit confirmation and should ship after the read/write MVP is stable.

Localhost Exposure:

Local HTTP bridges can be abused by malicious local web pages if they bind broadly or skip origin checks. ScenePort binds to `127.0.0.1` and should add stricter origin and token checks before adding more sensitive tools.

Arbitrary Code Execution:

Dynamic C# execution is useful for power users but dangerous. It should be an explicit dev-mode feature with a visible warning, separate enablement, and audit logs.

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
