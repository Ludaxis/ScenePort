#!/usr/bin/env node
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { createScenePortServer } from "./server.js";
import { UnityBridgeClient } from "./unityClient.js";

async function main() {
  const client = new UnityBridgeClient();
  const server = createScenePortServer(client);
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error("ScenePort MCP server running on stdio");
}

main().catch((error) => {
  console.error("Fatal ScenePort MCP server error:", error);
  process.exit(1);
});
