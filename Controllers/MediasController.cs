using BoilerPlateApi.Models.Responses;
using BoilerPlateApi.Models.Storage;
using BoilerPlateApi.Services;
using BoilerPlateApi.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoilerPlateApi.Controllers
{
    [Produces("application/json")]
    [Route("[controller]")]
    [ApiController]
    [Authorize]
    public class MediasController : ControllerBase
    {
        private readonly IStorageResolver _storage;

        public MediasController(IStorageResolver storage)
        {
            _storage = storage;
        }

        [HttpPost]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<Response<StoredFile>>> Upload([FromForm] UploadRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(new Response<object> { Status = 400, Message = "Problème de validation." });

            var result = await _storage.Resolve(model.Provider).Upload(model, HttpContext.RequestAborted);
            return StatusCode(result.Status, result);
        }

        /// <summary>
        /// Link for the stored object: a plain URL when public, a time-limited signed one when
        /// private. <paramref name="minutes"/> overrides SIGNED_URL_MINUTES.
        /// </summary>
        [HttpGet("url")]
        public async Task<ActionResult<Response<string>>> GetUrl(
            [FromQuery] string key,
            [FromQuery] StorageProviderEnum? provider,
            [FromQuery] int? minutes)
        {
            var ttl = minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : (TimeSpan?)null;
            var result = await _storage.Resolve(provider).GetUrl(key, ttl, HttpContext.RequestAborted);
            return StatusCode(result.Status, result);
        }

        [HttpGet("exists")]
        public async Task<ActionResult<Response<bool>>> Exists(
            [FromQuery] string key,
            [FromQuery] StorageProviderEnum? provider)
        {
            var result = await _storage.Resolve(provider).Exists(key, HttpContext.RequestAborted);
            return StatusCode(result.Status, result);
        }

        /// <summary>Streams the object to an authenticated caller, no signature needed.</summary>
        // No [Produces] override here: FileStreamResult sets its own Content-Type and is exempt
        // from the class-level one, while the error branch still needs to negotiate as JSON.
        [HttpGet("download")]
        public async Task<IActionResult> Download(
            [FromQuery] string key,
            [FromQuery] StorageProviderEnum? provider)
        {
            var result = await _storage.Resolve(provider).Download(key, HttpContext.RequestAborted);
            return Serve(result);
        }

        /// <summary>
        /// Target of the signed URLs produced by the local backend. Anonymous by design — the
        /// HMAC in the query string is the credential, so it is verified before anything is read.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("file/{**key}")]
        public async Task<IActionResult> SignedFile(
            string key,
            [FromQuery] long exp,
            [FromQuery] string? sig,
            [FromQuery] StorageProviderEnum? provider)
        {
            if (!StorageHelper.IsValidKey(key) || !StorageHelper.Verify(key, exp, sig))
                return StatusCode(403, new Response<object> { Status = 403, Message = "Lien invalide ou expiré." });

            var result = await _storage.Resolve(provider).Download(key, HttpContext.RequestAborted);
            return Serve(result);
        }

        [HttpDelete]
        [Authorize(Roles = $"{HardCode.ROLE_NAME_SUPER_ADMIN},{HardCode.ROLE_NAME_ADMIN}")]
        public async Task<ActionResult<Response<object>>> Delete(
            [FromQuery] string key,
            [FromQuery] StorageProviderEnum? provider)
        {
            var result = await _storage.Resolve(provider).Delete(key, HttpContext.RequestAborted);
            return StatusCode(result.Status, result);
        }

        /// <summary>Turns a storage result into either a file stream or the usual JSON envelope.</summary>
        private IActionResult Serve(Response<StoredFileStream> result)
        {
            if (result.Status != 200 || result.Data is null)
                return StatusCode(result.Status, new Response<object> { Status = result.Status, Message = result.Message });

            return File(result.Data.Content, result.Data.ContentType, result.Data.FileName, enableRangeProcessing: true);
        }
    }
}
