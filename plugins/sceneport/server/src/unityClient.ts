import { type BridgeTarget, discoverBridge, projectPathsEqual } from "./discovery.js";

const TOKEN_HEADER = "X-ScenePort-Token";

export interface StatusReport extends Record<string, unknown> {
  discoverySource: string;
  discoveryFilePath: string | null;
  tokenConfigured: boolean;
  identityMatch: boolean | null;
}

export class UnityBridgeClient {
  private target: BridgeTarget;
  private readonly expectedProjectPath?: string;
  private identityResolved = false;
  private identityError: string | null = null;

  constructor(target?: BridgeTarget | string, env: NodeJS.ProcessEnv = process.env) {
    if (typeof target === "string") {
      // Legacy: bare base URL. Still pick up a token/identity from discovery if present.
      const discovered = discoverBridge(env);
      this.target = { ...discovered, baseUrl: target.replace(/\/$/, ""), source: "env-url" };
    } else {
      this.target = target ?? discoverBridge(env);
    }
    this.expectedProjectPath = env.SCENEPORT_PROJECT_PATH;
  }

  get baseUrl(): string {
    return this.target.baseUrl;
  }

  async get(path: string, params: Record<string, string | number | boolean | undefined> = {}) {
    await this.guardIdentity();
    return this.request("GET", path, params);
  }

  async post(path: string, body: unknown) {
    await this.guardIdentity();
    return this.request("POST", path, undefined, body);
  }

  /**
   * Health plus discovery/identity metadata for unity_status. Never throws on identity
   * mismatch — it reports it, so the tool can be used to diagnose a wrong-project connection.
   */
  async statusReport(): Promise<StatusReport> {
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

    return {
      ...health,
      ...(error ? { status: "error", error } : {}),
      discoverySource: this.target.source,
      discoveryFilePath: this.target.discoveryFilePath ?? null,
      tokenConfigured: Boolean(this.target.token),
      identityMatch,
      ...(legacyBridge
        ? { warning: "Unity bridge is outdated (pre-0.3); update the ScenePort UPM package to enable auth and discovery." }
        : {}),
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
      throw new Error(this.identityError);
    }
  }

  private async request(
    method: "GET" | "POST",
    path: string,
    params: Record<string, string | number | boolean | undefined> = {},
    body?: unknown,
    isRetry = false,
  ): Promise<unknown> {
    const url = this.url(path, params);
    const headers: Record<string, string> = { Accept: "application/json" };
    if (this.target.token) {
      headers[TOKEN_HEADER] = this.target.token;
    }

    let response: Response;
    try {
      response =
        method === "GET"
          ? await fetch(url, { method, headers })
          : await fetch(url, {
              method,
              headers: { ...headers, "Content-Type": "application/json" },
              body: JSON.stringify(body),
            });
    } catch (error) {
      // Bridge unreachable (e.g. Unity restarted on a new port). Rediscover once and retry.
      if (!isRetry && this.target.source !== "env-url" && this.rediscover()) {
        return this.request(method, path, params, body, true);
      }
      throw error;
    }

    // Token may have been regenerated (user deleted Library). Rediscover once and retry.
    if (response.status === 401 && !isRetry && this.rediscover()) {
      return this.request(method, path, params, body, true);
    }

    return this.parse(response);
  }

  private rediscover(): boolean {
    const next = discoverBridge();
    const changed = next.baseUrl !== this.target.baseUrl || next.token !== this.target.token;
    this.target = next;
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

  private async parse(response: Response) {
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
      const message =
        typeof payload === "object" && payload && "error" in payload
          ? String((payload as { error: unknown }).error)
          : `Unity bridge returned HTTP ${response.status}`;
      throw new Error(message);
    }

    return payload;
  }
}
