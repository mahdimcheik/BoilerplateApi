namespace BoilerPlateApi.Utilities
{
    public static class EnvironmentVariables
    {
        private static string GetEnvVar(string name, string fallback) =>
            Environment.GetEnvironmentVariable(name) ?? fallback;

        private static int GetEnvVarInt(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;

        private static bool GetEnvVarBool(string name, bool fallback) =>
            bool.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;

        private static Guid GetEnvVarGuid(string name, string fallback) =>
            Guid.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : Guid.Parse(fallback);

        private static T GetEnvVarEnum<T>(string name, T fallback) where T : struct, Enum =>
            Enum.TryParse<T>(Environment.GetEnvironmentVariable(name), ignoreCase: true, out var v) ? v : fallback;

        // URLs
        public static string API_BACK_URL => GetEnvVar("API_BACK_URL", "https://localhost:7125");
        public static string API_FRONT_URL => GetEnvVar("API_FRONT_URL", "http://localhost:4200");

        // JWT / auth
        public static string JWT_KEY =>
            GetEnvVar("JWT_KEY", "dev-only-super-long-insecure-key-change-me-in-production-please-1234567890");
        public static int TOKEN_VALIDITY_MINUTES => GetEnvVarInt("TOKEN_VALIDITY_MINUTES", 30);
        public static int COOKIES_VALIDITY_DAYS => GetEnvVarInt("COOKIES_VALIDITY_DAYS", 7);

        // Database (PostgreSQL)
        public static string DB_HOST => GetEnvVar("DB_HOST", "localhost");
        public static string DB_PORT => GetEnvVar("DB_PORT", "5432");
        public static string DB_NAME => GetEnvVar("DB_NAME", "bontechnicien");
        public static string DB_USER => GetEnvVar("DB_USER", "postgres");
        public static string DB_PASSWORD => GetEnvVar("DB_PASSWORD", "postgres");

        public static string ConnectionString =>
            $"Host={DB_HOST};Port={DB_PORT};Database={DB_NAME};Username={DB_USER};Password={DB_PASSWORD};";

        // Google sign-in (server-side ID-token validation)
        public static string GOOGLE_CLIENT_ID => GetEnvVar("GOOGLE_CLIENT_ID", "");

        // Mail (SMTP). When SMTP_HOST is empty, MailService logs links instead of sending.
        public static string SMTP_HOST => GetEnvVar("SMTP_HOST", "");
        public static int SMTP_PORT => GetEnvVarInt("SMTP_PORT", 587);
        public static string SMTP_LOGIN => GetEnvVar("SMTP_LOGIN", "");
        public static string SMTP_KEY => GetEnvVar("SMTP_KEY", "");

        // Object storage (SeaweedFS S3 gateway — filer started with -s3, port 8333).
        // Any S3-compatible endpoint works; keep ForcePathStyle on for non-AWS providers.
        public static string S3_SERVICE_URL => GetEnvVar("S3_SERVICE_URL", "http://localhost:8333");
        public static string S3_ACCESS_KEY => GetEnvVar("S3_ACCESS_KEY", "dummy-key");
        public static string S3_SECRET_KEY => GetEnvVar("S3_SECRET_KEY", "dummy-secret");
        public static string S3_BUCKET => GetEnvVar("S3_BUCKET", "medias");
        public static string S3_REGION => GetEnvVar("S3_REGION", "us-east-1");

        // Base URL used to build public links to stored objects. Defaults to the
        // path-style URL of the S3 gateway itself.
        public static string S3_PUBLIC_URL =>
            GetEnvVar("S3_PUBLIC_URL", $"{S3_SERVICE_URL.TrimEnd('/')}/{S3_BUCKET}");

        // False when the gateway refuses anonymous GETs (SeaweedFS started with an S3 config
        // file, private R2 bucket, ...). Public objects then fall back to long-lived signed URLs.
        public static bool S3_PUBLIC_READ => GetEnvVarBool("S3_PUBLIC_READ", true);

        // Storage (shared by both IStorageService implementations)
        // Backend used when a call doesn't name one explicitly.
        public static StorageProviderEnum STORAGE_PROVIDER =>
            GetEnvVarEnum("STORAGE_PROVIDER", StorageProviderEnum.Local);

        // Local backend roots. Public files sit under wwwroot and are served by UseStaticFiles;
        // private files sit outside it so they can only be reached through a signed URL.
        public static string STORAGE_PUBLIC_FOLDER => GetEnvVar("STORAGE_PUBLIC_FOLDER", "uploads");
        public static string STORAGE_PRIVATE_ROOT => GetEnvVar("STORAGE_PRIVATE_ROOT", "storage");

        public static int MAX_UPLOAD_SIZE_MB => GetEnvVarInt("MAX_UPLOAD_SIZE_MB", 25);
        public static long MaxUploadSizeBytes => MAX_UPLOAD_SIZE_MB * 1024L * 1024L;

        public static string ALLOWED_EXTENSIONS =>
            GetEnvVar("ALLOWED_EXTENSIONS", ".jpg,.jpeg,.png,.webp,.gif,.pdf,.docx,.xlsx,.csv,.txt,.mp4");

        // Lifetime of the signed links handed out for private objects.
        public static int SIGNED_URL_MINUTES => GetEnvVarInt("SIGNED_URL_MINUTES", 15);

        // emails + PASSWORDS
        public static string DO_NOT_REPLY_EMAIL => GetEnvVar("DO_NOT_REPLY_EMAIL", "ne-pas-repondre@boilerplate.fr");
        public static string NOTIFICATION_EMAIL => GetEnvVar("NOTIFICATION_EMAIL", "notification@boilerplate.fr");
        public static string ADMIN_EMAIL => GetEnvVar("ADMIN_EMAIL", "admin@boilerplate.fr");
        public static string SUPER_ADMIN_EMAIL => GetEnvVar("SUPER_ADMIN_EMAIL", "super.admin@boilerplate.fr");
        public static string SUPER_ADMIN_PASSWORD => GetEnvVar("SUPER_ADMIN_PASSWORD", "SuperPassword123!");

    }

}
