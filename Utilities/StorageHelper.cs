using InteractivesApi.Models.Responses;
using System.Security.Cryptography;
using System.Text;

namespace InteractivesApi.Utilities
{
    /// <summary>
    /// Key building, upload validation and URL signing shared by every IStorageService
    /// implementation, so Local and S3 accept and reject exactly the same things.
    /// </summary>
    public static class StorageHelper
    {
        public const string PublicPrefix = "public/";
        public const string PrivatePrefix = "private/";

        /// <summary>Content types tolerated for a given extension. First entry is the canonical one.</summary>
        private static readonly Dictionary<string, string[]> ContentTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".jpg"] = ["image/jpeg", "image/jpg"],
                [".jpeg"] = ["image/jpeg", "image/jpg"],
                [".png"] = ["image/png"],
                [".webp"] = ["image/webp"],
                [".gif"] = ["image/gif"],
                [".svg"] = ["image/svg+xml"],
                [".pdf"] = ["application/pdf"],
                [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
                [".xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
                [".csv"] = ["text/csv", "application/csv", "text/plain"],
                [".txt"] = ["text/plain"],
                [".mp4"] = ["video/mp4"],
            };

        /// <summary>Content types a client may send when it doesn't know better.</summary>
        private static readonly HashSet<string> GenericContentTypes =
            new(StringComparer.OrdinalIgnoreCase) { "application/octet-stream", "binary/octet-stream" };

        private static HashSet<string> AllowedExtensions =>
            EnvironmentVariables.ALLOWED_EXTENSIONS
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.StartsWith('.') ? e : $".{e}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        public static string Prefix(StorageVisibilityEnum visibility) =>
            visibility == StorageVisibilityEnum.Public ? PublicPrefix : PrivatePrefix;

        public static StorageVisibilityEnum VisibilityOf(string key) =>
            key.StartsWith(PublicPrefix, StringComparison.Ordinal)
                ? StorageVisibilityEnum.Public
                : StorageVisibilityEnum.Private;

        // ---- Keys ----

        /// <summary>
        /// Builds "{visibility}/{folder}/{yyyy}/{MM}/{guid}{ext}". The original file name never
        /// reaches the key — only its extension — so nothing user-controlled ends up in a path.
        /// </summary>
        public static string BuildKey(string? folder, StorageVisibilityEnum visibility, string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var now = DateTimeOffset.UtcNow;
            var segment = SanitizeFolder(folder);
            var scope = string.IsNullOrEmpty(segment) ? string.Empty : $"{segment}/";

            return $"{Prefix(visibility)}{scope}{now:yyyy}/{now:MM}/{Guid.NewGuid()}{extension}";
        }

        /// <summary>Keeps [a-z0-9-_] per segment, drops everything else (including "." and "..").</summary>
        private static string SanitizeFolder(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return string.Empty;

            var segments = folder
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(segment => new string(segment
                    .ToLowerInvariant()
                    .Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                    .ToArray()))
                .Where(segment => segment.Length > 0)
                .Take(3);

            return string.Join('/', segments);
        }

        /// <summary>
        /// Gate for every read/delete: a key must be one we could have produced. Rejects traversal
        /// ("..\", "../"), absolute paths, drive letters and anything outside the two prefixes.
        /// </summary>
        public static bool IsValidKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 512)
                return false;

            if (!key.StartsWith(PublicPrefix, StringComparison.Ordinal) &&
                !key.StartsWith(PrivatePrefix, StringComparison.Ordinal))
                return false;

            if (key.Contains('\\') || key.Contains(':') || key.Contains("..") || key.Contains("//"))
                return false;

            return key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/');
        }

        /// <summary>
        /// Maps a validated key onto the local filesystem, returning null if the result would
        /// land outside <paramref name="root"/> — belt-and-braces behind <see cref="IsValidKey"/>.
        /// </summary>
        public static string? ResolvePhysicalPath(string root, string key)
        {
            var fullRoot = Path.GetFullPath(root);
            var candidate = Path.GetFullPath(
                Path.Combine(fullRoot, key.Replace('/', Path.DirectorySeparatorChar)));

            var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;

            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? candidate : null;
        }

        // ---- Validation ----

        /// <summary>
        /// Returns null when the upload is acceptable, otherwise the failure to hand back to the
        /// caller. Checks size, extension allowlist, declared content type and magic number.
        /// </summary>
        public static async Task<Response<T>?> ValidateAsync<T>(IFormFile? file, CancellationToken ct)
        {
            if (file is null || file.Length == 0)
                return Fail<T>(400, "Aucun fichier reçu.");

            if (file.Length > EnvironmentVariables.MaxUploadSizeBytes)
                return Fail<T>(413, $"Fichier trop volumineux (maximum {EnvironmentVariables.MAX_UPLOAD_SIZE_MB} Mo).");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
                return Fail<T>(415, "Type de fichier non autorisé.");

            // An extension can be allowlisted without us knowing how to verify its bytes.
            if (!FileSignatures.IsKnown(extension))
                return Fail<T>(415, "Type de fichier non autorisé.");

            var declared = file.ContentType?.Split(';')[0].Trim();
            if (!string.IsNullOrEmpty(declared) &&
                !GenericContentTypes.Contains(declared) &&
                ContentTypes.TryGetValue(extension, out var accepted) &&
                !accepted.Contains(declared, StringComparer.OrdinalIgnoreCase))
                return Fail<T>(415, "Le type MIME ne correspond pas à l'extension du fichier.");

            await using var stream = file.OpenReadStream();
            if (!await FileSignatures.MatchesAsync(stream, extension, ct))
                return Fail<T>(415, "Le contenu du fichier ne correspond pas à son extension.");

            return null;
        }

        /// <summary>Canonical content type for a key, used when serving and when storing.</summary>
        public static string ContentTypeFor(string keyOrFileName)
        {
            var extension = Path.GetExtension(keyOrFileName).ToLowerInvariant();
            return ContentTypes.TryGetValue(extension, out var types) ? types[0] : "application/octet-stream";
        }

        // ---- Signed URLs (local backend) ----

        /// <summary>
        /// Signs "{key}|{exp}" with JWT_KEY. Same secret as the access tokens: rotating it
        /// invalidates outstanding links, which is the desired behaviour.
        /// </summary>
        public static string Sign(string key, long expiresAtUnixSeconds)
        {
            var payload = Encoding.UTF8.GetBytes($"{key}|{expiresAtUnixSeconds}");
            var secret = Encoding.UTF8.GetBytes(EnvironmentVariables.JWT_KEY);
            return SecurityTokens.ToBase64Url(HMACSHA256.HashData(secret, payload));
        }

        /// <summary>Constant-time signature check plus expiry. Both must hold.</summary>
        public static bool Verify(string key, long expiresAtUnixSeconds, string? signature)
        {
            if (string.IsNullOrEmpty(signature))
                return false;

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAtUnixSeconds)
                return false;

            var expected = Encoding.UTF8.GetBytes(Sign(key, expiresAtUnixSeconds));
            var provided = Encoding.UTF8.GetBytes(signature);
            return CryptographicOperations.FixedTimeEquals(expected, provided);
        }

        private static Response<T> Fail<T>(int status, string message) => new() { Status = status, Message = message };
    }
}
