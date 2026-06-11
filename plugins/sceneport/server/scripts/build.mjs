import { rm } from "node:fs/promises";
import { build } from "esbuild";

await rm("build", { recursive: true, force: true });

await build({
  entryPoints: ["src/index.ts"],
  outfile: "build/index.js",
  bundle: true,
  platform: "node",
  format: "esm",
  target: "node18",
});
