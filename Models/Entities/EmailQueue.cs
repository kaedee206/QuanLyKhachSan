using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.Entities
{
    [Table("email_queue")]
    public class EmailQueue
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("to_email")]
        public string ToEmail { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        [Column("subject")]
        public string Subject { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        [Column("template")]
        public string Template { get; set; } = string.Empty;

        [Column("template_data")]
        public string? TemplateData { get; set; }

        [Column("status")]
        public EmailStatus Status { get; set; } = EmailStatus.Pending;

        [Column("attempts")]
        public int Attempts { get; set; }

        [Column("last_error")]
        public string? LastError { get; set; }

        [Column("sent_at")]
        public DateTime? SentAt { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}