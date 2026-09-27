using System.ComponentModel.DataAnnotations;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.ViewModels
{
    public class RoomSearchViewModel
    {
        [DataType(DataType.Date)]
        [Display(Name = "Ngày nhận phòng")]
        public DateOnly? CheckInDate { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Ngày trả phòng")]
        public DateOnly? CheckOutDate { get; set; }

        [Display(Name = "Loại phòng")]
        public int? RoomTypeId { get; set; }

        [Display(Name = "Số khách")]
        public int NumGuests { get; set; } = 1;
    }

    public class UpdateRoomStatusViewModel
    {
        [Required]
        public RoomStatus Status { get; set; }
    }

    public class RoomGridViewModel
    {
        public List<FloorData> Floors { get; set; } = new();
        public RoomSummary Summary { get; set; } = new();
    }

    public class FloorData
    {
        public int Floor { get; set; }
        public List<RoomGridItem> Rooms { get; set; } = new();
        public RoomSummary Summary { get; set; } = new();
    }

    public class RoomGridItem
    {
        public int Id { get; set; }
        public string RoomNumber { get; set; } = string.Empty;
        public RoomStatus Status { get; set; }
        public string RoomTypeName { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public string? GuestName { get; set; }
        public string? BookingCode { get; set; }
        public DateOnly? CheckInDate { get; set; }
        public DateOnly? CheckOutDate { get; set; }
    }

    public class RoomSummary
    {
        public int Total { get; set; }
        public int Available { get; set; }
        public int Occupied { get; set; }
        public int Cleaning { get; set; }
        public int Maintenance { get; set; }
    }
}
