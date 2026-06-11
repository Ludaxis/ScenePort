import { McpServer, ResourceTemplate } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { encodeSerializedValue, joinCsv, objectLocatorSchema, serializedValueSchema, vector3Schema } from "./encoding.js";
import { errorResult, jsonResult } from "./toolResult.js";
import type { UnityBridgeClient } from "./unityClient.js";
import { VERSION } from "./version.js";

export function createScenePortServer(client: UnityBridgeClient): McpServer {
  const server = new McpServer(
    {
      name: "sceneport",
      version: VERSION,
    },
    {
      instructions:
        "ScenePort exposes safe Unity Editor inspection, test, play-mode, asset, and reversible mutation tools. Read editor state before writing, prefer typed tools over scene YAML edits, and avoid arbitrary code execution.",
    },
  );

  function toolGet(path: string, params?: Record<string, string | number | boolean | undefined>) {
    return async () => {
      try {
        return jsonResult(await client.get(path, params));
      } catch (error) {
        return errorResult(error);
      }
    };
  }

  function toolPost(path: string, body: Record<string, unknown> = {}) {
    return async (args: Record<string, unknown> = {}) => {
      try {
        return jsonResult(await client.post(path, { ...body, ...args }));
      } catch (error) {
        return errorResult(error);
      }
    };
  }

  function jsonResource(uri: URL | string, payload: unknown) {
    const resourceUri = typeof uri === "string" ? uri : uri.href;
    return {
      contents: [
        {
          uri: resourceUri,
          mimeType: "application/json",
          text: JSON.stringify(payload, null, 2),
        },
      ],
    };
  }

  function sleep(milliseconds: number) {
    return new Promise((resolve) => setTimeout(resolve, milliseconds));
  }

  server.registerTool(
    "unity_status",
    {
      title: "Unity Bridge Status",
      description:
        "Check whether the ScenePort Unity Editor bridge is reachable, which Unity project it is bound to, and how it was discovered.",
      inputSchema: {},
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    async () => {
      try {
        return jsonResult(await client.statusReport());
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_scene_hierarchy",
    {
      title: "Unity Scene Hierarchy",
      description: "Read the active Unity scene hierarchy with optional pagination and depth limits.",
      inputSchema: {
        limit: z.number().int().min(1).max(1000).default(200).optional(),
        maxDepth: z.number().int().min(0).max(32).default(8).optional(),
      },
      annotations: { readOnlyHint: true },
    },
    async ({ limit, maxDepth }) => {
      try {
        return jsonResult(await client.get("/scene-hierarchy", { limit, maxDepth }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_selection",
    {
      title: "Unity Selection",
      description: "Read the currently selected GameObjects in the Unity Editor.",
      inputSchema: {},
      annotations: { readOnlyHint: true },
    },
    toolGet("/selection"),
  );

  server.registerTool(
    "unity_console_logs",
    {
      title: "Unity Console Logs",
      description: "Read recent Unity console messages captured by the ScenePort bridge.",
      inputSchema: {
        limit: z.number().int().min(1).max(500).default(100).optional(),
        type: z.enum(["all", "log", "warning", "error", "exception", "assert"]).default("all").optional(),
      },
      annotations: { readOnlyHint: true },
    },
    async ({ limit, type }) => {
      try {
        return jsonResult(await client.get("/console", { limit, type }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_get_game_object",
    {
      title: "Get Unity GameObject",
      description: "Read a detailed GameObject snapshot by instance ID or hierarchy path.",
      inputSchema: {
        ...objectLocatorSchema,
        includeComponents: z.boolean().default(true).optional(),
        propertyLimit: z.number().int().min(0).max(200).default(40).optional(),
      },
      annotations: { readOnlyHint: true },
    },
    async ({ instanceId, path, includeComponents, propertyLimit }) => {
      try {
        return jsonResult(await client.get("/game-object", { instanceId, path, includeComponents, propertyLimit }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_get_components",
    {
      title: "Get Unity Components",
      description: "Read Component summaries and serialized inspector properties for a GameObject.",
      inputSchema: {
        ...objectLocatorSchema,
        propertyLimit: z.number().int().min(0).max(300).default(80).optional(),
      },
      annotations: { readOnlyHint: true },
    },
    async ({ instanceId, path, propertyLimit }) => {
      try {
        return jsonResult(await client.get("/components", { instanceId, path, propertyLimit }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_create_game_object",
    {
      title: "Create Unity GameObject",
      description: "Create a GameObject in the active scene using Unity Undo.",
      inputSchema: {
        name: z.string().min(1).max(128).describe("Name for the new GameObject."),
        parentPath: z.string().min(1).max(512).optional().describe("Optional hierarchy path for the parent GameObject."),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    async ({ name, parentPath }) => {
      try {
        return jsonResult(await client.post("/create-game-object", { name, parentPath }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_set_transform",
    {
      title: "Set Unity Transform",
      description: "Set local transform values for a GameObject by Unity instance ID.",
      inputSchema: {
        instanceId: z.number().int().describe("Unity instance ID for the GameObject."),
        position: vector3Schema.optional().describe("Optional local position."),
        rotation: vector3Schema.optional().describe("Optional local Euler rotation."),
        scale: vector3Schema.optional().describe("Optional local scale."),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    },
    async ({ instanceId, position, rotation, scale }) => {
      try {
        return jsonResult(await client.post("/set-transform", { instanceId, position, rotation, scale }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_add_component",
    {
      title: "Add Unity Component",
      description: "Add a Component to a GameObject using Unity Undo.",
      inputSchema: {
        ...objectLocatorSchema,
        typeName: z.string().min(1).max(512).describe("Component type name, full type name, or assembly-qualified name."),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    async ({ instanceId, path, typeName }) => {
      try {
        return jsonResult(await client.post("/add-component", { instanceId, path, typeName }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_set_serialized_property",
    {
      title: "Set Serialized Property",
      description: "Set an inspector SerializedProperty on a GameObject or Component using Undo and prefab-aware Unity serialization.",
      inputSchema: {
        instanceId: z.number().int().describe("Unity instance ID for a GameObject or Component."),
        componentType: z.string().min(1).max(512).optional().describe("Optional component type when instanceId points at a GameObject."),
        componentIndex: z.number().int().min(0).optional().describe("Optional component index when instanceId points at a GameObject."),
        propertyPath: z.string().min(1).max(512).describe("SerializedProperty path such as m_Name or m_LocalPosition.x."),
        value: serializedValueSchema.describe("String, number, boolean, vector, or color value to write."),
        objectReferenceAssetPath: z.string().min(1).max(1024).optional().describe("Asset path for ObjectReference properties."),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    },
    async ({ instanceId, componentType, componentIndex, propertyPath, value, objectReferenceAssetPath }) => {
      try {
        return jsonResult(
          await client.post("/set-serialized-property", {
            instanceId,
            componentType,
            componentIndex,
            propertyPath,
            objectReferenceAssetPath,
            ...encodeSerializedValue(value),
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_asset_search",
    {
      title: "Unity Asset Search",
      description: "Search Unity assets through AssetDatabase.FindAssets.",
      inputSchema: {
        query: z.string().min(1).max(512).describe("Unity AssetDatabase search query, for example t:Prefab Player."),
        folders: z.array(z.string().min(1).max(512)).max(20).optional().describe("Optional Assets/... folders to search."),
        limit: z.number().int().min(1).max(500).default(100).optional(),
      },
      annotations: { readOnlyHint: true },
    },
    async ({ query, folders, limit }) => {
      try {
        return jsonResult(await client.get("/asset-search", { query, folders: joinCsv(folders), limit }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_get_compilation_status",
    {
      title: "Unity Compilation Status",
      description: "Read Unity script compilation, asset refresh, play-mode transition, and recent compiler error state.",
      inputSchema: {},
      annotations: { readOnlyHint: true },
    },
    toolGet("/compilation-status"),
  );

  function registerRunTestsTool(name: "unity_run_editmode_tests" | "unity_run_playmode_tests", mode: "editmode" | "playmode") {
    server.registerTool(
      name,
      {
        title: mode === "editmode" ? "Run EditMode Tests" : "Run PlayMode Tests",
        description:
          mode === "editmode"
            ? "Start a Unity EditMode test run and return its run ID plus last-known result state."
            : "Start a Unity PlayMode test run and return its run ID plus last-known result state.",
        inputSchema: {
          testNames: z.array(z.string().min(1).max(512)).max(50).optional(),
          groupNames: z.array(z.string().min(1).max(512)).max(50).optional(),
          categoryNames: z.array(z.string().min(1).max(256)).max(50).optional(),
          assemblyNames: z.array(z.string().min(1).max(256)).max(50).optional(),
          runSynchronously: z.boolean().default(false).optional().describe("Only supported for simple EditMode tests."),
        },
        annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
      },
      async ({ testNames, groupNames, categoryNames, assemblyNames, runSynchronously }) => {
        try {
          return jsonResult(
            await client.post("/run-tests", {
              mode,
              testNames: joinCsv(testNames),
              groupNames: joinCsv(groupNames),
              categoryNames: joinCsv(categoryNames),
              assemblyNames: joinCsv(assemblyNames),
              runSynchronously: mode === "editmode" ? runSynchronously : false,
            }),
          );
        } catch (error) {
          return errorResult(error);
        }
      },
    );
  }

  registerRunTestsTool("unity_run_editmode_tests", "editmode");
  registerRunTestsTool("unity_run_playmode_tests", "playmode");

  server.registerTool(
    "unity_capture_game_view",
    {
      title: "Capture Unity Game View",
      description: "Capture the Unity Game view to a PNG in the project's Temp/ScenePort folder.",
      inputSchema: {
        fileName: z.string().min(1).max(128).optional(),
        superSize: z.number().int().min(1).max(4).default(1).optional(),
      },
      // Writes a PNG file, so this is not read-only.
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    async ({ fileName, superSize }) => {
      try {
        return jsonResult(await client.post("/capture-game-view", { fileName, superSize }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_enter_play_mode",
    {
      title: "Enter Unity Play Mode",
      description: "Request Unity Editor play mode.",
      inputSchema: {},
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    },
    async () => {
      try {
        return jsonResult(await client.post("/play-mode", { action: "enter" }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_exit_play_mode",
    {
      title: "Exit Unity Play Mode",
      description: "Request Unity Editor to leave play mode.",
      inputSchema: {},
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    },
    async () => {
      try {
        return jsonResult(await client.post("/play-mode", { action: "exit" }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_start_playtest",
    {
      title: "Start Unity Playtest",
      description: "Start a ScenePort playtest session and optionally request Unity play mode plus an initial Game view capture.",
      inputSchema: {
        label: z.string().min(1).max(128).default("ScenePort Playtest").optional(),
        enterPlayMode: z.boolean().default(true).optional(),
        captureInitialFrame: z.boolean().default(false).optional(),
        superSize: z.number().int().min(1).max(4).default(1).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    async ({ label, enterPlayMode, captureInitialFrame, superSize }) => {
      try {
        return jsonResult(await client.post("/playtest/start", { label, enterPlayMode, captureInitialFrame, superSize }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_stop_playtest",
    {
      title: "Stop Unity Playtest",
      description: "Stop the current ScenePort playtest session, optionally exit play mode, and return the playtest report.",
      inputSchema: {
        exitPlayMode: z.boolean().default(true).optional(),
        captureFinalFrame: z.boolean().default(false).optional(),
        superSize: z.number().int().min(1).max(4).default(1).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    },
    async ({ exitPlayMode, captureFinalFrame, superSize }) => {
      try {
        return jsonResult(await client.post("/playtest/stop", { exitPlayMode, captureFinalFrame, superSize }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_playtest_status",
    {
      title: "Unity Playtest Status",
      description: "Read the active ScenePort playtest session status.",
      inputSchema: {},
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolGet("/playtest/status"),
  );

  server.registerTool(
    "unity_wait",
    {
      title: "Wait During Unity Playtest",
      description: "Wait without blocking the Unity Editor main thread, then optionally read playtest status.",
      inputSchema: {
        milliseconds: z.number().int().min(0).max(60000).default(1000).optional(),
        pollStatus: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    async ({ milliseconds, pollStatus }) => {
      try {
        const waitedMilliseconds = milliseconds ?? 1000;
        await sleep(waitedMilliseconds);
        const payload: Record<string, unknown> = {
          status: "ok",
          waitedMilliseconds,
        };
        if (pollStatus !== false) {
          payload.playtest = await client.get("/playtest/status");
        }
        return jsonResult(payload);
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_send_key",
    {
      title: "Send Key To Unity Game View",
      description: "Send a key event to the focused Unity Game view and record it in the active playtest session.",
      inputSchema: {
        key: z.string().min(1).max(64).describe("Unity KeyCode name, single letter/digit, or Space."),
        eventType: z.enum(["press", "down", "up"]).default("press").optional(),
        modifiers: z
          .array(z.enum(["Shift", "Control", "Alt", "Command", "Function", "CapsLock", "Numeric"]))
          .max(8)
          .optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    async ({ key, eventType, modifiers }) => {
      try {
        return jsonResult(await client.post("/playtest/send-key", { key, eventType, modifiers: joinCsv(modifiers) }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_send_click",
    {
      title: "Send Click To Unity Game View",
      description: "Send a mouse click event to the Unity Game view using normalized or pixel coordinates.",
      inputSchema: {
        x: z.number().describe("X coordinate, normalized 0-1 by default or pixels when coordinateSpace is pixels."),
        y: z.number().describe("Y coordinate, normalized 0-1 by default or pixels when coordinateSpace is pixels."),
        coordinateSpace: z.enum(["normalized", "pixels"]).default("normalized").optional(),
        button: z.number().int().min(0).max(2).default(0).optional(),
        eventType: z.enum(["press", "down", "up"]).default("press").optional(),
        modifiers: z
          .array(z.enum(["Shift", "Control", "Alt", "Command", "Function", "CapsLock", "Numeric"]))
          .max(8)
          .optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    async ({ x, y, coordinateSpace, button, eventType, modifiers }) => {
      try {
        return jsonResult(
          await client.post("/playtest/send-click", { x, y, coordinateSpace, button, eventType, modifiers: joinCsv(modifiers) }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_capture_playtest_frame",
    {
      title: "Capture Unity Playtest Frame",
      description: "Capture the Unity Game view and attach the image path to the active playtest session.",
      inputSchema: {
        fileName: z.string().min(1).max(128).optional(),
        superSize: z.number().int().min(1).max(4).default(1).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    async ({ fileName, superSize }) => {
      try {
        return jsonResult(await client.post("/playtest/capture-frame", { fileName, superSize }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_get_playtest_report",
    {
      title: "Get Unity Playtest Report",
      description: "Read the current ScenePort playtest report with actions, captures, console observations, and recommendations.",
      inputSchema: {},
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolGet("/playtest/report"),
  );

  server.registerTool(
    "unity_audit_log",
    {
      title: "Unity Audit Log",
      description: "Read recent ScenePort mutating requests recorded locally by the Unity bridge.",
      inputSchema: {
        limit: z.number().int().min(1).max(500).default(100).optional(),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    async ({ limit }) => {
      try {
        return jsonResult(await client.get("/audit-log", { limit }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_query_scene",
    {
      title: "Query Unity Scene",
      description: "Run a bounded rich query over the active Unity scene.",
      inputSchema: {
        limit: z.number().int().min(1).max(1000).default(200).optional(),
        cursor: z.number().int().min(0).optional(),
        maxDepth: z.number().int().min(0).max(64).default(16).optional(),
        nameContains: z.string().min(1).max(128).optional(),
        tag: z.string().min(1).max(64).optional(),
        componentType: z.string().min(1).max(512).optional(),
        includeComponents: z.boolean().default(false).optional(),
        includeTransform: z.boolean().default(true).optional(),
        propertyLimit: z.number().int().min(0).max(100).default(0).optional(),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolPost("/scene-query"),
  );

  server.registerTool(
    "unity_query_components",
    {
      title: "Query Unity Components",
      description: "Find components in the active scene by type and return bounded inspector snapshots.",
      inputSchema: {
        typeName: z.string().min(1).max(512).optional(),
        limit: z.number().int().min(1).max(1000).default(200).optional(),
        cursor: z.number().int().min(0).optional(),
        propertyLimit: z.number().int().min(0).max(100).default(20).optional(),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolPost("/component-query"),
  );

  server.registerTool(
    "unity_read_serialized_properties",
    {
      title: "Read Serialized Properties",
      description: "Read typed SerializedProperty values for a GameObject or Component without mutation.",
      inputSchema: {
        instanceId: z.number().int(),
        componentType: z.string().min(1).max(512).optional(),
        componentIndex: z.number().int().min(0).optional(),
        cursor: z.number().int().min(0).optional(),
        propertyLimit: z.number().int().min(1).max(500).default(100).optional(),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolPost("/serialized-read"),
  );

  server.registerTool(
    "unity_scene_view_state",
    {
      title: "Unity Scene View State",
      description: "Read the active Scene view camera state when an editor Scene view is available.",
      inputSchema: {},
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolGet("/scene-view"),
  );

  server.registerTool(
    "unity_capture_scene_view",
    {
      title: "Capture Unity Scene View",
      description: "Capture the active Unity Scene view camera to a PNG in Temp/ScenePort.",
      inputSchema: {
        fileName: z.string().min(1).max(128).optional(),
        width: z.number().int().min(64).max(4096).default(1024).optional(),
        height: z.number().int().min(64).max(4096).default(768).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/capture-scene-view"),
  );

  server.registerTool(
    "unity_runtime_status",
    {
      title: "Unity Runtime Status",
      description: "Read current play-mode/runtime status and frame counters.",
      inputSchema: {},
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolGet("/runtime-status"),
  );

  server.registerTool(
    "unity_query_runtime",
    {
      title: "Query Unity Runtime",
      description: "Run a bounded runtime object query using the same safe shape as scene query.",
      inputSchema: {
        limit: z.number().int().min(1).max(1000).default(200).optional(),
        cursor: z.number().int().min(0).optional(),
        maxDepth: z.number().int().min(0).max(64).default(16).optional(),
        nameContains: z.string().min(1).max(128).optional(),
        componentType: z.string().min(1).max(512).optional(),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolPost("/runtime-query"),
  );

  server.registerTool(
    "unity_get_runtime_object",
    {
      title: "Get Runtime Object",
      description: "Read a runtime GameObject snapshot by instance ID or path.",
      inputSchema: {
        ...objectLocatorSchema,
        includeComponents: z.boolean().default(true).optional(),
        propertyLimit: z.number().int().min(0).max(200).default(40).optional(),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    async ({ instanceId, path, includeComponents, propertyLimit }) => {
      try {
        return jsonResult(await client.get("/runtime-object", { instanceId, path, includeComponents, propertyLimit }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_console_stream",
    {
      title: "Unity Console Stream",
      description: "Read console events after a monotonic cursor.",
      inputSchema: {
        cursor: z.number().int().min(0).default(0).optional(),
        limit: z.number().int().min(1).max(500).default(100).optional(),
        type: z.enum(["all", "log", "warning", "error", "exception", "assert"]).default("all").optional(),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    async ({ cursor, limit, type }) => {
      try {
        return jsonResult(await client.get("/console-events", { cursor, limit, type }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_profiler_snapshot",
    {
      title: "Unity Profiler Snapshot",
      description: "Read lightweight Unity memory/frame counters.",
      inputSchema: {},
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolGet("/profiler-snapshot"),
  );

  server.registerTool(
    "unity_asset_graph",
    {
      title: "Unity Asset Graph",
      description: "Read dependencies and optional referencers for a Unity asset.",
      inputSchema: {
        path: z.string().min(1).max(1024).optional(),
        guid: z.string().min(1).max(128).optional(),
        includeReferencers: z.boolean().default(false).optional(),
        limit: z.number().int().min(1).max(500).default(100).optional(),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolPost("/asset-graph"),
  );

  server.registerTool(
    "unity_tests_run",
    {
      title: "Run Unity Tests",
      description: "Start a Unity EditMode or PlayMode test run.",
      inputSchema: {
        mode: z.enum(["editmode", "playmode"]).default("editmode").optional(),
        testNames: z.array(z.string().min(1).max(512)).max(50).optional(),
        groupNames: z.array(z.string().min(1).max(512)).max(50).optional(),
        categoryNames: z.array(z.string().min(1).max(256)).max(50).optional(),
        assemblyNames: z.array(z.string().min(1).max(256)).max(50).optional(),
        runSynchronously: z.boolean().default(false).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    async ({ mode, testNames, groupNames, categoryNames, assemblyNames, runSynchronously }) => {
      try {
        return jsonResult(
          await client.post("/tests/run", {
            mode,
            testNames: joinCsv(testNames),
            groupNames: joinCsv(groupNames),
            categoryNames: joinCsv(categoryNames),
            assemblyNames: joinCsv(assemblyNames),
            runSynchronously,
          }),
        );
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_tests_wait",
    {
      title: "Wait For Unity Tests",
      description: "Read the latest test run status through the proof-loop endpoint.",
      inputSchema: { mode: z.enum(["editmode", "playmode"]).default("editmode").optional() },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    async ({ mode }) => {
      try {
        return jsonResult(await client.get("/tests/wait", { mode }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_tests_artifacts",
    {
      title: "Unity Test Artifacts",
      description: "Write/read a machine-readable test artifact pack for the latest Unity test run.",
      inputSchema: { mode: z.enum(["editmode", "playmode"]).default("editmode").optional() },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    async ({ mode }) => {
      try {
        return jsonResult(await client.get("/tests/artifacts", { mode }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_assert_state",
    {
      title: "Assert Unity State",
      description: "Evaluate a batch of structured Unity assertions and write assertion evidence.",
      inputSchema: {
        checks: z.array(z.record(z.unknown())).max(100).default([]).optional(),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolPost("/assertions/evaluate"),
  );

  server.registerTool(
    "unity_capture_golden_frame",
    {
      title: "Capture Golden Frame",
      description: "Capture a Game view frame as proof evidence.",
      inputSchema: {
        baselineId: z.string().min(1).max(128).default("default").optional(),
        fileName: z.string().min(1).max(128).optional(),
        superSize: z.number().int().min(1).max(4).default(1).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/golden-frame/capture"),
  );

  server.registerTool(
    "unity_compare_golden_frame",
    {
      title: "Compare Golden Frame",
      description: "Compare two captured frame artifacts with deterministic metadata.",
      inputSchema: {
        baselinePath: z.string().min(1).max(2048),
        actualPath: z.string().min(1).max(2048),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolPost("/golden-frame/compare"),
  );

  server.registerTool(
    "unity_run_scenario",
    {
      title: "Run Unity Scenario",
      description: "Run a structured ScenePort scenario harness and write a report.",
      inputSchema: {
        name: z.string().min(1).max(128).optional(),
        steps: z.array(z.record(z.unknown())).max(100).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/scenario/run"),
  );

  server.registerTool(
    "unity_wait_for_scenario",
    {
      title: "Wait For Unity Scenario",
      description: "Read the current scenario status.",
      inputSchema: { scenarioRunId: z.string().min(1).max(128).optional() },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    async ({ scenarioRunId }) => {
      try {
        return jsonResult(await client.get("/scenario/wait", { scenarioRunId }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_get_scenario_report",
    {
      title: "Get Unity Scenario Report",
      description: "Read a scenario proof report.",
      inputSchema: { scenarioRunId: z.string().min(1).max(128).optional() },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    async ({ scenarioRunId }) => {
      try {
        return jsonResult(await client.get("/scenario/report", { scenarioRunId }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_perf_probe",
    {
      title: "Unity Perf Probe",
      description: "Capture lightweight Unity performance counters to a proof artifact.",
      inputSchema: {},
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolPost("/perf/probe"),
  );

  server.registerTool(
    "unity_check_perf_budgets",
    {
      title: "Check Unity Perf Budgets",
      description: "Check a single lightweight metric against a budget.",
      inputSchema: {
        metric: z.string().min(1).max(128),
        max: z.number().int(),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolPost("/perf/check-budget"),
  );

  server.registerTool(
    "unity_diagnostics",
    {
      title: "Unity ScenePort Diagnostics",
      description: "Read redacted bridge diagnostics, policy, capabilities, and recent audit entries.",
      inputSchema: {},
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolGet("/diagnostics"),
  );

  server.registerTool(
    "unity_validate_authoring_write",
    {
      title: "Validate Authoring Write",
      description: "Validate a supported authoring write without mutation.",
      inputSchema: {
        op: z.string().min(1).max(64).optional(),
        path: z.string().min(1).max(1024).optional(),
        dryRun: z.boolean().default(true).optional(),
        clientRequestId: z.string().min(1).max(128).optional(),
        reason: z.string().min(1).max(512).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    },
    toolPost("/authoring/validate", { dryRun: true }),
  );

  server.registerTool(
    "unity_authoring_batch",
    {
      title: "Unity Authoring Batch",
      description: "Run a dry-run or transactional batch of supported authoring operations.",
      inputSchema: {
        name: z.string().min(1).max(128).optional(),
        dryRun: z.boolean().default(true).optional(),
        transactional: z.boolean().default(true).optional(),
        operations: z
          .array(
            z.object({
              id: z.string().min(1).max(128).optional(),
              op: z.enum([
                "createGameObject",
                "setTransform",
                "addComponent",
                "setSerializedProperty",
                "createScript",
                "createMaterial",
                "createPrefab",
                "executeMenuItem",
              ]),
              args: z.record(z.unknown()).default({}).optional(),
            }),
          )
          .min(1)
          .max(25),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/authoring/batch"),
  );

  server.registerTool(
    "unity_create_script",
    {
      title: "Create Unity Script",
      description: "Create a template-only C# script under Assets/ with safe path validation.",
      inputSchema: {
        className: z.string().min(1).max(128),
        namespace: z.string().min(1).max(256).optional(),
        folder: z.string().min(1).max(1024).default("Assets").optional(),
        fileName: z.string().min(1).max(128).optional(),
        kind: z.enum(["MonoBehaviour", "ScriptableObject", "PlainClass"]).default("MonoBehaviour").optional(),
        dryRun: z.boolean().default(true).optional(),
        onConflict: z.enum(["error", "generateUniquePath"]).default("error").optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/create-script"),
  );

  server.registerTool(
    "unity_create_material",
    {
      title: "Create Unity Material",
      description: "Create a Material asset under Assets/ with validated shader/color fields.",
      inputSchema: {
        path: z.string().min(1).max(1024),
        shaderName: z.string().min(1).max(256).optional(),
        color: z.object({ r: z.number(), g: z.number(), b: z.number(), a: z.number().optional() }).optional(),
        dryRun: z.boolean().default(true).optional(),
        onConflict: z.enum(["error", "generateUniquePath"]).default("error").optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/create-material"),
  );

  server.registerTool(
    "unity_create_prefab",
    {
      title: "Create Unity Prefab",
      description: "Create a Prefab asset from a scene GameObject under Assets/.",
      inputSchema: {
        source: z.object({ instanceId: z.number().int().optional(), path: z.string().min(1).max(512).optional() }).optional(),
        instanceId: z.number().int().optional(),
        sourcePath: z.string().min(1).max(512).optional(),
        path: z.string().min(1).max(1024),
        connectToSource: z.boolean().default(true).optional(),
        dryRun: z.boolean().default(true).optional(),
        onConflict: z.enum(["error", "generateUniquePath"]).default("error").optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/create-prefab"),
  );

  server.registerTool(
    "unity_menu_item_allowlist",
    {
      title: "Unity Menu Item Allowlist",
      description: "Read the tiny exact-match menu item allowlist.",
      inputSchema: {},
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolGet("/menu-item-allowlist"),
  );

  server.registerTool(
    "unity_execute_menu_item",
    {
      title: "Execute Unity Menu Item",
      description: "Execute an exact allowlisted Unity menu item.",
      inputSchema: {
        path: z.string().min(1).max(256),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/execute-menu-item"),
  );

  server.registerResource(
    "sceneport-project-status",
    "sceneport://project/status",
    {
      title: "ScenePort Project Status",
      description: "Unity project, active scene, play mode, and bridge health.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/health")),
  );

  server.registerResource(
    "sceneport-bridge-capabilities",
    "sceneport://bridge/capabilities",
    {
      title: "ScenePort Bridge Capabilities",
      description: "Bridge protocol version, capability hash, and supported Unity endpoint groups.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.getCapabilities()),
  );

  server.registerResource(
    "sceneport-diagnostics",
    "sceneport://diagnostics",
    {
      title: "ScenePort Diagnostics",
      description: "Redacted bridge diagnostics, policy, capabilities, and recent audit entries.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/diagnostics")),
  );

  server.registerResource(
    "sceneport-active-scene",
    "sceneport://scene/active",
    {
      title: "ScenePort Active Scene",
      description: "Active Unity scene metadata.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/scene")),
  );

  server.registerResource(
    "sceneport-scene-hierarchy",
    "sceneport://scene/hierarchy",
    {
      title: "ScenePort Scene Hierarchy",
      description: "Active scene hierarchy with component summaries.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/scene-hierarchy", { limit: 500, maxDepth: 12 })),
  );

  server.registerResource(
    "sceneport-scene-query",
    new ResourceTemplate("sceneport://scene/query/{preset}", { list: undefined }),
    {
      title: "ScenePort Scene Query",
      description: "Rich active-scene query preset.",
      mimeType: "application/json",
    },
    async (uri, variables) => {
      const preset = String(variables.preset);
      const body =
        preset === "components"
          ? { includeComponents: true, propertyLimit: 20, limit: 200 }
          : preset === "all"
            ? { includeComponents: true, includeTransform: true, propertyLimit: 10, limit: 500 }
            : { limit: 200 };
      return jsonResource(uri, await client.post("/scene-query", body));
    },
  );

  server.registerResource(
    "sceneport-object",
    new ResourceTemplate("sceneport://object/{instanceId}", { list: undefined }),
    {
      title: "ScenePort Object",
      description: "Detailed GameObject snapshot by Unity instance ID.",
      mimeType: "application/json",
    },
    async (uri, variables) => jsonResource(uri, await client.get("/game-object", { instanceId: String(variables.instanceId) })),
  );

  server.registerResource(
    "sceneport-components-type",
    new ResourceTemplate("sceneport://components/type/{typeName}", { list: undefined }),
    {
      title: "ScenePort Components By Type",
      description: "Component query by type name.",
      mimeType: "application/json",
    },
    async (uri, variables) =>
      jsonResource(uri, await client.post("/component-query", { typeName: String(variables.typeName), limit: 200 })),
  );

  server.registerResource(
    "sceneport-serialized-object",
    new ResourceTemplate("sceneport://serialized/object/{instanceId}", { list: undefined }),
    {
      title: "ScenePort Serialized Object",
      description: "Typed serialized properties by instance ID.",
      mimeType: "application/json",
    },
    async (uri, variables) =>
      jsonResource(uri, await client.post("/serialized-read", { instanceId: Number(variables.instanceId), propertyLimit: 200 })),
  );

  server.registerResource(
    "sceneport-console-errors",
    "sceneport://console/errors",
    {
      title: "ScenePort Console Errors",
      description: "Recent Unity console errors and exceptions.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/console", { type: "error", limit: 200 })),
  );

  server.registerResource(
    "sceneport-console-events",
    new ResourceTemplate("sceneport://console/events/{cursor}", { list: undefined }),
    {
      title: "ScenePort Console Events",
      description: "Cursor-based Unity console events.",
      mimeType: "application/json",
    },
    async (uri, variables) => jsonResource(uri, await client.get("/console-events", { cursor: String(variables.cursor), limit: 200 })),
  );

  server.registerResource(
    "sceneport-assets-search",
    new ResourceTemplate("sceneport://assets/search/{query}", { list: undefined }),
    {
      title: "ScenePort Asset Search",
      description: "Unity AssetDatabase search results.",
      mimeType: "application/json",
    },
    async (uri, variables) => jsonResource(uri, await client.get("/asset-search", { query: String(variables.query), limit: 100 })),
  );

  server.registerResource(
    "sceneport-assets-graph",
    new ResourceTemplate("sceneport://assets/graph/{guid}", { list: undefined }),
    {
      title: "ScenePort Asset Graph",
      description: "Asset dependency graph by GUID.",
      mimeType: "application/json",
    },
    async (uri, variables) => jsonResource(uri, await client.post("/asset-graph", { guid: String(variables.guid), limit: 100 })),
  );

  server.registerResource(
    "sceneport-editmode-tests",
    "sceneport://tests/editmode",
    {
      title: "ScenePort EditMode Tests",
      description: "Last known Unity EditMode test run summary.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/tests-last", { mode: "editmode" })),
  );

  server.registerResource(
    "sceneport-playmode-tests",
    "sceneport://tests/playmode",
    {
      title: "ScenePort PlayMode Tests",
      description: "Last known Unity PlayMode test run summary.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/tests-last", { mode: "playmode" })),
  );

  server.registerResource(
    "sceneport-packages",
    "sceneport://packages",
    {
      title: "ScenePort Packages",
      description: "Unity package manifest and dependency summary.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/packages")),
  );

  server.registerResource(
    "sceneport-playtest-status",
    "sceneport://playtest/status",
    {
      title: "ScenePort Playtest Status",
      description: "Current playtest session status.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/playtest/status")),
  );

  server.registerResource(
    "sceneport-playtest-report",
    "sceneport://playtest/report",
    {
      title: "ScenePort Playtest Report",
      description: "Current playtest actions, captures, console observations, and recommendations.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/playtest/report")),
  );

  server.registerResource(
    "sceneport-scene-view-state",
    "sceneport://scene-view/state",
    {
      title: "ScenePort Scene View State",
      description: "Active Unity Scene view camera state.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/scene-view")),
  );

  server.registerResource(
    "sceneport-runtime-status",
    "sceneport://runtime/status",
    {
      title: "ScenePort Runtime Status",
      description: "Unity runtime/play-mode status.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/runtime-status")),
  );

  server.registerResource(
    "sceneport-runtime-object",
    new ResourceTemplate("sceneport://runtime/object/{instanceId}", { list: undefined }),
    {
      title: "ScenePort Runtime Object",
      description: "Runtime GameObject snapshot by instance ID.",
      mimeType: "application/json",
    },
    async (uri, variables) => jsonResource(uri, await client.get("/runtime-object", { instanceId: String(variables.instanceId) })),
  );

  server.registerResource(
    "sceneport-profiler-snapshot",
    "sceneport://profiler/snapshot",
    {
      title: "ScenePort Profiler Snapshot",
      description: "Lightweight Unity profiler counters.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/profiler-snapshot")),
  );

  server.registerResource(
    "sceneport-audit-log",
    "sceneport://audit/log",
    {
      title: "ScenePort Audit Log",
      description: "Recent local ScenePort mutating requests and results.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/audit-log", { limit: 200 })),
  );

  server.registerResource(
    "sceneport-authoring-menu-items",
    "sceneport://authoring/menu-items",
    {
      title: "ScenePort Authoring Menu Items",
      description: "Exact-match Unity menu item allowlist.",
      mimeType: "application/json",
    },
    async (uri) => jsonResource(uri, await client.get("/menu-item-allowlist")),
  );

  function registerPrompt(name: string, title: string, description: string, text: string) {
    server.registerPrompt(
      name,
      {
        title,
        description,
      },
      async () => ({
        messages: [
          {
            role: "user",
            content: {
              type: "text",
              text,
            },
          },
        ],
      }),
    );
  }

  registerPrompt(
    "sceneport:fix-console-errors",
    "Fix Unity Console Errors",
    "Inspect Unity console errors and propose or implement focused fixes.",
    "Use ScenePort to check Unity status, read recent console errors and exceptions, inspect the related scene/assets if needed, then make the smallest safe code or scene changes. Re-check compilation status and console logs after the fix.",
  );

  registerPrompt(
    "sceneport:inspect-scene",
    "Inspect Active Scene",
    "Summarize the active Unity scene structure, selected objects, and risks.",
    "Use ScenePort to read Unity status, active scene metadata, hierarchy, selection, and recent warnings/errors. Summarize the gameplay structure, likely responsibilities of major objects, and any anomalies worth fixing.",
  );

  registerPrompt(
    "sceneport:create-prefab",
    "Create Prefab Workflow",
    "Guide a safe prefab creation or refinement workflow.",
    "Use ScenePort to inspect the target GameObject, components, serialized properties, and relevant assets. Make reversible changes through ScenePort tools when possible, then tell me what prefab asset should be created or updated in Unity.",
  );

  registerPrompt(
    "sceneport:create-ui-from-screenshot",
    "Create UI From Screenshot",
    "Analyze a screenshot and map it to Unity UI objects.",
    "Capture or inspect the Unity Game view, compare it with the supplied screenshot or design intent, then identify the Canvas objects, assets, layout, and serialized property edits needed to reproduce the UI safely.",
  );

  registerPrompt(
    "sceneport:write-playmode-test",
    "Write PlayMode Test",
    "Create or improve PlayMode test coverage for a Unity flow.",
    "Use ScenePort to inspect scene hierarchy, play-mode readiness, compilation status, packages, and existing console issues. Then add a focused PlayMode test and use ScenePort to start the PlayMode test run.",
  );

  registerPrompt(
    "sceneport:debug-play-mode",
    "Debug Play Mode",
    "Enter play mode, observe status, and debug Unity runtime issues.",
    "Use ScenePort to check compilation status and console logs, enter play mode, capture the Game view if useful, inspect logs and scene state, then exit play mode before applying focused fixes.",
  );

  registerPrompt(
    "sceneport:playtest-pilot",
    "Run Playtest Pilot",
    "Run a safe Unity playtest loop with observations and a report.",
    "Use ScenePort to check compilation status, start a playtest, wait for Unity to enter play mode, capture the Game view, send minimal key or click interactions when appropriate, watch console logs, stop the playtest, and return the playtest report with blockers and next fixes.",
  );

  registerPrompt(
    "sceneport:prepare-build",
    "Prepare Unity Build",
    "Run pre-build checks and identify blockers.",
    "Use ScenePort to inspect Unity status, package dependencies, compilation status, console errors, active scene state, and relevant tests. Return a concise build-readiness checklist with blockers and recommended fixes.",
  );

  registerPrompt(
    "sceneport:team-readiness-smoke",
    "Team Readiness Smoke",
    "Run the v0.5 readiness loop for a Unity project.",
    "Use ScenePort to run a team-readiness smoke: call unity_status, inspect scene hierarchy and selection, read console errors, run relevant EditMode tests, start a short playtest with one capture when safe, read the audit log, and return blockers plus exact follow-up tasks.",
  );

  return server;
}
