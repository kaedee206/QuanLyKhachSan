using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;
using Newtonsoft.Json;

namespace QuanLyKhachSan.Services
{
    /// <summary>
    /// Service quan ly hoa don va thanh toan (VietQR + MoMo + SePay)
    /// </summary>
    public class InvoiceService
    {
        private readonly SunHotelDbContext _db;
        private readonly ILogger<InvoiceService> _logger;
        private readonly IConfiguration _config;
        private readonly EmailSftpService _emailService;

        public InvoiceService(SunHotelDbContext db, ILogger<InvoiceService> logger, IConfiguration config, EmailSftpService emailService)
        {
            _db = db;
            _logger = logger;
            _config = config;
            _emailService = emailService;
        }

        /// <summary>
        /// Lấy danh sách hóa đơn có phân trang
        /// </summary>
        public async Task<PaginatedList<Invoice>> GetInvoices(int page = 1, int pageSize = 20)
        {
            var query = _db.Invoices
                .Include(i => i.Booking).ThenInclude(b => b.RoomType)
                .Include(i => i.Creator)
                .OrderByDescending(i => i.CreatedAt);

            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PaginatedList<Invoice>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            };
        }

        /// <summary>
        /// Lấy hóa đơn theo số hóa đơn
        /// </summary>
        public async Task<Invoice?> GetByNumber(string invoiceNumber)
        {
            return await _db.Invoices
                .Include(i => i.Booking).ThenInclude(b => b.RoomType)
                .Include(i => i.Booking).ThenInclude(b => b.Room)
                .Include(i => i.Booking).ThenInclude(b => b.Services)
                .Include(i => i.Creator)
                .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber);
        }

        /// <summary>
        /// Xác nhận thanh toán (tiền mặt, chuyển khoản, thẻ)
        /// </summary>
        public async Task<Invoice> ConfirmPayment(string invoiceNumber, PaymentMethod method, int userId)
        {
            var invoice = await _db.Invoices
                .Include(i => i.Booking).ThenInclude(b => b.RoomType)
                .Include(i => i.Booking).ThenInclude(b => b.Room)
                .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber);

            if (invoice == null)
                throw new InvalidOperationException("Hóa đơn không tồn tại");
            if (invoice.PaymentStatus == PaymentStatus.Paid)
                throw new InvalidOperationException("Hóa đơn đã được thanh toán");
            if (invoice.PaymentStatus == PaymentStatus.Cancelled)
                throw new InvalidOperationException("Hóa đơn đã bị hủy do booking bị hủy");
            if (invoice.Booking != null && invoice.Booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Booking đã bị hủy, không thể xác nhận thanh toán");

            invoice.PaymentStatus = PaymentStatus.Paid;
            invoice.PaymentMethod = method;
            invoice.PaymentDate = DateTime.UtcNow;

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "CONFIRM_PAYMENT",
                EntityType = "invoice",
                EntityId = invoice.Id,
                NewValue = JsonConvert.SerializeObject(new
                {
                    payment_status = "Paid",
                    payment_method = method.ToString(),
                    total_amount = invoice.TotalAmount
                })
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("Payment confirmed: {InvoiceNumber}, Method: {Method}, Amount: {Amount}",
                invoiceNumber, method, invoice.TotalAmount);
            return invoice;
        }

        /// <summary>
        /// Tạo mã QR VietQR cho hóa đơn
        /// </summary>
        public VietQRViewModel GenerateVietQR(Invoice invoice)
        {
            var bankId = _config["VietQR:BankId"] ?? "vietinbank";
            var accountNo = _config["VietQR:AccountNo"] ?? "";
            var accountName = _config["VietQR:AccountName"] ?? "";
            var apiUrl = _config["VietQR:ApiUrl"] ?? "https://img.vietqr.io";

            // Lấy tên ngân hàng
            var bankName = bankId switch
            {
                "vietinbank" => "VietinBank",
                "vietcombank" => "Vietcombank",
                "bidv" => "BIDV",
                "techcombank" => "Techcombank",
                "mbbank" => "MB Bank",
                _ => "Ngân hàng"
            };

            var roomNumber = invoice.Booking?.Room?.RoomNumber ?? "N/A";
            var content = $"ThanhToanRoom{roomNumber}+{invoice.InvoiceNumber}";

            // Tạo URL QR VietQR
            var qrImageUrl = $"{apiUrl}/image/{bankId}-{accountNo}-compact.png" +
                $"?addInfo={Uri.EscapeDataString(content)}" +
                $"&amount={invoice.TotalAmount}" +
                $"&accountName={Uri.EscapeDataString(accountName)}";

            // Lưu thông tin VietQR vào invoice
            invoice.VietqrBankCode = bankId;
            invoice.VietqrAccountNo = accountNo;
            invoice.VietqrAccountName = accountName;
            invoice.VietqrAmount = invoice.TotalAmount;
            invoice.VietqrContent = content;
            invoice.VietqrImageUrl = qrImageUrl;
            _db.SaveChanges();

            return new VietQRViewModel
            {
                BankId = bankId,
                BankName = bankName,
                AccountNo = accountNo,
                AccountName = accountName,
                Amount = invoice.TotalAmount,
                Content = content,
                QrImageUrl = qrImageUrl,
                InvoiceNumber = invoice.InvoiceNumber
            };
        }

        /// <summary>
        /// Xác nhận thanh toán VietQR đã hoàn tất
        /// </summary>
        public async Task<Invoice> ConfirmVietQR(string invoiceNumber, string reference, int userId)
        {
            var invoice = await _db.Invoices
                .Include(i => i.Booking).ThenInclude(b => b.RoomType)
                .Include(i => i.Booking).ThenInclude(b => b.Room)
                .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber);
            if (invoice == null) throw new InvalidOperationException("Hóa đơn không tồn tại");
            if (invoice.PaymentStatus == PaymentStatus.Paid)
                throw new InvalidOperationException("Hóa đơn đã được thanh toán");
            if (invoice.PaymentStatus == PaymentStatus.Cancelled)
                throw new InvalidOperationException("Hóa đơn đã bị hủy do booking bị hủy");

            var booking = await _db.Bookings.FindAsync(invoice.BookingId);
            if (booking != null && booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Booking đã bị hủy, không thể xác nhận thanh toán VietQR");

            invoice.PaymentStatus = PaymentStatus.Paid;
            invoice.PaymentMethod = PaymentMethod.VietQR;
            invoice.PaymentDate = DateTime.UtcNow;
            invoice.VietqrReference = reference;
            invoice.VietqrPaidAt = DateTime.UtcNow;

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "CONFIRM_VIETQR",
                EntityType = "invoice",
                EntityId = invoice.Id,
                NewValue = JsonConvert.SerializeObject(new
                {
                    payment_status = "Paid",
                    vietqr_reference = reference
                })
            });

            await _db.SaveChangesAsync();
            return invoice;
        }

        /// <summary>
        /// Xác nhận thanh toán MoMo đã hoàn tất
        /// </summary>
        public async Task<Invoice> ConfirmMoMo(string invoiceNumber, string transId, int userId)
        {
            var invoice = await _db.Invoices
                .Include(i => i.Booking).ThenInclude(b => b.RoomType)
                .Include(i => i.Booking).ThenInclude(b => b.Room)
                .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber);
            if (invoice == null) throw new InvalidOperationException("Hóa đơn không tồn tại");
            if (invoice.PaymentStatus == PaymentStatus.Paid)
                throw new InvalidOperationException("Hóa đơn đã được thanh toán");
            if (invoice.PaymentStatus == PaymentStatus.Cancelled)
                throw new InvalidOperationException("Hóa đơn đã bị hủy do booking bị hủy");

            var booking = await _db.Bookings.FindAsync(invoice.BookingId);
            if (booking != null && booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Booking đã bị hủy, không thể xác nhận thanh toán MoMo");

            invoice.PaymentStatus = PaymentStatus.Paid;
            invoice.PaymentMethod = PaymentMethod.MoMo;
            invoice.PaymentDate = DateTime.UtcNow;
            invoice.MomoTransId = transId;
            invoice.MomoPaidAt = DateTime.UtcNow;

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "CONFIRM_MOMO",
                EntityType = "invoice",
                EntityId = invoice.Id,
                NewValue = JsonConvert.SerializeObject(new
                {
                    payment_status = "Paid",
                    momo_trans_id = transId
                })
            });

            await _db.SaveChangesAsync();
            return invoice;
        }
    }
}