using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Single source of truth for ScenePort JSON serialization. Replaces the previous
    /// hand-rolled StringBuilder/regex layer, which silently dropped exponent-notation
    /// numbers (e.g. 1e-7) and emitted invalid JSON for NaN/Infinity and control chars.
    /// </summary>
    internal static class ScenePortJson
    {
        // Non-finite floats serialize as null (honest and valid JSON). Numbers parse with
        // full JSON grammar including exponents, so values are never silently dropped.
        internal static readonly JsonSerializerSettings Settings = BuildSettings();

        private static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        private static JsonSerializerSettings BuildSettings()
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Include,
                Culture = System.Globalization.CultureInfo.InvariantCulture,
            };
            settings.Converters.Add(new NonFiniteFloatConverter());
            return settings;
        }

        internal static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, Settings);
        }

        /// <summary>
        /// Parse a request body into a JObject. Returns an empty object for null/blank/invalid
        /// bodies, matching the previous regex layer's "fall back to defaults" behavior.
        /// </summary>
        internal static JObject ParseBody(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return new JObject();
            }

            try
            {
                var token = JToken.Parse(body);
                return token as JObject ?? new JObject();
            }
            catch (JsonException)
            {
                return new JObject();
            }
        }

        internal static JsonSerializer SharedSerializer => Serializer;

        private sealed class NonFiniteFloatConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(float)
                    || objectType == typeof(double)
                    || objectType == typeof(float?)
                    || objectType == typeof(double?);
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value == null)
                {
                    writer.WriteNull();
                    return;
                }

                if (value is float f)
                {
                    if (float.IsNaN(f) || float.IsInfinity(f))
                    {
                        writer.WriteNull();
                    }
                    else
                    {
                        writer.WriteValue(f);
                    }
                    return;
                }

                if (value is double d)
                {
                    if (double.IsNaN(d) || double.IsInfinity(d))
                    {
                        writer.WriteNull();
                    }
                    else
                    {
                        writer.WriteValue(d);
                    }
                    return;
                }

                writer.WriteValue(value);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null)
                {
                    return objectType == typeof(float) || objectType == typeof(double) ? Activator.CreateInstance(objectType) : null;
                }

                if (objectType == typeof(float) || objectType == typeof(float?))
                {
                    return Convert.ToSingle(reader.Value, System.Globalization.CultureInfo.InvariantCulture);
                }

                return Convert.ToDouble(reader.Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
