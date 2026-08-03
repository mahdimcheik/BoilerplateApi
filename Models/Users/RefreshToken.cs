using System.ComponentModel.DataAnnotations;

namespace InteractivesApi.Models.Users
{
    public class RefreshToken
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string TokenHash { get; set; } = string.Empty;

        public DateTimeOffset ExpirationDate { get; set; }

        public Guid UserId { get; set; }
        public UserApp? User { get; set; }
    }
}
