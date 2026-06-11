import { existsSync, readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";

export type DiscoverySource = "env-url" | "discovery-file" | "cwd-walk" | "default";

export interface BridgeTarget {
  baseUrl: string;
  token?: string;
  projectPath?: string;
  source: DiscoverySource;
  discoveryFilePath?: string;
}

const DEFAULT_URL = "http://127.0.0.1:38987";
const MAX_WALK_DEPTH = 10;

interface BridgeFile {
  url?: string;
  token?: string;
  projectPath?: string;
}

function discoveryFilePath(projectPath: string): string {
  return join(projectPath, "Library", "ScenePort", "bridge.json");
}

function readBridgeFile(projectPath: string): BridgeFile | null {
  try {
    const path = discoveryFilePath(projectPath);
    if (!existsSync(path)) {
      return null;
    }
    const parsed = JSON.parse(readFileSync(path, "utf8")) as BridgeFile;
    return parsed && typeof parsed === "object" ? parsed : null;
  } catch {
    return null;
  }
}

function looksLikeUnityProject(dir: string): boolean {
  return existsSync(join(dir, "Assets")) && existsSync(join(dir, "ProjectSettings")) && existsSync(discoveryFilePath(dir));
}

function walkForProject(start: string): string | null {
  let dir = start;
  for (let i = 0; i < MAX_WALK_DEPTH; i++) {
    if (looksLikeUnityProject(dir)) {
      return dir;
    }
    const parent = dirname(dir);
    if (parent === dir) {
      break;
    }
    dir = parent;
  }
  return null;
}

function normalizeUrl(url: string): string {
  return url.replace(/\/$/, "");
}

/**
 * Resolve which Unity bridge to talk to, with zero configuration in the common case.
 * Order: explicit URL → SCENEPORT_PROJECT_PATH discovery file → cwd walk → default port.
 * The token is loaded from a discovery file even when the URL is given explicitly.
 */
export function discoverBridge(env: NodeJS.ProcessEnv = process.env, cwd: string = process.cwd()): BridgeTarget {
  const explicitUrl = env.SCENEPORT_UNITY_URL;
  const explicitToken = env.SCENEPORT_TOKEN;
  const projectPathEnv = env.SCENEPORT_PROJECT_PATH;

  let file: BridgeFile | null = null;
  let fileProject: string | null = null;

  if (projectPathEnv) {
    file = readBridgeFile(projectPathEnv);
    if (file) {
      fileProject = projectPathEnv;
    }
  }

  if (!file) {
    const walked = walkForProject(cwd);
    if (walked) {
      file = readBridgeFile(walked);
      fileProject = walked;
    }
  }

  if (explicitUrl) {
    return {
      baseUrl: normalizeUrl(explicitUrl),
      token: explicitToken ?? file?.token,
      projectPath: file?.projectPath ?? projectPathEnv,
      source: "env-url",
      discoveryFilePath: fileProject ? discoveryFilePath(fileProject) : undefined,
    };
  }

  if (file?.url) {
    return {
      baseUrl: normalizeUrl(file.url),
      token: explicitToken ?? file.token,
      projectPath: file.projectPath ?? fileProject ?? undefined,
      source: projectPathEnv && fileProject === projectPathEnv ? "discovery-file" : "cwd-walk",
      discoveryFilePath: fileProject ? discoveryFilePath(fileProject) : undefined,
    };
  }

  return {
    baseUrl: DEFAULT_URL,
    token: explicitToken,
    source: "default",
  };
}

export function projectPathsEqual(a: string | undefined, b: string | undefined): boolean {
  if (!a || !b) {
    return false;
  }
  const na = resolve(a);
  const nb = resolve(b);
  if (process.platform === "darwin" || process.platform === "win32") {
    return na.toLowerCase() === nb.toLowerCase();
  }
  return na === nb;
}
