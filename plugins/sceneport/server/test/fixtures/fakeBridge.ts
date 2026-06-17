import { type IncomingMessage, type Server, type ServerResponse, createServer } from "node:http";
import type { AddressInfo } from "node:net";

export interface RecordedRequest {
  method: string;
  url: string;
  headers: Record<string, string | string[] | undefined>;
  body: unknown;
}

export interface RouteResponse {
  status?: number;
  body: unknown;
}

export type RouteHandler = (req: RecordedRequest) => RouteResponse;

export class FakeBridge {
  private server: Server;
  readonly requests: RecordedRequest[] = [];
  private routes: Record<string, RouteHandler>;
  private _port = 0;

  constructor(routes: Record<string, RouteHandler> = {}) {
    this.routes = routes;
    this.server = createServer((req, res) => this.handle(req, res));
  }

  async start(): Promise<void> {
    await new Promise<void>((resolve) => this.server.listen(0, "127.0.0.1", resolve));
    this._port = (this.server.address() as AddressInfo).port;
  }

  /** Bind to a specific port so a stopped bridge can be brought back on the same URL. */
  async startOnPort(port: number): Promise<void> {
    await new Promise<void>((resolve, reject) => {
      this.server.once("error", reject);
      this.server.listen(port, "127.0.0.1", () => {
        this.server.removeListener("error", reject);
        resolve();
      });
    });
    this._port = (this.server.address() as AddressInfo).port;
  }

  async stop(): Promise<void> {
    await new Promise<void>((resolve, reject) => {
      this.server.close((err: NodeJS.ErrnoException | undefined) => {
        if (err && err.code !== "ERR_SERVER_NOT_RUNNING") {
          reject(err);
          return;
        }
        resolve();
      });
      this.server.closeAllConnections?.();
    });
  }

  get port(): number {
    return this._port;
  }

  get url(): string {
    return `http://127.0.0.1:${this._port}`;
  }

  private handle(req: IncomingMessage, res: ServerResponse): void {
    const chunks: Buffer[] = [];
    req.on("data", (c) => chunks.push(c));
    req.on("end", () => {
      const raw = Buffer.concat(chunks).toString("utf8");
      let body: unknown = raw;
      if (raw.length > 0) {
        try {
          body = JSON.parse(raw);
        } catch {
          body = raw;
        }
      }

      const recorded: RecordedRequest = { method: req.method ?? "", url: req.url ?? "", headers: req.headers, body };
      this.requests.push(recorded);

      const path = (req.url ?? "").split("?")[0];
      const handler = this.routes[path];
      if (!handler) {
        res.writeHead(404, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ status: "error", error: `Unknown endpoint: ${path}` }));
        return;
      }

      const result = handler(recorded);
      res.writeHead(result.status ?? 200, { "Content-Type": "application/json" });
      res.end(typeof result.body === "string" ? result.body : JSON.stringify(result.body));
    });
  }
}
