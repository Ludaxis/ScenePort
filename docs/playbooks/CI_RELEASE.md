# ScenePort CI And Release Playbook

## Required PR Gates

- `npm run lint`
- `npm run typecheck`
- `npm run test:coverage`
- `npm run build`
- `node scripts/sync-versions.mjs --check`
- `node scripts/check-trust-contract.mjs`
- Unity EditMode tests with `UNITY_LICENSE`

## Release Evidence

Every tag must produce `RELEASE_EVIDENCE.md` with versions, protocol, capability hash, Unity version, CI run, required gates, demo evidence, known risks, and rollback notes.

## Rollback

1. Revert the release tag.
2. Pin clients to the previous plugin/server version.
3. Reinstall the previous UPM tarball.
4. Delete `Library/ScenePort/bridge.json` only if rediscovery is needed.
