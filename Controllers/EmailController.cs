using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;
using QuanLyKhachSan.Services;

namespace QuanLyKhachSan.Controllers
{
    [Authorize(Roles = "Admin,Manager,Receptionist")]
    public class EmailController : Controller
    {
        private readonly EmailSftpService _emailService;
        private readonly SunHotelDbContext _db;
        private readonly ILogger<EmailController> _logger;

        public EmailController(
            EmailSftpService emailService,
            SunHotelDbContext db,
            ILogger<EmailController> logger)
        {
            _emailService = emailService;
            _db = db;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? search, EmailStatus? status)
        {
            var emails = await _emailService.GetEmailQueueListAsync(search, status, limit: 100);

            var total = await _db.EmailQueues.CountAsync();
            var sent = await _db.EmailQueues.CountAsync(e => e.Status == EmailStatus.Sent);
            var pending = await _db.EmailQueues.CountAsync(e => e.Status == EmailStatus.Pending);
            var failed = await _db.EmailQueues.CountAsync(e => e.Status == EmailStatus.Failed);

            var (isHealthy, healthMsg) = await _emailService.CheckSmtpHealthAsync();

            var viewModel = new EmailIndexViewModel
            {
                Emails = emails,
                SearchEmail = search,
                StatusFilter = status,
                TotalEmails = total,
                SentCount = sent,
                PendingCount = pending,
                FailedCount = failed,
                IsSmtpHealthy = isHealthy,
                SmtpStatusMessage = healthMsg
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> CheckStatus(int id)
        {
            var detail = await _emailService.CheckEmailDeliveryStatusAsync(id);
            if (detail == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy email." });
            }

            return Json(new
            {
                success = true,
                data = detail
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Retry(int id, [FromQuery] bool force = false)
        {
            try
            {
                var result = await _emailService.RetryEmailAsync(id, force);
                if (result.Success)
                {
                    TempData["Success"] = result.Message;
                }
                else
                {
                    TempData["Error"] = $"Không thể gửi lại: {result.Message}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi kích hoạt retry email {Id}", id);
                TempData["Error"] = $"Lỗi xử lý: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RetryAllFailed()
        {
            try
            {
                var count = await _emailService.RetryAllFailedEmailsAsync();
                TempData["Success"] = $"Đã thực hiện Self-Check và gửi lại thành công {count} email bị lỗi.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi retry tất cả email thất bại");
                TempData["Error"] = $"Lỗi xử lý: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> SmtpHealth()
        {
            var (isHealthy, message) = await _emailService.CheckSmtpHealthAsync();
            return Json(new { isHealthy, message });
        }
    }
}
