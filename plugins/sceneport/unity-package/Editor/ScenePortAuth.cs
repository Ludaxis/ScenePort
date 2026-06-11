using System;
using System.Security.Cryptography;
using System.Text;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Per-project shared-secret token generation and constant-time comparison.
    /// </summary>
    internal static class ScenePortAuth
    {
        internal const string TokenHeader = "X-ScenePort-Token";

        internal static string GenerateToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }

        internal static bool IsValidTokenFormat(string token)
        {
            if (token == null || token.Length != 64)
            {
                return false;
            }

            foreach (var c in token)
            {
                var hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Length-independent comparison to avoid leaking match length via timing.</summary>
        internal static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            var ab = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            var diff = ab.Length ^ bb.Length;
            for (var i = 0; i < ab.Length; i++)
            {
                diff |= ab[i] ^ (i < bb.Length ? bb[i] : 0);
            }

            return diff == 0;
        }
    }
}
