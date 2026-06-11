import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { buildDoctorReport, formatDoctorReport } from "../../src/doctor.js";
import { FakeBridge } from "../fixtures/fakeBridge.js";

describe("doctor", () => {
  it("reports a reachable bridge with token and identity", async () => {
    const root = mkdtempSync(join(tmpdir(), "sceneport-doctor-"));
    const bridge = new FakeBridge({
      "/health": () => ({
        body: {
          status: "ok",
          bridge: "sceneport",
          port: 38987,
          projectId: "abc",
          projectPath: root,
          projectName: "Game",
          tokenRequired: true,
        },
      }),
      "/capabilities": () => ({ body: { status: "ok", bridge: "sceneport", protocolVersion: 1, capabilitiesHash: "hash" } }),
    });
    await bridge.start();
    try {
      mkdirSync(join(root, "Assets"), { recursive: true });
      mkdirSync(join(root, "ProjectSettings"), { recursive: true });
      mkdirSync(join(root, "Library", "ScenePort"), { recursive: true });
      writeFileSync(
        join(root, "Library", "ScenePort", "bridge.json"),
        JSON.stringify({
          schemaVersion: 2,
          url: bridge.url,
          token: "tok",
          projectPath: root,
          projectId: "abc",
          processId: process.pid,
          editorRole: "editor",
          ownerLeaseId: "lease",
          heartbeatUtc: new Date().toISOString(),
        }),
      );
      const report = await buildDoctorReport(
        {
          SCENEPORT_UNITY_URL: bridge.url,
          SCENEPORT_PROJECT_PATH: root,
          SCENEPORT_TOKEN: "tok",
        },
        root,
      );
      expect(report.status).toBe("ok");
      expect(report.checks.find((item) => item.name === "bridge")?.status).toBe("pass");
      expect(formatDoctorReport(report)).toContain("ScenePort doctor");
    } finally {
      await bridge.stop();
      rmSync(root, { recursive: true, force: true });
    }
  });

  it("fails when the bridge is unreachable", async () => {
    const report = await buildDoctorReport({ SCENEPORT_UNITY_URL: "http://127.0.0.1:1" }, "/tmp");
    expect(report.status).toBe("error");
    expect(report.checks.find((item) => item.name === "bridge")?.status).toBe("fail");
  });

  it("diagnoses stale v2 discovery owner metadata", async () => {
    const root = mkdtempSync(join(tmpdir(), "sceneport-doctor-"));
    try {
      mkdirSync(join(root, "Assets"), { recursive: true });
      mkdirSync(join(root, "ProjectSettings"), { recursive: true });
      mkdirSync(join(root, "Library", "ScenePort"), { recursive: true });
      writeFileSync(
        join(root, "Library", "ScenePort", "bridge.json"),
        JSON.stringify({
          schemaVersion: 2,
          url: "http://127.0.0.1:1",
          projectPath: root,
          projectId: "abc",
          processId: 999_999,
          editorRole: "editor",
          ownerLeaseId: "lease",
          heartbeatUtc: new Date(Date.now() - 120_000).toISOString(),
        }),
      );

      const report = await buildDoctorReport({ SCENEPORT_PROJECT_PATH: root }, root);
      expect(report.status).toBe("error");
      expect(report.checks.find((item) => item.name === "owner-heartbeat")?.status).toBe("fail");
      expect(report.checks.find((item) => item.name === "owner-process")?.status).toBe("fail");
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
});
