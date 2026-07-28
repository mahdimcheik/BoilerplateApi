using BoilerPlateApi.Models.Responses;
using BoilerPlateApi.Models.Storage;
using BoilerPlateApi.Utilities;

namespace BoilerPlateApi.Services
{
    /// <summary>
    /// Disk backend. Public objects land under wwwroot and are served by UseStaticFiles; private
    /// objects land in a folder outside wwwroot so the only way in is a signed link checked by
    /// MediasController.
    /// </summary>
    public class LocalStorageService : IStorageService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<LocalStorageService> _logger;

        public LocalStorageService(IWebHostEnvironment env, ILogger<LocalStorageService> logger)
        {
            _env = env;
            _logger = logger;
        }

        public StorageProviderEnum Provider => StorageProviderEnum.Local;

        // WebRootPath is null until a wwwroot folder exists on disk, so it can't be trusted here.
        private string WebRoot => string.IsNullOrEmpty(_env.WebRootPath)
            ? Path.Combine(_env.ContentRootPath, "wwwroot")
            : _env.WebRootPath;

        private string PublicRoot => Path.Combine(WebRoot, EnvironmentVariables.STORAGE_PUBLIC_FOLDER);

        private string PrivateRoot => Path.Combine(_env.ContentRootPath, EnvironmentVariables.STORAGE_PRIVATE_ROOT);

        private string RootFor(string key) =>
            StorageHelper.VisibilityOf(key) == StorageVisibilityEnum.Public ? PublicRoot : PrivateRoot;

        // ---- Upload ----

        public async Task<Response<StoredFile>> Upload(UploadRequest req, CancellationToken ct)
        {
            if (await StorageHelper.ValidateAsync<StoredFile>(req.File, ct) is { } invalid)
                return invalid;

            var key = StorageHelper.BuildKey(req.Folder, req.Visibility, req.File.FileName);
            var path = StorageHelper.ResolvePhysicalPath(RootFor(key), key);
            if (path is null)
                return Fail<StoredFile>(400, "Chemin de fichier invalide.");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await req.File.CopyToAsync(destination, ct);
                }

                var url = await BuildUrl(key, null);
                return new Response<StoredFile>
                {
                    Status = 201,
                    Message = "Fichier téléversé.",
                    Data = new StoredFile(
                        key,
                        Provider,
                        req.Visibility,
                        url,
                        req.File.FileName,
                        StorageHelper.ContentTypeFor(key),
                        req.File.Length),
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Local upload failed for key {Key}", key);
                TryDelete(path);
                return Fail<StoredFile>(500, "Le téléversement a échoué.");
            }
        }

        // ---- Download ----

        public Task<Response<StoredFileStream>> Download(string key, CancellationToken ct)
        {
            if (!StorageHelper.IsValidKey(key))
                return Task.FromResult(Fail<StoredFileStream>(400, "Clé de fichier invalide."));

            var path = StorageHelper.ResolvePhysicalPath(RootFor(key), key);
            if (path is null || !File.Exists(path))
                return Task.FromResult(Fail<StoredFileStream>(404, "Fichier introuvable."));

            try
            {
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
                return Task.FromResult(new Response<StoredFileStream>
                {
                    Status = 200,
                    Message = "Fichier récupéré.",
                    Data = new StoredFileStream(
                        stream,
                        StorageHelper.ContentTypeFor(key),
                        Path.GetFileName(key),
                        stream.Length),
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Local download failed for key {Key}", key);
                return Task.FromResult(Fail<StoredFileStream>(500, "La lecture du fichier a échoué."));
            }
        }

        // ---- Delete ----

        public Task<Response<object>> Delete(string key, CancellationToken ct)
        {
            if (!StorageHelper.IsValidKey(key))
                return Task.FromResult(Fail<object>(400, "Clé de fichier invalide."));

            var path = StorageHelper.ResolvePhysicalPath(RootFor(key), key);
            if (path is null || !File.Exists(path))
                return Task.FromResult(Fail<object>(404, "Fichier introuvable."));

            try
            {
                File.Delete(path);
                return Task.FromResult(new Response<object> { Status = 200, Message = "Fichier supprimé." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Local delete failed for key {Key}", key);
                return Task.FromResult(Fail<object>(500, "La suppression a échoué."));
            }
        }

        // ---- URLs ----

        public async Task<Response<string>> GetUrl(string key, TimeSpan? ttl, CancellationToken ct)
        {
            if (!StorageHelper.IsValidKey(key))
                return Fail<string>(400, "Clé de fichier invalide.");

            return new Response<string>
            {
                Status = 200,
                Message = "Lien généré.",
                Data = await BuildUrl(key, ttl),
            };
        }

        public Task<Response<bool>> Exists(string key, CancellationToken ct)
        {
            if (!StorageHelper.IsValidKey(key))
                return Task.FromResult(Fail<bool>(400, "Clé de fichier invalide."));

            var path = StorageHelper.ResolvePhysicalPath(RootFor(key), key);
            var found = path is not null && File.Exists(path);

            return Task.FromResult(new Response<bool>
            {
                Status = found ? 200 : 404,
                Message = found ? "Fichier trouvé." : "Fichier introuvable.",
                Data = found,
            });
        }

        // ---- Helpers ----

        /// <summary>
        /// Public: the static-files path under wwwroot. Private: /medias/file/{key} carrying an
        /// expiry and an HMAC, the only route that reads the private root.
        /// </summary>
        private Task<string> BuildUrl(string key, TimeSpan? ttl)
        {
            var api = EnvironmentVariables.API_BACK_URL.TrimEnd('/');

            if (StorageHelper.VisibilityOf(key) == StorageVisibilityEnum.Public)
                return Task.FromResult($"{api}/{EnvironmentVariables.STORAGE_PUBLIC_FOLDER}/{key}");

            var lifetime = ttl ?? TimeSpan.FromMinutes(EnvironmentVariables.SIGNED_URL_MINUTES);
            var expires = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
            var signature = StorageHelper.Sign(key, expires);

            return Task.FromResult(
                $"{api}/medias/file/{key}?exp={expires}&sig={Uri.EscapeDataString(signature)}&provider={Provider}");
        }

        /// <summary>Best-effort cleanup of a half-written file; failing here must not mask the original error.</summary>
        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not clean up partial upload at {Path}", path);
            }
        }

        private static Response<T> Fail<T>(int status, string message) => new() { Status = status, Message = message };
    }
}
