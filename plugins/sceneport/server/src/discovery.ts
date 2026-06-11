import { existsSync, readFileSync, statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";

export type DiscoverySource = "env-url" | "discovery-file" | "cwd-walk" | "default";

export const DISCOVERY_HEARTBEAT_STALE_MS = 30_000;

export interface BridgeDiscoveryMetadata {
  schemaVersion?: number;
  bridge?: string;
  bridgeVersion?: string;
  protocolVersion?: number | string;
  capabilitiesHash?: string;
  heartbeatUtc?: string;
  heartbeatAgeMs?: number;
  heartbeatStale?: boolean;
  ownerLeaseId?: string;
  editorRole?: string;
  processId?: number;
  startedUtc?: string;
  projectId?: string;
  projectName?: string;
  unityVersion?: string;
  policyProfile?: string;
  tokenStorage?: string;
  tokenRef?: string;
  tokenFingerprint?: string;
  fileMtimeMs?: number;
}

export interface BridgeTarget {
  baseUrl: string;
  token?: string;
  projectPath?: string;
  projectId?: string;
  source: DiscoverySource;
  discoveryFilePath?: string;
  discovery?: BridgeDiscoveryMetadata;
  discoveryProblems?: string[];
}

const DEFAULT_URL = "http://127.0.0.1:38987";
const MAX_WALK_DEPTH = 10;

interface BridgeFile {
  schemaVersion?: number;
  bridge?: string;
  bridgeVersion?: string;
  protocolVersion?: number | string;
  capabilitiesHash?: string;
  heartbeatUtc?: string;
  ownerLeaseId?: string;
  editorRole?: string;
  processId?: number;
  startedUtc?: string;
  url?: string;
  port?: number;
  token?: string;
  projectPath?: string;
  projectId?: string;
  projectName?: string;
  unityVersion?: string;
  policyProfile?: string;
  tokenStorage?: string;
  tokenRef?: string;
  tokenFingerprint?: string;
}

interface BridgeFileSnapshot {
  path: string;
  file: BridgeFile | null;
  metadata?: BridgeDiscoveryMetadata;
  problems: string[];
}

function discoveryFilePath(projectPath: string): string {
  return join(projectPath, "Library", "ScenePort", "bridge.json");
}

function computeHeartbeat(heartbeatUtc: string | undefined, now = Date.now()) {
  if (!heartbeatUtc) {
    return {};
  }

  const heartbeatMs = Date.parse(heartbeatUtc);
  if (!Number.isFinite(heartbeatMs)) {
    return { heartbeatAgeMs: undefined, heartbeatStale: true };
  }

  const heartbeatAgeMs = Math.max(0, now - heartbeatMs);
  return {
    heartbeatAgeMs,
    heartbeatStale: heartbeatAgeMs > DISCOVERY_HEARTBEAT_STALE_MS,
  };
}

function metadataFor(file: BridgeFile, fileMtimeMs: number): BridgeDiscoveryMetadata {
  return {
    schemaVersion: typeof file.schemaVersion === "number" ? file.schemaVersion : undefined,
    bridge: typeof file.bridge === "string" ? file.bridge : undefined,
    bridgeVersion: typeof file.bridgeVersion === "string" ? file.bridgeVersion : undefined,
    protocolVersion:
      typeof file.protocolVersion === "number" || typeof file.protocolVersion === "string" ? file.protocolVersion : undefined,
    capabilitiesHash: typeof file.capabilitiesHash === "string" ? file.capabilitiesHash : undefined,
    heartbeatUtc: typeof file.heartbeatUtc === "string" ? file.heartbeatUtc : undefined,
    ...computeHeartbeat(typeof file.heartbeatUtc === "string" ? file.heartbeatUtc : undefined),
    ownerLeaseId: typeof file.ownerLeaseId === "string" ? file.ownerLeaseId : undefined,
    editorRole: typeof file.editorRole === "string" ? file.editorRole : undefined,
    processId: typeof file.processId === "number" ? file.processId : undefined,
    startedUtc: typeof file.startedUtc === "string" ? file.startedUtc : undefined,
    projectId: typeof file.projectId === "string" ? file.projectId : undefined,
    projectName: typeof file.projectName === "string" ? file.projectName : undefined,
    unityVersion: typeof file.unityVersion === "string" ? file.unityVersion : undefined,
    policyProfile: typeof file.policyProfile === "string" ? file.policyProfile : undefined,
    tokenStorage: typeof file.tokenStorage === "string" ? file.tokenStorage : undefined,
    tokenRef: typeof file.tokenRef === "string" ? file.tokenRef : undefined,
    tokenFingerprint: typeof file.tokenFingerprint === "string" ? file.tokenFingerprint : undefined,
    fileMtimeMs,
  };
}

export function readDiscoveryFile(projectPath: string): BridgeFileSnapshot {
  const path = discoveryFilePath(projectPath);
  const problems: string[] = [];
  try {
    if (!existsSync(path)) {
      return { path, file: null, problems: ["discovery.not_found"] };
    }
    const stat = statSync(path);
    const parsed = JSON.parse(readFileSync(path, "utf8")) as BridgeFile;
    if (!parsed || typeof parsed !== "object") {
      return { path, file: null, problems: ["discovery.invalid_shape"] };
    }

    const metadata = metadataFor(parsed, stat.mtimeMs);
    if (metadata.heartbeatStale) {
      problems.push("discovery.stale_heartbeat");
    }
    if (metadata.editorRole === "asset-import-worker") {
      problems.push("discovery.asset_import_worker_owner");
    }
    return { path, file: parsed, metadata, problems };
  } catch {
    return { path, file: null, problems: ["discovery.malformed_json"] };
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

function unsafeBridgeUrlsAllowed(env: NodeJS.ProcessEnv): boolean {
  return env.SCENEPORT_ALLOW_UNSAFE_BRIDGE_URL === "1" || env.SCENEPORT_ALLOW_UNSAFE_BRIDGE_URL === "true";
}

function explicitToken(env: NodeJS.ProcessEnv): string | undefined {
  if (env.SCENEPORT_TOKEN) {
    return env.SCENEPORT_TOKEN;
  }
  if (env.SCENEPORT_TOKEN_FILE) {
    try {
      return readFileSync(env.SCENEPORT_TOKEN_FILE, "utf8").trim();
    } catch {
      return undefined;
    }
  }
  return undefined;
}

export function isLoopbackBridgeUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return (
      url.protocol === "http:" &&
      (url.hostname === "127.0.0.1" || url.hostname === "localhost" || url.hostname === "::1" || url.hostname === "[::1]")
    );
  } catch {
    return false;
  }
}

function normalizeUrl(url: string): string {
  return url.replace(/\/$/, "");
}

function safeDiscoveryUrl(url: string | undefined, env: NodeJS.ProcessEnv): string | undefined {
  if (!url) {
    return undefined;
  }
  const normalized = normalizeUrl(url);
  return unsafeBridgeUrlsAllowed(env) || isLoopbackBridgeUrl(normalized) ? normalized : undefined;
}

function urlFromBridgeFile(file: BridgeFile): string | undefined {
  if (typeof file.url === "string" && file.url.length > 0) {
    return file.url;
  }
  if (typeof file.port === "number" && file.port > 0) {
    return `http://127.0.0.1:${file.port}`;
  }
  return undefined;
}

function targetFromFile(
  snapshot: BridgeFileSnapshot,
  source: DiscoverySource,
  env: NodeJS.ProcessEnv,
  explicitToken?: string,
): BridgeTarget | null {
  if (!snapshot.file) {
    return null;
  }
  const fileUrl = safeDiscoveryUrl(urlFromBridgeFile(snapshot.file), env);
  if (!fileUrl) {
    return null;
  }
  return {
    baseUrl: fileUrl,
    token: explicitToken ?? snapshot.file.token,
    projectPath: snapshot.file.projectPath,
    projectId: snapshot.file.projectId,
    source,
    discoveryFilePath: snapshot.path,
    discovery: snapshot.metadata,
    discoveryProblems: snapshot.problems,
  };
}

/**
 * Resolve which Unity bridge to talk to, with zero configuration in the common case.
 * Order: explicit URL → SCENEPORT_PROJECT_PATH discovery file → cwd walk → default port.
 * The token is loaded from a discovery file even when the URL is given explicitly.
 */
export function discoverBridge(env: NodeJS.ProcessEnv = process.env, cwd: string = process.cwd()): BridgeTarget {
  const explicitUrl = env.SCENEPORT_UNITY_URL;
  const token = explicitToken(env);
  const projectPathEnv = env.SCENEPORT_PROJECT_PATH;

  let snapshot: BridgeFileSnapshot | null = null;
  let fileProject: string | null = null;

  if (projectPathEnv) {
    const read = readDiscoveryFile(projectPathEnv);
    if (read.file) {
      snapshot = read;
      fileProject = projectPathEnv;
    }
  }

  if (!snapshot) {
    const walked = walkForProject(cwd);
    if (walked) {
      snapshot = readDiscoveryFile(walked);
      fileProject = walked;
    }
  }

  if (explicitUrl) {
    return {
      baseUrl: normalizeUrl(explicitUrl),
      token: token ?? snapshot?.file?.token,
      projectPath: snapshot?.file?.projectPath ?? projectPathEnv,
      projectId: snapshot?.file?.projectId,
      source: "env-url",
      discoveryFilePath: fileProject ? discoveryFilePath(fileProject) : undefined,
      discovery: snapshot?.metadata,
      discoveryProblems: snapshot?.problems,
    };
  }

  if (snapshot) {
    const target = targetFromFile(snapshot, projectPathEnv && fileProject === projectPathEnv ? "discovery-file" : "cwd-walk", env, token);
    if (target) {
      return {
        ...target,
        projectPath: target.projectPath ?? fileProject ?? undefined,
      };
    }
  }

  return {
    baseUrl: DEFAULT_URL,
    token,
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
