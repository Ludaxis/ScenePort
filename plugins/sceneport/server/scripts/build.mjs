import { copyFile, mkdir, rm } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { build } from "esbuild";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const serverDir = resolve(scriptDir, "..");
const bundlePath = resolve(serverDir, "build/index.js");

await rm("build", { recursive: true, force: true });

await build({
  entryPoints: ["src/index.ts"],
  outfile: "build/index.js",
  bundle: true,
  platform: "node",
  format: "esm",
  target: "node18",
});

// Ship the self-contained bundle inside the Unity UPM package so the Setup
// panel can wire a local `node <path>` MCP config with no npm/clone/publish.
// `Server~` (trailing tilde) is the Unity convention for on-disk files that the
// asset importer ignores, so no .meta is generated for it or its contents.
const unityServerDir = resolve(serverDir, "../unity-package/Server~");
await mkdir(unityServerDir, { recursive: true });
await copyFile(bundlePath, resolve(unityServerDir, "index.js"));
