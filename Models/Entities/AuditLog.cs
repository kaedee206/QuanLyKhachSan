using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanLyKhachSan.Models.Entities
{
    [Table("audit_log")]
    public class AuditLog
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("user_id")]
        public int? UserId { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("action")]
        public string Action { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        [Column("entity_type")]
        public string EntityType { get; set; } = string.Empty;

        [Column("entity_id")]
        public int? EntityId { get; set; }

        [Column("old_value")]
        public string? OldValue { get; set; }

        [Column("new_value")]
        public string? NewValue { get; set; }

        [MaxLength(45)]
        [Column("ip_address")]
        public string? IpAddress { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        [ForeignKey("UserId")]
        public User? User { get; set; }
    }
}