using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// PREVIEW (scaffold) ShaderGraph authoring. ScenePort has NO compile-time dependency on
    /// com.unity.shadergraph; a .shadergraph file is authored as JSON TEXT and imported. Because
    /// the on-disk JSON schema is internal to ShaderGraph and version-fragile, this op is gated to
    /// the full-safe-local policy (single-developer local trust) and round-trip validates the
    /// import: if Unity cannot load the asset back, the write is rolled back and reported as an
    /// unsupported capability for this Unity/ShaderGraph version. Follows the standard authoring
    /// contract (validate -> dry-run barrier -> mutate) reusing the AuthoringHandlers helpers.
    /// </summary>
    internal static class ShaderGraphPreviewHandlers
    {
        // Minimal known-good Unlit .shadergraph JSON. Authored/verified against ShaderGraph 14.x
        // (Unity 2022 LTS / Unity 6) graph-data format (m_SGVersion / JSONObject node list). This
        // is intentionally the smallest graph that imports as a Shader Graph asset; richer graphs
        // should be supplied via explicit `content`. Kept as a verbatim constant so the wire never
        // depends on the shadergraph package being installed at compile time.
        // VERSION NOTE: if a future ShaderGraph bumps its serialization, this template may fail the
        // round-trip validation below — that is the expected, safe failure mode (rollback + report).
        private const string MinimalUnlitTemplate =
            "{\n" +
            "    \"m_SGVersion\": 3,\n" +
            "    \"m_Type\": \"UnityEditor.ShaderGraph.GraphData\",\n" +
            "    \"m_ObjectId\": \"00000000000000000000000000000001\",\n" +
            "    \"m_Properties\": [],\n" +
            "    \"m_Keywords\": [],\n" +
            "    \"m_Dropdowns\": [],\n" +
            "    \"m_CategoryData\": [],\n" +
            "    \"m_Nodes\": [],\n" +
            "    \"m_GroupDatas\": [],\n" +
            "    \"m_StickyNoteDatas\": [],\n" +
            "    \"m_Edges\": [],\n" +
            "    \"m_VertexContext\": {\n" +
            "        \"m_Position\": { \"x\": 0.0, \"y\": 0.0 },\n" +
            "        \"m_Blocks\": []\n" +
            "    },\n" +
            "    \"m_FragmentContext\": {\n" +
            "        \"m_Position\": { \"x\": 0.0, \"y\": 200.0 },\n" +
            "        \"m_Blocks\": []\n" +
            "    },\n" +
            "    \"m_PreviewData\": {\n" +
            "        \"serializedMesh\": { \"m_SerializedMesh\": \"{\\\"mesh\\\":{\\\"instanceID\\\":0}}\", \"m_Guid\": \"\" },\n" +
            "        \"preventRotation\": false\n" +
            "    },\n" +
            "    \"m_Path\": \"Shader Graphs\",\n" +
            "    \"m_GraphPrecision\": 1,\n" +
            "    \"m_PreviewMode\": 2,\n" +
            "    \"m_OutputNode\": { \"m_Id\": \"\" },\n" +
            "    \"m_ActiveTargets\": []\n" +
            "}\n";

        internal static object CreateShaderGraph(ScenePortRequest req, ScenePortContext ctx)
        {
            var path = AuthoringHandlers.NormalizeAssetPath(req.ExtractString("path", req.GetString("path", null)));
            var dryRun = req.ExtractBool("dryRun", false);
            var error = AuthoringHandlers.ValidateAssetPath(path, ".shadergraph");
            if (error != null)
            {
                return error;
            }
            path = AuthoringHandlers.ResolveConflict(path, req);
            error = AuthoringHandlers.EnsureDoesNotExist(path);
            if (error != null)
            {
                return error;
            }

            var content = req.ExtractString("content", null);
            if (string.IsNullOrEmpty(content))
            {
                content = MinimalUnlitTemplate;
            }

            var response = new AuthoringResponse { DryRun = dryRun, Operation = "createShaderGraph" };
            response.Changes.Add(AuthoringHandlers.Change("asset", "create", path, false, true));
            if (dryRun)
            {
                response.Result = new { path };
                return response;
            }

            AuthoringHandlers.EnsureAssetFolder(Path.GetDirectoryName(path));
            File.WriteAllText(Path.Combine(ScenePortPaths.ProjectPath(), path), content);
            AssetDatabase.ImportAsset(path);

            // Round-trip validate: a .shadergraph only loads as an asset when a ShaderGraph importer
            // is present and the JSON matches its expected schema. A null load means either the
            // package is missing or the content/version is unsupported — roll back and report.
            var loaded = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (loaded == null)
            {
                AssetDatabase.DeleteAsset(path);
                return new ErrorResponse(
                    "capability.unsupported",
                    "ShaderGraph import failed for this Unity/ShaderGraph version",
                    "capability",
                    false,
                    null,
                    "Provide explicit valid .shadergraph content or enable a supported version",
                    new Dictionary<string, object> { { "unityVersion", Application.unityVersion }, { "path", path } });
            }

            response.Result = new { path, validated = true };
            return response;
        }
    }
}
