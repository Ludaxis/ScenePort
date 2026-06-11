import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { InMemoryTransport } from "@modelcontextprotocol/sdk/inMemory.js";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { createScenePortServer } from "../../src/server.js";
import { UnityBridgeClient } from "../../src/unityClient.js";
import { VERSION } from "../../src/version.js";
import { FakeBridge, type RouteHandler } from "../fixtures/fakeBridge.js";

const okHealth: RouteHandler = () => ({ body: { status: "ok", bridge: "sceneport", port: 38987, projectId: "abc" } });

const TOOL_NAMES = [
  "unity_status",
  "unity_scene_hierarchy",
  "unity_selection",
  "unity_console_logs",
  "unity_get_game_object",
  "unity_get_components",
  "unity_create_game_object",
  "unity_set_transform",
  "unity_add_component",
  "unity_set_serialized_property",
  "unity_asset_search",
  "unity_get_compilation_status",
  "unity_run_editmode_tests",
  "unity_run_playmode_tests",
  "unity_capture_game_view",
  "unity_enter_play_mode",
  "unity_exit_play_mode",
  "unity_start_playtest",
  "unity_stop_playtest",
  "unity_playtest_status",
  "unity_wait",
  "unity_send_key",
  "unity_send_click",
  "unity_capture_playtest_frame",
  "unity_get_playtest_report",
  "unity_audit_log",
  "unity_query_scene",
  "unity_query_components",
  "unity_read_serialized_properties",
  "unity_scene_view_state",
  "unity_capture_scene_view",
  "unity_runtime_status",
  "unity_query_runtime",
  "unity_get_runtime_object",
  "unity_console_stream",
  "unity_profiler_snapshot",
  "unity_asset_graph",
  "unity_tests_run",
  "unity_tests_wait",
  "unity_tests_artifacts",
  "unity_assert_state",
  "unity_capture_golden_frame",
  "unity_compare_golden_frame",
  "unity_run_scenario",
  "unity_wait_for_scenario",
  "unity_get_scenario_report",
  "unity_perf_probe",
  "unity_check_perf_budgets",
  "unity_diagnostics",
  "unity_validate_authoring_write",
  "unity_authoring_batch",
  "unity_create_script",
  "unity_create_material",
  "unity_create_prefab",
  "unity_menu_item_allowlist",
  "unity_execute_menu_item",
];

let bridge: FakeBridge | undefined;
let client: Client | undefined;

async function connectClient(unityBaseUrl: string) {
  const server = createScenePortServer(new UnityBridgeClient({ baseUrl: unityBaseUrl, source: "env-url" }, {}));
  const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair();
  const c = new Client({ name: "test", version: "1.0.0" });
  await Promise.all([server.connect(serverTransport), c.connect(clientTransport)]);
  return c;
}

async function connect(routes: Record<string, RouteHandler>) {
  bridge = new FakeBridge(routes);
  await bridge.start();
  client = await connectClient(bridge.url);
}

afterEach(async () => {
  try {
    await client?.close();
  } catch {
    // already closed
  }
  try {
    await bridge?.stop();
  } catch {
    // already stopped
  }
  client = undefined;
  bridge = undefined;
});

