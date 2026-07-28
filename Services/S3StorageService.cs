using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using BoilerPlateApi.Models.Responses;
using BoilerPlateApi.Models.Storage;
using BoilerPlateApi.Utilities;
using System.Net;

namespace BoilerPlateApi.Services
{
    /// <summary>
    /// S3-compatible backend, driven by the IAmazonS3 client configured in Program.cs (SeaweedFS
    /// by default; MinIO, R2 and AWS work unchanged). Private objects are handed out as native
    /// presigned GETs, so their bytes never pass through this API.
    /// </summary>
    public class S3StorageService : IStorageService
    {
        private readonly IAmazonS3 _s3;
        private readonly ILogger<S3StorageService> _logger;

        public S3StorageService(IAmazonS3 s3, ILogger<S3StorageService> logger)
        {
            _s3 = s3;
            _logger = logger;
        }

        public StorageProviderEnum Provider => StorageProviderEnum.S3;

        private static string Bucket => EnvironmentVariables.S3_BUCKET;

        /// <summary>
        /// An unreachable or failing endpoint surfaces as a transport exception, not an
        /// AmazonS3Exception — the SDK rethrows HttpRequestException once retries are exhausted.
        /// Those must come back as 503 rather than an unhandled 500 that leaks a stack trace.
        /// </summary>
        private static bool IsUnavailable(Exception ex) =>
            ex is AmazonServiceException or HttpRequestException or TaskCanceledException ||
            ex.InnerException is HttpRequestException or AmazonServiceException;

        // ---- Upload ----

