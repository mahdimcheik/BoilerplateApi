using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BoilerPlateApi.Models.Interfaces
{
    public interface IArchivable
    {
        DateTimeOffset? ArchivedAt { get; set; }
    }

    public interface IUpdateable
    {
        DateTimeOffset? UpdatedAt { get; set; }
    }

    public interface ICreatable
    {
        DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>Base for all entities: Guid id + timestamps + soft-delete marker.</summary>
    public abstract class BaseModel : IUpdateable, ICreatable, IArchivable
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [Column(TypeName = "timestamp with time zone")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Column(TypeName = "timestamp with time zone")]
        public DateTimeOffset? UpdatedAt { get; set; }

        [Column(TypeName = "timestamp with time zone")]
        public DateTimeOffset? ArchivedAt { get; set; }
    }

    /// <summary>Base for lookup/option tables (adds a display name, color, icon).</summary>
    public abstract class BaseModelOption : BaseModel
    {
        [Required]
        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(16)]
        public string? Color { get; set; }

        [MaxLength(256)]
        public string? Icon { get; set; }
    }
}
