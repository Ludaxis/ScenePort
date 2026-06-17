import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { claudeAddCommand, codexConfigJson, codexConfigToml, npxServerConfig, resolveProjectPath } from "../../src/setup.js";

describe("npxServerConfig", () => {
  it("uses the npx form with the project path in env", () => {
    const config = npxServerConfig("/path/to/UnityProject");
    expect(config).toEqual({
      command: "npx",
      args: ["-y", "sceneport-mcp"],
      env: { SCENEPORT_PROJECT_PATH: "/path/to/UnityProject" },
    });
  });
});

describe("claudeAddCommand", () => {
  it("contains the add-json invocation and parseable JSON with the project path", () => {
    const command = claudeAddCommand("/path/to/UnityProject");
    expect(command).toContain("claude mcp add-json sceneport");

    const jsonStart = command.indexOf("{");
    const jsonEnd = command.lastIndexOf("}");
    const parsed = JSON.parse(command.slice(jsonStart, jsonEnd + 1));
    expect(parsed.command).toBe("npx");
    expect(parsed.args).toEqual(["-y", "sceneport-mcp"]);
    expect(parsed.env.SCENEPORT_PROJECT_PATH).toBe("/path/to/UnityProject");
  });
});

describe("codexConfigJson", () => {
  it("nests the server under mcpServers.sceneport", () => {
    const parsed = JSON.parse(codexConfigJson("/path/to/UnityProject"));
    expect(parsed.mcpServers.sceneport.command).toBe("npx");
    expect(parsed.mcpServers.sceneport.args).toEqual(["-y", "sceneport-mcp"]);
    expect(parsed.mcpServers.sceneport.env.SCENEPORT_PROJECT_PATH).toBe("/path/to/UnityProject");
  });
});

describe("codexConfigToml", () => {
  it("emits a [mcp_servers.sceneport] section with the project path", () => {
    const toml = codexConfigToml("/path/to/UnityProject");
    expect(toml).toContain("[mcp_servers.sceneport]");
    expect(toml).toContain('command = "npx"');
    expect(toml).toContain('SCENEPORT_PROJECT_PATH = "/path/to/UnityProject"');
  });
});

describe("resolveProjectPath", () => {
  let root: string;

  beforeEach(() => {
    root = mkdtempSync(join(tmpdir(), "sceneport-setup-"));
  });

  afterEach(() => {
    rmSync(root, { recursive: true, force: true });
  });

  it("honors SCENEPORT_PROJECT_PATH", () => {
    expect(resolveProjectPath({ SCENEPORT_PROJECT_PATH: "/explicit/project" }, root)).toBe("/explicit/project");
  });

  it("falls back to cwd when nothing is discoverable", () => {
    expect(resolveProjectPath({}, root)).toBe(root);
  });

  it("detects a Unity project via the discovery file when walking up from cwd", () => {
    const project = join(root, "game");
    mkdirSync(join(project, "Assets"), { recursive: true });
    mkdirSync(join(project, "ProjectSettings"), { recursive: true });
    mkdirSync(join(project, "Library", "ScenePort"), { recursive: true });
    writeFileSync(
      join(project, "Library", "ScenePort", "bridge.json"),
      JSON.stringify({ url: "http://127.0.0.1:38992", projectPath: project }),
    );
    const nested = join(project, "Assets", "Scripts");
    mkdirSync(nested, { recursive: true });
    expect(resolveProjectPath({}, nested)).toBe(project);
  });
});
