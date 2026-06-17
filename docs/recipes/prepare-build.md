# `sceneport:prepare-build`

Run pre-build checks — compilation, tests, and performance budgets — and report blockers
before you kick off a player build.

## Copy-paste prompt

```text
> Use the sceneport:prepare-build prompt. Run my pre-build checks — compilation, tests, and
> perf budgets (preview) — and report any blockers before I start a player build.
```

## When to use it

- Before a build or release, to catch red flags early.
- As a pre-commit gate to confirm the project is in a shippable state.

## Tools it drives

- `unity_get_compilation_status` — confirm scripts compile cleanly.
- `unity_console_logs` — check for outstanding errors.
- `unity_run_editmode_tests` / `unity_run_playmode_tests`, `unity_tests_wait`,
  `unity_tests_artifacts` — run and read the test suites.
- `unity_perf_probe`, `unity_check_perf_budgets` — sample performance and compare against
  budgets.
- `unity_diagnostics` — collect a redacted diagnostics snapshot.

## Example transcript

> **You:** Use `sceneport:prepare-build`. Is this project ready to build?

1. The agent calls `unity_get_compilation_status` (compiles) and `unity_console_logs` (no
   errors).
2. It runs EditMode and PlayMode tests via `unity_tests_run`, waits with `unity_tests_wait`,
   and reads `unity_tests_artifacts`: all green.
3. It samples performance with `unity_perf_probe` and runs `unity_check_perf_budgets` — one
   scene exceeds the frame-time budget.
4. It pulls `unity_diagnostics` for context and reports: build is blocked by the perf budget;
   everything else passes.

> **Result:** A go/no-go summary with the single blocker called out.

## Tips

- Treat a budget overrun as a soft blocker — review the perf probe before overriding.
- Run this after [`sceneport:self-heal`](self-heal.md) so the console is already clean.
- Diagnostics are redacted by design; safe to paste into an issue.
