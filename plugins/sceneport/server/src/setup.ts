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
 * The default registration key. Single-project users keep this; driving several
 * Unity projects at once requires a distinct key per project (see instanceName).
 */
export const DEFAULT_INSTANCE_NAME = "sceneport";

/**
 * Derive a stable, distinct registration key from a project path so multiple
 * Unity projects can be registered side by side without colliding on the
 * default "sceneport" key. Slugifies the project folder name, e.g.
 * "/Users/me/Games/My Game" -> "sceneport-my-game".
 */
export function instanceName(projectPath: string, prefix: string = DEFAULT_INSTANCE_NAME): string {
  const base =
    projectPath
      .replace(/[/\\]+$/, "")
      .split(/[/\\]/)
      .pop() ?? "";
  const slug = base
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return slug ? `${prefix}-${slug}` : prefix;
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
 * Pass a distinct `name` per project to drive several Unity projects at once.
 */
export function claudeAddCommand(projectPath: string, name: string = DEFAULT_INSTANCE_NAME): string {
  const json = JSON.stringify(npxServerConfig(projectPath));
  return `claude mcp add-json ${name} '${json}'`;
}

/**
 * JSON snippet a Codex user pastes into their MCP config.
 */
export function codexConfigJson(projectPath: string, name: string = DEFAULT_INSTANCE_NAME): string {
  return JSON.stringify(
    {
      mcpServers: {
        [name]: npxServerConfig(projectPath),
      },
    },
    null,
    2,
  );
}

/**
 * TOML snippet a Codex user can paste into ~/.codex/config.toml.
 */
export function codexConfigToml(projectPath: string, name: string = DEFAULT_INSTANCE_NAME): string {
  const server = npxServerConfig(projectPath);
  const args = server.args.map((arg) => JSON.stringify(arg)).join(", ");
  return [
    `[mcp_servers.${name}]`,
    `command = ${JSON.stringify(server.command)}`,
    `args = [${args}]`,
    "",
    `[mcp_servers.${name}.env]`,
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
