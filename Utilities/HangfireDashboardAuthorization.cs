using Hangfire.Dashboard;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace BoilerPlateApi.Utilities
{
    /// <summary>
    /// Gate in front of the Hangfire dashboard. The API authenticates with a bearer JWT, but the
    /// dashboard is opened by a browser that has no way to attach one, so it is protected by HTTP
    /// Basic (HANGFIRE_DASHBOARD_USER / HANGFIRE_DASHBOARD_PASSWORD) instead.
    ///
    /// Hangfire denies by default: returning false hides every dashboard route, so an empty
    /// password (no credentials configured) leaves it reachable in Development only rather than
    /// silently publishing an unauthenticated job console in production.
    /// </summary>
    public class HangfireDashboardAuthorization : IDashboardAuthorizationFilter
    {
        private readonly bool _isDevelopment;

        public HangfireDashboardAuthorization(bool isDevelopment)
        {
            _isDevelopment = isDevelopment;
        }

        public bool Authorize(DashboardContext context)
        {
            var expectedUser = EnvironmentVariables.HANGFIRE_DASHBOARD_USER;
            var expectedPassword = EnvironmentVariables.HANGFIRE_DASHBOARD_PASSWORD;

            if (string.IsNullOrEmpty(expectedPassword))
                return _isDevelopment;

            var http = context.GetHttpContext();
            var header = http.Request.Headers.Authorization.ToString();

            if (AuthenticationHeaderValue.TryParse(header, out var auth)
                && "Basic".Equals(auth.Scheme, StringComparison.OrdinalIgnoreCase)
                && auth.Parameter is not null
                && TryDecode(auth.Parameter, out var user, out var password)
                && Matches(user, expectedUser)
                && Matches(password, expectedPassword))
            {
                return true;
            }

            // Challenge instead of a bare 401 body: the browser then prompts for credentials.
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            http.Response.Headers.WWWAuthenticate = "Basic realm=\"Hangfire\", charset=\"UTF-8\"";
            return false;
        }

        private static bool TryDecode(string parameter, out string user, out string password)
        {
            user = string.Empty;
            password = string.Empty;

            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parameter));
                var separator = decoded.IndexOf(':');
                if (separator < 0)
                    return false;

                user = decoded[..separator];
                password = decoded[(separator + 1)..];
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // Fixed-time so a wrong credential can't be narrowed down by timing the response.
        private static bool Matches(string candidate, string expected) =>
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(candidate),
                Encoding.UTF8.GetBytes(expected));
    }
}
