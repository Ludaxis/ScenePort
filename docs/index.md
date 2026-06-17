# ScenePort

**ScenePort is the safe MCP port into the Unity Editor for AI coding agents.**

It lets Claude Code, Codex, and other MCP clients **see** your live Unity Editor — Game view,
Scene view, hierarchy, inspector values, console, and tests — and make small, reversible,
audited edits through typed tools instead of guessing from files alone.

> The full project README (badges, hero media, and the complete tool/resource list) lives at
> the [repository root](https://github.com/Ludaxis/ScenePort#readme). This site collects the
> setup guide, recipe gallery, and reference docs.

## Start here

- **[60-second setup](setup.md)** — add the MCP server with `npx`, install the Unity package,
  and click connect in `Tools > ScenePort > Setup`.
- **[Recipe Gallery](recipes/README.md)** — named workflow prompts (self-heal, visual
  regression, build-UI-from-screenshot, explain-scene, and more) with example transcripts.

## What you can do

- **Build UI from a screenshot** — the agent recreates a layout as real Unity UI objects and
  captures the Game view to compare. See
  [`sceneport:create-ui-from-screenshot`](recipes/create-ui-from-screenshot.md).
- **Self-heal a broken scene** — read the console, apply a small reversible fix, recompile,
  and loop until clean. See [`sceneport:self-heal`](recipes/self-heal.md).
- **Catch visual regressions** — golden-frame capture plus a real pixel-diff image and
  `pixelDiffPercent`. See [`sceneport:visual-regression`](recipes/visual-regression.md).
- **Explain a scene** — capture the views, walk the hierarchy, and narrate how it is wired.
  See [`sceneport:explain-scene`](recipes/explain-scene.md).

## Reference

- [Architecture](architecture/ARCHITECTURE.md)
- [Security Model](security/SECURITY_MODEL.md)
- [Roadmap](roadmap/ROADMAP.md)
- [Troubleshooting](playbooks/TROUBLESHOOTING.md)
