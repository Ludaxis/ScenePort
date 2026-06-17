import { describe, expect, it } from "vitest";
import { bridgeError } from "../../src/bridgeError.js";
import { errorResult, imageResult, jsonResult } from "../../src/toolResult.js";

// 1x1 transparent PNG.
const TINY_PNG = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M8AAAMBAQDJ/pLvAAAAAElFTkSuQmCC";

describe("jsonResult", () => {
  it("renders text content as pretty JSON", () => {
    const result = jsonResult({ a: 1 });
    expect(result.content[0].type).toBe("text");
    expect(result.content[0].text).toBe(JSON.stringify({ a: 1 }, null, 2));
  });

  it("exposes object payloads directly as structuredContent", () => {
    const result = jsonResult({ a: 1, b: 2 });
    expect(result.structuredContent).toEqual({ a: 1, b: 2 });
  });

  it("wraps arrays under value", () => {
    expect(jsonResult([1, 2, 3]).structuredContent).toEqual({ value: [1, 2, 3] });
  });

  it("wraps primitives under value", () => {
    expect(jsonResult("ok").structuredContent).toEqual({ value: "ok" });
    expect(jsonResult(5).structuredContent).toEqual({ value: 5 });
  });

  it("wraps null under value", () => {
    expect(jsonResult(null).structuredContent).toEqual({ value: null });
  });
});

describe("imageResult", () => {
  it("returns an image block followed by a text metadata block", () => {
    const result = imageResult({ path: "/tmp/x.png", width: 16, height: 16, imageBase64: TINY_PNG }, TINY_PNG);
    expect(result.content[0]).toEqual({ type: "image", data: TINY_PNG, mimeType: "image/png" });
    expect(result.content[1].type).toBe("text");
  });

  it("honors a custom mime type", () => {
    const result = imageResult({ path: "/tmp/x.jpg" }, TINY_PNG, "image/jpeg");
    expect(result.content[0]).toMatchObject({ type: "image", mimeType: "image/jpeg" });
  });

  it("strips the base64 blob from the text and structured metadata but keeps path/size", () => {
    const result = imageResult({ path: "/tmp/x.png", width: 16, height: 16, imageBase64: TINY_PNG }, TINY_PNG);
    const text = (result.content[1] as { text: string }).text;
    expect(text).not.toContain(TINY_PNG);
    expect(text).toContain("/tmp/x.png");
    expect(result.structuredContent).toEqual({ path: "/tmp/x.png", width: 16, height: 16 });
    expect(result.structuredContent).not.toHaveProperty("imageBase64");
  });

  it("falls back to text-only with a note when the image is missing", () => {
    const result = imageResult({ path: "/tmp/x.png" }, "");
    expect(result.content).toHaveLength(1);
    expect(result.content[0].type).toBe("text");
    expect(result.structuredContent).toMatchObject({ path: "/tmp/x.png", imageNote: expect.any(String) });
  });

  it("falls back to text-only when the image exceeds the size cap", () => {
    const huge = "A".repeat(7_000_001);
    const result = imageResult({ path: "/tmp/x.png", imageBase64: huge }, huge);
    expect(result.content).toHaveLength(1);
    expect(result.content[0].type).toBe("text");
    expect((result.content[0] as { text: string }).text).not.toContain(huge);
    expect(result.structuredContent.imageNote).toContain("size limit");
  });
});

describe("errorResult", () => {
  it("marks the result as an error and uses the Error message", () => {
    const result = errorResult(new Error("boom"));
    expect(result.isError).toBe(true);
    expect(result.content[0].text).toBe("boom");
  });

  it("stringifies non-Error values", () => {
    expect(errorResult("raw").content[0].text).toBe("raw");
  });

  it("has no structuredContent for unknown errors", () => {
    expect("structuredContent" in errorResult(new Error("x"))).toBe(false);
  });

  it("exposes ScenePort errors as structuredContent", () => {
    const result = errorResult(
      bridgeError({
        code: "editor.busy.compiling",
        category: "editor",
        retryable: true,
        retryAfterMs: 1000,
        message: "Unity is compiling.",
      }),
    );
    expect(result.structuredContent).toEqual({
      status: "error",
      error: expect.objectContaining({
        code: "editor.busy.compiling",
        category: "editor",
        retryable: true,
        retryAfterMs: 1000,
        message: "Unity is compiling.",
      }),
    });
  });
});
