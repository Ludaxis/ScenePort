# ScenePort Product Brief

## Name

ScenePort

## One-Liner

ScenePort is the safe MCP port into the Unity Editor for AI coding agents.

## Problem

AI coding agents can edit Unity project files, but they cannot reliably see what Unity sees. They miss live scene state, selected objects, inspector values, console errors, import status, tests, play mode, and screenshots. This makes Unity agent work brittle, slow, and risky.

## Solution

ScenePort connects MCP clients such as Codex and Claude Code to a live Unity Editor through a localhost bridge and typed MCP tools. Agents can inspect the real editor state, make small reversible changes through UnityEditor APIs, read console feedback, and continue iterating with less manual copy-paste.

## Target Users

- Solo game developers using Codex or Claude Code.
- Unity teams adopting AI coding agents.
- Technical artists who need safe editor automation.
- Tooling engineers building internal Unity workflows.
- Open-source AI/game-dev contributors.

## Product Promise

ScenePort lets an agent understand and change Unity projects with the same respect a senior Unity tools engineer would bring: inspect first, mutate safely, preserve undo, and verify with logs and tests.

## MVP User Journey

1. Developer installs the ScenePort UPM package in Unity.
2. Developer builds the ScenePort MCP server.
3. Developer connects Codex or Claude Code.
4. Agent confirms bridge status.
5. Agent reads active scene, selection, and console logs.
6. Agent creates or edits a GameObject through typed tools.
7. Agent reads console feedback and reports what changed.

## Non-Goals For v0.1

- Remote editor access.
- Arbitrary C# execution by default.
- Asset deletion tools.
- Build publishing.
- Full prefab override editing.
- Cloud telemetry.

## Success Metrics

- Time to first successful `unity_status`: under 10 minutes from clone.
- Time to first useful scene inspection: under 12 minutes.
- Zero direct `.unity` or `.prefab` YAML mutation in default workflows.
- 95 percent of tool responses follow documented schema.
- All write tools are undoable in Unity.

## Positioning

ScenePort is not another chat UI for Unity. It is the shared protocol layer that gives any MCP-capable agent a safe editor surface.
