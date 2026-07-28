namespace BoilerPlateApi.Utilities
{
    /// <summary>
    /// Magic-number check: the first bytes of the uploaded stream must match the extension the
    /// client claims. Stops a renamed executable from being stored as ".jpg" — the extension and
    /// the Content-Type header are both attacker-controlled, the bytes are not.
    /// </summary>
    public static class FileSignatures
    {
        /// <summary>
        /// Extensions with no reliable leading signature. Listed explicitly rather than letting
        /// unknown extensions pass silently — anything absent from both tables is rejected.
        /// </summary>
        private static readonly HashSet<string> NoSignature =
            new(StringComparer.OrdinalIgnoreCase) { ".txt", ".csv", ".svg" };

        /// <summary>
        /// Accepted byte prefixes per extension. An extension matches when ANY of its patterns
        /// matches; a null slot in a pattern is a wildcard byte.
        /// </summary>
        private static readonly Dictionary<string, byte?[][]> Signatures =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".jpg"] = [[0xFF, 0xD8, 0xFF]],
                [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
                [".png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
                [".gif"] = [
                    [0x47, 0x49, 0x46, 0x38, 0x37, 0x61],   // GIF87a
                    [0x47, 0x49, 0x46, 0x38, 0x39, 0x61],   // GIF89a
                ],
                // RIFF....WEBP — bytes 4-7 are the file size, hence the wildcards.
                [".webp"] = [[0x52, 0x49, 0x46, 0x46, null, null, null, null, 0x57, 0x45, 0x42, 0x50]],
                [".pdf"] = [[0x25, 0x50, 0x44, 0x46]],      // %PDF
                // OOXML containers are ZIP archives; the empty/spanned variants are legal too.
                [".docx"] = [
                    [0x50, 0x4B, 0x03, 0x04],
                    [0x50, 0x4B, 0x05, 0x06],
                    [0x50, 0x4B, 0x07, 0x08],
                ],
                [".xlsx"] = [
                    [0x50, 0x4B, 0x03, 0x04],
                    [0x50, 0x4B, 0x05, 0x06],
                    [0x50, 0x4B, 0x07, 0x08],
                ],
                // ISO-BMFF: "ftyp" at offset 4, brand varies (isom, mp42, ...).
                [".mp4"] = [[null, null, null, null, 0x66, 0x74, 0x79, 0x70]],
            };

        /// <summary>Longest pattern in the table — how many bytes we need to peek.</summary>
        private const int HeaderLength = 16;

        /// <summary>True when <paramref name="extension"/> is known and needs no byte check.</summary>
        public static bool IsExempt(string extension) => NoSignature.Contains(extension);

        public static bool IsKnown(string extension) =>
            Signatures.ContainsKey(extension) || NoSignature.Contains(extension);

        /// <summary>
        /// Reads the header of <paramref name="content"/> and compares it with the patterns
        /// registered for <paramref name="extension"/>. The stream is rewound to position 0
        /// before returning, so the caller can still upload it.
        /// </summary>
        public static async Task<bool> MatchesAsync(Stream content, string extension, CancellationToken ct)
        {
            if (NoSignature.Contains(extension))
                return true;

            if (!Signatures.TryGetValue(extension, out var patterns))
                return false;

            var header = new byte[HeaderLength];
            var read = await content.ReadAtLeastAsync(header, HeaderLength, throwOnEndOfStream: false, ct);
            if (content.CanSeek)
                content.Position = 0;

            return patterns.Any(pattern => Matches(header, read, pattern));
        }

        private static bool Matches(byte[] header, int read, byte?[] pattern)
        {
            if (read < pattern.Length)
                return false;

            for (var i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] is { } expected && header[i] != expected)
                    return false;
            }

            return true;
        }
    }
}
