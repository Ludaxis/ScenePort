using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    internal static class ScenePortPaths
    {
        internal static string ProjectPath()
        {
            return Application.dataPath.EndsWith("/Assets", StringComparison.Ordinal)
                ? Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length)
                : Application.dataPath;
        }

        internal static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                builder.Append(Array.IndexOf(invalid, value[i]) >= 0 ? '-' : value[i]);
            }

            return builder.ToString();
        }
    }
}
