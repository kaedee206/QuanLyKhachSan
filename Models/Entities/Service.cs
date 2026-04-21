using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.Entities
{
    [Table("service")]
    public class Service
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("booking_id")]
        public int BookingId { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("service_name")]
        public string ServiceName { get; set; } = string.Empty;

        [Column("service_type")]
        public ServiceType ServiceType { get; set; }

        [Column("quantity")]
        public int Quantity { get; set; } = 1;

        [Column("unit_price", TypeName = "decimal(12,2)")]
        public decimal UnitPrice { get; set; }

        [Column("total_amount", TypeName = "decimal(12,2)")]
        public decimal TotalAmount { get; set; }

        [Column("notes")]
        public string? Notes { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        [ForeignKey("BookingId")]
        public Booking Booking { get; set; } = null!;
    }
}