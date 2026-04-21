using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.Entities
{
    [Table("invoice")]
    public class Invoice
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("booking_id")]
        public int BookingId { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("invoice_number")]
        public string InvoiceNumber { get; set; } = string.Empty;

        [Column("room_charge", TypeName = "decimal(12,2)")]
        public decimal RoomCharge { get; set; }

        [Column("service_charge", TypeName = "decimal(12,2)")]
        public decimal ServiceCharge { get; set; }

        [Column("discount", TypeName = "decimal(12,2)")]
        public decimal Discount { get; set; }

        [Column("total_amount", TypeName = "decimal(12,2)")]
        public decimal TotalAmount { get; set; }

        [Column("payment_method")]
        public PaymentMethod PaymentMethod { get; set; }

        [Column("payment_status")]
        public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

        [Column("payment_date")]
        public DateTime? PaymentDate { get; set; }

        // ── VietQR fields ────────────────────────────────
        [MaxLength(500)]
        [Column("vietqr_image_url")]
        public string? VietqrImageUrl { get; set; }

        [MaxLength(50)]
        [Column("vietqr_bank_code")]
        public string? VietqrBankCode { get; set; }

        [MaxLength(20)]
        [Column("vietqr_account_no")]
        public string? VietqrAccountNo { get; set; }

        [MaxLength(100)]
        [Column("vietqr_account_name")]
        public string? VietqrAccountName { get; set; }

        [Column("vietqr_amount", TypeName = "decimal(12,2)")]
        public decimal? VietqrAmount { get; set; }

        [MaxLength(200)]
        [Column("vietqr_content")]
        public string? VietqrContent { get; set; }

        [Column("vietqr_paid_at")]
        public DateTime? VietqrPaidAt { get; set; }

        [MaxLength(100)]
        [Column("vietqr_reference")]
        public string? VietqrReference { get; set; }

        // ── MoMo payment fields ─────────────────────────
        [MaxLength(100)]
        [Column("momo_order_id")]
        public string? MomoOrderId { get; set; }

        [MaxLength(100)]
        [Column("momo_request_id")]
        public string? MomoRequestId { get; set; }

        [MaxLength(500)]
        [Column("momo_pay_url")]
        public string? MomoPayUrl { get; set; }

        [MaxLength(50)]
        [Column("momo_result_code")]
        public string? MomoResultCode { get; set; }

        [MaxLength(200)]
        [Column("momo_message")]
        public string? MomoMessage { get; set; }

        [MaxLength(100)]
        [Column("momo_trans_id")]
        public string? MomoTransId { get; set; }

        [Column("momo_paid_at")]
        public DateTime? MomoPaidAt { get; set; }
        // ───────────────────────────────────────────────

        // ── SePay payment fields ───────────────────────
        [MaxLength(100)]
        [Column("sepay_order_id")]
        public string? SePayOrderId { get; set; }

        [MaxLength(100)]
        [Column("sepay_order_code")]
        public string? SePayOrderCode { get; set; }

        [MaxLength(100)]
        [Column("sepay_transaction_id")]
        public string? SePayTransactionId { get; set; }

        [MaxLength(50)]
        [Column("sepay_payment_method")]
        public string? SePayPaymentMethod { get; set; }

        [MaxLength(50)]
        [Column("sepay_order_status")]
        public string? SePayOrderStatus { get; set; }

        [Column("sepay_created_at")]
        public DateTime? SePayCreatedAt { get; set; }

        [Column("sepay_paid_at")]
        public DateTime? SePayPaidAt { get; set; }

        [Column("sepay_last_check_at")]
        public DateTime? SePayLastCheckAt { get; set; }

        [Column("sepay_polling_expires_at")]
        public DateTime? SePayPollingExpiresAt { get; set; }
        // ───────────────────────────────────────────────

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("created_by")]
        public int CreatedById { get; set; }

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        [ForeignKey("BookingId")]
        public Booking Booking { get; set; } = null!;

        [ForeignKey("CreatedById")]
        public User Creator { get; set; } = null!;
    }
}