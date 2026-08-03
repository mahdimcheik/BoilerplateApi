using InteractivesApi.Models.Responses;
using InteractivesApi.Models.Storage;
using InteractivesApi.Utilities;

namespace InteractivesApi.Services
{
    /// <summary>
    /// One contract, two backends (local disk and any S3-compatible endpoint). Every method takes
    /// the storage key returned by <see cref="Upload"/>; visibility is encoded in the key's first
    /// segment, so nothing else has to be carried alongside it.
    /// </summary>
    public interface IStorageService
    {
        /// <summary>Which backend this instance is, used by <see cref="IStorageResolver"/>.</summary>
        StorageProviderEnum Provider { get; }

        Task<Response<StoredFile>> Upload(UploadRequest req, CancellationToken ct);

        Task<Response<StoredFileStream>> Download(string key, CancellationToken ct);

        Task<Response<object>> Delete(string key, CancellationToken ct);

        /// <summary>
        /// Direct URL for a public object, time-limited signed URL for a private one.
        /// <paramref name="ttl"/> defaults to SIGNED_URL_MINUTES and is ignored when public.
        /// </summary>
        Task<Response<string>> GetUrl(string key, TimeSpan? ttl, CancellationToken ct);

        Task<Response<bool>> Exists(string key, CancellationToken ct);
    }

    /// <summary>
    /// Picks a backend per call. Both implementations are registered at once so files uploaded
    /// before a provider switch stay readable — the caller just passes the provider it stored.
    /// </summary>
    public interface IStorageResolver
    {
        IStorageService Resolve(StorageProviderEnum? provider = null);
    }

    public class StorageResolver : IStorageResolver
    {
        private readonly IEnumerable<IStorageService> _services;

        public StorageResolver(IEnumerable<IStorageService> services)
        {
            _services = services;
        }

        public IStorageService Resolve(StorageProviderEnum? provider = null)
        {
            var wanted = provider ?? EnvironmentVariables.STORAGE_PROVIDER;
            return _services.FirstOrDefault(s => s.Provider == wanted)
                ?? throw new InvalidOperationException($"No IStorageService registered for provider '{wanted}'.");
        }
    }
}
