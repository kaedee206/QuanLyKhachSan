using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.Entities
{
    [Table("room_type")]
    public class RoomType
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("description")]
        public string? Description { get; set; }

        [Required]
        [Column("base_price", TypeName = "decimal(12,2)")]
        public decimal BasePrice { get; set; }

        [Required]
        [Column("max_guests")]
        public int MaxGuests { get; set; }

        [Column("amenities")]
        public string? Amenities { get; set; }

        [MaxLength(255)]
        [Column("image_url")]
        public string? ImageUrl { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public ICollection<Room> Rooms { get; set; } = new List<Room>();
        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    }
}