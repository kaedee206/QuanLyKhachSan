using QuanLyKhachSan.Services;

namespace QuanLyKhachSan.BackgroundServices
{
    /// <summary>
    /// Background Service tự động kiểm tra và gửi lại các email bị lỗi hoặc đang chờ
    /// Thực hiện Self-Check trước khi thử lại để bảo đảm an toàn, không trùng lặp và không spam.
    /// </summary>
    public class EmailRetryBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<EmailRetryBackgroundService> _logger;
        private readonly int _intervalSeconds;
        private readonly bool _isEnabled;

        public EmailRetryBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<EmailRetryBackgroundService> logger,
            IConfiguration config)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _intervalSeconds = int.Parse(config["Email:RetryIntervalSeconds"] ?? "60");
            _isEnabled = bool.Parse(config["Email:EnableAutoRetry"] ?? "true");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_isEnabled)
            {
                _logger.LogInformation("Email Retry Background Service is disabled by configuration.");
                return;
            }

            _logger.LogInformation(
                "Email Retry Background Service started. Interval: {Interval}s, Self-Check: Enabled",
                _intervalSeconds);

            // Chờ 15s sau khi ứng dụng khởi động trước khi chạy chu kỳ đầu tiên
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var emailService = scope.ServiceProvider.GetRequiredService<EmailSftpService>();

                    var processedCount = await emailService.ProcessEmailRetryQueueAsync(maxProcess: 10);
                    if (processedCount > 0)
                    {
                        _logger.LogInformation("[EmailRetry] Đã tự động gửi lại thành công {Count} email sau khi hoàn tất Self-Check.", processedCount);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi chạy Email Retry Background Service.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("Email Retry Background Service stopped.");
        }
    }
}
