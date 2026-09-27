using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.ViewModels
{
    /// <summary>
    /// Kết quả tự kiểm tra (Self-Check) trước khi quyết định gửi lại email
    /// </summary>
    public class EmailSelfCheckResult
    {
        public bool CanSend { get; set; }
        public string Summary { get; set; } = string.Empty;
        public bool IsPermanentFailure { get; set; }
        public bool IsSmtpHealthy { get; set; }
        public string SmtpHealthDetail { get; set; } = string.Empty;
        public bool IsRecipientValid { get; set; }
        public string RecipientDetail { get; set; } = string.Empty;
        public bool IsContentValid { get; set; }
        public string ContentDetail { get; set; } = string.Empty;
        public bool IsDuplicateOrAlreadySent { get; set; }
        public bool IsWithinMaxAttempts { get; set; }
        public TimeSpan? CooldownRemaining { get; set; }
        public List<string> CheckMessages { get; set; } = new();
    }

    /// <summary>
    /// DTO hiển thị trạng thái chuyển phát email đến khách hàng
    /// </summary>
    public class EmailQueueItemViewModel
    {
        public int Id { get; set; }
        public string ToEmail { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public EmailStatus Status { get; set; }
        public int Attempts { get; set; }
        public string? LastError { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsDelivered => Status == EmailStatus.Sent;
        public bool CanRetry => Status != EmailStatus.Sent && Attempts < 5;
        public EmailSelfCheckResult? SelfCheck { get; set; }

        public string StatusBadgeClass => Status switch
        {
            EmailStatus.Sent => "bg-success text-white",
            EmailStatus.Pending => "bg-warning text-dark",
            EmailStatus.Failed => "bg-danger text-white",
            _ => "bg-secondary text-white"
        };

        public string StatusDisplay => Status switch
        {
            EmailStatus.Sent => "Đã gửi thành công",
            EmailStatus.Pending => "Đang chờ gửi / Thử lại",
            EmailStatus.Failed => "Gửi thất bại",
            _ => "Không xác định"
        };
    }

    /// <summary>
    /// ViewModel cho trang danh sách theo dõi email
    /// </summary>
    public class EmailIndexViewModel
    {
        public List<EmailQueueItemViewModel> Emails { get; set; } = new();
        public string? SearchEmail { get; set; }
        public EmailStatus? StatusFilter { get; set; }
        public int TotalEmails { get; set; }
        public int SentCount { get; set; }
        public int PendingCount { get; set; }
        public int FailedCount { get; set; }
        public bool IsSmtpHealthy { get; set; }
        public string SmtpStatusMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Kết quả trả về sau khi thực hiện retry
    /// </summary>
    public class EmailRetryResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public EmailSelfCheckResult? SelfCheckResult { get; set; }
    }
}
