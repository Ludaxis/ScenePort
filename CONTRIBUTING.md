# Contributing To ScenePort

Thanks for helping make Unity agent workflows safer and better.

## Development Setup

1. Install Node.js 18 or newer.
2. Install Unity 2022.3 LTS or newer.
3. Add `plugins/sceneport/unity-package/package.json` to a Unity project.
4. Build and test the MCP server:

   ```bash
   cd plugins/sceneport/server
   npm install
   npm run build
   npm test
   ```

5. Run the Unity EditMode tests via the harness:

   ```bash
   "/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents/MacOS/Unity" \
     -runTests -batchmode -projectPath TestProjects/BridgeHarness \
     -testPlatform EditMode -testResults results.xml
   ```

## Contribution Rules

- Read tools need bounded output.
- Write tools need Undo support.
- Destructive tools need explicit confirmation.
- Do not add arbitrary code execution to the default toolset.
- Keep tool schemas small, typed, and well described.
- Add QA coverage (vitest for the server, EditMode tests for the bridge) for every feature.

## Versioning

The version lives in one place: `plugins/sceneport/server/package.json`. To bump it:

```bash
# edit the version in plugins/sceneport/server/package.json, then:
node scripts/sync-versions.mjs   # propagates to the Unity package, plugin manifests, marketplace, and src/version.ts
cd plugins/sceneport/server && npm run build   # rebuild the committed bundle
```

CI runs `node scripts/sync-versions.mjs --check` and fails if anything is out of sync.
The Unity bridge reports its version from the package manifest at runtime, so it needs no manual edit.

## Pull Request Checklist

- Product behavior is documented.
- Security impact is described.
- QA path is included.
- Manifests remain valid JSON.
- Unity package version and changelog are updated for releases.
