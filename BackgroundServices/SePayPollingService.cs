using QuanLyKhachSan.Services;

namespace QuanLyKhachSan.BackgroundServices
{
    /// <summary>
    /// Background service tu dong kiem tra trang thai thanh toan SePay
    /// Chay dinh ky (mac dinh 60s/lan) de xu ly cac hoa don chua duoc IPN webhook xac nhan
    /// </summary>
    public class SePayPollingService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SePayPollingService> _logger;
        private readonly int _intervalSeconds;

        public SePayPollingService(
            IServiceProvider serviceProvider,
            ILogger<SePayPollingService> logger,
            IConfiguration config)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _intervalSeconds = int.Parse(config["SePay:PollingIntervalSeconds"] ?? "60");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "SePay Polling Service started. Interval: {Interval}s",
                _intervalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var sePayService = scope.ServiceProvider.GetRequiredService<SePayService>();

                    await sePayService.ProcessPendingInvoices();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Loi khi chay SePay polling");
                }

                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
            }

            _logger.LogInformation("SePay Polling Service stopped.");
        }
    }
}