describe("MCP server tool surface", () => {
  beforeEach(async () => {
    await connect({
      "/health": okHealth,
      "/capabilities": () => ({ body: { status: "ok", bridge: "sceneport", protocolVersion: 1, capabilitiesHash: "hash" } }),
      "/scene-hierarchy": (r) => ({ body: { status: "ok", url: r.url } }),
      "/create-game-object": () => ({ body: { status: "ok" } }),
      "/set-serialized-property": () => ({ body: { status: "ok" } }),
      "/run-tests": () => ({ body: { status: "ok", run: { mode: "editmode" } } }),
      "/game-object": () => ({ body: { status: "ok" } }),
      "/playtest/start": () => ({ body: { status: "ok", session: { status: "running" } } }),
      "/playtest/status": () => ({ body: { status: "ok", session: { status: "running" } } }),
      "/playtest/send-key": () => ({ body: { status: "ok" } }),
      "/playtest/send-click": () => ({ body: { status: "ok" } }),
      "/scene-query": (r) => ({ body: { status: "ok", url: r.url, body: r.body } }),
      "/serialized-read": (r) => ({ body: { status: "ok", body: r.body } }),
      "/tests/run": () => ({ body: { status: "ok", run: { mode: "editmode" } } }),
      "/assertions/evaluate": (r) => ({ body: { status: "ok", body: r.body } }),
      "/authoring/batch": (r) => ({ body: { status: "ok", body: r.body } }),
      "/create-script": (r) => ({ body: { status: "ok", body: r.body } }),
    });
  });

  it("lists exactly the expected staged-trust tools", async () => {
    const { tools } = await client!.listTools();
    expect(tools.map((t) => t.name).sort()).toEqual([...TOOL_NAMES].sort());
  });

  it("gives every tool a valid object input schema", async () => {
    const { tools } = await client!.listTools();
    for (const tool of tools) {
      expect(tool.inputSchema.type).toBe("object");
    }
  });

  it("marks read-only tools with readOnlyHint and capture as not read-only", async () => {
    const { tools } = await client!.listTools();
    const byName = Object.fromEntries(tools.map((t) => [t.name, t]));
    expect(byName.unity_scene_hierarchy.annotations?.readOnlyHint).toBe(true);
    expect(byName.unity_capture_game_view.annotations?.readOnlyHint).toBe(false);
    expect(byName.unity_create_game_object.annotations?.readOnlyHint).toBe(false);
    expect(byName.unity_query_scene.annotations?.readOnlyHint).toBe(true);
    expect(byName.unity_create_script.annotations?.readOnlyHint).toBe(false);
  });

  it("round-trips unity_status through the bridge", async () => {
    const result = await client!.callTool({ name: "unity_status", arguments: {} });
    const payload = JSON.parse((result.content as Array<{ text: string }>)[0].text);
    expect(payload.bridge).toBe("sceneport");
    expect(payload.discoverySource).toBe("env-url");
    expect(payload.capabilities.protocolVersion).toBe(1);
  });

  it("plumbs query params to the bridge", async () => {
    await client!.callTool({ name: "unity_scene_hierarchy", arguments: { limit: 5, maxDepth: 2 } });
    const hit = bridge!.requests.find((r) => r.url.startsWith("/scene-hierarchy"));
    expect(hit?.url).toContain("limit=5");
    expect(hit?.url).toContain("maxDepth=2");
  });

  it("encodes a color value to the tagged wire format", async () => {
    await client!.callTool({
      name: "unity_set_serialized_property",
      arguments: { instanceId: 1, propertyPath: "colorField", value: { r: 1, g: 0, b: 0 } },
    });
    const hit = bridge!.requests.find((r) => r.url === "/set-serialized-property");
    expect((hit?.body as Record<string, unknown>).valueKind).toBe("color");
    expect((hit?.body as { colorValue: { a: number } }).colorValue.a).toBe(1);
  });

  it("joins test name arrays to CSV", async () => {
    await client!.callTool({ name: "unity_run_editmode_tests", arguments: { testNames: ["A", "B"] } });
    const hit = bridge!.requests.find((r) => r.url === "/run-tests");
    expect((hit?.body as Record<string, unknown>).testNames).toBe("A,B");
    expect((hit?.body as Record<string, unknown>).mode).toBe("editmode");
  });

  it("plumbs staged-trust tools to their endpoints", async () => {
    await client!.callTool({ name: "unity_query_scene", arguments: { limit: 5, componentType: "Camera" } });
    await client!.callTool({ name: "unity_read_serialized_properties", arguments: { instanceId: 42, propertyLimit: 5 } });
    await client!.callTool({ name: "unity_tests_run", arguments: { mode: "editmode", testNames: ["Smoke"] } });
    await client!.callTool({ name: "unity_assert_state", arguments: { checks: [{ type: "health.status" }] } });
    await client!.callTool({
      name: "unity_authoring_batch",
      arguments: { dryRun: true, operations: [{ op: "createGameObject", args: { name: "DryRun" } }] },
    });
    await client!.callTool({ name: "unity_create_script", arguments: { className: "ScenePortGenerated", dryRun: true } });

    expect(bridge!.requests.some((r) => r.url === "/scene-query" && (r.body as Record<string, unknown>).componentType === "Camera")).toBe(
      true,
    );
    expect(bridge!.requests.some((r) => r.url === "/serialized-read" && (r.body as Record<string, unknown>).instanceId === 42)).toBe(true);
    expect(bridge!.requests.some((r) => r.url === "/tests/run" && (r.body as Record<string, unknown>).testNames === "Smoke")).toBe(true);
    expect(bridge!.requests.some((r) => r.url === "/assertions/evaluate")).toBe(true);
    expect(bridge!.requests.some((r) => r.url === "/authoring/batch" && (r.body as Record<string, unknown>).dryRun === true)).toBe(true);
    expect(bridge!.requests.some((r) => r.url === "/create-script" && (r.body as Record<string, unknown>).dryRun === true)).toBe(true);
  });

  it("plumbs playtest commands to the bridge", async () => {
    await client!.callTool({ name: "unity_start_playtest", arguments: { label: "Smoke", enterPlayMode: false } });
    await client!.callTool({ name: "unity_send_key", arguments: { key: "Space", modifiers: ["Shift"] } });
    await client!.callTool({ name: "unity_send_click", arguments: { x: 0.5, y: 0.75, button: 0 } });
    await client!.callTool({ name: "unity_wait", arguments: { milliseconds: 0 } });

    const start = bridge!.requests.find((r) => r.url === "/playtest/start");
    expect((start?.body as Record<string, unknown>).label).toBe("Smoke");
    expect((start?.body as Record<string, unknown>).enterPlayMode).toBe(false);

    const key = bridge!.requests.find((r) => r.url === "/playtest/send-key");
    expect((key?.body as Record<string, unknown>).key).toBe("Space");
    expect((key?.body as Record<string, unknown>).modifiers).toBe("Shift");

    const click = bridge!.requests.find((r) => r.url === "/playtest/send-click");
    expect((click?.body as Record<string, unknown>).x).toBe(0.5);
    expect((click?.body as Record<string, unknown>).y).toBe(0.75);
    expect(bridge!.requests.some((r) => r.url === "/playtest/status")).toBe(true);
  });

  it("reports invalid input as a validation error", async () => {
    const result = await client!.callTool({ name: "unity_create_game_object", arguments: {} });
    expect(result.isError).toBe(true);
    expect((result.content as Array<{ text: string }>)[0].text).toContain("validation");
  });
});

