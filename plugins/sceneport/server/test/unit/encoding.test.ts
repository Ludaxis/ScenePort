import { describe, expect, it } from "vitest";
import { encodeSerializedValue, joinCsv, serializedValueSchema } from "../../src/encoding.js";

describe("encodeSerializedValue", () => {
  it("encodes strings (including empty)", () => {
    expect(encodeSerializedValue("hello")).toEqual({ valueKind: "string", stringValue: "hello" });
    expect(encodeSerializedValue("")).toEqual({ valueKind: "string", stringValue: "" });
  });

  it("encodes numbers (including zero and negatives)", () => {
    expect(encodeSerializedValue(42)).toEqual({ valueKind: "number", numberValue: 42 });
    expect(encodeSerializedValue(0)).toEqual({ valueKind: "number", numberValue: 0 });
    expect(encodeSerializedValue(-1.5)).toEqual({ valueKind: "number", numberValue: -1.5 });
  });

  it("encodes booleans (including false)", () => {
    expect(encodeSerializedValue(true)).toEqual({ valueKind: "bool", boolValue: true });
    expect(encodeSerializedValue(false)).toEqual({ valueKind: "bool", boolValue: false });
  });

  it("encodes colors and injects default alpha", () => {
    expect(encodeSerializedValue({ r: 1, g: 0, b: 0 })).toEqual({
      valueKind: "color",
      colorValue: { a: 1, r: 1, g: 0, b: 0 },
    });
  });

  it("preserves explicit alpha", () => {
    expect(encodeSerializedValue({ r: 1, g: 0, b: 0, a: 0.5 })).toEqual({
      valueKind: "color",
      colorValue: { a: 0.5, r: 1, g: 0, b: 0 },
    });
  });

  it("disambiguates vectors by w then z", () => {
    expect(encodeSerializedValue({ x: 1, y: 2, z: 3, w: 4 })).toEqual({
      valueKind: "vector4",
      vector4Value: { x: 1, y: 2, z: 3, w: 4 },
    });
    expect(encodeSerializedValue({ x: 1, y: 2, z: 3 })).toEqual({
      valueKind: "vector3",
      vector3Value: { x: 1, y: 2, z: 3 },
    });
    expect(encodeSerializedValue({ x: 1, y: 2 })).toEqual({
      valueKind: "vector2",
      vector2Value: { x: 1, y: 2 },
    });
  });

  it("treats a color as color, not a vector (r checked before w/z)", () => {
    const encoded = encodeSerializedValue({ r: 1, g: 2, b: 3 });
    expect(encoded.valueKind).toBe("color");
  });
});

describe("joinCsv", () => {
  it("returns undefined for empty or missing input", () => {
    expect(joinCsv(undefined)).toBeUndefined();
    expect(joinCsv([])).toBeUndefined();
  });

  it("joins with commas", () => {
    expect(joinCsv(["a", "b"])).toBe("a,b");
  });
});

describe("serializedValueSchema", () => {
  it("rejects a malformed vector", () => {
    expect(serializedValueSchema.safeParse({ x: 1 }).success).toBe(false);
  });

  it("accepts a color without alpha", () => {
    expect(serializedValueSchema.safeParse({ r: 1, g: 0, b: 0 }).success).toBe(true);
  });
});
