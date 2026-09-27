using System.ComponentModel.DataAnnotations;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.ViewModels
{
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

    public class PaymentViewModel
    {
        public Booking? Booking { get; set; }
        public Invoice? Invoice { get; set; }
        public string BookingCode { get; set; } = "";
        public string InvoiceNumber { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public bool IsMoMoEnabled { get; set; }
        public bool IsVietQREnabled { get; set; }
        public bool IsSePayEnabled { get; set; } = true;
    }
}
