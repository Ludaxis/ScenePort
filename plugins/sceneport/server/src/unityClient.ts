import { ScenePortBridgeError, bridgeError } from "./bridgeError.js";
import { type BridgeTarget, discoverBridge, isLoopbackBridgeUrl, projectPathsEqual } from "./discovery.js";

const TOKEN_HEADER = "X-ScenePort-Token";

export interface BridgeCapabilities extends Record<string, unknown> {
  status: string;
  bridge?: string;
  protocolVersion?: number | string;
  capabilitiesHash?: string;
  mode?: "current" | "legacy-v0.5";
}

export interface StatusReport extends Record<string, unknown> {
  discoverySource: string;
  discoveryFilePath: string | null;
  discovery?: BridgeTarget["discovery"];
  discoveryProblems?: string[];
  tokenConfigured: boolean;
  identityMatch: boolean | null;
  capabilities?: BridgeCapabilities;
}

export class UnityBridgeClient {
  private target: BridgeTarget;
  private readonly env: NodeJS.ProcessEnv;
  private readonly cwd: string;
  private readonly resolverEnabled: boolean;
  private readonly expectedProjectPath?: string;
  private identityResolved = false;
  private identityError: string | null = null;

  constructor(target?: BridgeTarget | string, env: NodeJS.ProcessEnv = process.env, cwd: string = process.cwd()) {
    this.env = env;
    this.cwd = cwd;
    if (typeof target === "string") {
      // Legacy: bare base URL. Still pick up a token/identity from discovery if present.
      const discovered = discoverBridge(env, cwd);
      this.target = { ...discovered, baseUrl: target.replace(/\/$/, ""), source: "env-url" };
      this.resolverEnabled = false;
    } else {
      this.target = target ?? discoverBridge(env, cwd);
      this.resolverEnabled = target === undefined;
    }
    const unsafeAllowed = env.SCENEPORT_ALLOW_UNSAFE_BRIDGE_URL === "1" || env.SCENEPORT_ALLOW_UNSAFE_BRIDGE_URL === "true";
    if (!isLoopbackBridgeUrl(this.target.baseUrl) && !unsafeAllowed) {
      throw new Error(
        `ScenePort bridge URL must be loopback by default: ${this.target.baseUrl}. Set SCENEPORT_ALLOW_UNSAFE_BRIDGE_URL=1 only for an explicitly trusted development tunnel.`,
      );
    }
    this.expectedProjectPath = env.SCENEPORT_PROJECT_PATH;
  }

  get baseUrl(): string {
    return this.target.baseUrl;
  }

  async get(path: string, params: Record<string, string | number | boolean | undefined> = {}) {
    this.resolveFresh();
    await this.guardIdentity();
    return this.request("GET", path, params);
  }

  async post(path: string, body: unknown) {
    this.resolveFresh();
    await this.guardIdentity();
    return this.request("POST", path, undefined, body);
  }

  /**
   * Health plus discovery/identity metadata for unity_status. Never throws on identity
   * mismatch — it reports it, so the tool can be used to diagnose a wrong-project connection.
   */
  async statusReport(): Promise<StatusReport> {
    this.resolveFresh();
    let health: Record<string, unknown> = {};
    let error: string | undefined;
    try {
      const result = await this.request("GET", "/health");
      if (result && typeof result === "object") {
        health = result as Record<string, unknown>;
      }
    } catch (e) {
      error = e instanceof Error ? e.message : String(e);
    }

    const actualPath = typeof health.projectPath === "string" ? health.projectPath : undefined;
    const identityMatch = this.expectedProjectPath ? projectPathsEqual(actualPath, this.expectedProjectPath) : null;
    const legacyBridge = health.projectId === undefined && !error;
    const capabilities = await this.getCapabilities();

    return {
      ...health,
      ...(error ? { status: "error", error } : {}),
      discoverySource: this.target.source,
      discoveryFilePath: this.target.discoveryFilePath ?? null,
      discovery: this.target.discovery,
      discoveryProblems: this.target.discoveryProblems,
      tokenConfigured: Boolean(this.target.token),
      identityMatch,
      capabilities,
      ...(legacyBridge
        ? { warning: "Unity bridge is outdated (pre-0.3); update the ScenePort UPM package to enable auth and discovery." }
        : {}),
    };
  }

