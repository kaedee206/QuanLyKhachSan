using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.Entities
{
    [Table("booking")]
    public class Booking
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("booking_code")]
        public string BookingCode { get; set; } = string.Empty;

        [Column("room_id")]
        public int? RoomId { get; set; }

        [Column("room_type_id")]
        public int RoomTypeId { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("guest_name")]
        public string GuestName { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        [Column("phone")]
        public string Phone { get; set; } = string.Empty;

        [MaxLength(100)]
        [Column("email")]
        public string? Email { get; set; }

        [MaxLength(20)]
        [Column("id_number")]
        public string? IdNumber { get; set; }

        [Column("check_in_date")]
        public DateOnly CheckInDate { get; set; }

        [Column("check_out_date")]
        public DateOnly CheckOutDate { get; set; }

        [Column("actual_check_in")]
        public DateTime? ActualCheckIn { get; set; }

        [Column("actual_check_out")]
        public DateTime? ActualCheckOut { get; set; }

        [Column("num_guests")]
        public int NumGuests { get; set; } = 1;

        [Column("status")]
        public BookingStatus Status { get; set; } = BookingStatus.Pending;

        [Column("total_amount", TypeName = "decimal(12,2)")]
        public decimal TotalAmount { get; set; }

        [Column("notes")]
        public string? Notes { get; set; }

        [Column("created_by")]
        public int? CreatedBy { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        [ForeignKey("RoomId")]
        public Room? Room { get; set; }

        [ForeignKey("RoomTypeId")]
        public RoomType RoomType { get; set; } = null!;

        public ICollection<Service> Services { get; set; } = new List<Service>();
        public Invoice? Invoice { get; set; }
    }
}