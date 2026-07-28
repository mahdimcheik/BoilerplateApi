using System.ComponentModel.DataAnnotations;

namespace BoilerPlateApi.Models.Responses
{
    public class Response<T>
    {
        [Required]
        public string Message { get; set; } = string.Empty;

        [Required]
        public int Status { get; set; }

        public T? Data { get; set; }

        public long? Count { get; set; }
    }
}