  async getCapabilities(): Promise<BridgeCapabilities> {
    try {
      const result = await this.request("GET", "/capabilities", {}, undefined, false, { skipLogicalError: true });
      if (result && typeof result === "object") {
        return result as BridgeCapabilities;
      }
    } catch (error) {
      if (error instanceof ScenePortBridgeError && (error.httpStatus === 404 || error.code === "capability.unsupported")) {
        return {
          status: "ok",
          bridge: "sceneport",
          mode: "legacy-v0.5",
        };
      }
    }
    return {
      status: "ok",
      bridge: "sceneport",
      mode: "legacy-v0.5",
    };
  }

  private async guardIdentity(): Promise<void> {
    if (!this.expectedProjectPath) {
      return;
    }
    if (!this.identityResolved) {
      try {
        const health = (await this.request("GET", "/health")) as Record<string, unknown>;
        const actual = typeof health?.projectPath === "string" ? health.projectPath : undefined;
        if (actual && !projectPathsEqual(actual, this.expectedProjectPath)) {
          this.identityError = `ScenePort is connected to Unity project '${actual}' but SCENEPORT_PROJECT_PATH expects '${this.expectedProjectPath}'. Another Unity instance probably owns this port. Close it, or point SCENEPORT_UNITY_URL at the correct bridge (see Library/ScenePort/bridge.json in the expected project).`;
        }
        this.identityResolved = true;
      } catch {
        // Leave unresolved so a later call retries once the bridge is reachable.
      }
    }
    if (this.identityError) {
      throw bridgeError({
        code: "identity.mismatch",
        category: "discovery",
        retryable: false,
        message: this.identityError,
        remediation: "Set SCENEPORT_PROJECT_PATH to the desired Unity project root or restart the stale Unity bridge.",
        details: { expectedProjectPath: this.expectedProjectPath },
      });
    }
  }

  private requestTimeoutMs(): number {
    const raw = this.env.SCENEPORT_HTTP_TIMEOUT_MS;
    const parsed = raw === undefined ? Number.NaN : Number(raw);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : 15000;
  }

  private async request(
    method: "GET" | "POST",
    path: string,
    params: Record<string, string | number | boolean | undefined> = {},
    body?: unknown,
    isRetry = false,
    options: { skipLogicalError?: boolean } = {},
  ): Promise<unknown> {
    const url = this.url(path, params);
    const headers: Record<string, string> = { Accept: "application/json" };
    if (this.target.token) {
      headers[TOKEN_HEADER] = this.target.token;
    }

    const timeoutMs = this.requestTimeoutMs();
    let response: Response;
    try {
      const signal = AbortSignal.timeout(timeoutMs);
      response =
        method === "GET"
          ? await fetch(url, { method, headers, signal })
          : await fetch(url, {
              method,
              headers: { ...headers, "Content-Type": "application/json" },
              body: JSON.stringify(body),
              signal,
            });
    } catch (error) {
      // A timeout means the bridge accepted the connection but did not respond
      // (Unity compiling, paused, or wedged). Rediscovery cannot fix that, so fail fast.
      const timedOut = error instanceof Error && (error.name === "TimeoutError" || error.name === "AbortError");
      if (timedOut) {
        throw bridgeError({
          code: "bridge.timeout",
          category: "network",
          retryable: true,
          message: `Unity bridge did not respond within ${timeoutMs}ms.`,
          remediation:
            "Unity may be compiling, paused, or busy. Wait for it to finish, or restart the bridge from Tools > ScenePort. Set SCENEPORT_HTTP_TIMEOUT_MS to change the limit.",
          details: { baseUrl: this.target.baseUrl, source: this.target.source, timeoutMs },
        });
      }
      // Bridge unreachable (e.g. Unity restarted on a new port). Rediscover once and retry.
      if (!isRetry && this.target.source !== "env-url" && this.rediscover()) {
        return this.request(method, path, params, body, true, options);
      }
      throw bridgeError({
        code: "bridge.unreachable",
        category: "network",
        retryable: true,
        message: error instanceof Error ? error.message : String(error),
        remediation: "Open the Unity project, confirm ScenePort is installed, then run sceneport doctor.",
        details: { baseUrl: this.target.baseUrl, source: this.target.source },
      });
    }

    // Token may have been regenerated (user deleted Library). Rediscover once and retry.
    if (response.status === 401 && !isRetry && this.rediscover()) {
      return this.request(method, path, params, body, true, options);
    }

    return this.parse(response, options);
  }

