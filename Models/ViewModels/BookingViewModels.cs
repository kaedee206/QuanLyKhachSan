using System.ComponentModel.DataAnnotations;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.ViewModels
{
    public class CreateBookingViewModel
    {
        [Required(ErrorMessage = "Vui lòng chọn loại phòng")]
        [Display(Name = "Loại phòng")]
        public int? RoomTypeId { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên khách")]
        [MaxLength(100)]
        [Display(Name = "Tên khách")]
        public string GuestName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập số điện thoại")]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
        [Display(Name = "Số điện thoại")]
        public string Phone { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Email không hợp lệ")]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [MaxLength(20)]
        [Display(Name = "CMND/CCCD")]
        public string? IdNumber { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn ngày nhận phòng")]
        [DataType(DataType.Date)]
        [Display(Name = "Ngày nhận phòng")]
        public DateOnly CheckInDate { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn ngày trả phòng")]
        [DataType(DataType.Date)]
        [Display(Name = "Ngày trả phòng")]
        public DateOnly CheckOutDate { get; set; }

        [Range(1, 10, ErrorMessage = "Số khách phải từ 1-10")]
        [Display(Name = "Số khách")]
        public int NumGuests { get; set; } = 1;

        [Display(Name = "Ghi chú")]
        public string? Notes { get; set; }
    }

    public class BookingLookupViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập mã đặt phòng")]
        [Display(Name = "Mã đặt phòng")]
        public string BookingCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập số điện thoại")]
        [Display(Name = "Số điện thoại")]
        public string Phone { get; set; } = string.Empty;
    }

    public class ConfirmBookingViewModel
    {
        [Required]
        public int RoomId { get; set; }
    }

    public class CheckInViewModel
    {
        public int? RoomId { get; set; }
        public DateTime? ActualCheckIn { get; set; }
    }

    public class CheckOutViewModel
    {
        public DateTime? ActualCheckOut { get; set; }

        [Range(0, 100, ErrorMessage = "Giảm giá phải từ 0-100%")]
        [Display(Name = "Giảm giá (%)")]
        public decimal Discount { get; set; }

        [Display(Name = "Ghi chú")]
        public string? Notes { get; set; }

        [Display(Name = "Phương thức thanh toán")]
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    }

    public class BookingFilterViewModel
    {
        public string? Search { get; set; }
        public BookingStatus? Status { get; set; }
        public DateOnly? FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class BookingDetailViewModel
    {
        public Booking Booking { get; set; } = null!;
        public List<Service> Services { get; set; } = new();
        public Invoice? Invoice { get; set; }
        public List<Room> AvailableRooms { get; set; } = new();
    }
}
