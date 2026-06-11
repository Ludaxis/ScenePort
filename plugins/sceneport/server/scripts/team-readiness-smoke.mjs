#!/usr/bin/env node
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { mkdir } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../../../", import.meta.url));
const evidencePath = process.env.SCENEPORT_SMOKE_EVIDENCE ?? join(root, "Temp", "ScenePort", "team-readiness-smoke.json");

function discoveryFilePath(projectPath) {
  return join(projectPath, "Library", "ScenePort", "bridge.json");
}

function resolveTarget() {
  const explicit = process.env.SCENEPORT_UNITY_URL;
  const projectPath = process.env.SCENEPORT_PROJECT_PATH ?? process.cwd();
  let file = {};
  const path = discoveryFilePath(projectPath);
  if (existsSync(path)) {
    file = JSON.parse(readFileSync(path, "utf8"));
  }

  return {
    baseUrl: (explicit ?? file.url ?? "http://127.0.0.1:38987").replace(/\/$/, ""),
    token: process.env.SCENEPORT_TOKEN ?? file.token,
    source: explicit ? "env-url" : existsSync(path) ? "discovery-file" : "default",
    discoveryFilePath: existsSync(path) ? path : null,
  };
}

async function call(baseUrl, token, path) {
  const headers = { Accept: "application/json" };
  if (token) {
    headers["X-ScenePort-Token"] = token;
  }
  const response = await fetch(new URL(path, baseUrl), { headers });
  const text = await response.text();
  let body = text;
  try {
    body = text ? JSON.parse(text) : {};
  } catch {
    // Preserve raw body in evidence.
  }
  return { ok: response.ok, status: response.status, body };
}

async function smoke() {
  const target = resolveTarget();
  const checks = [];
  const endpoints = [
    "/health",
    "/capabilities",
    "/scene-hierarchy?limit=50&maxDepth=4",
    "/console?limit=20&type=all",
    "/audit-log?limit=20",
  ];

  for (const endpoint of endpoints) {
    const result = await call(target.baseUrl, target.token, endpoint);
    checks.push({ endpoint, ...result });
  }

  const evidence = {
    status: checks.every((check) => check.ok || check.status === 404) ? "ok" : "error",
    baseUrl: target.baseUrl,
    discoverySource: target.source,
    discoveryFilePath: target.discoveryFilePath ?? null,
    generatedUtc: new Date().toISOString(),
    checks,
  };

  await mkdir(dirname(evidencePath), { recursive: true });
  writeFileSync(evidencePath, JSON.stringify(evidence, null, 2));
  console.log(JSON.stringify(evidence, null, 2));
  process.exitCode = evidence.status === "ok" ? 0 : 1;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  smoke().catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  });
}
