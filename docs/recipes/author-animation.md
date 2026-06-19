# Author animation

Create animation clips and animator controllers, wire up states and transitions, and bind the
controller to a scene object — all through ScenePort's safe, dry-run-first authoring path.

## Copy-paste prompt

```text
> Author animation with ScenePort. Create the AnimationClip(s) and AnimatorController I
> describe, add the states and transitions, then assign the controller to the right scene
> object. Validate each write as a dry run first.
```

## When to use it

- You want to scaffold an AnimatorController (idle / walk / run) with states and transitions.
- You need an AnimationClip baked from keyframes (e.g. a bobbing pickup, a door swing).
- You want to attach an existing controller to a GameObject's `Animator`.

## Tools it drives

- `unity_create_animation_clip` — create an AnimationClip `.asset`, optionally baking float
  curves from time/value keyframes (per child path + component type + property).
- `unity_create_animator_controller` — create an AnimatorController `.asset`, optionally with
  typed parameters (`float` / `int` / `bool` / `trigger`).
- `unity_add_animator_state` — add a state to the controller's first layer, optionally
  assigning a motion clip and marking it the default.
- `unity_add_animator_transition` — add a transition between two named states, with optional
  parameter conditions.
- `unity_assign_animator` — assign a RuntimeAnimatorController to a scene GameObject's
  `Animator` (adds the component if missing). Undo-backed.

## Example transcript

> **You:** Build a `Coin` controller that idles by spinning, and attach it to my selected coin.

1. The agent calls `unity_create_animation_clip` to bake a `Spin` clip from rotation
   keyframes, running a `dryRun` first to confirm the asset path is writable.
2. It calls `unity_create_animator_controller` to create `Coin.controller`.
3. It calls `unity_add_animator_state` to add a `Spin` state referencing the clip and mark it
   the default state.
4. It calls `unity_assign_animator` to attach the controller to the selected coin, adding an
   `Animator` if one is missing.
5. It reports the new asset paths and that the scene assignment is Undo-able.

> **Result:** `Coin.controller` with a default `Spin` state, attached to the coin in the
> scene.

## Tips

- Asset creation is rollback-tracked and dry-run aware (`dryRun` defaults to `true`); the agent
  previews the change set before writing.
- `unity_assign_animator` is the only scene-mutating step here — it is Undo-backed and audited.
- Add parameters when you create the controller, then reference them in
  `unity_add_animator_transition` conditions so the transition graph is consistent.
- Animation tools are denied under the `team-safe`, `playtest`, and `read-only` profiles, like
  `mesh` and `authoring`.
