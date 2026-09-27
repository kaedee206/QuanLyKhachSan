using System;
using System.Collections.Concurrent;

namespace QuanLyKhachSan.Services
{
    public class EmailRateLimitResult
    {
        public bool IsAllowed { get; set; }
        public bool Allowed => IsAllowed;
        public int WaitSeconds { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string Message => ErrorMessage;
    }

    /// <summary>
    /// Bộ điều tiết giới hạn tần suất gửi email (Anti-spam, Anti-hammering & SMTP Quota Protection)
    /// Hỗ trợ cả 2 lớp bảo vệ:
    /// 1. Cooldown giữa 2 lần gửi liên tiếp (mặc định 60 giây).
    /// 2. Hạn mức tối đa theo khung thời gian (mặc định tối đa 5 lần trong 15 phút).
    /// </summary>
    public class EmailRateLimiter
    {
        public static EmailRateLimiter Instance { get; } = new EmailRateLimiter();

        private class RateRecord
        {
            public DateTime LastSentAt { get; set; } = DateTime.MinValue;
            public int AttemptsInWindow { get; set; } = 0;
            public DateTime WindowExpiresAt { get; set; } = DateTime.MinValue;
        }

        private readonly ConcurrentDictionary<string, RateRecord> _records = new();

        /// <summary>
        /// Kiểm tra và ghi nhận 1 lần gửi email nếu thỏa mãn điều kiện rate limit.
        /// </summary>
        /// <param name="key">Định danh cần rate limit (Email, Token hoặc IP)</param>
        /// <param name="cooldownSeconds">Thời gian chờ tối thiểu giữa 2 lần gửi liên tiếp (giây)</param>
        /// <param name="maxInWindow">Số lần gửi tối đa trong một khung thời gian</param>
        /// <param name="windowMinutes">Độ dài khung thời gian (phút)</param>
        public EmailRateLimitResult CheckAndConsume(string key, int cooldownSeconds = 60, int maxInWindow = 5, int windowMinutes = 15)
        {
            var cleanKey = (key ?? "unknown").Trim().ToLowerInvariant();
            var now = DateTime.UtcNow;

            var record = _records.GetOrAdd(cleanKey, _ => new RateRecord
            {
                LastSentAt = DateTime.MinValue,
                AttemptsInWindow = 0,
                WindowExpiresAt = now.AddMinutes(windowMinutes)
            });

            lock (record)
            {
                // Nếu cửa sổ thời gian cũ đã hết hạn, reset lại cửa sổ mới
                if (now > record.WindowExpiresAt)
                {
                    record.AttemptsInWindow = 0;
                    record.WindowExpiresAt = now.AddMinutes(windowMinutes);
                }

                // 1. Kiểm tra Cooldown giữa 2 lần gửi liên tiếp
                var elapsedSeconds = (now - record.LastSentAt).TotalSeconds;
                if (elapsedSeconds < cooldownSeconds)
                {
                    int remainingCooldown = (int)Math.Ceiling(cooldownSeconds - elapsedSeconds);
                    return new EmailRateLimitResult
                    {
                        IsAllowed = false,
                        WaitSeconds = remainingCooldown,
                        ErrorMessage = $"Quý khách vừa yêu cầu gửi email cách đây ít phút. Vui lòng đợi thêm {remainingCooldown} giây trước khi bấm gửi lại."
                    };
                }

                // 2. Kiểm tra số lần gửi tối đa trong khung thời gian (Quota exhaustion)
                if (record.AttemptsInWindow >= maxInWindow)
                {
                    int windowWaitMinutes = (int)Math.Max(1, Math.Ceiling((record.WindowExpiresAt - now).TotalMinutes));
                    return new EmailRateLimitResult
                    {
                        IsAllowed = false,
                        WaitSeconds = (int)Math.Ceiling((record.WindowExpiresAt - now).TotalSeconds),
                        ErrorMessage = $"Quý khách đã yêu cầu gửi email quá {maxInWindow} lần trong {windowMinutes} phút qua. Để bảo mật và tránh quá tải, vui lòng thử lại sau {windowWaitMinutes} phút hoặc liên hệ trực tiếp Lễ tân qua hotline 0901 234 567."
                    };
                }

                // Hợp lệ! Cập nhật thời điểm gửi và số lần gửi
                record.LastSentAt = now;
                record.AttemptsInWindow++;

                return new EmailRateLimitResult
                {
                    IsAllowed = true,
                    WaitSeconds = cooldownSeconds
                };
            }
        }

        /// <summary>
        /// Lấy số giây cooldown còn lại trước khi được phép bấm gửi lại.
        /// </summary>
        public int GetRemainingCooldown(string key, int cooldownSeconds = 60)
        {
            var cleanKey = (key ?? "unknown").Trim().ToLowerInvariant();
            if (_records.TryGetValue(cleanKey, out var record))
            {
                lock (record)
                {
                    var elapsed = (DateTime.UtcNow - record.LastSentAt).TotalSeconds;
                    if (elapsed < cooldownSeconds)
                    {
                        return (int)Math.Ceiling(cooldownSeconds - elapsed);
                    }
                }
            }
            return 0;
        }

        /// <summary>
        /// Lấy số lần đã gửi trong khung thời gian hiện tại.
        /// </summary>
        public int GetAttemptsInWindow(string key)
        {
            var cleanKey = (key ?? "unknown").Trim().ToLowerInvariant();
            if (_records.TryGetValue(cleanKey, out var record))
            {
                lock (record)
                {
                    if (DateTime.UtcNow <= record.WindowExpiresAt)
                    {
                        return record.AttemptsInWindow;
                    }
                }
            }
            return 0;
        }
    }
}
