# ScenePort Team Charter

## Product Leadership

Product Lead owns positioning, roadmap, release criteria, community adoption, and the trust model.

Core questions:

- What Unity workflows should agents perform without surprising users?
- Which actions must remain read-only, approval-gated, or out of scope?
- What is the shortest path to a magical first-run experience?

## Engineering Roles

Unity Editor Lead:

- Owns UPM package, editor lifecycle, main-thread execution, Undo, scene hierarchy, prefab-safe edits, test integration, and package compatibility.

MCP Platform Lead:

- Owns TypeScript MCP server, tool schemas, transport behavior, client compatibility, error handling, and plugin packaging.

Agent UX Engineer:

- Owns skills/prompts, task flows, tool descriptions, context economy, and host-specific ergonomics for Codex and Claude Code.

Security Engineer:

- Owns localhost binding, permission model, destructive action policy, output sanitization, audit logs, and threat modeling.

QA Automation Engineer:

- Owns Unity EditMode tests, MCP protocol smoke tests, host setup checks, version matrix, and regression suites.

Data/Observability Engineer:

- Owns local diagnostics, structured logs, anonymized optional metrics design, health checks, tool latency, and failure taxonomies.

DevRel/Docs Lead:

- Owns README, installation flows, demos, examples, contribution guide, community marketplace submission, and sample Unity project.

Release Captain:

- Owns semver, changelog, tags, package validation, GitHub releases, npm package release, and marketplace updates.

## Operating Cadence

- Weekly product review: adoption blockers, user-reported workflows, roadmap cuts.
- Weekly engineering review: bridge stability, protocol changes, Unity compatibility.
- Release candidate checklist before every minor version.
- Security review for every new write or execution tool.

## Decision Principles

- Prefer editor truth over filesystem guesses.
- Prefer typed tools over arbitrary code execution.
- Prefer reversible operations over raw speed.
- Prefer small, composable tools over giant "do anything" tools.
- Prefer host-neutral MCP core over host-specific shortcuts.
