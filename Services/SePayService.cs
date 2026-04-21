using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Services
{
    public class SePayService
    {
        private readonly SunHotelDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<SePayService> _logger;
        private readonly EmailSftpService _emailService;
        private readonly HttpClient _httpClient;

        private static readonly string[] SignatureFields = new[]
        {
            "order_amount", "merchant", "currency", "operation",
            "order_description", "order_invoice_number", "customer_id",
            "payment_method", "success_url", "error_url", "cancel_url"
        };

        public SePayService(
            SunHotelDbContext db,
            IConfiguration config,
            ILogger<SePayService> logger,
            EmailSftpService emailService)
        {
            _db = db;
            _config = config;
            _logger = logger;
            _emailService = emailService;
            _httpClient = new HttpClient();
        }

        #region ─── Cấu hình ─────────────────────────────────────────────────

        private string MerchantId => _config["SePay:MerchantId"] ?? "";
        private string SecretKey => _config["SePay:SecretKey"] ?? "";
        private string Env => _config["SePay:Env"] ?? "sandbox";

        private bool IsProduction => Env.Equals("production", StringComparison.OrdinalIgnoreCase);

        private string BaseUrl =>
            IsProduction ? "https://pay.sepay.vn" : "https://pay-sandbox.sepay.vn";

        private string ApiUrl =>
            IsProduction ? "https://pgapi.sepay.vn" : "https://pgapi-sandbox.sepay.vn";

        private string CheckoutInitUrl => IsProduction
            ? "https://pay.sepay.vn/v1/checkout/init"
            : "https://pay-sandbox.sepay.vn/v1/checkout/init";

        private string SuccessUrl => _config["SePay:SuccessUrl"] ?? "http://localhost:5000/Payment/SePaySuccess";
        private string ErrorUrl => _config["SePay:ErrorUrl"] ?? "http://localhost:5000/Payment/SePayError";
        private string CancelUrl => _config["SePay:CancelUrl"] ?? "http://localhost:5000/Payment/SePayCancel";

        private string BuildCallbackUrl(string baseUrl, string invoiceNumber)
        {
            var sep = baseUrl.Contains('?') ? "&" : "?";
            return $"{baseUrl}{sep}inv={Uri.EscapeDataString(invoiceNumber)}";
        }

        private int PollingIntervalSeconds => int.Parse(_config["SePay:PollingIntervalSeconds"] ?? "60");
        private int PollingMaxMinutes => int.Parse(_config["SePay:PollingMaxMinutes"] ?? "5");

        #endregion

        #region ─── Tạo thanh toán ─────────────────────────────────────────

        public async Task<SePayCheckoutResult> CreatePayment(Invoice invoice)
        {
            var booking = invoice.Booking;
            if (booking == null)
                return new SePayCheckoutResult { Success = false, ErrorMessage = "Hóa đơn không có thông tin booking" };

            var orderInitResult = await InitOrder(invoice);
            if (!orderInitResult.Success)
                return orderInitResult;

            invoice.SePayOrderId = orderInitResult.OrderId;
            invoice.SePayOrderCode = orderInitResult.OrderCode;
            invoice.SePayPaymentMethod = "BANK_TRANSFER";
            invoice.SePayOrderStatus = "PENDING";
            invoice.SePayCreatedAt = DateTime.UtcNow;

            invoice.SePayPollingExpiresAt = DateTime.UtcNow.AddMinutes(PollingMaxMinutes);
            invoice.SePayLastCheckAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "SePay payment created: Invoice={InvoiceNumber}, OrderId={OrderId}, OrderCode={OrderCode}",
                invoice.InvoiceNumber, orderInitResult.OrderId, orderInitResult.OrderCode);

            return new SePayCheckoutResult
            {
                Success = true,
                OrderId = orderInitResult.OrderId,
                OrderCode = orderInitResult.OrderCode,
                CheckoutUrl = orderInitResult.CheckoutUrl,
                AutoRedirect = true
            };
        }

        private async Task<SePayCheckoutResult> InitOrder(Invoice invoice)
        {
            var booking = invoice.Booking!;

            var orderInvoiceNumber = $"SP-{invoice.InvoiceNumber}";
            var orderDescription = $"Thanh toán phòng KS-{booking.BookingCode}";
            var customerId = booking.Email ?? booking.Phone ?? "GUEST";

            var fields = NormalizeFields(new Dictionary<string, string?>
            {
                ["merchant"] = MerchantId,
                ["operation"] = "PURCHASE",
                ["payment_method"] = "BANK_TRANSFER",
                ["order_amount"] = invoice.TotalAmount.ToString("F0"),
                ["currency"] = "VND",
                ["order_invoice_number"] = orderInvoiceNumber,
                ["order_description"] = orderDescription,
                ["customer_id"] = customerId,
                ["success_url"] = BuildCallbackUrl(SuccessUrl, invoice.InvoiceNumber),
                ["error_url"] = BuildCallbackUrl(ErrorUrl, invoice.InvoiceNumber),
                ["cancel_url"] = BuildCallbackUrl(CancelUrl, invoice.InvoiceNumber)
            });

            var signature = CreateSignature(fields);
            fields["signature"] = signature;

            var sb = new StringBuilder();
            var orderedFields = SignatureFields
                .Where(f => fields.ContainsKey(f) && !string.IsNullOrEmpty(fields[f]))
                .ToList();
            for (var i = 0; i < orderedFields.Count; i++)
            {
                var f = orderedFields[i];
                if (i > 0) sb.Append("&");
                sb.Append(Uri.EscapeDataString(f));
                sb.Append("=");
                sb.Append(Uri.EscapeDataString(fields[f]!));
            }
            if (sb.Length > 0) sb.Append("&");
            sb.Append("signature=");
            sb.Append(Uri.EscapeDataString(fields["signature"] ?? ""));

            var bodyContent = sb.ToString();
            _logger.LogInformation("SePay POST fields: {Fields}", bodyContent);

            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var redirectClient = new HttpClient(handler);

            var httpContent = new StringContent(bodyContent, Encoding.UTF8, "application/x-www-form-urlencoded");
            var response = await redirectClient.PostAsync(CheckoutInitUrl, httpContent);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("SePay init raw: Status={StatusCode} Location={Location} ContentType={ContentType}",
                (int)response.StatusCode,
                response.Headers.Location,
                response.Content.Headers.ContentType?.MediaType);

            var redirectUrl = response.Headers.Location?.ToString();
            if (!string.IsNullOrEmpty(redirectUrl))
            {
                _logger.LogInformation("SePay redirect to: {RedirectUrl}", redirectUrl);
                var (extractedOrderId, extractedOrderCode) = ExtractOrderInfoFromUrl(redirectUrl);
                return new SePayCheckoutResult
                {
                    Success = true,
                    OrderId = extractedOrderId,
                    OrderCode = extractedOrderCode,
                    CheckoutUrl = redirectUrl
                };
            }

            if (responseBody.Contains("sepay.vn"))
            {
                var match = Regex.Match(responseBody, @"https://(?:pay(?:-sandbox)?|my)\.sepay\.vn/v1/checkout[?][^""'\s}\)\|]+");
                if (match.Success)
                {
                    var extractedUrl = match.Value;
                    _logger.LogInformation("SePay checkout URL extracted from HTML: {Url}", extractedUrl);
                    var (extractedOrderId, extractedOrderCode) = ExtractOrderInfoFromUrl(extractedUrl);
                    return new SePayCheckoutResult
                    {
                        Success = true,
                        OrderId = extractedOrderId,
                        OrderCode = extractedOrderCode,
                        CheckoutUrl = extractedUrl
                    };
                }
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("application/json"))
            {
                _logger.LogWarning("SePay trả về content-type={ContentType} thay vì JSON. Body={Body}", contentType, responseBody);
                return new SePayCheckoutResult
                {
                    Success = false,
                    ErrorMessage = $"SePay API trả về lỗi (HTTP {(int)response.StatusCode}). Vui lòng kiểm tra config."
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new SePayCheckoutResult
                {
                    Success = false,
                    ErrorMessage = $"SePay API lỗi: HTTP {(int)response.StatusCode} - {responseBody}"
                };
            }

            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(responseBody);
                var checkoutUrl = json.GetProperty("checkoutUrl").GetString() ?? "";
                var orderId = json.TryGetProperty("orderId", out var o) ? o.GetString() ?? "" : "";
                var orderCode = json.TryGetProperty("orderCode", out var oc) ? oc.GetString() ?? "" : "";
                var paymentUrl = json.TryGetProperty("paymentUrl", out var pu) ? pu.GetString() ?? checkoutUrl : checkoutUrl;

                if (string.IsNullOrEmpty(checkoutUrl) && string.IsNullOrEmpty(paymentUrl))
                {
                    return new SePayCheckoutResult
                    {
                        Success = false,
                        ErrorMessage = $"SePay không trả về checkoutUrl. Response: {responseBody}"
                    };
                }

                return new SePayCheckoutResult
                {
                    Success = true,
                    OrderId = orderId,
                    OrderCode = orderCode,
                    CheckoutUrl = !string.IsNullOrEmpty(checkoutUrl) ? checkoutUrl : paymentUrl
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi parse response SePay: {Body}", responseBody);
                return new SePayCheckoutResult
                {
                    Success = false,
                    ErrorMessage = $"Lỗi parse response SePay: {ex.Message}"
                };
            }
        }

        #endregion

        #region ─── Kiểm tra trạng thái đơn hàng ───────────────────────────

        public async Task<bool> CheckOrderStatus(string orderCode)
        {
            if (string.IsNullOrEmpty(orderCode)) return false;

            try
            {
                // Thử với /v1/order/{id} — SePay production dùng order_id (PAY...)
                var url = $"{ApiUrl}/v1/order/{orderCode}";
                var response = await SendApiRequestAsync(HttpMethod.Get, url);
                var body = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("SePay CheckOrderStatus: GET {Url} → {Status}, body={Body}",
                    url, (int)response.StatusCode, body.Length > 200 ? body[..200] : body);

                if (response.IsSuccessStatusCode)
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(body);

                    // Thử lấy status từ cả 2 cấu trúc: { order: { order_status } } hoặc { order_status }
                    string? status = null;
                    if (json.TryGetProperty("order", out var orderNode))
                        status = orderNode.TryGetProperty("order_status", out var s1) ? s1.GetString() : null;
                    if (status == null)
                        status = json.TryGetProperty("order_status", out var s2) ? s2.GetString() : null;

                    _logger.LogInformation("SePay CheckOrderStatus: order_status={Status} for {OrderCode}", status, orderCode);

                    if (status == "CAPTURED")
                        return true;
                }
                else
                {
                    _logger.LogWarning("SePay CheckOrderStatus failed: {StatusCode} for {OrderCode}", response.StatusCode, orderCode);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi kiểm tra trạng thái SePay order: {OrderCode}", orderCode);
                return false;
            }
        }

        public async Task ProcessPendingInvoices()
        {
            var now = DateTime.UtcNow;

            var pendingInvoices = await _db.Invoices
                .Include(i => i.Booking).ThenInclude(b => b.RoomType)
                .Include(i => i.Booking).ThenInclude(b => b.Room)
                .Where(i => i.PaymentStatus == PaymentStatus.Unpaid
                    && i.PaymentMethod == PaymentMethod.SePay
                    && !string.IsNullOrEmpty(i.SePayOrderCode)
                    && (i.SePayPollingExpiresAt == null || i.SePayPollingExpiresAt > now)
                    && i.SePayOrderStatus != "CAPTURED")
                .ToListAsync();

            foreach (var invoice in pendingInvoices)
            {
                var isPaid = await CheckOrderStatus(invoice.SePayOrderCode!);
                invoice.SePayLastCheckAt = now;

                if (isPaid)
                {
                    await ConfirmSePayPayment(invoice, "", "POLLING_AUTO");
                    _logger.LogInformation(
                        "SePay payment confirmed via polling: Invoice={InvoiceNumber}, OrderCode={OrderCode}",
                        invoice.InvoiceNumber, invoice.SePayOrderCode);
                }
            }

            await _db.SaveChangesAsync();
        }

        #endregion

        #region ─── Xử lý IPN webhook ─────────────────────────────────────

        public async Task<bool> ProcessIpn(SePayIpnPayload payload)
        {
            _logger.LogInformation(
                "SePay IPN received: Type={Type}, OrderId={OrderId}, Status={Status}, InvoiceNumber={InvNum}",
                payload.NotificationType,
                payload.Order?.OrderId,
                payload.Order?.OrderStatus,
                payload.Order?.OrderInvoiceNumber);

            if (payload.NotificationType != "ORDER_PAID")
            {
                _logger.LogInformation("SePay IPN: bỏ qua notification type = {Type}", payload.NotificationType);
                return true;
            }

            var orderInvoiceNumber = payload.Order?.OrderInvoiceNumber;
            if (string.IsNullOrEmpty(orderInvoiceNumber))
            {
                _logger.LogWarning("SePay IPN: không có order_invoice_number");
                return false;
            }

            // Strip prefix SP- nếu có
            var invoiceNumber = orderInvoiceNumber.StartsWith("SP-", StringComparison.OrdinalIgnoreCase)
                ? orderInvoiceNumber[3..]
                : orderInvoiceNumber;

            _logger.LogInformation("SePay IPN: tìm invoice={InvoiceNumber}", invoiceNumber);

            var invoice = await _db.Invoices
                .Include(i => i.Booking).ThenInclude(b => b.RoomType)
                .Include(i => i.Booking).ThenInclude(b => b.Room)
                .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber);

            if (invoice == null)
            {
                _logger.LogWarning("SePay IPN: không tìm thấy hóa đơn {InvoiceNumber} (raw={Raw})", invoiceNumber, orderInvoiceNumber);
                return false;
            }

            if (invoice.PaymentStatus == PaymentStatus.Paid)
            {
                _logger.LogInformation("SePay IPN: hóa đơn {InvoiceNumber} đã được thanh toán trước đó", invoiceNumber);
                return true;
            }

            var paidAmount = decimal.TryParse(
                payload.Transaction?.TransactionAmount ?? "0",
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var amt) ? amt : 0;

            _logger.LogInformation(
                "SePay IPN: xác nhận thanh toán Invoice={InvoiceNumber}, PaidAmount={Paid}, Expected={Expected}",
                invoiceNumber, paidAmount, invoice.TotalAmount);

            if (paidAmount < invoice.TotalAmount)
            {
                _logger.LogWarning(
                    "SePay IPN: số tiền thấp hơn dự kiến. Expected={Expected}, Received={Received} — vẫn xác nhận",
                    invoice.TotalAmount, paidAmount);
            }

            var transactionId = payload.Transaction?.TransactionId ?? "";
            await ConfirmSePayPayment(invoice, transactionId, "IPN_WEBHOOK");

            _logger.LogInformation(
                "SePay IPN: đã xác nhận thành công Invoice={InvoiceNumber}, TransId={TransId}",
                invoice.InvoiceNumber, transactionId);

            return true;
        }

        #endregion

        #region ─── Xác nhận thanh toán ─────────────────────────────────

        private async Task ConfirmSePayPayment(Invoice invoice, string transactionId, string source)
        {
            invoice.PaymentStatus = PaymentStatus.Paid;
            invoice.PaymentMethod = PaymentMethod.SePay;
            invoice.PaymentDate = DateTime.UtcNow;
            invoice.SePayOrderStatus = "CAPTURED";
            invoice.SePayTransactionId = transactionId;
            invoice.SePayPaidAt = DateTime.UtcNow;
            invoice.SePayPollingExpiresAt = null;

            // Cập nhật trạng thái booking thành Confirmed nếu đang Pending
            var booking = invoice.Booking;
            if (booking != null && booking.Status == BookingStatus.Pending)
            {
                booking.Status = BookingStatus.Confirmed;
            }

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "CONFIRM_SEPAY",
                EntityType = "invoice",
                EntityId = invoice.Id,
                NewValue = System.Text.Json.JsonSerializer.Serialize(new
                {
                    payment_status = "Paid",
                    source,
                    transaction_id = transactionId,
                    amount = invoice.TotalAmount
                })
            });

            await _db.SaveChangesAsync();

            if (booking != null)
            {
                var checkInTime = booking.ActualCheckIn
                    ?? booking.CheckInDate.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(14)));
                await _emailService.SendCheckInReadyEmail(invoice, checkInTime);
            }

            _logger.LogInformation(
                "SePay payment confirmed: Invoice={InvoiceNumber}, Source={Source}",
                invoice.InvoiceNumber, source);
        }

        #endregion

        #region ─── Helpers ───────────────────────────────────────────────

        private async Task<HttpResponseMessage> SendApiRequestAsync(HttpMethod method, string url, FormUrlEncodedContent? content = null)
        {
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{MerchantId}:{SecretKey}"));

            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            if (content != null)
                request.Content = content;

            return await _httpClient.SendAsync(request);
        }

        private string CreateSignature(Dictionary<string, string?> fields)
        {
            var signedParts = SignatureFields
                .Where(f => fields.ContainsKey(f) && !string.IsNullOrEmpty(fields[f]))
                .Select(f => $"{f}={fields[f]}")
                .ToList();

            var rawSignature = string.Join(",", signedParts);

            _logger.LogInformation("SePay raw signature string: {RawSignature}", rawSignature);

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SecretKey));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawSignature));
            return Convert.ToBase64String(hash);
        }

        private static (string OrderId, string OrderCode) ExtractOrderInfoFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var orderId = query["order_id"] ?? query["orderId"] ?? "";
                var orderCode = query["order_code"] ?? query["orderCode"] ?? "";
                return (orderId, orderCode);
            }
            catch
            {
                return ("", "");
            }
        }

        private static Dictionary<string, string?> NormalizeFields(Dictionary<string, string?> fields)
        {
            return fields
                .Where(kv => kv.Value != null)
                .ToDictionary(
                    kv => kv.Key.ToLowerInvariant(),
                    kv => kv.Value?.Trim());
        }

        #endregion
    }

    #region ─── ViewModels / DTOs ──────────────────────────────────────────

    public class SePayCheckoutResult
    {
        public bool Success { get; set; }
        public string? OrderId { get; set; }
        public string? OrderCode { get; set; }
        public string? CheckoutUrl { get; set; }
        public bool AutoRedirect { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class SePayIpnPayload
    {
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("notification_type")]
        public string NotificationType { get; set; } = "";

        [JsonPropertyName("order")]
        public SePayOrderInfo? Order { get; set; }

        [JsonPropertyName("transaction")]
        public SePayTransactionInfo? Transaction { get; set; }
    }

    public class SePayOrderInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("order_id")]
        public string? OrderId { get; set; }

        [JsonPropertyName("order_status")]
        public string? OrderStatus { get; set; }

        [JsonPropertyName("order_currency")]
        public string? OrderCurrency { get; set; }

        [JsonPropertyName("order_amount")]
        public string? OrderAmount { get; set; }

        [JsonPropertyName("order_invoice_number")]
        public string? OrderInvoiceNumber { get; set; }

        [JsonPropertyName("order_description")]
        public string? OrderDescription { get; set; }

        [JsonPropertyName("custom_data")]
        public List<object>? CustomData { get; set; }
    }

    public class SePayTransactionInfo
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("payment_method")]
        public string? PaymentMethod { get; set; }

        [JsonPropertyName("transaction_id")]
        public string? TransactionId { get; set; }

        [JsonPropertyName("transaction_type")]
        public string? TransactionType { get; set; }

        [JsonPropertyName("transaction_date")]
        public string? TransactionDate { get; set; }

        [JsonPropertyName("transaction_status")]
        public string? TransactionStatus { get; set; }

        [JsonPropertyName("transaction_amount")]
        public string? TransactionAmount { get; set; }

        [JsonPropertyName("transaction_currency")]
        public string? TransactionCurrency { get; set; }
    }

    #endregion
}
