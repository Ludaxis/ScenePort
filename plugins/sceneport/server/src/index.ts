#!/usr/bin/env node
import { McpServer, ResourceTemplate } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { UnityBridgeClient } from "./unityClient.js";
import { errorResult, jsonResult } from "./toolResult.js";

const client = new UnityBridgeClient();

const vector2Schema = z.object({ x: z.number(), y: z.number() });
const vector3Schema = z.object({ x: z.number(), y: z.number(), z: z.number() });
const vector4Schema = z.object({ x: z.number(), y: z.number(), z: z.number(), w: z.number() });
const colorSchema = z.object({ r: z.number(), g: z.number(), b: z.number(), a: z.number().optional() });
const objectLocatorSchema = {
  instanceId: z.number().int().optional().describe("Unity instance ID for a GameObject, Component, or asset."),
  path: z.string().min(1).max(1024).optional().describe("Hierarchy path, used when an instance ID is not available."),
};
const serializedValueSchema = z.union([z.string(), z.number(), z.boolean(), vector2Schema, vector3Schema, vector4Schema, colorSchema]);

const server = new McpServer(
  {
    name: "sceneport",
    version: "0.2.0",
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

function joinCsv(values: string[] | undefined) {
  return values && values.length > 0 ? values.join(",") : undefined;
}

function encodeSerializedValue(value: z.infer<typeof serializedValueSchema>) {
  if (typeof value === "string") {
    return { valueKind: "string", stringValue: value };
  }
  if (typeof value === "number") {
    return { valueKind: "number", numberValue: value };
  }
  if (typeof value === "boolean") {
    return { valueKind: "bool", boolValue: value };
  }
  if ("r" in value) {
    return { valueKind: "color", colorValue: { a: 1, ...value } };
  }
  if ("w" in value) {
    return { valueKind: "vector4", vector4Value: value };
  }
  if ("z" in value) {
    return { valueKind: "vector3", vector3Value: value };
  }
  return { valueKind: "vector2", vector2Value: value };
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

server.registerTool(
  "unity_status",
  {
    title: "Unity Bridge Status",
    description: "Check whether the ScenePort Unity Editor bridge is reachable.",
    inputSchema: {},
    annotations: { readOnlyHint: true },
  },
  toolGet("/health"),
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
    annotations: { readOnlyHint: true },
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
  },
  async () => {
    try {
      return jsonResult(await client.post("/play-mode", { action: "exit" }));
    } catch (error) {
      return errorResult(error);
    }
  },
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
  "sceneport:prepare-build",
  "Prepare Unity Build",
  "Run pre-build checks and identify blockers.",
  "Use ScenePort to inspect Unity status, package dependencies, compilation status, console errors, active scene state, and relevant tests. Return a concise build-readiness checklist with blockers and recommended fixes.",
);

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error("ScenePort MCP server running on stdio");
}

main().catch((error) => {
  console.error("Fatal ScenePort MCP server error:", error);
  process.exit(1);
});