describe("MCP server error handling", () => {
  it("returns an isError result (not a thrown protocol error) when the bridge is down", async () => {
    // Point at a closed port; afterEach closes this client.
    client = await connectClient("http://127.0.0.1:1");
    const result = await client!.callTool({ name: "unity_selection", arguments: {} });
    expect(result.isError).toBe(true);
  });

  it("surfaces a 500 from the bridge as an isError result", async () => {
    await connect({
      "/health": okHealth,
      "/compilation-status": () => ({ status: 500, body: { status: "error", error: "compiling" } }),
    });
    const result = await client!.callTool({ name: "unity_get_compilation_status", arguments: {} });
    expect(result.isError).toBe(true);
    expect((result.content as Array<{ text: string }>)[0].text).toContain("compiling");
  });

  it("surfaces a logical error envelope as an isError result", async () => {
    await connect({
      "/health": okHealth,
      "/play-mode": () => ({ body: { status: "error", error: "busy", code: "editor.busy.compiling", retryable: true } }),
    });
    const result = await client!.callTool({ name: "unity_enter_play_mode", arguments: {} });
    expect(result.isError).toBe(true);
    expect(result.structuredContent).toMatchObject({
      status: "error",
      error: { code: "editor.busy.compiling", retryable: true },
    });
  });
});

