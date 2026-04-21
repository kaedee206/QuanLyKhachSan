using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.Entities
{
    [Table("ticket")]
    public class Ticket
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        [Column("ticket_number")]
        public string TicketNumber { get; set; } = string.Empty;

        [Column("room_id")]
        public int RoomId { get; set; }

        [Column("type")]
        public TicketType Type { get; set; } = TicketType.Maintenance;

        [Required]
        [Column("description")]
        public string Description { get; set; } = string.Empty;

        [Column("priority")]
        public TicketPriority Priority { get; set; } = TicketPriority.Medium;

        [Column("status")]
        public TicketStatus Status { get; set; } = TicketStatus.Open;

        [Column("reported_by")]
        public int ReportedById { get; set; }

        [Column("assigned_to")]
        public int? AssignedToId { get; set; }

        [MaxLength(255)]
        [Column("image_url")]
        public string? ImageUrl { get; set; }

        [Column("resolution_notes")]
        public string? ResolutionNotes { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("resolved_at")]
        public DateTime? ResolvedAt { get; set; }

        [Column("closed_at")]
        public DateTime? ClosedAt { get; set; }

        // Navigation properties
        [ForeignKey("RoomId")]
        public Room Room { get; set; } = null!;

        [ForeignKey("ReportedById")]
        public User Reporter { get; set; } = null!;

        [ForeignKey("AssignedToId")]
        public User? Assignee { get; set; }
    }
}