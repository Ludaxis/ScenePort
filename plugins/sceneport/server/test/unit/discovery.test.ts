import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { discoverBridge } from "../../src/discovery.js";

let root: string;

function makeProject(name: string, bridge?: Record<string, unknown>): string {
  const dir = join(root, name);
  mkdirSync(join(dir, "Assets"), { recursive: true });
  mkdirSync(join(dir, "ProjectSettings"), { recursive: true });
  if (bridge) {
    mkdirSync(join(dir, "Library", "ScenePort"), { recursive: true });
    writeFileSync(join(dir, "Library", "ScenePort", "bridge.json"), JSON.stringify(bridge));
  }
  return dir;
}

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), "sceneport-discovery-"));
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

describe("discoverBridge", () => {
  it("prefers SCENEPORT_UNITY_URL but still picks up a token from a project file", () => {
    const project = makeProject("p", { url: "http://127.0.0.1:38990", token: "tok", projectPath: "/p" });
    const target = discoverBridge({ SCENEPORT_UNITY_URL: "http://127.0.0.1:9999", SCENEPORT_PROJECT_PATH: project }, root);
    expect(target.source).toBe("env-url");
    expect(target.baseUrl).toBe("http://127.0.0.1:9999");
    expect(target.token).toBe("tok");
  });

  it("reads the discovery file via SCENEPORT_PROJECT_PATH", () => {
    const project = makeProject("p", { url: "http://127.0.0.1:38991", token: "abc", projectPath: "/p" });
    const target = discoverBridge({ SCENEPORT_PROJECT_PATH: project }, root);
    expect(target.source).toBe("discovery-file");
    expect(target.baseUrl).toBe("http://127.0.0.1:38991");
    expect(target.token).toBe("abc");
  });

  it("walks up from cwd to find a Unity project", () => {
    const project = makeProject("game", { url: "http://127.0.0.1:38992", token: "walk", projectPath: "/game" });
    const nested = join(project, "Assets", "Scripts", "deep");
    mkdirSync(nested, { recursive: true });
    const target = discoverBridge({}, nested);
    expect(target.source).toBe("cwd-walk");
    expect(target.baseUrl).toBe("http://127.0.0.1:38992");
    expect(target.token).toBe("walk");
  });

  it("falls back to the default port with no token", () => {
    const target = discoverBridge({}, root);
    expect(target.source).toBe("default");
    expect(target.baseUrl).toBe("http://127.0.0.1:38987");
    expect(target.token).toBeUndefined();
  });

  it("tolerates a malformed discovery file", () => {
    const dir = join(root, "broken");
    mkdirSync(join(dir, "Assets"), { recursive: true });
    mkdirSync(join(dir, "ProjectSettings"), { recursive: true });
    mkdirSync(join(dir, "Library", "ScenePort"), { recursive: true });
    writeFileSync(join(dir, "Library", "ScenePort", "bridge.json"), "{ not json");
    const target = discoverBridge({ SCENEPORT_PROJECT_PATH: dir }, root);
    expect(target.source).toBe("default");
  });

  it("honors an explicit SCENEPORT_TOKEN override", () => {
    const project = makeProject("p", { url: "http://127.0.0.1:38993", token: "file-token" });
    const target = discoverBridge({ SCENEPORT_PROJECT_PATH: project, SCENEPORT_TOKEN: "override" }, root);
    expect(target.token).toBe("override");
  });
});