describe("MCP server resources and prompts", () => {
  beforeEach(async () => {
    await connect({
      "/health": okHealth,
      "/capabilities": () => ({ body: { status: "ok", bridge: "sceneport", protocolVersion: 1, capabilitiesHash: "hash" } }),
      "/diagnostics": () => ({ body: { status: "ok", policy: { profile: "read-only" } } }),
      "/game-object": (r) => ({ body: { status: "ok", url: r.url } }),
      "/scene-query": (r) => ({ body: { status: "ok", url: r.url, body: r.body } }),
      "/component-query": (r) => ({ body: { status: "ok", url: r.url, body: r.body } }),
      "/serialized-read": (r) => ({ body: { status: "ok", url: r.url, body: r.body } }),
      "/console-events": (r) => ({ body: { status: "ok", url: r.url } }),
      "/asset-graph": (r) => ({ body: { status: "ok", url: r.url, body: r.body } }),
      "/scene-view": () => ({ body: { status: "ok", available: false } }),
      "/runtime-status": () => ({ body: { status: "ok", isPlaying: false } }),
      "/runtime-object": (r) => ({ body: { status: "ok", url: r.url } }),
      "/profiler-snapshot": () => ({ body: { status: "ok", frameCount: 1 } }),
      "/menu-item-allowlist": () => ({ body: { status: "ok", items: ["Assets/Refresh"] } }),
      "/playtest/status": () => ({ body: { status: "ok", session: { status: "idle" } } }),
      "/playtest/report": () => ({ body: { status: "ok", report: { summary: "idle" } } }),
      "/audit-log": () => ({ body: { status: "ok", entries: [] } }),
    });
  });

  it("lists staged-trust static resources and templates", async () => {
    const resources = await client!.listResources();
    expect(resources.resources.length).toBe(16);
    const templates = await client!.listResourceTemplates();
    expect(templates.resourceTemplates.length).toBe(8);
  });

  it("reads a static resource as JSON", async () => {
    const result = await client!.readResource({ uri: "sceneport://project/status" });
    expect(result.contents[0].mimeType).toBe("application/json");
    expect(() => JSON.parse((result.contents[0] as { text: string }).text)).not.toThrow();
  });

  it("reads bridge capabilities as JSON", async () => {
    const result = await client!.readResource({ uri: "sceneport://bridge/capabilities" });
    const payload = JSON.parse((result.contents[0] as { text: string }).text);
    expect(payload.capabilitiesHash).toBe("hash");
  });

  it("plumbs a resource template variable to the bridge", async () => {
    await client!.readResource({ uri: "sceneport://object/123" });
    const hit = bridge!.requests.find((r) => r.url.startsWith("/game-object"));
    expect(hit?.url).toContain("instanceId=123");
  });

  it("reads diagnostics as a redacted JSON resource", async () => {
    const result = await client!.readResource({ uri: "sceneport://diagnostics" });
    const payload = JSON.parse((result.contents[0] as { text: string }).text);
    expect(payload.policy.profile).toBe("read-only");
    expect(JSON.stringify(payload)).not.toContain("tok");
  });

  it("lists 9 prompts that render non-empty text", async () => {
    const { prompts } = await client!.listPrompts();
    expect(prompts.length).toBe(9);
    const rendered = await client!.getPrompt({ name: prompts[0].name });
    expect((rendered.messages[0].content as { text: string }).text.length).toBeGreaterThan(0);
  });
});

describe("MCP server identity", () => {
  it("reports the server version to the client", async () => {
    await connect({ "/health": okHealth });
    const version = client!.getServerVersion();
    expect(version?.version).toBe(VERSION);
  });
});
