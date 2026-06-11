export interface ScenePortErrorShape {
  code: string;
  category: string;
  retryable: boolean;
  retryAfterMs?: number;
  message: string;
  remediation?: string;
  details?: Record<string, unknown>;
  httpStatus?: number;
}

export class ScenePortBridgeError extends Error {
  readonly code: string;
  readonly category: string;
  readonly retryable: boolean;
  readonly retryAfterMs?: number;
  readonly remediation?: string;
  readonly details?: Record<string, unknown>;
  readonly httpStatus?: number;

  constructor(shape: ScenePortErrorShape) {
    super(shape.message);
    this.name = "ScenePortBridgeError";
    this.code = shape.code;
    this.category = shape.category;
    this.retryable = shape.retryable;
    this.retryAfterMs = shape.retryAfterMs;
    this.remediation = shape.remediation;
    this.details = shape.details;
    this.httpStatus = shape.httpStatus;
  }

  toJSON(): ScenePortErrorShape {
    return {
      code: this.code,
      category: this.category,
      retryable: this.retryable,
      retryAfterMs: this.retryAfterMs,
      message: this.message,
      remediation: this.remediation,
      details: this.details,
      httpStatus: this.httpStatus,
    };
  }
}

export function isScenePortBridgeError(error: unknown): error is ScenePortBridgeError {
  return error instanceof ScenePortBridgeError;
}

export function bridgeError(shape: Partial<ScenePortErrorShape> & { message: string }): ScenePortBridgeError {
  return new ScenePortBridgeError({
    code: shape.code ?? "bridge.error",
    category: shape.category ?? "bridge",
    retryable: shape.retryable ?? false,
    retryAfterMs: shape.retryAfterMs,
    message: shape.message,
    remediation: shape.remediation,
    details: shape.details,
    httpStatus: shape.httpStatus,
  });
}
