import { describe, it, expect } from "vitest";
import { jsonResult, errorResult } from "../../src/toolResult.js";

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

describe("errorResult", () => {
  it("marks the result as an error and uses the Error message", () => {
    const result = errorResult(new Error("boom"));
    expect(result.isError).toBe(true);
    expect(result.content[0].text).toBe("boom");
  });

  it("stringifies non-Error values", () => {
    expect(errorResult("raw").content[0].text).toBe("raw");
  });

  it("has no structuredContent", () => {
    expect("structuredContent" in errorResult(new Error("x"))).toBe(false);
  });
});
