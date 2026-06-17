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

// Roughly ~5MB of decoded image once base64 is expanded (base64 is ~4/3 the byte size).
const MAX_BASE64_LENGTH = 7_000_000;

// Fields that hold the large base64 blob; stripped from the text/structured metadata.
const BASE64_FIELDS = ["imageBase64", "data"];

export function imageResult(payload: unknown, base64Png: string, mimeType = "image/png") {
  const metadata = stripBase64(payload);

  if (typeof base64Png !== "string" || base64Png.length === 0 || base64Png.length > MAX_BASE64_LENGTH) {
    const note =
      typeof base64Png === "string" && base64Png.length > MAX_BASE64_LENGTH
        ? "Inline image omitted because it exceeded the inline size limit; use the file path instead."
        : "Inline image was not available; use the file path instead.";
    const textOnly = withImageNote(metadata, note);
    return {
      content: [
        {
          type: "text" as const,
          text: JSON.stringify(textOnly, null, 2),
        },
      ],
      structuredContent: toStructuredContent(textOnly),
    };
  }

  return {
    content: [
      {
        type: "image" as const,
        data: base64Png,
        mimeType,
      },
      {
        type: "text" as const,
        text: JSON.stringify(metadata, null, 2),
      },
    ],
    structuredContent: toStructuredContent(metadata),
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

function stripBase64(payload: unknown): unknown {
  if (payload === null || typeof payload !== "object" || Array.isArray(payload)) {
    return payload;
  }

  const copy: Record<string, unknown> = { ...(payload as Record<string, unknown>) };
  for (const field of BASE64_FIELDS) {
    delete copy[field];
  }
  return copy;
}

function withImageNote(payload: unknown, note: string): Record<string, unknown> {
  const base = toStructuredContent(payload);
  return { ...base, imageNote: note };
}
