# `sceneport:create-ui-from-screenshot`

Hand the agent a reference image of a UI; it recreates the layout as real Unity UI objects
(`Canvas`, `RectTransform`, `Image`, `Text`/`TextMeshPro`), then captures the Game view to
compare its result against your mockup.

## When to use it

- You have a Figma export, a screenshot, or a hand sketch and want a first-pass Unity UI.
- You want to scaffold a HUD or menu quickly and refine it by hand afterward.
- You want the agent to iterate against a visual target it can actually see.

## Tools it drives

- `unity_scene_hierarchy` / `unity_query_scene` — find or create the target Canvas.
- `unity_create_game_object`, `unity_add_component`, `unity_set_transform`,
  `unity_set_serialized_property` — build the UI tree (panels, text, images, anchors).
- `unity_authoring_batch` — apply grouped edits as one dry-run-first, Undo-able transaction.
- `unity_capture_game_view` — capture the rendered UI and visually compare to the screenshot.

## Example transcript

> **You:** Use `sceneport:create-ui-from-screenshot`. Here's a screenshot of a pause menu —
> rebuild it on my UI canvas. [attach image]

1. The agent calls `unity_scene_hierarchy`, finds an existing `Canvas`, and decides to nest
   the menu under it.
2. It reads the screenshot: a centered dark panel, a "Paused" title, and three stacked
   buttons (Resume / Settings / Quit).
3. It composes an `unity_authoring_batch` (dry-run first) that creates a panel
   `RectTransform`, an `Image` background, a title `Text`, and three button objects with
   labels and anchored layout.
4. It runs the batch for real; edits are Undo-backed.
5. It calls `unity_capture_game_view` and **sees** its own result rendered in Unity.
6. It compares the capture to your screenshot, notices the buttons are too wide, and issues a
   follow-up `unity_set_serialized_property` to fix the widths — then re-captures.

> **Result:** A real, editable pause-menu hierarchy on your Canvas, visually matched to the
> mockup, with screenshots of each pass.

## Tips

- Tell the agent which Canvas / parent to build under, or it will pick one and tell you.
- Anchoring is the hard part — ask it to use anchor presets (corners/stretch) so the layout
  survives resolution changes.
- For pixel-faithful matching, follow up with
  [`sceneport:visual-regression`](visual-regression.md) using the screenshot region as the
  golden reference.
- It builds structure, not assets — supply sprites/fonts you want, or it uses defaults and
  placeholders you can swap.
