import { z } from "zod";

export const vector2Schema = z.object({ x: z.number(), y: z.number() });
export const vector3Schema = z.object({ x: z.number(), y: z.number(), z: z.number() });
export const vector4Schema = z.object({ x: z.number(), y: z.number(), z: z.number(), w: z.number() });
export const colorSchema = z.object({ r: z.number(), g: z.number(), b: z.number(), a: z.number().optional() });

export const objectLocatorSchema = {
  instanceId: z.number().int().optional().describe("Unity instance ID for a GameObject, Component, or asset."),
  path: z.string().min(1).max(1024).optional().describe("Hierarchy path, used when an instance ID is not available."),
};

export const serializedValueSchema = z.union([
  z.string(),
  z.number(),
  z.boolean(),
  vector2Schema,
  vector3Schema,
  vector4Schema,
  colorSchema,
]);

export function encodeSerializedValue(value: z.infer<typeof serializedValueSchema>) {
  if (typeof value === "string") {
    return { valueKind: "string", stringValue: value };
  }
  if (typeof value === "number") {
    return { valueKind: "number", numberValue: value };
  }
  if (typeof value === "boolean") {
    return { valueKind: "bool", boolValue: value };
  }
  if ("r" in value) {
    return { valueKind: "color", colorValue: { a: 1, ...value } };
  }
  if ("w" in value) {
    return { valueKind: "vector4", vector4Value: value };
  }
  if ("z" in value) {
    return { valueKind: "vector3", vector3Value: value };
  }
  return { valueKind: "vector2", vector2Value: value };
}

export function joinCsv(values: string[] | undefined) {
  return values && values.length > 0 ? values.join(",") : undefined;
}
