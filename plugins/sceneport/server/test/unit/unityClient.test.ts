import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { createServer } from "node:http";
import type { AddressInfo } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { UnityBridgeClient } from "../../src/unityClient.js";
import { FakeBridge } from "../fixtures/fakeBridge.js";

let bridge: FakeBridge | undefined;
let bridge2: FakeBridge | undefined;
let tempRoot: string | undefined;

afterEach(async () => {
  if (bridge) {
    await bridge.stop();
    bridge = undefined;
  }
  if (bridge2) {
    await bridge2.stop();
    bridge2 = undefined;
  }
  if (tempRoot) {
    rmSync(tempRoot, { recursive: true, force: true });
    tempRoot = undefined;
  }
});

function makeProjectWithDiscovery(file: Record<string, unknown>): string {
  tempRoot = mkdtempSync(join(tmpdir(), "sceneport-client-"));
  mkdirSync(join(tempRoot, "Assets"), { recursive: true });
  mkdirSync(join(tempRoot, "ProjectSettings"), { recursive: true });
  mkdirSync(join(tempRoot, "Library", "ScenePort"), { recursive: true });
  writeFileSync(join(tempRoot, "Library", "ScenePort", "bridge.json"), JSON.stringify(file));
  return tempRoot;
}

function makeEmptyProject(): string {
  tempRoot = mkdtempSync(join(tmpdir(), "sceneport-client-"));
  mkdirSync(join(tempRoot, "Assets"), { recursive: true });
  mkdirSync(join(tempRoot, "ProjectSettings"), { recursive: true });
  mkdirSync(join(tempRoot, "Library", "ScenePort"), { recursive: true });
  return tempRoot;
}

function writeDiscovery(project: string, file: Record<string, unknown>): void {
  writeFileSync(join(project, "Library", "ScenePort", "bridge.json"), JSON.stringify(file));
}

describe("UnityBridgeClient URL handling", () => {
  it("rejects non-loopback bridge URLs unless explicitly allowed", () => {
    expect(() => new UnityBridgeClient({ baseUrl: "http://example.com:38987", source: "env-url" }, {})).toThrow(/loopback/);
    expect(
      () => new UnityBridgeClient({ baseUrl: "http://example.com:38987", source: "env-url" }, { SCENEPORT_ALLOW_UNSAFE_BRIDGE_URL: "1" }),
    ).not.toThrow();
  });

  it("builds query params and skips undefined but keeps false/0", async () => {
    bridge = new FakeBridge({ "/scene-hierarchy": () => ({ body: { status: "ok" } }) });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: bridge.url, source: "env-url" }, {});

    await c.get("/scene-hierarchy", { limit: 0, maxDepth: 8, skip: undefined, flag: false });
    const url = bridge.requests[0].url;
    expect(url).toContain("limit=0");
    expect(url).toContain("maxDepth=8");
    expect(url).toContain("flag=false");
    expect(url).not.toContain("skip=");
  });

  it("normalizes a trailing slash in the base URL", async () => {
    bridge = new FakeBridge({ "/health": () => ({ body: { status: "ok" } }) });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: `${bridge.url}/`, source: "env-url" }, {});
    await c.get("/health");
    expect(bridge.requests[0].url).toBe("/health");
  });
});

describe("UnityBridgeClient token + headers", () => {
  it("sends the auth token header on GET and POST", async () => {
    bridge = new FakeBridge({
      "/health": () => ({ body: { status: "ok" } }),
      "/create-game-object": () => ({ body: { status: "ok" } }),
    });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: bridge.url, token: "secret-token", source: "env-url" }, {});

    await c.get("/health");
    await c.post("/create-game-object", { name: "X" });
    expect(bridge.requests[0].headers["x-sceneport-token"]).toBe("secret-token");
    expect(bridge.requests[1].headers["x-sceneport-token"]).toBe("secret-token");
    expect(bridge.requests[1].headers["content-type"]).toContain("application/json");
  });
});

