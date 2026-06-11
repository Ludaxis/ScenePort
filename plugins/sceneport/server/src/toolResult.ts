import { isScenePortBridgeError } from "./bridgeError.js";

export function jsonResult(payload: unknown) {
  return {
    content: [
      {
        type: "text" as const,
        text: JSON.stringify(payload, null, 2),
      },
    ],
    structuredContent: toStructuredContent(payload),
  };
}

export function errorResult(error: unknown) {
  const message = error instanceof Error ? error.message : String(error);
  const result: {
    isError: true;
    content: Array<{ type: "text"; text: string }>;
    structuredContent?: Record<string, unknown>;
  } = {
    isError: true as const,
    content: [
      {
        type: "text" as const,
        text: message,
      },
    ],
  };

  if (isScenePortBridgeError(error)) {
    result.structuredContent = {
      status: "error",
      error: error.toJSON(),
    };
  }

  return result;
}

function toStructuredContent(payload: unknown): Record<string, unknown> {
  if (payload !== null && typeof payload === "object" && !Array.isArray(payload)) {
    return payload as Record<string, unknown>;
  }

  return { value: payload };
}
