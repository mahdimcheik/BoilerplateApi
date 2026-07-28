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

        // emails + PASSWORDS
        public static string DO_NOT_REPLY_EMAIL => GetEnvVar("DO_NOT_REPLY_EMAIL", "ne-pas-repondre@boilerplate.fr");
        public static string NOTIFICATION_EMAIL => GetEnvVar("NOTIFICATION_EMAIL", "notification@boilerplate.fr");
        public static string ADMIN_EMAIL => GetEnvVar("ADMIN_EMAIL", "admin@boilerplate.fr");
        public static string SUPER_ADMIN_EMAIL => GetEnvVar("SUPER_ADMIN_EMAIL", "super.admin@boilerplate.fr");
        public static string SUPER_ADMIN_PASSWORD => GetEnvVar("SUPER_ADMIN_PASSWORD", "SuperPassword123!");

    }

}
