using System.ComponentModel.DataAnnotations;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Services;

namespace QuanLyKhachSan.Models.ViewModels
{
    public class CustomerLoginViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập Mã hóa đơn hoặc Mã đặt phòng")]
        [Display(Name = "Mã hóa đơn / Mã đặt phòng")]
        public string BookingOrInvoiceCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập Căn cước công dân (CCCD) hoặc Hộ chiếu")]
        [Display(Name = "Số CCCD / Hộ chiếu")]
        public string IdNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập Email")]
        [EmailAddress(ErrorMessage = "Email không đúng định dạng")]
        [Display(Name = "Địa chỉ Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập Số điện thoại")]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
        [Display(Name = "Số điện thoại")]
        public string Phone { get; set; } = string.Empty;
    }

    public class VerifyCustomerOtpViewModel
    {
        [Required]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mã OTP 6 chữ số")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã OTP phải gồm 6 chữ số")]
        [Display(Name = "Mã xác thực OTP")]
        public string OtpCode { get; set; } = string.Empty;

        public string MaskedEmail { get; set; } = string.Empty;
        public int ExpiresInSeconds { get; set; } = 600;
        public int ResendCooldownSeconds { get; set; } = 60;
    }

    public class CustomerDashboardViewModel
    {
        public Booking Booking { get; set; } = null!;
        public Invoice? Invoice { get; set; }
        public decimal RoomCharge { get; set; }
        public decimal ExtraServicesTotal { get; set; }
        public decimal CurrentTotalAmount { get; set; }
        public bool IsRoomPaid { get; set; }
        public List<Service> ActiveServices { get; set; } = new();
        public List<Ticket> ActiveTickets { get; set; } = new();
        public List<HotelMenuItem> MenuItems { get; set; } = new();
    }

    public class CustomerOrderFoodViewModel
    {
        [Required]
        public string MenuItemId { get; set; } = string.Empty;

        [Range(1, 20, ErrorMessage = "Số lượng từ 1 đến 20")]
        public int Quantity { get; set; } = 1;

        public string? Notes { get; set; }
    }

    public class CustomerCreateTicketViewModel
    {
        [Required(ErrorMessage = "Vui lòng chọn phòng ban xử lý")]
        [Display(Name = "Phòng ban tiếp nhận")]
        public TicketType Type { get; set; } = TicketType.Service;

        [Required(ErrorMessage = "Vui lòng nhập nội dung yêu cầu")]
        [MaxLength(500, ErrorMessage = "Nội dung tối đa 500 ký tự")]
        [Display(Name = "Nội dung yêu cầu / Sự cố")]
        public string Description { get; set; } = string.Empty;

        public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    }
}
