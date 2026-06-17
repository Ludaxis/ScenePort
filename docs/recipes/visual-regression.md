# `sceneport:visual-regression`

Capture a known-good "golden" frame, make a change, then diff the current Game view against
the golden pixel-for-pixel. ScenePort returns a real diff image plus a `pixelDiffPercent`, so
the agent can judge whether a visual change was intended.

## When to use it

- You refactored UI, materials, lighting, or layout and want to confirm nothing else shifted.
- You want a repeatable before/after check around a risky edit.
- You are building a lightweight visual baseline for a scene or screen.

## Tools it drives

- `unity_capture_golden_frame` — capture and store the reference frame (returns an image
  block; `inline` defaults true, `maxEdge` defaults 1024).
- `unity_compare_golden_frame` — capture the current frame, diff against the golden, and
  return a per-pixel diff image plus `pixelDiffPercent`.
- `unity_capture_game_view` / `unity_capture_scene_view` — capture additional context views.
- `unity_set_serialized_property`, `unity_authoring_batch` — apply the change under test.

## Example transcript

> **You:** Use `sceneport:visual-regression`. I'm about to tweak the HUD layout — make sure
> nothing else moves.

1. The agent calls `unity_capture_golden_frame` on the current Game view and stores it as the
   baseline. It shows you the captured image.
2. You (or the agent) apply the HUD change via `unity_set_serialized_property` /
   `unity_authoring_batch`.
3. The agent calls `unity_compare_golden_frame`. The response includes:
   - a per-pixel **diff image** (changed pixels highlighted), and
   - `pixelDiffPercent: 3.1`.
4. The agent inspects the diff image: the changed pixels are all inside the HUD region, none
   elsewhere. It reports the change as expected.
5. If the diff had lit up unrelated areas (say a shadow shifted across the whole frame), the
   agent would flag it and offer to revert.

> **Result:** A diff image and a `3.1%` change confined to the HUD — confidence the edit was
> surgical.

## Tips

- Re-capture the golden frame intentionally after an approved visual change, or the next run
  will diff against the old baseline.
- Keep the editor framing stable (same Game view resolution / aspect) between golden capture
  and compare, or the diff will be dominated by reframing noise.
- A low `pixelDiffPercent` with diff pixels in the wrong region is still a regression — read
  *where* the pixels changed, not just how many.
- Use `maxEdge` to trade detail for speed on large captures; keep it consistent across golden
  and compare.