        public async Task<Response<StoredFile>> Upload(UploadRequest req, CancellationToken ct)
        {
            if (await StorageHelper.ValidateAsync<StoredFile>(req.File, ct) is { } invalid)
                return invalid;

            var key = StorageHelper.BuildKey(req.Folder, req.Visibility, req.File.FileName);
            var contentType = StorageHelper.ContentTypeFor(key);

            try
            {
                await using var content = req.File.OpenReadStream();
                var request = new PutObjectRequest
                {
                    BucketName = Bucket,
                    Key = key,
                    InputStream = content,
                    ContentType = contentType,
                    AutoCloseStream = false,
                };

                // Only worth sending when the endpoint actually honours anonymous reads;
                // gateways that don't support ACLs would otherwise reject the request.
                if (req.Visibility == StorageVisibilityEnum.Public && EnvironmentVariables.S3_PUBLIC_READ)
                    request.CannedACL = S3CannedACL.PublicRead;

                await _s3.PutObjectAsync(request, ct);

                var url = await BuildUrl(key, null);
                return new Response<StoredFile>
                {
                    Status = 201,
                    Message = "Fichier téléversé.",
                    Data = new StoredFile(
                        key, Provider, req.Visibility, url, req.File.FileName, contentType, req.File.Length),
                };
            }
            catch (Exception ex) when (IsUnavailable(ex))
            {
                _logger.LogError(ex, "S3 upload failed for key {Key} on {Endpoint}", key, EnvironmentVariables.S3_SERVICE_URL);
                return Fail<StoredFile>(503, "Stockage indisponible.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "S3 upload failed for key {Key}", key);
                return Fail<StoredFile>(500, "Le téléversement a échoué.");
            }
        }

        // ---- Download ----

        public async Task<Response<StoredFileStream>> Download(string key, CancellationToken ct)
        {
            if (!StorageHelper.IsValidKey(key))
                return Fail<StoredFileStream>(400, "Clé de fichier invalide.");

            try
            {
                var response = await _s3.GetObjectAsync(Bucket, key, ct);
                var contentType = string.IsNullOrEmpty(response.Headers.ContentType)
                    ? StorageHelper.ContentTypeFor(key)
                    : response.Headers.ContentType;

                return new Response<StoredFileStream>
                {
                    Status = 200,
                    Message = "Fichier récupéré.",
                    // The caller owns the stream; disposing it releases the underlying response.
                    Data = new StoredFileStream(
                        response.ResponseStream, contentType, Path.GetFileName(key), response.Headers.ContentLength),
                };
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return Fail<StoredFileStream>(404, "Fichier introuvable.");
            }
            catch (Exception ex) when (IsUnavailable(ex))
            {
                _logger.LogError(ex, "S3 download failed for key {Key}", key);
                return Fail<StoredFileStream>(503, "Stockage indisponible.");
            }
        }

        // ---- Delete ----

        public async Task<Response<object>> Delete(string key, CancellationToken ct)
        {
            if (!StorageHelper.IsValidKey(key))
                return Fail<object>(400, "Clé de fichier invalide.");

            try
            {
                // DeleteObject is idempotent on S3, so check first to report a real 404.
                var exists = await Exists(key, ct);
                if (exists.Status != 200)
                    return Fail<object>(exists.Status, exists.Message);

                await _s3.DeleteObjectAsync(Bucket, key, ct);
                return new Response<object> { Status = 200, Message = "Fichier supprimé." };
            }
            catch (Exception ex) when (IsUnavailable(ex))
            {
                _logger.LogError(ex, "S3 delete failed for key {Key}", key);
                return Fail<object>(503, "Stockage indisponible.");
            }
        }

        // ---- URLs ----

        public async Task<Response<string>> GetUrl(string key, TimeSpan? ttl, CancellationToken ct)
        {
            if (!StorageHelper.IsValidKey(key))
                return Fail<string>(400, "Clé de fichier invalide.");

            try
            {
                return new Response<string>
                {
                    Status = 200,
                    Message = "Lien généré.",
                    Data = await BuildUrl(key, ttl),
                };
            }
            catch (Exception ex) when (IsUnavailable(ex))
            {
                _logger.LogError(ex, "S3 presign failed for key {Key}", key);
                return Fail<string>(503, "Stockage indisponible.");
            }
        }

        public async Task<Response<bool>> Exists(string key, CancellationToken ct)
        {
            if (!StorageHelper.IsValidKey(key))
                return Fail<bool>(400, "Clé de fichier invalide.");

            try
            {
                await _s3.GetObjectMetadataAsync(Bucket, key, ct);
                return new Response<bool> { Status = 200, Message = "Fichier trouvé.", Data = true };
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                return new Response<bool> { Status = 404, Message = "Fichier introuvable.", Data = false };
            }
            catch (Exception ex) when (IsUnavailable(ex))
            {
                _logger.LogError(ex, "S3 metadata lookup failed for key {Key}", key);
                return Fail<bool>(503, "Stockage indisponible.");
            }
        }

        // ---- Helpers ----

        /// <summary>
        /// Public objects get the plain S3_PUBLIC_URL path; everything else gets a presigned GET.
        /// Note the presigned signature is bound to the host it was signed with — S3_PUBLIC_URL
        /// and S3_SERVICE_URL must resolve to the host clients actually hit, or links come back 403.
        /// </summary>
        private async Task<string> BuildUrl(string key, TimeSpan? ttl)
        {
            if (StorageHelper.VisibilityOf(key) == StorageVisibilityEnum.Public &&
                EnvironmentVariables.S3_PUBLIC_READ)
                return $"{EnvironmentVariables.S3_PUBLIC_URL.TrimEnd('/')}/{key}";

            var lifetime = ttl ?? TimeSpan.FromMinutes(EnvironmentVariables.SIGNED_URL_MINUTES);
            var signed = await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
            {
                BucketName = Bucket,
                Key = key,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(lifetime),
            });

            return AlignScheme(signed);
        }

        /// <summary>
        /// The SDK's endpoint resolution emits https:// even when ServiceURL is plaintext, which
        /// makes every signed link unusable against a local SeaweedFS gateway. SigV4 signs the
        /// host, not the scheme, so realigning the scheme alone keeps the signature valid. Only
        /// applied when the signed host is the configured endpoint, to leave real AWS untouched.
        /// </summary>
        private static string AlignScheme(string signedUrl)
        {
            if (!Uri.TryCreate(signedUrl, UriKind.Absolute, out var signed) ||
                !Uri.TryCreate(EnvironmentVariables.S3_SERVICE_URL, UriKind.Absolute, out var service))
                return signedUrl;

            if (!string.Equals(signed.Host, service.Host, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(signed.Scheme, service.Scheme, StringComparison.OrdinalIgnoreCase))
                return signedUrl;

            return new UriBuilder(signed) { Scheme = service.Scheme, Port = signed.Port }.Uri.ToString();
        }

        private static Response<T> Fail<T>(int status, string message) => new() { Status = status, Message = message };
    }
}
