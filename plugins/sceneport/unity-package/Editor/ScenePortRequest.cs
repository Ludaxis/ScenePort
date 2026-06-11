using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Typed access to a parsed request: query-string parameters plus a JSON body parsed
    /// once into a JObject. Replaces the regex Extract*/Get* helpers, which mishandled
    /// exponent notation, nested braces, and key names appearing inside string values.
    ///
    /// Precedence helpers mirror the original call sites exactly:
    ///   Extract*  → body value, else fallback   (original ExtractString(body, ...))
    ///   Get*      → query value, else fallback   (original GetString(query, ...))
    /// Vector/Color values are read from the body, matching the original behavior.
    /// </summary>
    internal sealed class ScenePortRequest
    {
        internal IReadOnlyDictionary<string, string> Query { get; }
        internal JObject Body { get; }

        internal ScenePortRequest(string queryString, string body)
        {
            Query = ParseQuery(queryString);
            Body = ScenePortJson.ParseBody(body);
        }

        internal ScenePortRequest(IReadOnlyDictionary<string, string> query, JObject body)
        {
            Query = query ?? new Dictionary<string, string>();
            Body = body ?? new JObject();
        }

        // --- Body-first accessors ---

        internal string ExtractString(string key, string fallback)
        {
            var token = Body[key];
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Object || token.Type == JTokenType.Array)
            {
                return fallback;
            }

            return token.Type == JTokenType.String ? token.Value<string>() : token.ToString(Newtonsoft.Json.Formatting.None);
        }

        internal int ExtractInt(string key, int fallback)
        {
            var token = Body[key];
            if (token == null)
            {
                return fallback;
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                return (int)token.Value<long>();
            }

            return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
        }

        internal bool ExtractBool(string key, bool fallback)
        {
            var token = Body[key];
            if (token == null)
            {
                return fallback;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            return bool.TryParse(token.ToString(), out var parsed) ? parsed : fallback;
        }

        internal float ExtractFloat(string key, float fallback)
        {
            var token = Body[key];
            if (token == null)
            {
                return fallback;
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                return token.Value<float>();
            }

            return float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
        }

        internal bool HasObject(string key)
        {
            return Body.TryGetValue(key, out var token) && token != null && token.Type != JTokenType.Null;
        }

        internal Vector2 GetVector2(string key, Vector2 fallback)
        {
            if (!(Body[key] is JObject obj))
            {
                return fallback;
            }

            return new Vector2(
                ReadFloat(obj, "x", fallback.x),
                ReadFloat(obj, "y", fallback.y));
        }

        internal Vector3 GetVector3(string key, Vector3 fallback)
        {
            if (!(Body[key] is JObject obj))
            {
                return fallback;
            }

            return new Vector3(
                ReadFloat(obj, "x", fallback.x),
                ReadFloat(obj, "y", fallback.y),
                ReadFloat(obj, "z", fallback.z));
        }

        internal Vector4 GetVector4(string key, Vector4 fallback)
        {
            if (!(Body[key] is JObject obj))
            {
                return fallback;
            }

            return new Vector4(
                ReadFloat(obj, "x", fallback.x),
                ReadFloat(obj, "y", fallback.y),
                ReadFloat(obj, "z", fallback.z),
                ReadFloat(obj, "w", fallback.w));
        }

        internal Vector3 GetVector3Named(string key, string xName, string yName, string zName, Vector3 fallback)
        {
            if (!(Body[key] is JObject obj))
            {
                return fallback;
            }

            return new Vector3(
                ReadFloat(obj, xName, fallback.x),
                ReadFloat(obj, yName, fallback.y),
                ReadFloat(obj, zName, fallback.z));
        }

        internal Color GetColor(string key, Color fallback)
        {
            if (!(Body[key] is JObject obj))
            {
                return fallback;
            }

            return new Color(
                ReadFloat(obj, "r", fallback.r),
                ReadFloat(obj, "g", fallback.g),
                ReadFloat(obj, "b", fallback.b),
                ReadFloat(obj, "a", fallback.a));
        }

        // --- Query-only accessors ---

        internal string GetString(string key, string fallback)
        {
            return Query.TryGetValue(key, out var value) ? value : fallback;
        }

        internal int GetInt(string key, int fallback)
        {
            return Query.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        internal bool GetBool(string key, bool fallback)
        {
            return Query.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;
        }

        internal static string[] SplitCsv(string value)
        {
            return string.IsNullOrEmpty(value)
                ? null
                : value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()).Where(part => part.Length > 0).ToArray();
        }

        private static float ReadFloat(JObject obj, string key, float fallback)
        {
            var token = obj[key];
            if (token == null)
            {
                return fallback;
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                return token.Value<float>();
            }

            return float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
        }

        private static Dictionary<string, string> ParseQuery(string queryString)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(queryString))
            {
                return result;
            }

            var trimmed = queryString.TrimStart('?');
            var parts = trimmed.Split('&');
            for (var i = 0; i < parts.Length; i++)
            {
                var pair = parts[i].Split(new[] { '=' }, 2);
                if (pair.Length == 2)
                {
                    result[Uri.UnescapeDataString(pair[0])] = Uri.UnescapeDataString(pair[1]);
                }
            }

            return result;
        }
    }
}
