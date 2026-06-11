import { describe, it, expect, afterEach } from "vitest";
import { UnityBridgeClient } from "../../src/unityClient.js";
import { FakeBridge } from "../fixtures/fakeBridge.js";

let bridge: FakeBridge | undefined;

afterEach(async () => {
  if (bridge) {
    await bridge.stop();
    bridge = undefined;
  }
});

describe("UnityBridgeClient URL handling", () => {
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
});
