using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.Entities
{
    [Table("room")]
    public class Room
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("room_type_id")]
        public int RoomTypeId { get; set; }

        [Required]
        [MaxLength(10)]
        [Column("room_number")]
        public string RoomNumber { get; set; } = string.Empty;

        [Column("floor")]
        public int Floor { get; set; }

        [Column("status")]
        public RoomStatus Status { get; set; } = RoomStatus.Available;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        [ForeignKey("RoomTypeId")]
        public RoomType RoomType { get; set; } = null!;
        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
        public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
    }
}