# Create a mesh and put it in the scene

Goal: have the agent generate geometry as a real Mesh asset, drop it onto a GameObject, and
visually confirm the result — all reversible and audited.

## Steps

1. **Make a folder** for the generated assets:

   ```text
   unity_create_folder { "path": "Assets/Generated", "dryRun": false }
   ```

2. **Create the mesh.** Either a built-in primitive (scaled), or fully procedural geometry.

   Primitive:

   ```text
   unity_create_primitive_mesh {
     "path": "Assets/Generated/Platform.asset",
     "shape": "box",
     "size": { "x": 4, "y": 0.5, "z": 4 },
     "dryRun": false
   }
   ```

   Procedural (a single triangle; indices are range-validated against the vertex list):

   ```text
   unity_create_procedural_mesh {
     "path": "Assets/Generated/Tri.asset",
     "vertices": [ {"x":0,"y":0,"z":0}, {"x":1,"y":0,"z":0}, {"x":0,"y":1,"z":0} ],
     "triangles": [0, 1, 2],
     "dryRun": false
   }
   ```

   Omit `normals` to have them recalculated; omit `uv` to skip texture coordinates.

3. **Put it in the scene.** Create a host GameObject, then assign the mesh (this adds a
   `MeshFilter` + `MeshRenderer` if missing — Undo-backed):

   ```text
   unity_create_game_object { "name": "Platform" }
   unity_assign_mesh {
     "instanceId": <id from create>,
     "meshPath": "Assets/Generated/Platform.asset",
     "materialPath": "Assets/Generated/Platform.mat",
     "dryRun": false
   }
   ```

4. **See it.** Capture the Game or Scene view to confirm the geometry rendered:

   ```text
   unity_capture_scene_view {}
   ```

## Notes

- Every step supports `dryRun` (the default is `true` in the tool schema) — preview the change
  set before committing.
- Mesh tools live in the `mesh` capability group, denied by the `team-safe`/`playtest`/`read-only`
  policy profiles.
- To author many objects atomically, batch `createPrimitiveMesh` / `createProceduralMesh` /
  `assignMesh` ops through `unity_authoring_batch` — a failure mid-batch rolls back created assets.
