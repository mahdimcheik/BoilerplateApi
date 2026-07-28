using System.Security.Cryptography;
using System.Text;

namespace BoilerPlateApi.Utilities
{
    public static class SecurityTokens
    {
        /// <summary>Cryptographically-strong URL-safe random token.</summary>
        public static string GenerateOpaqueToken() => ToBase64Url(RandomNumberGenerator.GetBytes(32));

        /// <summary>Base64 without the characters that need escaping in a URL or query string.</summary>
        public static string ToBase64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');

        public static string Hash(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }
    }
}
