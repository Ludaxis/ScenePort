import { McpServer, ResourceTemplate } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { ScenePortBridgeError } from "./bridgeError.js";
import { encodeSerializedValue, joinCsv, objectLocatorSchema, serializedValueSchema, vector3Schema } from "./encoding.js";
import { errorResult, imageResult, jsonResult } from "./toolResult.js";
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

  // Shared inline-capture inputs: ask the bridge to return a base64 PNG so vision models can see it.
  const inlineCaptureSchema = {
    inline: z.boolean().default(true).optional().describe("Return the captured image inline so the model can see it."),
    maxEdge: z
      .number()
      .int()
      .min(64)
      .max(4096)
      .default(1024)
      .optional()
      .describe("Downscale the captured image's longest edge to this many pixels to keep the payload small."),
  };

  // Capture responses optionally carry a base64 PNG; return it as an MCP image block when present.
  function captureResult(response: unknown) {
    const imageBase64 = response !== null && typeof response === "object" ? (response as { imageBase64?: unknown }).imageBase64 : undefined;
    if (typeof imageBase64 === "string" && imageBase64.length > 0) {
      return imageResult(response, imageBase64);
    }
    return jsonResult(response);
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
        value: serializedValueSchema
          .optional()
          .describe(
            "String, number, boolean, vector, or color value to write. Omit when wiring an ObjectReference by asset path or instance id.",
          ),
        objectReferenceAssetPath: z.string().min(1).max(1024).optional().describe("Asset path for ObjectReference properties."),
        objectReferenceInstanceId: z
          .number()
          .int()
          .optional()
          .describe("Instance ID of a live scene object/component to wire into an ObjectReference property when no asset path is given."),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    },
    async ({ instanceId, componentType, componentIndex, propertyPath, value, objectReferenceAssetPath, objectReferenceInstanceId }) => {
      try {
        return jsonResult(
          await client.post("/set-serialized-property", {
            instanceId,
            componentType,
            componentIndex,
            propertyPath,
            objectReferenceAssetPath,
            objectReferenceInstanceId,
            ...(value === undefined ? {} : encodeSerializedValue(value)),
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
      description:
        "Read Unity script compilation, asset refresh, play-mode transition, and recent compiler state. Returns structured compiler messages (compilerMessages[] with file/line/column/type/message/assembly), isCompiling, isUpdating, and reloadEpoch alongside the legacy fields.",
      inputSchema: {},
      annotations: { readOnlyHint: true },
    },
    toolGet("/compilation-status"),
  );

  function compilerMessages(payload: unknown): Array<Record<string, unknown>> {
    if (payload === null || typeof payload !== "object") {
      return [];
    }
    const raw = (payload as { compilerMessages?: unknown }).compilerMessages;
    return Array.isArray(raw) ? (raw.filter((m) => m !== null && typeof m === "object") as Array<Record<string, unknown>>) : [];
  }

  function countByType(messages: Array<Record<string, unknown>>, type: string): number {
    return messages.filter((m) => m.type === type).length;
  }

  server.registerTool(
    "unity_get_compile_errors",
    {
      title: "Unity Compile Errors",
      description: "Read only the error-level Unity compiler messages from the current compilation status.",
      inputSchema: {},
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    async () => {
      try {
        const status = await client.get("/compilation-status");
        const messages = compilerMessages(status);
        const errors = messages.filter((m) => m.type === "error");
        return jsonResult({
          status: "ok",
          isCompiling: (status as { isCompiling?: unknown }).isCompiling === true,
          compilerErrors: errors.length,
          compilerMessages: errors,
        });
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_wait_for_idle",
    {
      title: "Wait For Unity Idle",
      description:
        "Poll the Unity bridge until script compilation and asset refresh finish, tolerating the bridge briefly disappearing during a domain reload, then return the final compiler messages. Use this after a code change before relying on the editor.",
      inputSchema: {
        timeoutMs: z.number().int().min(500).max(120000).default(60000).optional(),
        pollIntervalMs: z.number().int().min(100).max(10000).default(500).optional(),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    async ({ timeoutMs, pollIntervalMs }) => {
      const overallTimeoutMs = timeoutMs ?? 60000;
      const intervalMs = pollIntervalMs ?? 500;
      const deadline = Date.now() + overallTimeoutMs;
      try {
        while (true) {
          let status: unknown;
          try {
            status = await client.get("/compilation-status");
          } catch (error) {
            // While Unity reloads the domain the bridge stops answering. A transient
            // network error (timeout/unreachable) means "still busy", not failure:
            // keep polling until the overall deadline elapses.
            const code = error instanceof ScenePortBridgeError ? error.code : undefined;
            const transient = code === "bridge.timeout" || code === "bridge.unreachable";
            if (!transient || Date.now() >= deadline) {
              if (transient) {
                return jsonResult({ idle: false, timedOut: true });
              }
              throw error;
            }
            await sleep(intervalMs);
            continue;
          }

          const record = (status !== null && typeof status === "object" ? status : {}) as Record<string, unknown>;
          const isCompiling = record.isCompiling === true;
          const isUpdating = record.isUpdating === true;
          if (!isCompiling && !isUpdating) {
            const messages = compilerMessages(status);
            return jsonResult({
              idle: true,
              reloadEpoch: typeof record.reloadEpoch === "number" ? record.reloadEpoch : undefined,
              isCompiling: false,
              isUpdating: false,
              compilerErrors: countByType(messages, "error"),
              compilerWarnings: countByType(messages, "warning"),
              compilerMessages: messages,
            });
          }

          if (Date.now() >= deadline) {
            return jsonResult({ idle: false, timedOut: true });
          }
          await sleep(intervalMs);
        }
      } catch (error) {
        return errorResult(error);
      }
    },
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
        ...inlineCaptureSchema,
      },
      // Writes a PNG file, so this is not read-only.
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    async ({ fileName, superSize, inline, maxEdge }) => {
      try {
        return captureResult(await client.post("/capture-game-view", { fileName, superSize, inline, maxEdge }));
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
        ...inlineCaptureSchema,
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    async ({ fileName, superSize, inline, maxEdge }) => {
      try {
        return captureResult(await client.post("/playtest/capture-frame", { fileName, superSize, inline, maxEdge }));
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
        ...inlineCaptureSchema,
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    async ({ fileName, width, height, inline, maxEdge }) => {
      try {
        return captureResult(await client.post("/capture-scene-view", { fileName, width, height, inline, maxEdge }));
      } catch (error) {
        return errorResult(error);
      }
    },
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
        ...inlineCaptureSchema,
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    async ({ baselineId, fileName, superSize, inline, maxEdge }) => {
      try {
        return captureResult(await client.post("/golden-frame/capture", { baselineId, fileName, superSize, inline, maxEdge }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_compare_golden_frame",
    {
      title: "Compare Golden Frame",
      description: "Compare two captured frame artifacts with deterministic metadata.",
      inputSchema: {
        baselinePath: z.string().min(1).max(2048),
        actualPath: z.string().min(1).max(2048),
        threshold: z.number().min(0).max(1).default(0.02).optional(),
        passThreshold: z.number().min(0).max(100).default(0).optional(),
        maxEdge: z.number().int().min(64).max(4096).default(1024).optional(),
      },
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    async ({ baselinePath, actualPath, threshold, passThreshold, maxEdge }) => {
      try {
        return captureResult(await client.post("/golden-frame/compare", { baselinePath, actualPath, threshold, passThreshold, maxEdge }));
      } catch (error) {
        return errorResult(error);
      }
    },
  );

  server.registerTool(
    "unity_run_scenario",
    {
      title: "Run Unity Scenario",
      description:
        "PREVIEW (partially implemented in v1.0): Run a structured ScenePort scenario harness and write a report. The scenario harness is incomplete and must NOT be relied on as a pass/fail gate.",
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
      description:
        "PREVIEW (partially implemented in v1.0): Read the current scenario status. Part of the incomplete scenario harness and must NOT be relied on as a pass/fail gate.",
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
      description:
        "PREVIEW (partially implemented in v1.0): Read a scenario proof report. Part of the incomplete scenario harness and must NOT be relied on as a pass/fail gate.",
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
      description:
        "PREVIEW (partially implemented in v1.0): Check a single lightweight metric against a budget. The perf-budget checks are not fully implemented and must NOT be relied on as a pass/fail gate.",
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
    "unity_create_folder",
    {
      title: "Create Unity Folder",
      description: "Create a folder (recursively) under Assets/ with safe path validation.",
      inputSchema: {
        path: z.string().min(1).max(1024).describe("Folder path under Assets/, e.g. Assets/Art/Meshes."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    },
    toolPost("/create-folder"),
  );

  server.registerTool(
    "unity_create_text_asset",
    {
      title: "Create Unity Text Asset",
      description:
        "Create an inert text/config/source asset under Assets/ (extension-allowlisted: .txt/.json/.md/.cs/.shader/.hlsl/.asmdef/.uss/.uxml and similar).",
      inputSchema: {
        path: z.string().min(1).max(1024),
        content: z.string().max(1_000_000).default("").optional(),
        dryRun: z.boolean().default(true).optional(),
        onConflict: z.enum(["error", "generateUniquePath"]).default("error").optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/create-text-asset"),
  );

  server.registerTool(
    "unity_create_shader",
    {
      title: "Create Unity Shader",
      description:
        "Create a .shader (ShaderLab) asset under Assets/. Provide `content` verbatim, or omit it to scaffold from a `template` (urpUnlit|unlit). Verify it compiled with unity_wait_for_idle + unity_get_compile_errors.",
      inputSchema: {
        path: z.string().min(1).max(1024).describe("Shader path under Assets/, must end with .shader."),
        content: z.string().max(1_000_000).optional().describe("Full ShaderLab source. If omitted, a template is generated."),
        template: z.enum(["urpUnlit", "unlit"]).default("urpUnlit").optional(),
        shaderName: z
          .string()
          .min(1)
          .max(256)
          .default("ScenePort/Generated")
          .optional()
          .describe('The Shader "..." name used by the template.'),
        dryRun: z.boolean().default(true).optional(),
        onConflict: z.enum(["error", "generateUniquePath"]).default("error").optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/create-shader"),
  );

  server.registerTool(
    "unity_sg_create_graph",
    {
      title: "Create Unity ShaderGraph (preview)",
      description:
        "PREVIEW (scaffold): create a .shadergraph asset under Assets/ from verbatim JSON `content`, or omit it to write a minimal Unlit template. The .shadergraph is authored as JSON text (no com.unity.shadergraph dependency) and round-trip validated after import; if it cannot be loaded back the write is rolled back and reported as an unsupported capability. Off by default except under the full-safe-local policy.",
      inputSchema: {
        path: z.string().min(1).max(1024).describe("ShaderGraph asset path under Assets/, must end with .shadergraph."),
        content: z.string().max(1_000_000).optional().describe("Full .shadergraph JSON. If omitted, a minimal Unlit template is written."),
        dryRun: z.boolean().default(true).optional(),
        onConflict: z.enum(["error", "generateUniquePath"]).default("error").optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/shadergraph/create"),
  );

  server.registerTool(
    "unity_create_primitive_mesh",
    {
      title: "Create Unity Primitive Mesh",
      description: "Create a Mesh .asset from a built-in primitive (box, sphere, cylinder, capsule, plane, quad), optionally scaled.",
      inputSchema: {
        path: z.string().min(1).max(1024).describe("Mesh asset path under Assets/, must end with .asset."),
        shape: z.enum(["box", "sphere", "cylinder", "capsule", "plane", "quad"]).default("box").optional(),
        size: vector3Schema.optional().describe("Per-axis scale applied to the primitive's vertices (default 1,1,1)."),
        dryRun: z.boolean().default(true).optional(),
        onConflict: z.enum(["error", "generateUniquePath"]).default("error").optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/mesh/create-primitive"),
  );

  server.registerTool(
    "unity_create_procedural_mesh",
    {
      title: "Create Unity Procedural Mesh",
      description:
        "Create a Mesh .asset from explicit vertices and triangle indices, with optional normals and UVs. Indices are range-validated; normals are recalculated when omitted.",
      inputSchema: {
        path: z.string().min(1).max(1024).describe("Mesh asset path under Assets/, must end with .asset."),
        vertices: z.array(vector3Schema).min(3).max(200_000).describe("Vertex positions."),
        triangles: z.array(z.number().int().min(0)).min(3).max(600_000).describe("Triangle indices (length must be a multiple of 3)."),
        normals: z.array(vector3Schema).max(200_000).optional().describe("Optional per-vertex normals; length must equal vertices."),
        uv: z
          .array(z.object({ x: z.number(), y: z.number() }))
          .max(200_000)
          .optional()
          .describe("Optional per-vertex UVs; length must equal vertices."),
        dryRun: z.boolean().default(true).optional(),
        onConflict: z.enum(["error", "generateUniquePath"]).default("error").optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/mesh/create-procedural"),
  );

  server.registerTool(
    "unity_assign_mesh",
    {
      title: "Assign Mesh To GameObject",
      description:
        "Assign a Mesh asset to a scene GameObject's MeshFilter (adding MeshFilter/MeshRenderer if missing), optionally with a material. Undo-wrapped.",
      inputSchema: {
        instanceId: z.number().int().optional().describe("Target GameObject instance ID."),
        path: z.string().min(1).max(512).optional().describe("Target GameObject hierarchy path (alternative to instanceId)."),
        meshPath: z.string().min(1).max(1024).describe("Mesh asset path under Assets/ ending with .asset."),
        materialPath: z.string().min(1).max(1024).optional().describe("Optional material asset path under Assets/ ending with .mat."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    },
    toolPost("/mesh/assign"),
  );

  server.registerTool(
    "unity_create_animation_clip",
    {
      title: "Create Unity Animation Clip",
      description:
        "Create an AnimationClip .asset, optionally with float curves. Each curve targets a child path + component type + property and is built from time/value keyframes.",
      inputSchema: {
        path: z.string().min(1).max(1024).describe("Clip asset path under Assets/, must end with .anim."),
        name: z.string().min(1).max(256).optional().describe("Clip name (defaults to the file name)."),
        curves: z
          .array(
            z.object({
              path: z
                .string()
                .max(512)
                .default("")
                .optional()
                .describe("Relative GameObject path the curve animates (empty = the animated root)."),
              type: z
                .string()
                .min(1)
                .max(256)
                .default("UnityEngine.Transform")
                .optional()
                .describe("Component type the property lives on, e.g. UnityEngine.Transform."),
              property: z.string().min(1).max(256).describe('Animated property name, e.g. "m_LocalPosition.x".'),
              keys: z
                .array(z.object({ time: z.number(), value: z.number() }))
                .min(1)
                .max(4096)
                .describe("Keyframes (time in seconds, float value)."),
            }),
          )
          .max(256)
          .optional()
          .describe("Optional float curves to bake into the clip."),
        dryRun: z.boolean().default(true).optional(),
        onConflict: z.enum(["error", "generateUniquePath"]).default("error").optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/animation/create-clip"),
  );

  server.registerTool(
    "unity_create_animator_controller",
    {
      title: "Create Unity Animator Controller",
      description: "Create an AnimatorController .asset, optionally adding typed parameters (float/int/bool/trigger).",
      inputSchema: {
        path: z.string().min(1).max(1024).describe("Controller asset path under Assets/, must end with .controller."),
        parameters: z
          .array(
            z.object({
              name: z.string().min(1).max(256).describe("Parameter name."),
              type: z.enum(["float", "int", "bool", "trigger"]).default("float").optional().describe("Parameter type."),
            }),
          )
          .max(64)
          .optional()
          .describe("Optional animator parameters to add."),
        dryRun: z.boolean().default(true).optional(),
        onConflict: z.enum(["error", "generateUniquePath"]).default("error").optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/animation/create-controller"),
  );

  server.registerTool(
    "unity_add_animator_state",
    {
      title: "Add Animator State",
      description:
        "Add a state to an animator controller's first layer, optionally assigning a motion clip and marking it the default state.",
      inputSchema: {
        controllerPath: z.string().min(1).max(1024).describe("Animator controller asset path under Assets/ ending with .controller."),
        stateName: z.string().min(1).max(256).describe("Name of the state to add."),
        motionPath: z.string().min(1).max(1024).optional().describe("Optional AnimationClip asset path under Assets/ ending with .anim."),
        isDefault: z.boolean().default(false).optional().describe("Make this the state machine's default state."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/animation/add-state"),
  );

  server.registerTool(
    "unity_add_animator_transition",
    {
      title: "Add Animator Transition",
      description: "Add a transition between two named states on an animator controller's first layer, with optional parameter conditions.",
      inputSchema: {
        controllerPath: z.string().min(1).max(1024).describe("Animator controller asset path under Assets/ ending with .controller."),
        fromState: z.string().min(1).max(256).describe("Source state name (must already exist)."),
        toState: z.string().min(1).max(256).describe("Destination state name (must already exist)."),
        conditions: z
          .array(
            z.object({
              parameter: z.string().min(1).max(256).describe("Parameter the condition tests."),
              mode: z
                .enum(["if", "ifNot", "greater", "less", "equals", "notEqual"])
                .default("greater")
                .optional()
                .describe("Condition comparison mode."),
              threshold: z.number().default(0).optional().describe("Comparison threshold (ignored for if/ifNot)."),
            }),
          )
          .max(32)
          .optional()
          .describe("Optional transition conditions."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/animation/add-transition"),
  );

  server.registerTool(
    "unity_assign_animator",
    {
      title: "Assign Animator Controller To GameObject",
      description:
        "Assign a RuntimeAnimatorController asset to a scene GameObject's Animator (adding the Animator component if missing). Undo-wrapped.",
      inputSchema: {
        instanceId: z.number().int().optional().describe("Target GameObject instance ID."),
        path: z.string().min(1).max(512).optional().describe("Target GameObject hierarchy path (alternative to instanceId)."),
        controllerPath: z.string().min(1).max(1024).describe("Animator controller asset path under Assets/ ending with .controller."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    },
    toolPost("/animation/assign-animator"),
  );

  server.registerTool(
    "unity_reparent_game_object",
    {
      title: "Reparent Unity GameObject",
      description:
        "Move a scene GameObject under a new parent (or to the scene root when no parent is given), preserving world position by default. Undo-wrapped.",
      inputSchema: {
        instanceId: z.number().int().optional().describe("Target GameObject instance ID."),
        path: z.string().min(1).max(512).optional().describe("Target GameObject hierarchy path (alternative to instanceId)."),
        parentInstanceId: z
          .number()
          .int()
          .optional()
          .describe("New parent instance ID. Omit both parent fields to unparent to the scene root."),
        parentPath: z.string().min(1).max(512).optional().describe("New parent hierarchy path (alternative to parentInstanceId)."),
        worldPositionStays: z.boolean().default(true).optional().describe("Keep the GameObject's world transform when reparenting."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/reparent"),
  );

  server.registerTool(
    "unity_rename_game_object",
    {
      title: "Rename Unity GameObject",
      description: "Rename a scene GameObject. Undo-wrapped.",
      inputSchema: {
        instanceId: z.number().int().optional().describe("Target GameObject instance ID."),
        path: z.string().min(1).max(512).optional().describe("Target GameObject hierarchy path (alternative to instanceId)."),
        newName: z.string().min(1).max(512).describe("New name for the GameObject."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    },
    toolPost("/rename"),
  );

  server.registerTool(
    "unity_delete_game_object",
    {
      title: "Delete Unity GameObject",
      description: "Destroy a scene GameObject (and its children). Undo-wrapped, but destructive.",
      inputSchema: {
        instanceId: z.number().int().optional().describe("Target GameObject instance ID."),
        path: z.string().min(1).max(512).optional().describe("Target GameObject hierarchy path (alternative to instanceId)."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: true, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/delete-game-object"),
  );

  server.registerTool(
    "unity_duplicate_game_object",
    {
      title: "Duplicate Unity GameObject",
      description: "Clone a scene GameObject under the same parent, keeping the source name. Undo-wrapped.",
      inputSchema: {
        instanceId: z.number().int().optional().describe("Source GameObject instance ID."),
        path: z.string().min(1).max(512).optional().describe("Source GameObject hierarchy path (alternative to instanceId)."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/duplicate-game-object"),
  );

  server.registerTool(
    "unity_reorder_sibling",
    {
      title: "Reorder Unity Sibling",
      description: "Set a scene GameObject's sibling index among its parent's children. Undo-wrapped.",
      inputSchema: {
        instanceId: z.number().int().optional().describe("Target GameObject instance ID."),
        path: z.string().min(1).max(512).optional().describe("Target GameObject hierarchy path (alternative to instanceId)."),
        siblingIndex: z.number().int().min(0).describe("New zero-based sibling index."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    },
    toolPost("/reorder-sibling"),
  );

  server.registerTool(
    "unity_instantiate_prefab",
    {
      title: "Instantiate Unity Prefab",
      description: "Instantiate a prefab asset into the active scene, optionally under a parent and at a local position. Undo-wrapped.",
      inputSchema: {
        prefabPath: z.string().min(1).max(1024).describe("Prefab asset path under Assets/, must end with .prefab."),
        parentInstanceId: z.number().int().optional().describe("Optional parent instance ID."),
        parentPath: z.string().min(1).max(512).optional().describe("Optional parent hierarchy path (alternative to parentInstanceId)."),
        position: vector3Schema.optional().describe("Optional local position for the new instance."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/instantiate-prefab"),
  );

  server.registerTool(
    "unity_apply_prefab_overrides",
    {
      title: "Apply Unity Prefab Overrides",
      description: "Apply a scene prefab instance's overrides back to the source prefab asset. Undo-wrapped.",
      inputSchema: {
        instanceId: z.number().int().optional().describe("Prefab instance GameObject instance ID."),
        path: z.string().min(1).max(512).optional().describe("Prefab instance hierarchy path (alternative to instanceId)."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/prefab-apply"),
  );

  server.registerTool(
    "unity_revert_prefab_overrides",
    {
      title: "Revert Unity Prefab Overrides",
      description: "Revert a scene prefab instance's overrides back to the source prefab asset's values. Undo-wrapped.",
      inputSchema: {
        instanceId: z.number().int().optional().describe("Prefab instance GameObject instance ID."),
        path: z.string().min(1).max(512).optional().describe("Prefab instance hierarchy path (alternative to instanceId)."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
    },
    toolPost("/prefab-revert"),
  );

  server.registerTool(
    "unity_get_settings",
    {
      title: "Read Unity Settings",
      description: "Read the allowlisted project/player/quality/time/physics settings ScenePort can change, with their current values.",
      inputSchema: {},
      annotations: { readOnlyHint: true, openWorldHint: false },
    },
    toolGet("/settings/get"),
  );

  server.registerTool(
    "unity_set_setting",
    {
      title: "Set Unity Setting",
      description:
        "Set one allowlisted setting by key (e.g. quality.level, time.fixedDeltaTime, player.productName, physics.gravity). Echoes the previous value for manual revert. Settings are not Unity-Undo reversible.",
      inputSchema: {
        key: z.string().min(1).max(128).describe("Allowlisted setting key from unity_get_settings."),
        value: z
          .union([z.string(), z.number(), z.boolean(), z.object({ x: z.number(), y: z.number(), z: z.number().optional() })])
          .describe("New value; type must match the key (string/int/float/vector3)."),
        dryRun: z.boolean().default(true).optional(),
      },
      annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true, openWorldHint: false },
    },
    toolPost("/settings/set"),
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
    "Call unity_capture_game_view with inline:true so you can SEE the current Unity Game view, then compare it pixel-for-pixel against the supplied screenshot or design intent. Identify the Canvas objects, assets, layout, and serialized property edits needed to reproduce the target UI, apply them with typed ScenePort tools (create_game_object, add_component, set_serialized_property), and re-capture the Game view to visually confirm the result matches before finishing.",
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

  registerPrompt(
    "sceneport:self-heal",
    "Self-Heal Play Mode Loop",
    "Autonomously enter play mode, observe, fix the top issue, and re-verify.",
    "Run an autonomous repair loop with ScenePort: check compilation status and console logs, enter play mode, capture the Game view with inline:true so you can SEE what the player sees, and stream the console. Diagnose the single highest-impact runtime problem (error, exception, broken visual, or stuck state). Exit play mode, apply the smallest safe fix through typed ScenePort tools or a focused code edit, then re-enter play mode and re-capture to confirm the issue is resolved. Repeat until the captured frame and console are clean or you hit a blocker you must report. Keep every change reversible and summarize what you healed.",
  );

  registerPrompt(
    "sceneport:visual-regression",
    "Visual Regression Check",
    "Capture a golden baseline, then detect and explain visual changes.",
    "Use ScenePort for visual regression: if no baseline exists, call unity_capture_golden_frame to record one and stop. Otherwise capture the current frame, then call unity_compare_golden_frame (tune threshold/passThreshold as needed) and LOOK at the returned diff image — red regions mark changed pixels. Report pixelDiffPercent, where the changes are (changedBox), whether they are intended or a regression, and the likely cause in the scene/assets. Only approve a new baseline when the change is intentional.",
  );

  registerPrompt(
    "sceneport:explain-scene",
    "Explain This Scene",
    "Answer how the scene works using hierarchy and asset relationships.",
    "Use ScenePort to explain how the active Unity scene works: read unity_scene_hierarchy and unity_selection, inspect key GameObjects and their components, and walk unity_asset_graph to trace which scripts, prefabs, and assets reference each other. Answer the user's question about responsibilities and data flow (e.g. 'what spawns enemies?', 'what references the player?') by grounding every claim in concrete objects and components you inspected, and flag anything ambiguous or risky you noticed.",
  );

  return server;
}