describe("UnityBridgeClient POST idempotency", () => {
  it("stamps a non-empty clientRequestId on the bridge-received body", async () => {
    bridge = new FakeBridge({ "/create-game-object": () => ({ body: { status: "ok" } }) });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: bridge.url, source: "env-url" }, {});

    await c.post("/create-game-object", { name: "X" });
    const body = bridge.requests[0].body as Record<string, unknown>;
    expect(body).toMatchObject({ name: "X" });
    expect(typeof body.clientRequestId).toBe("string");
    expect((body.clientRequestId as string).length).toBeGreaterThan(0);
  });

  it("does not add a clientRequestId to GET requests", async () => {
    bridge = new FakeBridge({ "/selection": () => ({ body: { status: "ok" } }) });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: bridge.url, source: "env-url" }, {});

    await c.get("/selection");
    expect(bridge.requests[0].url).not.toContain("clientRequestId");
  });

  it("reuses the SAME clientRequestId across a rediscover retry", async () => {
    const project = makeEmptyProject();
    // bridge2 accepts; defined first so its URL is known when bridge1's handler
    // rotates discovery to point at it (which makes rediscover() detect a change).
    bridge2 = new FakeBridge({
      "/health": () => ({ body: { status: "ok", projectId: "two" } }),
      "/create-game-object": () => ({ body: { status: "ok" } }),
    });
    await bridge2.start();

    // bridge1 replies 401 once and, as a side effect, rotates discovery to bridge2 so
    // the client's 401-triggered rediscover lands on the fresh bridge and retries.
    bridge = new FakeBridge({
      "/health": () => ({ body: { status: "ok", projectId: "one" } }),
      "/create-game-object": () => {
        writeDiscovery(project, {
          schemaVersion: 2,
          url: bridge2!.url,
          token: "two",
          projectPath: project,
          projectId: "two",
          ownerLeaseId: "lease-two",
          startedUtc: "two",
          heartbeatUtc: new Date().toISOString(),
        });
        return { status: 401, body: { status: "error", error: "stale token" } };
      },
    });
    await bridge.start();

    writeDiscovery(project, {
      schemaVersion: 2,
      url: bridge.url,
      token: "one",
      projectPath: project,
      projectId: "one",
      ownerLeaseId: "lease-one",
      startedUtc: "one",
      heartbeatUtc: new Date().toISOString(),
    });
    const c = new UnityBridgeClient(undefined, {}, project);

    await c.post("/create-game-object", { name: "Y" });

    const first = bridge.requests.find((r) => r.url === "/create-game-object")?.body as Record<string, unknown>;
    const second = bridge2.requests.find((r) => r.url === "/create-game-object")?.body as Record<string, unknown>;
    expect(typeof first.clientRequestId).toBe("string");
    expect((first.clientRequestId as string).length).toBeGreaterThan(0);
    expect(second.clientRequestId).toBe(first.clientRequestId);
  });
});

describe("UnityBridgeClient response parsing", () => {
  it("returns parsed JSON for ok responses", async () => {
    bridge = new FakeBridge({ "/health": () => ({ body: { status: "ok", port: 38987 } }) });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: bridge.url, source: "env-url" }, {});
    expect(await c.get("/health")).toEqual({ status: "ok", port: 38987 });
  });

  it("throws the error message from a non-ok JSON body", async () => {
    bridge = new FakeBridge({ "/scene": () => ({ status: 500, body: { status: "error", error: "compiling" } }) });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: bridge.url, source: "env-url" }, {});
    await expect(c.get("/scene")).rejects.toThrow("compiling");
  });

  it("throws logical 200 error envelopes instead of returning them as success", async () => {
    bridge = new FakeBridge({ "/play-mode": () => ({ body: { status: "error", error: "busy", code: "editor.busy.compiling" } }) });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: bridge.url, source: "env-url" }, {});
    await expect(c.post("/play-mode", { action: "enter" })).rejects.toMatchObject({ code: "editor.busy.compiling" });
  });

  it("falls back to a status message when a non-ok body is empty", async () => {
    bridge = new FakeBridge({ "/scene": () => ({ status: 503, body: "" }) });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: bridge.url, source: "env-url" }, {});
    await expect(c.get("/scene")).rejects.toThrow("HTTP 503");
  });

  it("wraps non-JSON error text", async () => {
    bridge = new FakeBridge({ "/scene": () => ({ status: 500, body: "plain text failure" }) });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: bridge.url, source: "env-url" }, {});
    await expect(c.get("/scene")).rejects.toThrow("plain text failure");
  });
});

