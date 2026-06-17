# Trust & Safety

ScenePort lets AI agents change a live Unity project, so the defaults are built so a mistaken
or surprising edit is **visible, bounded, and reversible**. This page documents the
guarantees reviewers asked us to communicate more clearly. For the full network/auth model,
see the [Security Model](SECURITY_MODEL.md).

## Undo and rollback

- **Editor mutations use Unity Undo.** Component creation, transform changes, and
  serialized-property writes are registered with `Undo`, so a single **Edit > Undo**
  (`Ctrl/Cmd+Z`) reverses an agent's last change exactly as if you had made it by hand.
- **`unity_authoring_batch` is transactional for created assets.** If a batch fails partway
  through, the assets it created during that batch are **rolled back** (removed), so a failed
  batch does not leave half-created scripts/materials/prefabs behind. Validate first with
  `unity_validate_authoring_write` (or `dryRun: true`) before committing a batch.
- **Assets-only, no deletes.** Authoring writes target paths under `Assets/`, reject path
  traversal, and will not overwrite an existing asset. The bridge does **not** delete assets
  or scenes and does not expose arbitrary code execution.

## Destructive-operation confirmation

- ScenePort deliberately ships **no** destructive primitives (no asset/scene delete, no
  arbitrary code execution) as default capabilities.
- For any operation that is hard to reverse — or that an agent proposes outside the typed,
  Undo-backed authoring surface — the expectation is an **explicit human confirmation**
  before it runs. Treat authoring as dry-run first; review the validated diff; then commit.
- Menu execution is **exact-match allowlist only** (`unity_menu_item_allowlist` /
  `unity_execute_menu_item`): an agent can only run menu items you have allowlisted, never an
  arbitrary editor command.

## Audit log and retention

- Every **mutating** ScenePort request is recorded to a **bounded local audit log** at
  `Library/ScenePort/audit.json`.
- The log is **bounded to roughly the most recent ~200 entries** — it is a recent-activity
  trail for "what did the agent just do," not long-term storage. Older entries roll off as
  new ones arrive.
- The log is **local only** — it stays inside your project's `Library/` folder and is never
  sent off the machine. Read it from an agent via `unity_audit_log` or
  `sceneport://audit/log`.

## Policy scoping

The active **policy profile** scopes which endpoint groups an agent may call. A blocked group
returns `capability.denied`; widen the policy only after team approval. See
[Security Operations](../playbooks/SECURITY_OPERATIONS.md) and
[Team Adoption](../playbooks/TEAM_ADOPTION.md).

## Quick reference

| Concern | Guarantee |
| --- | --- |
| Reversing an edit | Unity Undo on every editor mutation. |
| Failed authoring batch | Created assets rolled back. |
| Deleting assets/scenes | Not supported. |
| Arbitrary code execution | Not a default capability. |
| Menu commands | Exact-match allowlist only. |
| Mutation history | Bounded local audit log (~200 entries, `Library/ScenePort/audit.json`). |
| Network exposure | Loopback-only, per-project token; see Security Model. |