  private resolveFresh(): void {
    if (!this.resolverEnabled) {
      return;
    }

    const next = discoverBridge(this.env, this.cwd);
    const changed =
      next.baseUrl !== this.target.baseUrl ||
      next.token !== this.target.token ||
      next.projectId !== this.target.projectId ||
      next.discovery?.ownerLeaseId !== this.target.discovery?.ownerLeaseId ||
      next.discovery?.startedUtc !== this.target.discovery?.startedUtc ||
      next.discovery?.capabilitiesHash !== this.target.discovery?.capabilitiesHash ||
      next.discovery?.fileMtimeMs !== this.target.discovery?.fileMtimeMs;

    if (changed) {
      this.target = next;
      this.identityResolved = false;
      this.identityError = null;
    }
  }

  private rediscover(): boolean {
    const next = discoverBridge(this.env, this.cwd);
    const changed =
      next.baseUrl !== this.target.baseUrl ||
      next.token !== this.target.token ||
      next.projectId !== this.target.projectId ||
      next.discovery?.ownerLeaseId !== this.target.discovery?.ownerLeaseId ||
      next.discovery?.startedUtc !== this.target.discovery?.startedUtc;
    this.target = next;
    if (changed) {
      this.identityResolved = false;
      this.identityError = null;
    }
    return changed;
  }

  private url(path: string, params: Record<string, string | number | boolean | undefined> = {}) {
    const url = new URL(path, this.target.baseUrl);
    for (const [key, value] of Object.entries(params)) {
      if (value !== undefined) {
        url.searchParams.set(key, String(value));
      }
    }
    return url;
  }

  private async parse(response: Response, options: { skipLogicalError?: boolean } = {}) {
    const text = await response.text();
    let payload: unknown = text;

    if (text.length > 0) {
      try {
        payload = JSON.parse(text);
      } catch {
        payload = { status: "error", error: text };
      }
    }

    if (!response.ok) {
      throw this.errorFromPayload(response.status, payload);
    }

    if (!options.skipLogicalError && isLogicalErrorPayload(payload)) {
      throw this.errorFromPayload(response.status, payload);
    }

    return payload;
  }

  private errorFromPayload(httpStatus: number, payload: unknown): ScenePortBridgeError {
    if (payload && typeof payload === "object") {
      const record = payload as Record<string, unknown>;
      const code = typeof record.code === "string" ? record.code : codeForStatus(httpStatus);
      const category = typeof record.category === "string" ? record.category : categoryForStatus(httpStatus);
      const message =
        typeof record.message === "string"
          ? record.message
          : typeof record.error === "string"
            ? record.error
            : `Unity bridge returned HTTP ${httpStatus}`;
      return bridgeError({
        code,
        category,
        retryable: typeof record.retryable === "boolean" ? record.retryable : httpStatus >= 500,
        retryAfterMs: typeof record.retryAfterMs === "number" ? record.retryAfterMs : undefined,
        message,
        remediation: typeof record.remediation === "string" ? record.remediation : undefined,
        details: typeof record.details === "object" && record.details !== null ? (record.details as Record<string, unknown>) : undefined,
        httpStatus,
      });
    }

    return bridgeError({
      code: codeForStatus(httpStatus),
      category: categoryForStatus(httpStatus),
      retryable: httpStatus >= 500,
      message: typeof payload === "string" && payload.length > 0 ? payload : `Unity bridge returned HTTP ${httpStatus}`,
      httpStatus,
    });
  }
}

function isLogicalErrorPayload(payload: unknown): boolean {
  return Boolean(payload && typeof payload === "object" && (payload as Record<string, unknown>).status === "error");
}

function codeForStatus(status: number): string {
  if (status === 400) {
    return "request.invalid";
  }
  if (status === 401) {
    return "bridge.unauthorized";
  }
  if (status === 404) {
    return "capability.unsupported";
  }
  if (status === 503) {
    return "editor.busy";
  }
  return status >= 500 ? "bridge.error" : "request.failed";
}

function categoryForStatus(status: number): string {
  if (status === 400) {
    return "request";
  }
  if (status === 401 || status === 403) {
    return "auth";
  }
  if (status === 404) {
    return "capability";
  }
  if (status === 503) {
    return "editor";
  }
  return "bridge";
}
