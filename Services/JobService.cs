using BoilerPlateApi.Contexts;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace BoilerPlateApi.Services
{
    /// <summary>
    /// Work executed by Hangfire workers rather than during a request. Methods are resolved
    /// through the interface so the serialized job only carries <c>IJobService</c> plus arguments
    /// — the implementation can move or be renamed without orphaning jobs already queued.
    ///
    /// Enqueue from a controller/service with <c>IBackgroundJobClient</c>:
    /// <c>_jobs.Enqueue&lt;IJobService&gt;(j =&gt; j.PurgeExpiredRefreshTokens(CancellationToken.None));</c>
    /// Recurring registrations live in <c>Program.RegisterRecurringJobs</c>.
    /// </summary>
    public interface IJobService
    {
        /// <summary>Deletes refresh tokens whose expiry has passed.</summary>
        Task PurgeExpiredRefreshTokens(CancellationToken ct);
    }

    public class JobService : IJobService
    {
        private readonly MainContext _context;
        private readonly ILogger<JobService> _logger;

        public JobService(MainContext context, ILogger<JobService> logger)
        {
            _context = context;
            _logger = logger;
        }

        // A row per user is rotated on every refresh, so a purge that overlaps itself would only
        // duplicate work; the retries cover a transient database hiccup.
        [AutomaticRetry(Attempts = 3)]
        [DisableConcurrentExecution(timeoutInSeconds: 300)]
        public async Task PurgeExpiredRefreshTokens(CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow;

            var deleted = await _context.RefreshTokens
                .Where(token => token.ExpirationDate < now)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
                _logger.LogInformation("{Count} refresh token(s) expiré(s) supprimé(s).", deleted);
        }
    }
}
