# Contributing To ScenePort

Thanks for helping make Unity agent workflows safer and better.

## Development Setup

1. Install Node.js 18 or newer.
2. Install Unity 2022.3 LTS or newer.
3. Add `plugins/sceneport/unity-package/package.json` to a Unity project.
4. Build the MCP server:

   ```bash
   cd plugins/sceneport/server
   npm install
   npm run build
   ```

## Contribution Rules

- Read tools need bounded output.
- Write tools need Undo support.
- Destructive tools need explicit confirmation.
- Do not add arbitrary code execution to the default toolset.
- Keep tool schemas small, typed, and well described.
- Add QA coverage or a manual test script for every new feature.

## Pull Request Checklist

- Product behavior is documented.
- Security impact is described.
- QA path is included.
- Manifests remain valid JSON.
- Unity package version and changelog are updated for releases.
