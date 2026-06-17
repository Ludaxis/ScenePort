using System;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Shared helpers for turning a captured <see cref="Texture2D"/> into an inline base64 PNG
    /// (optionally downscaled) so vision-capable models can see the screenshot instead of just a
    /// file path. Encoding never throws; failures yield an empty result that callers treat as
    /// "no inline image available".
    /// </summary>
    internal static class ScenePortImage
    {
        internal struct EncodedImage
        {
            public string Base64;
            public int Width;
            public int Height;
        }

        /// <summary>
        /// Compute the downscaled dimensions for a source size, preserving aspect ratio so the
        /// longest edge is at most <paramref name="maxEdge"/>. Returns the source size unchanged
        /// when it already fits or when inputs are invalid.
        /// </summary>
        internal static void FitDimensions(int sourceWidth, int sourceHeight, int maxEdge, out int width, out int height)
        {
            width = Mathf.Max(1, sourceWidth);
            height = Mathf.Max(1, sourceHeight);
            if (maxEdge <= 0)
            {
                return;
            }

            var longest = Mathf.Max(width, height);
            if (longest <= maxEdge)
            {
                return;
            }

            var scale = (float)maxEdge / longest;
            width = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
        }

        /// <summary>
        /// Encode <paramref name="texture"/> as a base64 PNG, downscaling its longest edge to
        /// <paramref name="maxEdge"/> when needed. Returns an empty result on any failure.
        /// </summary>
        internal static EncodedImage EncodeBase64(Texture2D texture, int maxEdge)
        {
            var result = default(EncodedImage);
            if (texture == null)
            {
                return result;
            }

            try
            {
                FitDimensions(texture.width, texture.height, maxEdge, out var targetWidth, out var targetHeight);
                if (targetWidth == texture.width && targetHeight == texture.height)
                {
                    var bytes = texture.EncodeToPNG();
                    if (bytes == null || bytes.Length == 0)
                    {
                        return result;
                    }

                    result.Base64 = Convert.ToBase64String(bytes);
                    result.Width = texture.width;
                    result.Height = texture.height;
                    return result;
                }

                return EncodeDownscaled(texture, targetWidth, targetHeight);
            }
            catch (Exception)
            {
                return result;
            }
        }

        private static EncodedImage EncodeDownscaled(Texture2D source, int targetWidth, int targetHeight)
        {
            var result = default(EncodedImage);
            var renderTexture = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var scaled = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(source, renderTexture);
                RenderTexture.active = renderTexture;
                scaled.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                scaled.Apply();

                var bytes = scaled.EncodeToPNG();
                if (bytes != null && bytes.Length > 0)
                {
                    result.Base64 = Convert.ToBase64String(bytes);
                    result.Width = targetWidth;
                    result.Height = targetHeight;
                }
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                UnityEngine.Object.DestroyImmediate(scaled);
            }

            return result;
        }
    }
}