describe("UnityBridgeClient resolver freshness", () => {
  it("re-reads discovery when bridge.json changes between requests", async () => {
    bridge = new FakeBridge({
      "/health": () => ({ body: { status: "ok", projectPath: tempRoot, projectId: "one" } }),
      "/selection": () => ({ body: { status: "ok", bridge: "one" } }),
    });
    bridge2 = new FakeBridge({
      "/health": () => ({ body: { status: "ok", projectPath: tempRoot, projectId: "two" } }),
      "/selection": () => ({ body: { status: "ok", bridge: "two" } }),
    });
    await bridge.start();
    await bridge2.start();

    const project = makeEmptyProject();
    writeDiscovery(project, {
      schemaVersion: 2,
      url: bridge.url,
      token: "one",
      projectPath: project,
      projectId: "one",
      ownerLeaseId: "lease-one",
      startedUtc: "one",
      heartbeatUtc: new Date().toISOString(),
    });
    const c = new UnityBridgeClient(undefined, { SCENEPORT_PROJECT_PATH: project }, project);
    expect(await c.get("/selection")).toMatchObject({ bridge: "one" });

    writeDiscovery(project, {
      schemaVersion: 2,
      url: bridge2.url,
      token: "two",
      projectPath: project,
      projectId: "two",
      ownerLeaseId: "lease-two",
      startedUtc: "two",
      heartbeatUtc: new Date().toISOString(),
    });

    expect(await c.get("/selection")).toMatchObject({ bridge: "two" });
  });

  it("classifies missing capabilities as legacy v0.5", async () => {
    bridge = new FakeBridge({ "/capabilities": () => ({ status: 404, body: { status: "error", error: "nope" } }) });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: bridge.url, source: "env-url" }, {});
    await expect(c.getCapabilities()).resolves.toMatchObject({ mode: "legacy-v0.5" });
  });
});

describe("UnityBridgeClient identity guard", () => {
  it("throws on every call when the connected project does not match SCENEPORT_PROJECT_PATH", async () => {
    bridge = new FakeBridge({ "/health": () => ({ body: { status: "ok", projectPath: "/actual/project", projectId: "x" } }) });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: bridge.url, source: "env-url" }, { SCENEPORT_PROJECT_PATH: "/expected/project" });
    await expect(c.get("/scene")).rejects.toThrow(/SCENEPORT_PROJECT_PATH expects/);
  });

  it("statusReport reports the mismatch instead of throwing", async () => {
    bridge = new FakeBridge({ "/health": () => ({ body: { status: "ok", projectPath: "/actual/project", projectId: "x" } }) });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: bridge.url, source: "env-url" }, { SCENEPORT_PROJECT_PATH: "/expected/project" });
    const report = await c.statusReport();
    expect(report.identityMatch).toBe(false);
    expect(report.discoverySource).toBe("env-url");
  });

  it("statusReport flags a pre-0.3 bridge missing projectId", async () => {
    bridge = new FakeBridge({ "/health": () => ({ body: { status: "ok", port: 38987 } }) });
    await bridge.start();
    const c = new UnityBridgeClient({ baseUrl: bridge.url, source: "env-url" }, {});
    const report = await c.statusReport();
    expect(report.warning).toContain("outdated");
    expect(report.tokenConfigured).toBe(false);
  });

  it("times out instead of hanging when the bridge accepts but never responds", async () => {
    // Server that accepts the connection but never writes a response (e.g. Unity mid-recompile).
    const hung = createServer(() => {
      /* intentionally never responds */
    });
    await new Promise<void>((resolve) => hung.listen(0, "127.0.0.1", resolve));
    const port = (hung.address() as AddressInfo).port;
    try {
      const c = new UnityBridgeClient({ baseUrl: `http://127.0.0.1:${port}`, source: "env-url" }, { SCENEPORT_HTTP_TIMEOUT_MS: "150" });
      await expect(c.get("/health")).rejects.toMatchObject({ code: "bridge.timeout" });
    } finally {
      hung.closeAllConnections?.();
      await new Promise<void>((resolve) => hung.close(() => resolve()));
    }
  });
});
