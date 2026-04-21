namespace QuanLyKhachSan.Models.Enums
{
    /// <summary>
    /// Trạng thái phòng trong khách sạn
    /// </summary>
    public enum RoomStatus
    {
        Available = 0,
        Occupied = 1,
        Cleaning = 2,
        Maintenance = 3
    }

    /// <summary>
    /// Vai trò người dùng trong hệ thống
    /// </summary>
    public enum UserRole
    {
        Customer = 0,
        Admin = 1,
        Manager = 2,
        Receptionist = 3,
        Housekeeping = 4,
        Maintenance = 5
    }

    /// <summary>
    /// Trạng thái đặt phòng
    /// </summary>
    public enum BookingStatus
    {
        Pending = 0,
        Confirmed = 1,
        CheckedIn = 2,
        CheckedOut = 3,
        Cancelled = 4,
        NoShow = 5
    }

    /// <summary>
    /// Loại dịch vụ khách sạn
    /// </summary>
    public enum ServiceType
    {
        Laundry = 0,
        Minibar = 1,
        Breakfast = 2,
        Transport = 3,
        Extra = 4,
        VietQR = 5,
        MoMo = 6
    }

    /// <summary>
    /// Phương thức thanh toán
    /// </summary>
    public enum PaymentMethod
    {
        Cash = 0,
        Transfer = 1,
        VietQR = 2,
        Card = 3,
        MoMo = 4,
        SePay = 5
    }

    /// <summary>
    /// Trạng thái thanh toán
    /// </summary>
    public enum PaymentStatus
    {
        Unpaid = 0,
        Pending = 1,
        Paid = 2,
        Refunded = 3,
        Cancelled = 4
    }

    /// <summary>
    /// Mức độ ưu tiên của ticket yêu cầu
    /// </summary>
    public enum TicketPriority
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Urgent = 3
    }

    /// <summary>
    /// Trạng thái ticket yêu cầu
    /// </summary>
    public enum TicketStatus
    {
        Open = 0,
        InProgress = 1,
        Resolved = 2,
        Closed = 3
    }

    /// <summary>
    /// Loại ticket yêu cầu
    /// </summary>
    public enum TicketType
    {
        Maintenance = 0,
        Housekeeping = 1,
        Service = 2,
        Other = 3
    }

    /// <summary>
    /// Trạng thái email trong hàng đợi
    /// </summary>
    public enum EmailStatus
    {
        Pending = 0,
        Sent = 1,
        Failed = 2
    }
}