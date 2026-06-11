import { type BridgeTarget, DISCOVERY_HEARTBEAT_STALE_MS, discoverBridge, readDiscoveryFile } from "./discovery.js";
import { type StatusReport, UnityBridgeClient } from "./unityClient.js";
import { VERSION } from "./version.js";

type CheckStatus = "pass" | "warn" | "fail";

export interface DoctorCheck {
  name: string;
  status: CheckStatus;
  detail: string;
}

export interface DoctorReport {
  status: "ok" | "warning" | "error";
  version: string;
  nodeVersion: string;
  cwd: string;
  discovery: BridgeTarget;
  health: StatusReport | null;
  checks: DoctorCheck[];
}

export interface DoctorOptions {
  json?: boolean;
}

function check(name: string, status: CheckStatus, detail: string): DoctorCheck {
  return { name, status, detail };
}

function nodeMajor(version: string): number {
  return Number.parseInt(version.replace(/^v/, "").split(".")[0] ?? "0", 10);
}

function reportStatus(checks: DoctorCheck[]): DoctorReport["status"] {
  if (checks.some((item) => item.status === "fail")) {
    return "error";
  }
  return checks.some((item) => item.status === "warn") ? "warning" : "ok";
}

function processIsAlive(pid: number | undefined): boolean | null {
  if (!pid || !Number.isInteger(pid) || pid <= 0) {
    return null;
  }
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    const code = typeof error === "object" && error && "code" in error ? String((error as { code: unknown }).code) : "";
    return code === "EPERM";
  }
}

function redactedDiscovery(discovery: BridgeTarget): BridgeTarget {
  const { token: _token, ...safe } = discovery;
  return {
    ...safe,
    discovery: discovery.discovery
      ? {
          ...discovery.discovery,
          tokenFingerprint: discovery.discovery.tokenFingerprint,
          tokenRef: discovery.discovery.tokenRef,
        }
      : discovery.discovery,
  };
}

