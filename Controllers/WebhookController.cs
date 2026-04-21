using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuanLyKhachSan.Services;

namespace QuanLyKhachSan.Controllers
{
    /// <summary>
    /// Controller riêng cho webhooks từ payment providers.
    /// KHÔNG có [Authorize] — toàn bộ endpoint là public, xác thực bằng signature/secret riêng.
    /// </summary>
    [AllowAnonymous]
    [Route("webhook")]
    public class WebhookController : Controller
    {
        private readonly SePayService _sePayService;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(SePayService sePayService, ILogger<WebhookController> logger)
        {
            _sePayService = sePayService;
            _logger = logger;
        }

        /// <summary>
        /// SePay IPN endpoint — primary: POST /webhook/sepay
        /// </summary>
        [HttpPost("sepay")]
        public async Task<IActionResult> SePayIpn([FromBody] SePayIpnPayload payload)
        {
            _logger.LogInformation("Webhook SePayIpn called, payload null={IsNull}", payload == null);

            if (payload == null)
            {
                _logger.LogWarning("SePayIpn: payload is null — possible JSON parse failure");
                return Content("{\"status\":\"FAIL\",\"reason\":\"null payload\"}", "application/json");
            }

            var success = await _sePayService.ProcessIpn(payload);
            return Content(success ? "{\"status\":\"OK\"}" : "{\"status\":\"FAIL\"}", "application/json");
        }

        /// <summary>
        /// Alias cũ: POST /Invoice/SePayIpn — giữ để tương thích nếu đã config trong SePay dashboard
        /// </summary>
        [HttpPost("/Invoice/SePayIpn")]
        public async Task<IActionResult> SePayIpnLegacy([FromBody] SePayIpnPayload payload)
            => await SePayIpn(payload);
    }
}
