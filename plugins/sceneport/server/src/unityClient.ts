export class UnityBridgeClient {
  private readonly baseUrl: string;

  constructor(baseUrl = process.env.SCENEPORT_UNITY_URL ?? "http://127.0.0.1:38987") {
    this.baseUrl = baseUrl.replace(/\/$/, "");
  }

  async get(path: string, params: Record<string, string | number | boolean | undefined> = {}) {
    const url = this.url(path, params);
    const response = await fetch(url, {
      method: "GET",
      headers: { Accept: "application/json" },
    });
    return this.parse(response);
  }

  async post(path: string, body: unknown) {
    const response = await fetch(this.url(path), {
      method: "POST",
      headers: {
        Accept: "application/json",
        "Content-Type": "application/json",
      },
      body: JSON.stringify(body),
    });
    return this.parse(response);
  }

  private url(path: string, params: Record<string, string | number | boolean | undefined> = {}) {
    const url = new URL(path, this.baseUrl);
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
