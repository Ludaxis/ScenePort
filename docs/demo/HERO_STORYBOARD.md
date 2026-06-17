# ScenePort Hero Video Storyboard

A 45-second shot list for the hero clip at the top of `README.md`. Goal: show a complete
"agent that can see Unity" moment — open Unity, one-click connect, ask the agent to fix
something, watch the agent **see** the Game view, done.

Record once, export both formats, and drop them at:

- `docs/media/hero.gif` (used by the README and docs landing page)
- `docs/media/hero.mp4` (MP4 fallback, referenced in a commented block in the README)

## Shot list (target 45s)

| Time | On-screen action | Voiceover / caption |
| --- | --- | --- |
| 0:00–0:04 | Unity Editor open on a small game scene. Title card fades: "ScenePort". | "Your AI agent can edit Unity files — but it can't *see* Unity." |
| 0:04–0:10 | Open `Tools > ScenePort > Setup`. The window shows discovery + token state. Click **Connect**; status turns green. | "One click connects the editor to your agent." |
| 0:10–0:16 | Cut to the MCP client (Claude Code / Codex). Type: "Use ScenePort to fix the errors in my scene." | "Now ask it to fix what's broken." |
| 0:16–0:24 | Agent calls `unity_console_logs`, shows a `MissingReferenceException`, then a small `unity_authoring_batch` edit. Unity recompiles; the console clears. | "It reads the console, makes one reversible fix, and re-checks." |
| 0:24–0:32 | Agent calls `unity_capture_game_view`; the returned screenshot appears inline in the chat. | "And it can actually *see* the result — a real screenshot, in the model's eyes." |
| 0:32–0:40 | Second ask: "Build this pause menu" with a screenshot attached. Agent assembles UI; Game view capture matches the mockup. | "Hand it a screenshot; it rebuilds the UI and checks its own work." |
| 0:40–0:45 | Cut back to a clean console + rendered Game view. End card: "ScenePort — the safe port into the Unity Editor." plus repo URL. | "Stop flying blind. ScenePort." |

## Recording tips

- **Resolution:** record at 1920×1080 (or 2× Retina downscaled to 1080p). Keep the Unity Game
  view at 16:9 so feature stills crop cleanly.
- **Frame rate:** 30 fps is plenty; trim dead air between agent steps so the 45s budget holds.
- **Tool:** [Kap](https://getkap.co) (free, exports GIF + MP4) or QuickTime screen recording
  on macOS; OBS works cross-platform.
- **Cursor / zoom:** enable cursor highlighting; zoom into the `Tools > ScenePort > Setup`
  window and the inline screenshot moment — those are the "wow" beats.
- **Captions:** burn in short captions (the right column above) so the clip reads without
  audio, since the README autoplays the GIF muted.

## Export

- **GIF:** ≤ 12 MB if possible (GitHub will still serve larger, but it slows the README). 860px
  wide matches the README `<img width="860">`. Consider trimming color depth.
- **MP4:** H.264, muted, ~860px wide. Smaller and smoother than the GIF; the README has a
  commented `<video>` block ready to enable once it exists.
- Save both into `docs/media/`. No README edit is needed — the `<img src="docs/media/hero.gif">`
  slot picks up the GIF automatically; uncomment the `<video>` block to prefer the MP4.
