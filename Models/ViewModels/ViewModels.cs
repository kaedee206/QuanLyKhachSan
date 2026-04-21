using System.ComponentModel.DataAnnotations;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.ViewModels
{
    // ═══════════════════════════════════════════════════════════
    // AUTH VIEW MODELS
    // ═══════════════════════════════════════════════════════════

    public class LoginViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập")]
        [Display(Name = "Tên đăng nhập")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu")]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }

    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu hiện tại")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới")]
        [MinLength(8, ErrorMessage = "Mật khẩu tối thiểu 8 ký tự")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu mới")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu mới")]
        [Compare("NewPassword", ErrorMessage = "Mật khẩu xác nhận không khớp")]
        [DataType(DataType.Password)]
        [Display(Name = "Xác nhận mật khẩu mới")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    // ═══════════════════════════════════════════════════════════
    // BOOKING VIEW MODELS
    // ═══════════════════════════════════════════════════════════

    public class CreateBookingViewModel
    {
        [Required(ErrorMessage = "Vui lòng chọn loại phòng")]
        [Display(Name = "Loại phòng")]
        public int RoomTypeId { get; set; }

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
        public Entities.Booking Booking { get; set; } = null!;
        public List<Entities.Service> Services { get; set; } = new();
        public Entities.Invoice? Invoice { get; set; }
        public List<Entities.Room> AvailableRooms { get; set; } = new();
    }

    // ═══════════════════════════════════════════════════════════
    // ROOM VIEW MODELS
    // ═══════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════
    // SERVICE VIEW MODELS
    // ═══════════════════════════════════════════════════════════

    public class AddServiceViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập mã đặt phòng")]
        [Display(Name = "Mã đặt phòng")]
        public string BookingCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập tên dịch vụ")]
        [MaxLength(100)]
        [Display(Name = "Tên dịch vụ")]
        public string ServiceName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Loại dịch vụ")]
        public ServiceType ServiceType { get; set; }

        [Range(1, 100)]
        [Display(Name = "Số lượng")]
        public int Quantity { get; set; } = 1;

        [Required]
        [Range(0, double.MaxValue)]
        [Display(Name = "Đơn giá")]
        public decimal UnitPrice { get; set; }

        [Display(Name = "Ghi chú")]
        public string? Notes { get; set; }
    }

    // ═══════════════════════════════════════════════════════════
    // INVOICE VIEW MODELS
    // ═══════════════════════════════════════════════════════════

    public class ConfirmPaymentViewModel
    {
        [Required]
        public PaymentMethod PaymentMethod { get; set; }
        public DateTime? PaymentDate { get; set; }
    }

    public class VietQRViewModel
    {
        public string BankId { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string AccountNo { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Content { get; set; } = string.Empty;
        public string QrImageUrl { get; set; } = string.Empty;
        public string InvoiceNumber { get; set; } = string.Empty;
    }

    /// <summary>
    /// ViewModel cho trang thanh toán MoMo
    /// </summary>
    public class MoMoPaymentViewModel
    {
        public string InvoiceNumber { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? PayUrl { get; set; }
        public string? OrderId { get; set; }
        public string? Message { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    // ═══════════════════════════════════════════════════════════
    // TICKET VIEW MODELS
    // ═══════════════════════════════════════════════════════════

    public class CreateTicketViewModel
    {
        [Required(ErrorMessage = "Vui lòng chọn phòng")]
        [Display(Name = "Phòng")]
        public int RoomId { get; set; }

        [Required]
        [Display(Name = "Loại ticket")]
        public TicketType Type { get; set; } = TicketType.Maintenance;

        [Required(ErrorMessage = "Vui lòng nhập mô tả")]
        [Display(Name = "Mô tả")]
        public string Description { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Mức độ ưu tiên")]
        public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    }

    public class UpdateTicketViewModel
    {
        public TicketStatus? Status { get; set; }
        public int? AssignedToId { get; set; }
        public string? ResolutionNotes { get; set; }
    }

    public class TicketFilterViewModel
    {
        public TicketStatus? Status { get; set; }
        public TicketType? Type { get; set; }
        public TicketPriority? Priority { get; set; }
        public int? RoomId { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    // ═══════════════════════════════════════════════════════════
    // USER VIEW MODELS
    // ═══════════════════════════════════════════════════════════

    public class CreateUserViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập")]
        [MaxLength(50)]
        [Display(Name = "Tên đăng nhập")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu")]
        [MinLength(8)]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập họ tên")]
        [MaxLength(100)]
        [Display(Name = "Họ tên")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Vai trò")]
        public UserRole Role { get; set; }

        [EmailAddress]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [Phone]
        [Display(Name = "Số điện thoại")]
        public string? Phone { get; set; }
    }

    public class EditUserViewModel
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Display(Name = "Họ tên")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Vai trò")]
        public UserRole Role { get; set; }

        [EmailAddress]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [Phone]
        [Display(Name = "Số điện thoại")]
        public string? Phone { get; set; }

        [Display(Name = "Trạng thái")]
        public bool IsActive { get; set; } = true;
    }

    public class ResetPasswordViewModel
    {
        [Required]
        public int UserId { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới")]
        [MinLength(8)]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu mới")]
        public string NewPassword { get; set; } = string.Empty;
    }

    // ═══════════════════════════════════════════════════════════
    // REPORT VIEW MODELS
    // ═══════════════════════════════════════════════════════════

    public class ReportFilterViewModel
    {
        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Từ ngày")]
        public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddMonths(-1));

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Đến ngày")]
        public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        public string GroupBy { get; set; } = "day";
    }

    public class DashboardViewModel
    {
        public int TodayNewBookings { get; set; }
        public decimal TodayRevenue { get; set; }
        public RoomSummary RoomStats { get; set; } = new();
        public decimal OccupancyRate { get; set; }
        public int PendingTickets { get; set; }
        public int ActiveBookings { get; set; }
        public int PendingPayments { get; set; }
        public List<Entities.Booking> RecentBookings { get; set; } = new();
        public List<RoomGridItem> RoomGrid { get; set; } = new();
    }

    public class RevenueReportViewModel
    {
        public ReportFilterViewModel Filter { get; set; } = new();
        public decimal TotalRevenue { get; set; }
        public decimal RoomRevenue { get; set; }
        public decimal ServiceRevenue { get; set; }
        public decimal TotalDiscount { get; set; }
        public int TotalInvoices { get; set; }
        public decimal AvgInvoice { get; set; }
        public List<RevenueBreakdown> Breakdown { get; set; } = new();
        public List<PaymentMethodStat> PaymentMethods { get; set; } = new();
    }

    public class RevenueBreakdown
    {
        public string Period { get; set; } = string.Empty;
        public int InvoiceCount { get; set; }
        public decimal Revenue { get; set; }
        public decimal RoomRevenue { get; set; }
        public decimal ServiceRevenue { get; set; }
    }

    public class PaymentMethodStat
    {
        public string Method { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal Total { get; set; }
    }

    // ═══════════════════════════════════════════════════════════
    // PAYMENT VIEW MODELS
    // ═══════════════════════════════════════════════════════════

    public class PaymentViewModel
    {
        public Entities.Booking? Booking { get; set; }
        public Entities.Invoice? Invoice { get; set; }
        public string BookingCode { get; set; } = "";
        public string InvoiceNumber { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public bool IsMoMoEnabled { get; set; }
        public bool IsVietQREnabled { get; set; }
        public bool IsSePayEnabled { get; set; } = true;
    }

    // ═══════════════════════════════════════════════════════════
    // PAGINATION
    // ═══════════════════════════════════════════════════════════

    public class PaginatedList<T>
    {
        public List<T> Items { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
        public bool HasPrevious => Page > 1;
        public bool HasNext => Page < TotalPages;
    }
}