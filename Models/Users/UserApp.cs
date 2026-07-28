using BoilerPlateApi.Models.Interfaces;
using BoilerPlateApi.Utilities;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BoilerPlateApi.Models.Users
{
    public class UserApp : IdentityUser<Guid>, IArchivable, IUpdateable, ICreatable
    {
        [Required]
        [MaxLength(64)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string LastName { get; set; } = string.Empty;

        public DateTimeOffset? DateOfBirth { get; set; }

        [MaxLength(500)]
        public string? ImgUrl { get; set; }

        // How this account signs in, and the external provider's subject id (Google "sub").
        public AuthProviderEnum AuthProvider { get; set; } = AuthProviderEnum.Local;

        [MaxLength(256)]
        public string? ProviderKey { get; set; }

        public bool DataProcessingConsent { get; set; }
        public bool PrivacyPolicyConsent { get; set; }

        [Column(TypeName = "timestamp with time zone")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Column(TypeName = "timestamp with time zone")]
        public DateTimeOffset? UpdatedAt { get; set; }

        [Column(TypeName = "timestamp with time zone")]
        public DateTimeOffset? ArchivedAt { get; set; }

        [ForeignKey(nameof(Status))]
        public Guid StatusId { get; set; }
        public StatusAccount? Status { get; set; }
    }

}
