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
    /// <summary>
    /// Service tich hop SePay Payment Gateway
    /// Ho tro: tao thanh toan, kiem tra trang thai, xu ly IPN webhook, polling
    /// </summary>
    public class SePayService
    {
        private readonly SunHotelDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<SePayService> _logger;
        private readonly EmailSftpService _emailService;
        private readonly HttpClient _httpClient;

        // Cac truong bat buoc de tao signature (theo tai lieu SePay)
        private static readonly string[] SignatureFields = new[]
        {
            // Thu tu theo SePay SDK docs - KHONG doi thu tu
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

        #region ─── Cau hinh ─────────────────────────────────────────────────

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

        private int PollingIntervalSeconds => int.Parse(_config["SePay:PollingIntervalSeconds"] ?? "60");
        private int PollingMaxMinutes => int.Parse(_config["SePay:PollingMaxMinutes"] ?? "5");

        #endregion

        #region ─── Tao thanh toan ─────────────────────────────────────────

        /// <summary>
        /// Tao thanh toan SePay cho mot hoa don, tra ve URL checkout de redirect
        /// </summary>
        public async Task<SePayCheckoutResult> CreatePayment(Invoice invoice)
        {
            var booking = invoice.Booking;
            if (booking == null)
                return new SePayCheckoutResult { Success = false, ErrorMessage = "Hoa don khong co thong tin booking" };

            // Buoc 1: Khoi tao order voi API SePay de lay order_id va checkout_url
            var orderInitResult = await InitOrder(invoice);
            if (!orderInitResult.Success)
                return orderInitResult;

            // Buoc 2: Luu thong tin SePay vao invoice
            invoice.SePayOrderId = orderInitResult.OrderId;
            invoice.SePayOrderCode = orderInitResult.OrderCode;
            invoice.SePayPaymentMethod = "BANK_TRANSFER";
            invoice.SePayOrderStatus = "PENDING";
            invoice.SePayCreatedAt = DateTime.UtcNow;

            // Set thoi diem het han polling (mac dinh 5 phut)
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

        /// <summary>
        /// Khoi tao order qua API SePay
        /// </summary>
        private async Task<SePayCheckoutResult> InitOrder(Invoice invoice)
        {
            var booking = invoice.Booking!;

            // Tao order_invoice_number: them prefix SP- de phan biet voi cac loai hoa don khac
            var orderInvoiceNumber = $"SP-{invoice.InvoiceNumber}";
            var orderDescription = $"Thanh toan phong KS-{booking.BookingCode}";
            var customerId = booking.Email ?? booking.Phone ?? "GUEST";

            // Build fields dictionary (chuan hoa: viet thuong, loai bo key rong)
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
                ["success_url"] = SuccessUrl,
                ["error_url"] = ErrorUrl,
                ["cancel_url"] = CancelUrl
            });

            // Tao signature
            var signature = CreateSignature(fields);
            fields["signature"] = signature;

            // Gui POST request den SePay
            // SePay tra ve HTTP 302 -> Location header chua URL checkout
            // Build body thu cong de giu dung thu tu fields nhu SignatureFields
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
            // Signature phai o cuoi cung
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

            // Lay URL redirect tu Location header (HTTP 302)
            var redirectUrl = response.Headers.Location?.ToString();
            if (!string.IsNullOrEmpty(redirectUrl))
            {
                _logger.LogInformation("SePay redirect to: {RedirectUrl}", redirectUrl);
                return new SePayCheckoutResult
                {
                    Success = true,
                    OrderId = "",
                    OrderCode = "",
                    CheckoutUrl = redirectUrl
                };
            }

            // Khong co Location header -> kiem tra body HTML co chua checkout URL khong
            if (responseBody.Contains("CURRENT_URL") || responseBody.Contains("my.sepay.vn"))
            {
                // SePay tra ve trang checkout HTML -> extract URL checkout day du tu body
                // URL checkout day du nam trong <a href="https://my.sepay.vn/v1/checkout?...">
                // Ky tu ket thuc URL la & (next param), } (trong JS), " hoac newline
                var match = Regex.Match(responseBody, @"https://my\.sepay\.vn/v1/checkout\?[^""&\s}\)\|]+");
                if (match.Success)
                {
                    var extractedUrl = match.Value;
                    _logger.LogInformation("SePay checkout URL extracted from HTML: {Url}", extractedUrl);
                    return new SePayCheckoutResult
                    {
                        Success = true,
                        OrderId = "",
                        OrderCode = "",
                        CheckoutUrl = extractedUrl
                    };
                }
            }

            // Khong the extract URL -> khong phai JSON
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("application/json"))
            {
                _logger.LogWarning("SePay tra ve content-type={ContentType} thay vi JSON. Body={Body}", contentType, responseBody);
                return new SePayCheckoutResult
                {
                    Success = false,
                    ErrorMessage = $"SePay API tra ve loi (HTTP {(int)response.StatusCode}). Vui long kiem tra config."
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new SePayCheckoutResult
                {
                    Success = false,
                    ErrorMessage = $"SePay API loi: HTTP {(int)response.StatusCode} - {responseBody}"
                };
            }

            // Parse response JSON
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
                        ErrorMessage = $"SePay khong tra ve checkoutUrl. Response: {responseBody}"
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
                _logger.LogError(ex, "Loi parse response SePay: {Body}", responseBody);
                return new SePayCheckoutResult
                {
                    Success = false,
                    ErrorMessage = $"Loi parse response SePay: {ex.Message}"
                };
            }
        }

        #endregion

        #region ─── Kiem tra trang thai don hang ───────────────────────────

        /// <summary>
        /// Kiem tra trang thai don hang cua SePay ( dung cho polling)
        /// Tra ve true neu da thanh toan
        /// </summary>
        public async Task<bool> CheckOrderStatus(string orderCode)
        {
            if (string.IsNullOrEmpty(orderCode)) return false;

            try
            {
                var url = $"{ApiUrl}/v1/order/{orderCode}";
                var response = await SendApiRequestAsync(HttpMethod.Get, url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("SePay CheckOrderStatus failed: {StatusCode}", response.StatusCode);
                    return false;
                }

                var body = await response.Content.ReadAsStringAsync();
                var json = JsonSerializer.Deserialize<JsonElement>(body);

                // Kiem tra trang thai don: CAPTURED = thanh toan thanh cong
                if (json.TryGetProperty("order", out var order))
                {
                    var status = order.TryGetProperty("order_status", out var s) ? s.GetString() : null;
                    if (status == "CAPTURED")
                    {
                        _logger.LogInformation("SePay order confirmed via polling: {OrderCode}", orderCode);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loi khi kiem tra trang thai SePay order: {OrderCode}", orderCode);
                return false;
            }
        }

        /// <summary>
        /// Xu ly tat ca cac hoa don dang cho thanh toan (chay dinh ky)
        /// </summary>
        public async Task ProcessPendingInvoices()
        {
            var now = DateTime.UtcNow;

            // Lay tat ca hoa don chua thanh toan, co SePay order, chua het han polling
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

        #region ─── Xu ly IPN webhook ─────────────────────────────────────

        /// <summary>
        /// Xu ly IPN webhook tu SePay (SePay goi POST ve khi co giao dich)
        /// </summary>
        public async Task<bool> ProcessIpn(SePayIpnPayload payload)
        {
            _logger.LogInformation(
                "SePay IPN received: Type={Type}, OrderId={OrderId}, Status={Status}",
                payload.NotificationType, payload.Order?.OrderId, payload.Order?.OrderStatus);

            if (payload.NotificationType != "ORDER_PAID")
            {
                _logger.LogInformation("SePay IPN: bỏ qua notification type = {Type}", payload.NotificationType);
                return true; // Tra 200 de SePay ngung retry
            }

            var orderInvoiceNumber = payload.Order?.OrderInvoiceNumber;
            if (string.IsNullOrEmpty(orderInvoiceNumber))
            {
                _logger.LogWarning("SePay IPN: khong co order_invoice_number");
                return false;
            }

            // Tim invoice: loai bo prefix "SP-" neu co
            var invoiceNumber = orderInvoiceNumber.StartsWith("SP-")
                ? orderInvoiceNumber[3..]
                : orderInvoiceNumber;

            var invoice = await _db.Invoices
                .Include(i => i.Booking).ThenInclude(b => b.RoomType)
                .Include(i => i.Booking).ThenInclude(b => b.Room)
                .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber);

            if (invoice == null)
            {
                _logger.LogWarning("SePay IPN: khong tim thay hoa don {InvoiceNumber}", invoiceNumber);
                return false;
            }

            if (invoice.PaymentStatus == PaymentStatus.Paid)
            {
                _logger.LogInformation("SePay IPN: hoa don {InvoiceNumber} da duoc thanh toan", invoiceNumber);
                return true;
            }

            // Kiem tra so tien (anti-fraud)
            var paidAmount = decimal.TryParse(payload.Transaction?.TransactionAmount ?? "0", out var amt) ? amt : 0;
            if (paidAmount < invoice.TotalAmount)
            {
                _logger.LogWarning(
                    "SePay IPN: so tien khong khop. Expected={Expected}, Received={Received}",
                    invoice.TotalAmount, paidAmount);
                // Van xu ly vi SePay da xac nhan thanh toan
            }

            var transactionId = payload.Transaction?.TransactionId ?? "";
            await ConfirmSePayPayment(invoice, transactionId, "IPN_WEBHOOK");

            _logger.LogInformation(
                "SePay payment confirmed via IPN: Invoice={InvoiceNumber}, TransId={TransId}",
                invoice.InvoiceNumber, transactionId);

            return true;
        }

        #endregion

        #region ─── Xac nhan thanh toan ─────────────────────────────────

        /// <summary>
        /// Xac nhan hoa don da duoc thanh toan qua SePay
        /// </summary>
        private async Task ConfirmSePayPayment(Invoice invoice, string transactionId, string source)
        {
            invoice.PaymentStatus = PaymentStatus.Paid;
            invoice.PaymentMethod = PaymentMethod.SePay;
            invoice.PaymentDate = DateTime.UtcNow;
            invoice.SePayOrderStatus = "CAPTURED";
            invoice.SePayTransactionId = transactionId;
            invoice.SePayPaidAt = DateTime.UtcNow;
            invoice.SePayPollingExpiresAt = null; // Ngung polling

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

            // Gui email thong bao check-in cho khach hang
            var booking = invoice.Booking;
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

        /// <summary>
        /// Gui API request den SePay voi Basic Auth
        /// </summary>
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

        /// <summary>
        /// Tao signature HMAC-SHA256 theo chuan SePay
        /// Signature = base64(HMAC-SHA256(ke1=val1,ke2=val2,..., signedField1, signedField2,...))
        /// </summary>
        private string CreateSignature(Dictionary<string, string?> fields)
        {
            // Theo SePay SDK: chi loc fields ton tai, giu dung thu tu SignatureFields
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

        /// <summary>
        /// Chuan hoa fields: lower-case keys, loai bo gia tri null/empty
        /// </summary>
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

    /// <summary>
    /// Payload nhan tu SePay IPN webhook
    /// </summary>
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
