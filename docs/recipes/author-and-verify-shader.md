# Author a shader and verify it compiles

Goal: write a `.shader` (ShaderLab) asset, then prove it compiled before using it — the
verified-authoring loop that distinguishes ScenePort from "write the file and hope."

## Steps

1. **Create the shader.** Provide full ShaderLab `content`, or scaffold from a template
   (`urpUnlit` for URP projects, `unlit` for Built-in):

   ```text
   unity_create_shader {
     "path": "Assets/Shaders/Flat.shader",
     "template": "urpUnlit",
     "shaderName": "ScenePort/Flat",
     "dryRun": false
   }
   ```

2. **Wait for the import + compile to settle:**

   ```text
   unity_wait_for_idle { "timeoutMs": 30000 }
   ```

3. **Check for compile errors.** A clean result means the shader is valid; otherwise the
   structured messages tell the agent exactly what to fix and re-author:

   ```text
   unity_get_compile_errors {}
   ```

4. **Use it.** Create a material bound to the new shader, then assign it:

   ```text
   unity_create_material {
     "path": "Assets/Materials/Flat.mat",
     "shaderName": "ScenePort/Flat",
     "color": { "r": 0.2, "g": 0.6, "b": 1 },
     "dryRun": false
   }
   ```

## Notes

- ScenePort intentionally does **not** execute arbitrary shader/C# code — it writes the asset and
  lets Unity's own compiler be the source of truth, surfaced through `unity_get_compile_errors`.
- Node-level **Shader Graph** authoring is a separate, version-gated preview capability (later
  phase); `unity_create_shader` covers hand-written ShaderLab today.
