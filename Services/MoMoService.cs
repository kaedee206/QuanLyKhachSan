using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;

namespace QuanLyKhachSan.Services
{
    /// <summary>
    /// Service xử lý thanh toán MoMo theo chuẩn API MoMo Wallet
    /// Tham khảo: https://github.com/fdhhhdjd/Class-Payment-MOMO
    /// </summary>
    public class MoMoService
    {
        private readonly SunHotelDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<MoMoService> _logger;

        public MoMoService(SunHotelDbContext db, IConfiguration config, ILogger<MoMoService> logger)
        {
            _db = db;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Tạo yêu cầu thanh toán MoMo và trả về payUrl để redirect
        /// </summary>
        public async Task<MoMoPaymentResult> CreatePayment(Invoice invoice, string? returnUrl = null)
        {
            // Lấy cấu hình MoMo từ appsettings.json
            var partnerCode = _config["MoMo:PartnerCode"] ?? "";
            var accessKey = _config["MoMo:AccessKey"] ?? "";
            var secretKey = _config["MoMo:SecretKey"] ?? "";
            var endpoint = _config["MoMo:Endpoint"] ?? "https://test-payment.momo.vn/v2/gateway/api/create";
            var defaultReturnUrl = _config["MoMo:ReturnUrl"] ?? "http://localhost:5000/Invoice/MoMoReturn";
            var defaultIpnUrl = _config["MoMo:IpnUrl"] ?? "http://localhost:5000/Invoice/MoMoIpn";

            // Tạo requestId và orderId unique
            var requestId = $"{partnerCode}{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var orderId = requestId; // Dùng requestId làm orderId cho đơn giản

            // Lấy thông tin phòng từ booking
            var roomNumber = invoice.Booking?.Room?.RoomNumber ?? "N/A";
            var orderInfo = $"Thanh toan Phong {roomNumber} - HD {invoice.InvoiceNumber}";

            // Nội dung chuyển khoản
            var extraData = $"invoice={invoice.InvoiceNumber}";

            // Build raw signature theo chuẩn MoMo
            // Thứ tự: accessKey, amount, extraData, ipnUrl, orderId, orderInfo, partnerCode, redirectUrl, requestId, requestType
            var rawSignature = $"accessKey={accessKey}" +
                               $"&amount={invoice.TotalAmount}" +
                               $"&extraData={extraData}" +
                               $"&ipnUrl={defaultIpnUrl}" +
                               $"&orderId={orderId}" +
                               $"&orderInfo={orderInfo}" +
                               $"&partnerCode={partnerCode}" +
                               $"&redirectUrl={returnUrl ?? defaultReturnUrl}" +
                               $"&requestId={requestId}" +
                               $"&requestType=captureWallet";

            var signature = CreateSignature(rawSignature, secretKey);

            // Tạo payload gửi MoMo API
            var payload = new
            {
                partnerCode,
                accessKey,
                requestId,
                amount = invoice.TotalAmount.ToString("F0"),
                orderId,
                orderInfo,
                redirectUrl = returnUrl ?? defaultReturnUrl,
                ipnUrl = defaultIpnUrl,
                extraData,
                requestType = "captureWallet",
                signature,
                lang = "vi"
            };

            _logger.LogInformation("MoMo payment request: OrderId={OrderId}, Amount={Amount}", orderId, invoice.TotalAmount);

            // Gọi MoMo API
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(endpoint, content);

                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("MoMo API response: {Response}", responseBody);

                var momoResponse = JsonConvert.DeserializeObject<MoMoApiResponse>(responseBody);

                if (momoResponse != null && !string.IsNullOrEmpty(momoResponse.PayUrl))
                {
                    // Lưu MoMo order info vào invoice
                    invoice.MomoOrderId = orderId;
                    invoice.MomoRequestId = requestId;
                    invoice.MomoPayUrl = momoResponse.PayUrl;
                    invoice.PaymentMethod = PaymentMethod.MoMo;
                    invoice.PaymentStatus = PaymentStatus.Pending;
                    await _db.SaveChangesAsync();

                    return new MoMoPaymentResult
                    {
                        Success = true,
                        PayUrl = momoResponse.PayUrl,
                        OrderId = orderId,
                        RequestId = requestId,
                        Message = momoResponse.Message ?? "Thành công"
                    };
                }
                else
                {
                    var errorMsg = momoResponse?.Message ?? momoResponse?.LocalMessage ?? "Không nhận được payUrl từ MoMo";
                    _logger.LogWarning("MoMo payment failed: {Error}", errorMsg);

                    return new MoMoPaymentResult
                    {
                        Success = false,
                        ErrorMessage = errorMsg
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gọi MoMo API");
                return new MoMoPaymentResult
                {
                    Success = false,
                    ErrorMessage = $"Lỗi kết nối MoMo: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Xử lý IPN (Instant Payment Notification) từ MoMo
        /// </summary>
        public async Task<bool> ProcessIpn(MoMoIpnModel ipnData)
        {
            var secretKey = _config["MoMo:SecretKey"] ?? "";
            var accessKey = _config["MoMo:AccessKey"] ?? "";

            // Build raw signature để verify
            var rawSignature = $"accessKey={accessKey}" +
                              $"&amount={ipnData.Amount}" +
                              $"&extraData={ipnData.ExtraData}" +
                              $"&message={ipnData.Message}" +
                              $"&orderId={ipnData.OrderId}" +
                              $"&orderInfo={ipnData.OrderInfo}" +
                              $"&orderType={ipnData.OrderType}" +
                              $"&partnerCode={ipnData.PartnerCode}" +
                              $"&payType={ipnData.PayType}" +
                              $"&requestId={ipnData.RequestId}" +
                              $"&responseTime={ipnData.ResponseTime}" +
                              $"&resultCode={ipnData.ResultCode}";

            var verifySignature = CreateSignature(rawSignature, secretKey);

            if (verifySignature != ipnData.Signature)
            {
                _logger.LogWarning("MoMo IPN signature mismatch for OrderId: {OrderId}", ipnData.OrderId);
                return false;
            }

            // Tìm invoice theo MoMo orderId
            var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.MomoOrderId == ipnData.OrderId);
            if (invoice == null)
            {
                _logger.LogWarning("Invoice not found for MoMo OrderId: {OrderId}", ipnData.OrderId);
                return false;
            }

            // Kiểm tra resultCode: 0 = thành công
            if (ipnData.ResultCode == 0)
            {
                invoice.PaymentStatus = PaymentStatus.Paid;
                invoice.MomoResultCode = "0";
                invoice.MomoMessage = ipnData.Message;
                invoice.MomoTransId = ipnData.TransId;
                invoice.MomoPaidAt = DateTime.UtcNow;
                _logger.LogInformation("MoMo payment confirmed for Invoice: {InvoiceNumber}, TransId: {TransId}",
                    invoice.InvoiceNumber, ipnData.TransId);
            }
            else
            {
                invoice.MomoResultCode = ipnData.ResultCode.ToString();
                invoice.MomoMessage = ipnData.LocalMessage ?? ipnData.Message;
                _logger.LogWarning("MoMo payment failed for Invoice: {InvoiceNumber}, ResultCode: {ResultCode}",
                    invoice.InvoiceNumber, ipnData.ResultCode);
            }

            await _db.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Xử lý return URL từ MoMo (redirect về browser)
        /// </summary>
        public async Task<MoMoPaymentResult> ProcessReturn(MoMoReturnModel returnData)
        {
            var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.MomoOrderId == returnData.OrderId);
            if (invoice == null)
            {
                return new MoMoPaymentResult
                {
                    Success = false,
                    ErrorMessage = "Không tìm thấy hóa đơn"
                };
            }

            if (returnData.ResultCode == 0)
            {
                return new MoMoPaymentResult
                {
                    Success = true,
                    OrderId = returnData.OrderId,
                    Message = "Thanh toán MoMo thành công",
                    InvoiceNumber = invoice.InvoiceNumber
                };
            }
            else
            {
                return new MoMoPaymentResult
                {
                    Success = false,
                    ErrorMessage = returnData.LocalMessage ?? $"Thanh toán thất bại (code: {returnData.ResultCode})"
                };
            }
        }

        /// <summary>
        /// Tạo chữ ký HMAC SHA256 cho MoMo
        /// </summary>
        private static string CreateSignature(string rawData, string secretKey)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ViewModels cho MoMo
    // ═══════════════════════════════════════════════════════════════════════

    public class MoMoPaymentResult
    {
        public bool Success { get; set; }
        public string? PayUrl { get; set; }
        public string? OrderId { get; set; }
        public string? RequestId { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
        public string? InvoiceNumber { get; set; }
    }

    public class MoMoApiResponse
    {
        [JsonProperty("partnerCode")]
        public string? PartnerCode { get; set; }

        [JsonProperty("requestId")]
        public string? RequestId { get; set; }

        [JsonProperty("orderId")]
        public string? OrderId { get; set; }

        [JsonProperty("payUrl")]
        public string? PayUrl { get; set; }

        [JsonProperty("resultCode")]
        public int ResultCode { get; set; }

        [JsonProperty("message")]
        public string? Message { get; set; }

        [JsonProperty("localMessage")]
        public string? LocalMessage { get; set; }

        [JsonProperty("signature")]
        public string? Signature { get; set; }
    }

    public class MoMoIpnModel
    {
        [JsonProperty("partnerCode")]
        public string? PartnerCode { get; set; }

        [JsonProperty("accessKey")]
        public string? AccessKey { get; set; }

        [JsonProperty("amount")]
        public string? Amount { get; set; }

        [JsonProperty("orderId")]
        public string? OrderId { get; set; }

        [JsonProperty("orderInfo")]
        public string? OrderInfo { get; set; }

        [JsonProperty("orderType")]
        public string? OrderType { get; set; }

        [JsonProperty("transId")]
        public string? TransId { get; set; }

        [JsonProperty("message")]
        public string? Message { get; set; }

        [JsonProperty("localMessage")]
        public string? LocalMessage { get; set; }

        [JsonProperty("responseTime")]
        public string? ResponseTime { get; set; }

        [JsonProperty("resultCode")]
        public int ResultCode { get; set; }

        [JsonProperty("requestId")]
        public string? RequestId { get; set; }

        [JsonProperty("payType")]
        public string? PayType { get; set; }

        [JsonProperty("extraData")]
        public string? ExtraData { get; set; }

        [JsonProperty("signature")]
        public string? Signature { get; set; }
    }

    public class MoMoReturnModel
    {
        public string? OrderId { get; set; }
        public string? RequestId { get; set; }
        public int ResultCode { get; set; }
        public string? Message { get; set; }
        public string? LocalMessage { get; set; }
        public string? ExtraData { get; set; }
    }
}