export async function buildDoctorReport(env: NodeJS.ProcessEnv = process.env, cwd: string = process.cwd()): Promise<DoctorReport> {
  const checks: DoctorCheck[] = [];
  const nodeVersion = process.version;
  checks.push(check("node", nodeMajor(nodeVersion) >= 18 ? "pass" : "fail", `Node ${nodeVersion}; ScenePort requires Node 18 or newer.`));

  let discovery: BridgeTarget;
  try {
    discovery = discoverBridge(env, cwd);
    checks.push(
      check(
        "discovery",
        discovery.source === "default" ? "warn" : "pass",
        discovery.discoveryFilePath
          ? `Using ${discovery.source} at ${discovery.discoveryFilePath}.`
          : `Using ${discovery.source} bridge URL ${discovery.baseUrl}.`,
      ),
    );
  } catch (error) {
    discovery = { baseUrl: "http://127.0.0.1:38987", source: "default" };
    checks.push(check("discovery", "fail", error instanceof Error ? error.message : String(error)));
  }

  const discoveryProjectPath = env.SCENEPORT_PROJECT_PATH ?? discovery.projectPath;
  if (discoveryProjectPath) {
    const raw = readDiscoveryFile(discoveryProjectPath);
    if (!raw.file) {
      checks.push(
        check(
          "discovery-file",
          env.SCENEPORT_UNITY_URL ? "warn" : "fail",
          `Cannot read ${raw.path}: ${raw.problems.join(", ") || "missing"}.`,
        ),
      );
    } else {
      const schema = raw.metadata?.schemaVersion ?? 1;
      checks.push(
        check(
          "discovery-schema",
          schema >= 2 ? "pass" : "warn",
          schema >= 2 ? `Discovery schema v${schema} with live owner metadata.` : "Legacy discovery schema; update the Unity package.",
        ),
      );

      if (raw.metadata?.heartbeatUtc) {
        const age = raw.metadata.heartbeatAgeMs ?? 0;
        checks.push(
          check(
            "owner-heartbeat",
            raw.metadata.heartbeatStale ? "fail" : "pass",
            raw.metadata.heartbeatStale
              ? `Heartbeat is stale (${Math.round(age / 1000)}s old; expected under ${DISCOVERY_HEARTBEAT_STALE_MS / 1000}s).`
              : `Heartbeat is fresh (${Math.round(age / 1000)}s old).`,
          ),
        );
      } else {
        checks.push(check("owner-heartbeat", "warn", "Discovery file has no heartbeat; owner freshness cannot be verified."));
      }

      const alive = processIsAlive(raw.metadata?.processId);
      if (alive !== null) {
        checks.push(
          check(
            "owner-process",
            alive ? "pass" : "fail",
            alive
              ? `Owner process ${raw.metadata?.processId} is reachable (${raw.metadata?.editorRole ?? "unknown role"}).`
              : `Owner process ${raw.metadata?.processId} is not running; restart Unity or delete the stale discovery file.`,
          ),
        );
      }

      if (raw.metadata?.editorRole === "asset-import-worker") {
        checks.push(check("owner-role", "fail", "Discovery is owned by an AssetImportWorker, which must never host ScenePort."));
      } else if (raw.metadata?.editorRole) {
        checks.push(check("owner-role", "pass", `Bridge owner role is ${raw.metadata.editorRole}.`));
      }
    }
  }

  let health: StatusReport | null = null;
  try {
    const client = new UnityBridgeClient(discovery, env);
    health = await client.statusReport();
    if (health.status === "error") {
      checks.push(check("bridge", "fail", String(health.error ?? "Unity bridge is not reachable.")));
    } else {
      checks.push(
        check(
          "bridge",
          "pass",
          `Connected to ${String(health.projectName ?? "Unity project")} on ${String(health.port ?? discovery.baseUrl)}.`,
        ),
      );
    }

    if (health.tokenRequired === true && !health.tokenConfigured) {
      checks.push(check("auth", "fail", "Unity requires a ScenePort token, but the MCP server did not discover one."));
    } else if (health.tokenRequired === false) {
      checks.push(check("auth", "warn", "Unity bridge auth token requirement is disabled in the Editor."));
    } else {
      checks.push(check("auth", health.tokenConfigured ? "pass" : "warn", "Token discovery state is acceptable for this bridge."));
    }

    if (health.identityMatch === false) {
      checks.push(check("identity", "fail", "Connected Unity project does not match SCENEPORT_PROJECT_PATH."));
    } else if (health.identityMatch === true) {
      checks.push(check("identity", "pass", "Connected Unity project matches SCENEPORT_PROJECT_PATH."));
    } else {
      checks.push(check("identity", "warn", "SCENEPORT_PROJECT_PATH is not set, so wrong-project protection is advisory only."));
    }

    const capabilities = health.capabilities;
    if (capabilities?.mode === "legacy-v0.5") {
      checks.push(check("capabilities", "warn", "Bridge does not expose /capabilities; treating it as legacy v0.5-compatible."));
    } else if (capabilities?.status === "ok") {
      checks.push(
        check(
          "capabilities",
          "pass",
          `Protocol ${String(capabilities.protocolVersion ?? health.protocolVersion ?? "unknown")} capabilities ${String(
            capabilities.capabilitiesHash ?? health.capabilitiesHash ?? "unknown",
          )}.`,
        ),
      );
    }
  } catch (error) {
    checks.push(check("bridge", "fail", error instanceof Error ? error.message : String(error)));
  }

  checks.push(check("mcp", "pass", `ScenePort MCP server version ${VERSION} can start in stdio mode.`));

  return {
    status: reportStatus(checks),
    version: VERSION,
    nodeVersion,
    cwd,
    discovery: redactedDiscovery(discovery),
    health,
    checks,
  };
}

export function formatDoctorReport(report: DoctorReport): string {
  const icon: Record<CheckStatus, string> = { pass: "PASS", warn: "WARN", fail: "FAIL" };
  const lines = [
    `ScenePort doctor ${report.version}`,
    `Status: ${report.status}`,
    `CWD: ${report.cwd}`,
    `Bridge: ${report.discovery.baseUrl} (${report.discovery.source})`,
    "",
    ...report.checks.map((item) => `[${icon[item.status]}] ${item.name}: ${item.detail}`),
  ];

  if (report.health?.projectPath) {
    lines.push("", `Project: ${String(report.health.projectPath)}`);
  }

  lines.push(
    "",
    "Codex/Claude MCP command:",
    "  node /absolute/path/to/ScenePort/plugins/sceneport/server/build/index.js",
    "Set SCENEPORT_PROJECT_PATH to the Unity project root when the MCP server is not launched from that project.",
  );

  return lines.join("\n");
}

export async function runDoctor(
  env: NodeJS.ProcessEnv = process.env,
  cwd: string = process.cwd(),
  options: DoctorOptions = {},
): Promise<number> {
  const report = await buildDoctorReport(env, cwd);
  console.log(options.json ? JSON.stringify(report, null, 2) : formatDoctorReport(report));
  return report.status === "error" ? 1 : 0;
}
