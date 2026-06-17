# `sceneport:fix-console-errors`

Triage the current Unity console, group the errors, and propose targeted fixes — a focused,
single-pass complement to the looping [`sceneport:self-heal`](self-heal.md) recipe.

## Copy-paste prompt

```text
> Use the sceneport:fix-console-errors prompt. Read my Unity console, group the errors by
> root cause, and propose a targeted fix for each — do not apply anything until I approve.
```

## When to use it

- You want to understand what is red in the console before deciding how to fix it.
- You prefer to review proposed fixes rather than have the agent loop autonomously.

## Tools it drives

- `unity_console_logs` — read current errors and warnings.
- `unity_get_compilation_status` — separate compile errors from runtime exceptions.
- `unity_get_game_object`, `unity_get_components`, `unity_read_serialized_properties` —
  inspect the objects named in stack traces.
- `unity_asset_search` — locate the scripts or assets involved.

## Example transcript

> **You:** Use `sceneport:fix-console-errors` and tell me what's wrong.

1. The agent calls `unity_console_logs` and clusters the output: 1 compile error in
   `Enemy.cs`, 4 `NullReferenceException`s at runtime.
2. `unity_get_compilation_status` confirms the compile error blocks everything else.
3. It reads `Enemy.cs` context via `unity_asset_search` and pinpoints a missing semicolon.
4. It explains: fix the compile error first; the null refs are likely downstream and may
   vanish once scripts compile.
5. It proposes the exact edit and, if you approve, hands off to
   [`sceneport:self-heal`](self-heal.md) to apply and verify.

> **Result:** A ranked list of root-cause-first fixes with the noise filtered out.

## Tips

- Compile errors first — runtime exceptions are often cascades from a non-compiling project.
- Ask the agent to dedupe repeated exceptions so you see distinct root causes, not log spam.
- Use this to review; use [`sceneport:self-heal`](self-heal.md) to actually loop on the fix.
