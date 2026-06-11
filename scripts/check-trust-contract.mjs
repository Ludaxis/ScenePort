#!/usr/bin/env node
import { readFileSync } from "node:fs";
import { join } from "node:path";

const root = new URL("..", import.meta.url).pathname;
const read = (path) => readFileSync(join(root, path), "utf8");

const server = read("plugins/sceneport/server/src/server.ts");
const router = read("plugins/sceneport/unity-package/Editor/ScenePortRouter.cs");
const readme = read("README.md");
const architecture = read("docs/architecture/ARCHITECTURE.md");
const security = read("docs/security/SECURITY_MODEL.md");
const version = JSON.parse(read("plugins/sceneport/server/package.json")).version;

const requiredTools = [
  "unity_query_scene",
  "unity_tests_run",
  "unity_diagnostics",
  "unity_authoring_batch",
  "unity_create_script",
  "unity_create_material",
  "unity_create_prefab",
  "unity_execute_menu_item",
];

const requiredEndpoints = [
  "/scene-query",
  "/serialized-read",
  "/tests/run",
  "/assertions/evaluate",
  "/diagnostics",
  "/authoring/batch",
  "/create-script",
  "/create-material",
  "/create-prefab",
];

const requiredDocs = [
  "Staged Trust",
  "sceneport doctor --json",
  "unity_query_scene",
  "unity_authoring_batch",
  "capability.denied",
];

const failures = [];

for (const tool of requiredTools) {
  if (!server.includes(`"${tool}"`)) {
    failures.push(`Missing MCP tool ${tool} in server.ts`);
  }
}

for (const endpoint of requiredEndpoints) {
  if (!router.includes(`"${endpoint}"`)) {
    failures.push(`Missing Unity endpoint ${endpoint} in ScenePortRouter.cs`);
  }
  if (!architecture.includes(endpoint) && !readme.includes(endpoint)) {
    failures.push(`Endpoint ${endpoint} is not documented in README or architecture docs`);
  }
}

for (const docNeedle of requiredDocs) {
  if (!readme.includes(docNeedle) && !architecture.includes(docNeedle) && !security.includes(docNeedle)) {
    failures.push(`Missing trust-contract doc text: ${docNeedle}`);
  }
}

if (!read("plugins/sceneport/server/src/version.ts").includes(`"${version}"`)) {
  failures.push(`version.ts is not synced to ${version}`);
}

if (failures.length > 0) {
  console.error("ScenePort trust contract check failed:");
  for (const failure of failures) {
    console.error(`  - ${failure}`);
  }
  process.exit(1);
}

console.log(`ScenePort trust contract OK for ${version}.`);
