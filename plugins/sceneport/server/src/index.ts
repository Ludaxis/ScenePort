#!/usr/bin/env node
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { runDoctor } from "./doctor.js";
import { createScenePortServer } from "./server.js";
import { claudeAddCommand, codexConfigJson, codexConfigToml, npxServerConfig, resolveProjectPath } from "./setup.js";
import { UnityBridgeClient } from "./unityClient.js";
import { VERSION } from "./version.js";

function printHostConfig(host: "codex" | "claude") {
  const projectPath = resolveProjectPath(process.env, process.cwd());
  if (host === "claude") {
    console.log(claudeAddCommand(projectPath));
    return;
  }
  console.log(codexConfigJson(projectPath));
}

function writeClaudeConfig(projectPath: string): number {
  const config = npxServerConfig(projectPath);
  const json = JSON.stringify(config);
  try {
    const output = execFileSync("claude", ["mcp", "add-json", "sceneport", json], { encoding: "utf8" });
    if (output.trim()) {
      console.log(output.trim());
    }
    console.log("Registered ScenePort with Claude (npx form).");
    return 0;
  } catch (error) {
    const code = typeof error === "object" && error && "code" in error ? String((error as { code: unknown }).code) : "";
    if (code === "ENOENT") {
      console.error("Could not find the `claude` CLI on your PATH.");
    } else {
      const stderr = typeof error === "object" && error && "stderr" in error ? String((error as { stderr: unknown }).stderr) : "";
      if (stderr.trim()) {
        console.error(stderr.trim());
      }
      console.error("Running `claude mcp add-json` failed.");
    }
    console.error("Run this manually instead:");
    console.error(`  ${claudeAddCommand(projectPath)}`);
    return 1;
  }
}

function writeCodexConfig(projectPath: string, target: string | undefined): number {
  const destination = resolve(target ?? "./sceneport.codex.json");
  try {
    writeFileSync(destination, `${codexConfigJson(projectPath)}\n`, "utf8");
  } catch (error) {
    console.error(`Could not write Codex config to ${destination}: ${error instanceof Error ? error.message : String(error)}`);
    return 1;
  }
  console.log(`Wrote Codex MCP config to ${destination}.`);
  console.log("Merge its `mcpServers.sceneport` entry into your Codex MCP config, or paste this TOML into ~/.codex/config.toml:");
  console.log("");
  console.log(codexConfigToml(projectPath));
  return 0;
}

function runConfig(host: "codex" | "claude", argv: string[]): number {
  const write = argv.includes("--write");
  if (host === "claude") {
    if (!write) {
      printHostConfig("claude");
      return 0;
    }
    return writeClaudeConfig(resolveProjectPath(process.env, process.cwd()));
  }

  // codex
  if (!write) {
    printHostConfig("codex");
    return 0;
  }
  const targetFlag = argv.indexOf("--target");
  const target = targetFlag >= 0 ? argv[targetFlag + 1] : undefined;
  return writeCodexConfig(resolveProjectPath(process.env, process.cwd()), target);
}

async function runInit(argv: string[]): Promise<number> {
  const write = argv.includes("--write");
  const projectPath = resolveProjectPath(process.env, process.cwd());
  console.log(`ScenePort setup ${VERSION}`);
  console.log(`Unity project: ${projectPath}`);
  console.log("");
  await runDoctor(process.env, process.cwd());
  console.log("");
  console.log("Recommended Claude command:");
  console.log(`  ${claudeAddCommand(projectPath)}`);
  console.log("");
  console.log("Recommended Codex config (paste into your Codex MCP config):");
  console.log(codexConfigJson(projectPath));
  console.log("");

  if (write) {
    console.log("Writing the Claude registration now...");
    return writeClaudeConfig(projectPath);
  }

  console.log("Next steps:");
  console.log("  - Run `sceneport config claude --write` to register with Claude automatically.");
  console.log("  - Run `sceneport config codex --write` to write a Codex config snippet.");
  return 0;
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
      process.exitCode = runConfig(host, process.argv.slice(4));
      return;
    }
    console.error("Usage: sceneport config claude|codex [--write] [--target <path>]");
    process.exitCode = 1;
    return;
  }

  if (command === "init") {
    process.exitCode = await runInit(process.argv.slice(3));
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
