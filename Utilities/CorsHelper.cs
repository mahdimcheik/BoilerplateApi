namespace BoilerPlateApi.Utilities
{
    public static class CorsHelper
    {
        private static readonly string[] AllowedOrigins =
        {
        "http://localhost:4200",
        "https://localhost:4200",
        "http://localhost:5275",
        "https://localhost:7125",
    };

        public static bool IsOriginAllowed(string origin) =>
            AllowedOrigins.Contains(origin) ||
            origin == EnvironmentVariables.API_FRONT_URL ||
            origin == EnvironmentVariables.API_BACK_URL;
    }
}
