#!/usr/bin/env node
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { runDoctor } from "./doctor.js";
import { createScenePortServer } from "./server.js";
import { UnityBridgeClient } from "./unityClient.js";
import { VERSION } from "./version.js";

function printHostConfig(host: "codex" | "claude") {
  const command = process.argv[1] ?? "sceneport";
  const config = {
    sceneport: {
      command: "node",
      args: [command],
      env: {
        SCENEPORT_PROJECT_PATH: "/absolute/path/to/UnityProject",
      },
    },
  };
  if (host === "claude") {
    console.log(`claude mcp add-json sceneport '${JSON.stringify(config.sceneport)}'`);
    return;
  }
  console.log(JSON.stringify(config, null, 2));
}

async function runAuth(command: string | undefined) {
  if (command === "status") {
    const client = new UnityBridgeClient();
    const report = await client.statusReport();
    const discovery = report.discovery as Record<string, unknown> | undefined;
    console.log(
      JSON.stringify(
        {
          status: report.status ?? "ok",
          tokenConfigured: report.tokenConfigured,
          tokenRequired: report.tokenRequired,
          tokenStorage: discovery?.tokenStorage ?? "library",
          tokenFingerprint: discovery?.tokenFingerprint,
          discoverySource: report.discoverySource,
          discoveryFilePath: report.discoveryFilePath,
        },
        null,
        2,
      ),
    );
    return 0;
  }

  if (command === "rotate") {
    const client = new UnityBridgeClient();
    const result = await client.post("/auth/rotate", {});
    console.log(JSON.stringify(result, null, 2));
    return 0;
  }

  if (command === "migrate") {
    console.log(
      JSON.stringify(
        { status: "ok", message: "OS credential store migration is optional; current fallback storage remains supported." },
        null,
        2,
      ),
    );
    return 0;
  }

  console.error("Usage: sceneport auth status|rotate|migrate");
  return 1;
}

async function main() {
  const command = process.argv[2];
  if (command === "doctor" || command === "--doctor") {
    process.exitCode = await runDoctor(process.env, process.cwd(), { json: process.argv.includes("--json") });
    return;
  }

  if (command === "auth") {
    process.exitCode = await runAuth(process.argv[3]);
    return;
  }

  if (command === "config") {
    const host = process.argv[3];
    if (host === "codex" || host === "claude") {
      printHostConfig(host);
      return;
    }
    console.error("Usage: sceneport config codex|claude");
    process.exitCode = 1;
    return;
  }

  if (command === "update-check") {
    console.log(
      JSON.stringify(
        { status: "ok", localVersion: VERSION, source: process.argv.includes("--github") ? "github-unavailable-offline" : "local" },
        null,
        2,
      ),
    );
    return;
  }

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
