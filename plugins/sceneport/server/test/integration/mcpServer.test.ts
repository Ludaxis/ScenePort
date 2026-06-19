import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { InMemoryTransport } from "@modelcontextprotocol/sdk/inMemory.js";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { createScenePortServer } from "../../src/server.js";
import { UnityBridgeClient } from "../../src/unityClient.js";
import { VERSION } from "../../src/version.js";
import { FakeBridge, type RouteHandler } from "../fixtures/fakeBridge.js";

const okHealth: RouteHandler = () => ({ body: { status: "ok", bridge: "sceneport", port: 38987, projectId: "abc" } });

// 1x1 transparent PNG used as the inline-capture fixture.
const TINY_PNG = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M8AAAMBAQDJ/pLvAAAAAElFTkSuQmCC";

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
  "unity_get_compile_errors",
  "unity_wait_for_idle",
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
  "unity_create_folder",
  "unity_create_text_asset",
  "unity_create_shader",
  "unity_create_primitive_mesh",
  "unity_create_procedural_mesh",
  "unity_assign_mesh",
  "unity_get_settings",
  "unity_set_setting",
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
      "/create-folder": (r) => ({ body: { status: "ok", body: r.body } }),
      "/create-text-asset": (r) => ({ body: { status: "ok", body: r.body } }),
      "/create-shader": (r) => ({ body: { status: "ok", body: r.body } }),
      "/mesh/create-primitive": (r) => ({ body: { status: "ok", body: r.body } }),
      "/mesh/create-procedural": (r) => ({ body: { status: "ok", body: r.body } }),
      "/mesh/assign": (r) => ({ body: { status: "ok", body: r.body } }),
      "/settings/get": () => ({ body: { status: "ok", settings: [{ key: "quality.level", type: "int", value: 2 }] } }),
      "/settings/set": (r) => ({ body: { status: "ok", body: r.body } }),
      "/capture-game-view": (r) => ({
        body: { status: "ok", path: "/tmp/x.png", imageBase64: TINY_PNG, width: 16, height: 16, body: r.body },
      }),
      "/golden-frame/compare": (r) => ({
        body: {
          status: "ok",
          passed: false,
          pixelDiffPercent: 12.3,
          imageBase64: TINY_PNG,
          width: 16,
          height: 16,
          changedPixels: 5,
          totalPixels: 256,
          body: r.body,
        },
      }),
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

  it("returns an inline image content block from unity_capture_game_view", async () => {
    const result = await client!.callTool({ name: "unity_capture_game_view", arguments: {} });
    const content = result.content as Array<{ type: string; data?: string; mimeType?: string; text?: string }>;
    const image = content.find((item) => item.type === "image");
    expect(image).toBeDefined();
    expect(image?.mimeType).toBe("image/png");
    expect(image?.data).toBe(TINY_PNG);
    // The large base64 blob must not be duplicated into the text metadata.
    const text = content.find((item) => item.type === "text");
    expect(text?.text).not.toContain(TINY_PNG);
    expect(text?.text).toContain("/tmp/x.png");
  });

  it("forwards inline and maxEdge capture params to the bridge", async () => {
    await client!.callTool({ name: "unity_capture_game_view", arguments: { inline: true, maxEdge: 512 } });
    const hit = bridge!.requests.find((r) => r.url === "/capture-game-view");
    expect((hit?.body as Record<string, unknown>).inline).toBe(true);
    expect((hit?.body as Record<string, unknown>).maxEdge).toBe(512);
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

  it("plumbs Phase 1 authoring tools to their endpoints", async () => {
    await client!.callTool({ name: "unity_create_folder", arguments: { path: "Assets/Generated", dryRun: true } });
    await client!.callTool({ name: "unity_create_text_asset", arguments: { path: "Assets/notes.txt", content: "hi", dryRun: true } });
    await client!.callTool({ name: "unity_create_shader", arguments: { path: "Assets/Gen.shader", template: "urpUnlit", dryRun: true } });
    await client!.callTool({ name: "unity_create_primitive_mesh", arguments: { path: "Assets/Cube.asset", shape: "box", dryRun: true } });
    await client!.callTool({
      name: "unity_create_procedural_mesh",
      arguments: {
        path: "Assets/Tri.asset",
        vertices: [
          { x: 0, y: 0, z: 0 },
          { x: 1, y: 0, z: 0 },
          { x: 0, y: 1, z: 0 },
        ],
        triangles: [0, 1, 2],
        dryRun: true,
      },
    });
    await client!.callTool({ name: "unity_assign_mesh", arguments: { instanceId: 7, meshPath: "Assets/Cube.asset", dryRun: true } });
    await client!.callTool({ name: "unity_set_setting", arguments: { key: "quality.level", value: 3, dryRun: true } });
    await client!.callTool({ name: "unity_get_settings", arguments: {} });

    expect(
      bridge!.requests.some((r) => r.url === "/create-folder" && (r.body as Record<string, unknown>).path === "Assets/Generated"),
    ).toBe(true);
    expect(bridge!.requests.some((r) => r.url === "/create-text-asset" && (r.body as Record<string, unknown>).content === "hi")).toBe(true);
    expect(bridge!.requests.some((r) => r.url === "/create-shader" && (r.body as Record<string, unknown>).template === "urpUnlit")).toBe(
      true,
    );
    expect(bridge!.requests.some((r) => r.url === "/mesh/create-primitive" && (r.body as Record<string, unknown>).shape === "box")).toBe(
      true,
    );
    expect(
      bridge!.requests.some((r) => r.url === "/mesh/create-procedural" && Array.isArray((r.body as Record<string, unknown>).triangles)),
    ).toBe(true);
    expect(
      bridge!.requests.some((r) => r.url === "/mesh/assign" && (r.body as Record<string, unknown>).meshPath === "Assets/Cube.asset"),
    ).toBe(true);
    expect(bridge!.requests.some((r) => r.url === "/settings/set" && (r.body as Record<string, unknown>).key === "quality.level")).toBe(
      true,
    );
    expect(bridge!.requests.some((r) => r.url === "/settings/get")).toBe(true);
  });

  it("marks settings read as read-only and mesh creation as a mutation", async () => {
    const { tools } = await client!.listTools();
    const byName = Object.fromEntries(tools.map((t) => [t.name, t]));
    expect(byName.unity_get_settings.annotations?.readOnlyHint).toBe(true);
    expect(byName.unity_create_primitive_mesh.annotations?.readOnlyHint).toBe(false);
    expect(byName.unity_set_setting.annotations?.readOnlyHint).toBe(false);
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

  it("returns an inline diff image from unity_compare_golden_frame", async () => {
    const result = await client!.callTool({
      name: "unity_compare_golden_frame",
      arguments: { baselinePath: "/tmp/base.png", actualPath: "/tmp/actual.png" },
    });
    const content = result.content as Array<{ type: string; data?: string; mimeType?: string; text?: string }>;
    const image = content.find((item) => item.type === "image");
    expect(image).toBeDefined();
    expect(image?.mimeType).toBe("image/png");
    expect(image?.data).toBe(TINY_PNG);
    const text = content.find((item) => item.type === "text");
    expect(text?.text).not.toContain(TINY_PNG);
    expect(text?.text).toContain("12.3");
  });

  it("forwards threshold and maxEdge to the golden-frame compare bridge", async () => {
    await client!.callTool({
      name: "unity_compare_golden_frame",
      arguments: { baselinePath: "/tmp/base.png", actualPath: "/tmp/actual.png", threshold: 0.05, maxEdge: 512 },
    });
    const hit = bridge!.requests.find((r) => r.url === "/golden-frame/compare");
    expect((hit?.body as Record<string, unknown>).threshold).toBe(0.05);
    expect((hit?.body as Record<string, unknown>).maxEdge).toBe(512);
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

describe("MCP server unity_wait_for_idle", () => {
  it("polls compilation status until idle and surfaces compiler errors", async () => {
    let calls = 0;
    await connect({
      "/health": okHealth,
      "/compilation-status": () => {
        calls += 1;
        if (calls < 2) {
          return { body: { status: "ok", isCompiling: true, isUpdating: false, reloadEpoch: 1, compilerMessages: [] } };
        }
        return {
          body: {
            status: "ok",
            isCompiling: false,
            isUpdating: false,
            reloadEpoch: 2,
            compilerMessages: [{ file: "Assets/A.cs", line: 3, column: 5, type: "error", message: "CS0103", assembly: "Assembly-CSharp" }],
          },
        };
      },
    });

    const result = await client!.callTool({
      name: "unity_wait_for_idle",
      arguments: { pollIntervalMs: 100, timeoutMs: 5000 },
    });
    const payload = JSON.parse((result.content as Array<{ text: string }>)[0].text);
    expect(payload.idle).toBe(true);
    expect(payload.isCompiling).toBe(false);
    expect(payload.isUpdating).toBe(false);
    expect(payload.reloadEpoch).toBe(2);
    expect(payload.compilerErrors).toBe(1);
    expect(payload.compilerMessages[0].message).toBe("CS0103");
    expect(calls).toBeGreaterThanOrEqual(2);
  });

  it("tolerates the bridge disappearing mid-reload and still resolves to idle", async () => {
    // Simulate a domain reload: the bridge is up, then unreachable for a window
    // (Unity restarting on a new port), then up again and idle. wait_for_idle must
    // ride through the unreachable window via its transient-error catch.
    let calls = 0;
    const routes: Record<string, RouteHandler> = {
      "/health": okHealth,
      "/compilation-status": () => {
        calls += 1;
        return { body: { status: "ok", isCompiling: false, isUpdating: false, reloadEpoch: 9, compilerMessages: [] } };
      },
    };
    bridge = new FakeBridge(routes);
    await bridge.start();
    // Stop the bridge so the very first poll hits a connection-refused (bridge.unreachable).
    const baseUrl = bridge.url;
    const port = bridge.port;
    await bridge.stop();
    client = await connectClient(baseUrl);

    // Bring the bridge back on the SAME port after a short delay, mid-poll.
    const restart = (async () => {
      await new Promise((resolve) => setTimeout(resolve, 250));
      bridge = new FakeBridge(routes);
      await bridge.startOnPort(port);
    })();

    const result = await client!.callTool({
      name: "unity_wait_for_idle",
      arguments: { pollIntervalMs: 100, timeoutMs: 5000 },
    });
    await restart;
    const payload = JSON.parse((result.content as Array<{ text: string }>)[0].text);
    expect(payload.idle).toBe(true);
    expect(payload.reloadEpoch).toBe(9);
    expect(calls).toBeGreaterThanOrEqual(1);
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

  it("lists 12 prompts that render non-empty text", async () => {
    const { prompts } = await client!.listPrompts();
    expect(prompts.length).toBe(12);
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
