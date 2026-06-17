#!/usr/bin/env node
//
// capture-demo.mjs — generate real ScenePort screenshots for the README/docs.
//
// What it does:
//   Spawns the ScenePort MCP server as a stdio child, calls the capture tools, decodes the
//   returned MCP image content blocks, and writes PNGs into docs/media/.
//
// PRECONDITION: a Unity Editor with the ScenePort bridge running and reachable. This script
//   drives the *live* editor; it cannot synthesize frames on its own. If the bridge is not
//   reachable it prints guidance and exits non-zero.
//
// SDK NOTE: this uses @modelcontextprotocol/sdk, which is installed in
//   plugins/sceneport/server/node_modules. Run it from that directory so Node resolves the
//   SDK, e.g.:
//
//     cd plugins/sceneport/server
//     SCENEPORT_PROJECT_PATH=/path/to/UnityProject \
//       node ../../../scripts/capture-demo.mjs
//
//   Or point SCENEPORT_SERVER at a built/published server entry. By default it launches the
//   bundled build at plugins/sceneport/server/build/index.js. Set SCENEPORT_USE_NPX=1 to run
//   `npx -y sceneport-mcp` instead.
//
// Usage:
//   node scripts/capture-demo.mjs                 # capture game + scene view -> docs/media
//   node scripts/capture-demo.mjs game            # only the Game view
//   node scripts/capture-demo.mjs scene           # only the Scene view
//
// Environment:
//   SCENEPORT_PROJECT_PATH   Unity project root (required for bridge discovery).
//   SCENEPORT_SERVER         Override path to the server entry (default: bundled build).
//   SCENEPORT_USE_NPX=1      Launch `npx -y sceneport-mcp` instead of a local build.
//   SCENEPORT_MEDIA_DIR      Output dir (default: <repo>/docs/media).

import { spawnSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(__dirname, "..");
const mediaDir = process.env.SCENEPORT_MEDIA_DIR
  ? resolve(process.env.SCENEPORT_MEDIA_DIR)
  : join(repoRoot, "docs", "media");

const which = (process.argv[2] ?? "all").toLowerCase();
const wantGame = which === "all" || which === "game";
const wantScene = which === "all" || which === "scene";

function die(message) {
  console.error(`\n[capture-demo] ${message}\n`);
  process.exit(1);
}

if (!process.env.SCENEPORT_PROJECT_PATH) {
  console.warn(
    "[capture-demo] SCENEPORT_PROJECT_PATH is not set. The server will try cwd-based " +
      "discovery, which usually fails outside a Unity project. Set it to your Unity " +
      "project root for reliable bridge discovery.",
  );
}

// Resolve the SDK from the server package, regardless of cwd.
let Client;
let StdioClientTransport;
try {
  ({ Client } = await import("@modelcontextprotocol/sdk/client/index.js"));
  ({ StdioClientTransport } = await import("@modelcontextprotocol/sdk/client/stdio.js"));
} catch {
  die(
    "Could not import @modelcontextprotocol/sdk. Run this script from " +
      "plugins/sceneport/server (where the SDK is installed), e.g.:\n" +
      "  cd plugins/sceneport/server && node ../../../scripts/capture-demo.mjs",
  );
}

// Decide how to launch the server.
let command;
let args;
if (process.env.SCENEPORT_USE_NPX === "1") {
  command = "npx";
  args = ["-y", "sceneport-mcp"];
} else {
  const serverEntry = process.env.SCENEPORT_SERVER
    ? resolve(process.env.SCENEPORT_SERVER)
    : join(repoRoot, "plugins", "sceneport", "server", "build", "index.js");
  command = process.execPath; // current node
  args = [serverEntry];
}

const CAPTURES = [
  wantGame && { tool: "unity_capture_game_view", file: "game-view.png" },
  wantScene && { tool: "unity_capture_scene_view", file: "scene-view.png" },
].filter(Boolean);

function extractImage(result) {
  // MCP tool results carry content blocks; capture tools return an image block when
  // inline is true (the default). data is base64, mimeType e.g. image/png.
  const blocks = result?.content ?? [];
  const image = blocks.find((b) => b?.type === "image" && typeof b.data === "string");
  return image ?? null;
}

async function main() {
  mkdirSync(mediaDir, { recursive: true });

  const transport = new StdioClientTransport({
    command,
    args,
    env: { ...process.env },
    stderr: "inherit",
  });
  const client = new Client(
    { name: "sceneport-capture-demo", version: "0.0.0" },
    { capabilities: {} },
  );

  try {
    await client.connect(transport);
  } catch (error) {
    die(
      `Failed to start/connect to the ScenePort MCP server.\n` +
        `Command: ${command} ${args.join(" ")}\n` +
        `Cause: ${error?.message ?? error}`,
    );
  }

  let wrote = 0;
  for (const { tool, file } of CAPTURES) {
    let result;
    try {
      result = await client.callTool({
        name: tool,
        arguments: { inline: true, maxEdge: 1024 },
      });
    } catch (error) {
      console.error(
        `[capture-demo] ${tool} failed. Is the Unity Editor open with the ScenePort ` +
          `bridge running? Cause: ${error?.message ?? error}`,
      );
      continue;
    }

    if (result?.isError) {
      console.error(
        `[capture-demo] ${tool} returned an error result. Confirm the bridge is ` +
          `reachable (run 'sceneport doctor --json').`,
      );
      continue;
    }

    const image = extractImage(result);
    if (!image) {
      console.error(
        `[capture-demo] ${tool} returned no inline image block. The bridge may not be ` +
          `play/scene ready, or 'inline' was disabled.`,
      );
      continue;
    }

    const outPath = join(mediaDir, file);
    writeFileSync(outPath, Buffer.from(image.data, "base64"));
    console.log(`[capture-demo] wrote ${outPath} (${image.mimeType ?? "image/png"})`);
    wrote += 1;
  }

  await client.close().catch(() => {});

  if (wrote === 0) {
    die(
      "No captures were written. Ensure Unity is open with the ScenePort bridge running, " +
        "SCENEPORT_PROJECT_PATH points at that project, and 'sceneport doctor --json' is " +
        "healthy.",
    );
  }
  console.log(`[capture-demo] done: ${wrote} image(s) in ${mediaDir}`);
}

// Friendly preflight: warn if npx is requested but not present.
if (process.env.SCENEPORT_USE_NPX === "1") {
  const probe = spawnSync("npx", ["--version"], { stdio: "ignore" });
  if (probe.error) {
    die("SCENEPORT_USE_NPX=1 set but `npx` is not on PATH.");
  }
}

await main();
