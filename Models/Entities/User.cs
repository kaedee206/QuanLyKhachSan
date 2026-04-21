using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.Entities
{
    [Table("user")]
    public class User
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("username")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        [Column("password")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        [Column("full_name")]
        public string FullName { get; set; } = string.Empty;

        [Column("role")]
        public UserRole Role { get; set; }

        [MaxLength(100)]
        [Column("email")]
        public string? Email { get; set; }

        [MaxLength(20)]
        [Column("phone")]
        public string? Phone { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("last_login")]
        public DateTime? LastLogin { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
        public ICollection<Ticket> ReportedTickets { get; set; } = new List<Ticket>();
        public ICollection<Ticket> AssignedTickets { get; set; } = new List<Ticket>();
        public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    }
}