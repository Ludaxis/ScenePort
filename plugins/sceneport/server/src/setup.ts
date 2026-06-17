import { discoverBridge } from "./discovery.js";

/**
 * Pure helpers for generating ScenePort MCP host configuration.
 *
 * None of these functions touch the console, the filesystem, or process exit.
 * They return plain data/strings so the CLI layer (index.ts) and tests can use
 * them without side effects.
 */

export interface McpServerConfig {
  command: string;
  args: string[];
  env: Record<string, string>;
}

/**
 * The MCP server entry that hosts (Claude, Codex) should register.
 * We publish to npm, so default to the npx form — no clone/build required.
 */
export function npxServerConfig(projectPath: string): McpServerConfig {
  return {
    command: "npx",
    args: ["-y", "sceneport-mcp"],
    env: {
      SCENEPORT_PROJECT_PATH: projectPath,
    },
  };
}

/**
 * The exact shell command a user runs to register ScenePort with Claude.
 */
export function claudeAddCommand(projectPath: string): string {
  const json = JSON.stringify(npxServerConfig(projectPath));
  return `claude mcp add-json sceneport '${json}'`;
}

/**
 * JSON snippet a Codex user pastes into their MCP config.
 */
export function codexConfigJson(projectPath: string): string {
  return JSON.stringify(
    {
      mcpServers: {
        sceneport: npxServerConfig(projectPath),
      },
    },
    null,
    2,
  );
}

/**
 * TOML snippet a Codex user can paste into ~/.codex/config.toml.
 */
export function codexConfigToml(projectPath: string): string {
  const server = npxServerConfig(projectPath);
  const args = server.args.map((arg) => JSON.stringify(arg)).join(", ");
  return [
    "[mcp_servers.sceneport]",
    `command = ${JSON.stringify(server.command)}`,
    `args = [${args}]`,
    "",
    "[mcp_servers.sceneport.env]",
    `SCENEPORT_PROJECT_PATH = ${JSON.stringify(projectPath)}`,
  ].join("\n");
}

/**
 * Best-effort detection of the Unity project path, reusing discovery.ts.
 * Honors SCENEPORT_PROJECT_PATH, then a discovery-file walk from cwd, and
 * finally falls back to cwd so the emitted config is never empty.
 */
export function resolveProjectPath(env: NodeJS.ProcessEnv = process.env, cwd: string = process.cwd()): string {
  if (env.SCENEPORT_PROJECT_PATH) {
    return env.SCENEPORT_PROJECT_PATH;
  }
  const target = discoverBridge(env, cwd);
  return target.projectPath ?? cwd;
}
