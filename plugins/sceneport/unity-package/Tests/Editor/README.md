# Editor Tests

EditMode test coverage for the ScenePort bridge:

- **ScenePortJsonTests** — non-finite floats serialize as null, control characters and
  unicode round-trip, null-handling rules, malformed body tolerance.
- **ScenePortRequestTests** — body-over-query precedence, exponent-notation parsing
  (the original regex dropped these), nested braces / keys-inside-strings, presence
  detection, query decoding, CSV splitting.
- **ScenePortRouterTests** — every documented endpoint is routed, path normalization,
  unknown-endpoint error envelope.
- **ScenePortConsoleBufferTests** — bounded ring buffer, newest-first snapshots, type
  filtering, error filtering.
- **ComponentTypeCacheTests** — resolution by short/full name, case-insensitivity,
  negative caching, memoization.
- **SceneHandlerTests** — create + Undo, parenting, transform edits (incl. exponent
  values), set-serialized-property across float/int/bool/string/Color/enum, hierarchy
  pagination/truncation (incl. the exact-count regression), selection, path resolution.
- **ScenePortHttpIntegrationTests** — real HTTP round-trips through the auto-started
  bridge (`[UnityTest]` coroutines).

Run locally via the BridgeHarness project (see `TestProjects/BridgeHarness`):

```bash
"/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents/MacOS/Unity" \
  -runTests -batchmode -projectPath TestProjects/BridgeHarness \
  -testPlatform EditMode -testResults results.xml
```

Validated on Unity 2022.3 LTS and Unity 6.
