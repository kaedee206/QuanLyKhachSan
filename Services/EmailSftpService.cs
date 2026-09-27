using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;

namespace QuanLyKhachSan.Services
{
    public class EmailSftpService
    {
        private readonly SunHotelDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<EmailSftpService> _logger;
        private readonly IServiceScopeFactory? _scopeFactory;

        public EmailSftpService(
            SunHotelDbContext db,
            IConfiguration config,
            ILogger<EmailSftpService> logger,
            IServiceScopeFactory? scopeFactory = null)
        {
            _db = db;
            _config = config;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        private (SunHotelDbContext db, IServiceScope? scope) GetActiveDbContext()
        {
            try
            {
                _ = _db.Model;
                return (_db, null);
            }
            catch (ObjectDisposedException) when (_scopeFactory != null)
            {
                var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SunHotelDbContext>();
                return (db, scope);
            }
        }

        public async Task<bool> SendEmail(string toEmail, string subject, string body, string template = "general")
        {
            var (activeDb, fallbackScope) = GetActiveDbContext();
            try
            {
                var smtpHost = _config["Email:Host"];
                var smtpPort = int.Parse(_config["Email:Port"] ?? "587");
                var smtpUser = _config["Email:User"];
                var smtpPass = _config["Email:Password"];
                var (fromEmail, fromName) = GetSenderInfo();
                var useSftp = bool.Parse(_config["Email:UseSftp"] ?? "false");
                var sftpHost = _config["Email:SftpHost"];
                var sftpPort = int.Parse(_config["Email:SftpPort"] ?? "22");
                var sftpUser = _config["Email:SftpUser"];
                var sftpPass = _config["Email:SftpPassword"];

                var emailQueue = new EmailQueue
                {
                    ToEmail = toEmail,
                    Subject = subject,
                    Template = template,
                    TemplateData = System.Text.Json.JsonSerializer.Serialize(new { body, subject }),
                    Status = EmailStatus.Pending,
                    Attempts = 0
                };
                activeDb.EmailQueues.Add(emailQueue);
                await activeDb.SaveChangesAsync();

                try
                {
                    if (useSftp && !string.IsNullOrEmpty(sftpHost))
                    {
                        await SendViaSftpTunnel(smtpHost!, smtpPort, smtpUser!, smtpPass!, fromEmail, fromName, toEmail, subject, body);
                    }
                    else
                    {
                        await SendViaSmtp(smtpHost!, smtpPort, smtpUser!, smtpPass!, fromEmail, fromName, toEmail, subject, body);
                    }

                    emailQueue.Status = EmailStatus.Sent;
                    emailQueue.SentAt = DateTime.UtcNow;
                    await activeDb.SaveChangesAsync();

                    _logger.LogInformation("Email sent successfully to: {ToEmail}, Subject: {Subject}", toEmail, subject);
                    return true;
                }
                catch (Exception ex)
                {
                    emailQueue.Status = EmailStatus.Failed;
                    emailQueue.Attempts++;
                    emailQueue.LastError = ex.Message;
                    await activeDb.SaveChangesAsync();

                    _logger.LogError(ex, "Failed to send email to: {ToEmail}", toEmail);
                    return false;
                }
            }
            finally
            {
                fallbackScope?.Dispose();
            }
        }

        private async Task SendViaSmtp(string host, int port, string user, string password, string fromEmail, string fromName, string toEmail, string subject, string body)
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(user, password),
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            using var message = new MailMessage
            {
                From = CreateMailAddress(fromEmail, fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            message.To.Add(toEmail.Trim());

            await client.SendMailAsync(message);
        }

        private async Task SendViaSftpTunnel(string smtpHost, int smtpPort, string smtpUser, string smtpPassword, string fromEmail, string fromName, string toEmail, string subject, string body)
        {
            var sftpHost = _config["Email:SftpHost"]!;
            var sftpPort = int.Parse(_config["Email:SftpPort"] ?? "22");
            var sftpUser = _config["Email:SftpUser"]!;
            var sftpPass = _config["Email:SftpPassword"] ?? "";
            var localPort = int.Parse(_config["Email:LocalSmtpPort"] ?? "1025");

            using var client = new SshClient(sftpHost, sftpPort, sftpUser, sftpPass);
            client.Connect();

            if (!client.IsConnected)
                throw new InvalidOperationException("Không thể kết nối SFTP server");

            var forwardedPort = new Renci.SshNet.ForwardedPortLocal("127.0.0.1", (uint)localPort, smtpHost, (uint)smtpPort);
            client.AddForwardedPort(forwardedPort);
            forwardedPort.Start();

            try
            {
                using var smtpClient = new SmtpClient("127.0.0.1", localPort)
                {
                    EnableSsl = false,
                    Credentials = new NetworkCredential(smtpUser, smtpPassword),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                using var message = new MailMessage
                {
                    From = CreateMailAddress(fromEmail, fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                message.To.Add(toEmail.Trim());

                await smtpClient.SendMailAsync(message);
                _logger.LogInformation("Email sent via SFTP tunnel to: {ToEmail}", toEmail);
            }
            finally
            {
                forwardedPort.Stop();
                if (client.IsConnected)
                    client.Disconnect();
            }
        }

        private (string fromEmail, string fromName) GetSenderInfo()
        {
            var rawFrom = _config["Email:From"]?.Trim();
            var envFromName = Environment.GetEnvironmentVariable("EMAIL_FROM_NAME")?.Trim();
            var rawFromName = !string.IsNullOrEmpty(envFromName)
                ? envFromName
                : _config["Email:FromName"]?.Trim();
            var rawUser = _config["Email:User"]?.Trim();

            string fromEmail;
            string fromName;

            if (!string.IsNullOrEmpty(rawFrom) && rawFrom.Contains('@') && !rawFrom.StartsWith("${"))
            {
                fromEmail = rawFrom;
                fromName = !string.IsNullOrEmpty(rawFromName) && !rawFromName.StartsWith("${") ? rawFromName : "Sun Hotel";
            }
            else
            {
                if (!string.IsNullOrEmpty(rawFrom) && !rawFrom.StartsWith("${"))
                {
                    fromName = rawFrom;
                }
                else
                {
                    fromName = !string.IsNullOrEmpty(rawFromName) && !rawFromName.StartsWith("${") ? rawFromName : "Sun Hotel";
                }

                if (!string.IsNullOrEmpty(rawUser) && rawUser.Contains('@') && !rawUser.StartsWith("${"))
                {
                    fromEmail = rawUser;
                }
                else
                {
                    fromEmail = "noreply@sunhotel.vn";
                }
            }

            return (fromEmail, fromName);
        }

        private MailAddress CreateMailAddress(string fromEmail, string fromName)
        {
            if (MailAddress.TryCreate(fromEmail, fromName, out var addr))
                return addr;

            if (MailAddress.TryCreate(fromEmail, out addr))
                return addr;

            var rawUser = _config["Email:User"]?.Trim();
            if (!string.IsNullOrEmpty(rawUser) && MailAddress.TryCreate(rawUser, fromName, out addr))
                return addr;

            return new MailAddress("noreply@sunhotel.vn", fromName);
        }

        public async Task SendBookingConfirmationEmail(Booking booking)
        {
            var subject = $"Xác nhận đặt phòng - Mã {booking.BookingCode} | Sun Hotel";
            var nights = booking.CheckOutDate.DayNumber - booking.CheckInDate.DayNumber;

            var body = $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Xác nhận đặt phòng - Sun Hotel</title>
</head>
<body style='margin:0;padding:0;font-family:'Segoe UI',Tahoma,Geneva,Verdana,sans-serif;background-color:#f4f6f9;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background:#f4f6f9;padding:30px 15px;'>
        <tr>
            <td align='center'>
                <table width='600' cellpadding='0' cellspacing='0' style='background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 4px 20px rgba(0,0,0,0.08);'>

                    <!-- Header -->
                    <tr>
                        <td style='background:linear-gradient(135deg,#1a3a5c,#2d6a8f);padding:30px 40px;text-align:center;'>
                            <h1 style='color:#ffffff;margin:0 0 5px;font-size:24px;font-weight:700;'>SUN HOTEL</h1>
                            <p style='color:#a8d4f0;margin:0;font-size:13px;letter-spacing:2px;'>PREMIUM ACCOMMODATION</p>
                        </td>
                    </tr>

                    <!-- Confirmation badge -->
                    <tr>
                        <td style='padding:30px 40px 0;text-align:center;'>
                            <div style='display:inline-block;background:#e8f5e9;color:#2e7d32;font-size:13px;font-weight:700;padding:6px 18px;border-radius:20px;letter-spacing:1px;margin-bottom:15px;'>✓ ĐẶT PHÒNG THÀNH CÔNG</div>
                            <h2 style='color:#1a3a5c;margin:0 0 5px;font-size:22px;'>Xin chào, <span style='color:#2d6a8f;'>{booking.GuestName}</span>!</h2>
                            <p style='color:#666;margin:5px 0 0;font-size:14px;'>Chúng tôi xác nhận đặt phòng của quý khách thành công.<br>Đơn của quý khách đang được xử lý và chờ thanh toán.</p>
                        </td>
                    </tr>

                    <!-- Booking details card -->
                    <tr>
                        <td style='padding:25px 40px 10px;'>
                            <table width='100%' cellpadding='0' cellspacing='0' style='background:#f8fafc;border-radius:10px;border:1px solid #e2e8f0;'>
                                <tr>
                                    <td colspan='2' style='padding:15px 20px 10px;border-bottom:1px solid #e2e8f0;'>
                                        <span style='color:#1a3a5c;font-weight:700;font-size:15px;'>Thông tin đặt phòng</span>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Mã đặt phòng</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{booking.BookingCode}</p>
                                    </td>
                                    <td style='padding:10px 20px;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Loại phòng</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{booking.RoomType?.Name ?? "N/A"}</p>
                                    </td>
                                </tr>
                                <tr style='background:#ffffff;'>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Ngày nhận phòng</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{booking.CheckInDate:dd/MM/yyyy}</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Ngày trả phòng</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{booking.CheckOutDate:dd/MM/yyyy}</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Số đêm</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{nights} đêm</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Số khách</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{booking.NumGuests} người</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td colspan='2' style='padding:15px 20px;border-top:2px solid #1a3a5c;background:#f0f7ff;'>
                                        <table width='100%' cellpadding='0' cellspacing='0'>
                                            <tr>
                                                <td style='color:#1a3a5c;font-size:14px;font-weight:600;'>Tổng thanh toán:</td>
                                                <td align='right' style='color:#d4af37;font-size:22px;font-weight:800;'>{booking.TotalAmount:N0} VND</td>
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Notice -->
                    <tr>
                        <td style='padding:15px 40px;'>
                            <div style='background:#fff8e1;border-left:4px solid #ffb300;padding:12px 16px;border-radius:0 6px 6px 0;'>
                                <p style='margin:0;color:#795548;font-size:13px;line-height:1.6;'>📋 <strong>Lưu ý:</strong> Đơn đặt phòng đang chờ xử lý. Vui lòng thanh toán để xác nhận đặt phòng. Sau khi thanh toán, quý khách sẽ nhận được email thông báo có thể check-in.</p>
                            </div>
                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td style='background:#1a3a5c;padding:25px 40px;text-align:center;'>
                            <p style='color:#a8d4f0;margin:0 0 8px;font-size:13px;'>Cảm ơn quý khách đã chọn <strong style='color:#ffffff;'>Sun Hotel</strong>!</p>
                            <p style='color:#6b9fc4;margin:0;font-size:12px;'>📞 Hotline: 1900 1234  |  📧 noreply@sunhotel.vn  |  📍 123 Nguyen Hue, Q1, TP.HCM</p>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>";

            if (!string.IsNullOrEmpty(booking.Email))
            {
                await SendEmail(booking.Email, subject, body, "booking_confirmation");
            }
        }

        public async Task SendPaymentConfirmationEmail(Invoice invoice)
        {
            var booking = invoice.Booking;
            var guestName = booking?.GuestName ?? "Khách hàng";
            var roomNumber = booking?.Room?.RoomNumber ?? "Chưa xếp phòng";
            var roomTypeName = booking?.RoomType?.Name ?? "Chưa xếp loại phòng";

            var subject = $"Thanh toán thành công - Hóa đơn {invoice.InvoiceNumber} | Sun Hotel";

            var paymentMethodDisplay = invoice.PaymentMethod.ToString();

            var body = $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Thanh toán thành công - Sun Hotel</title>
</head>
<body style='margin:0;padding:0;font-family:'Segoe UI',Tahoma,Geneva,Verdana,sans-serif;background-color:#f4f6f9;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background:#f4f6f9;padding:30px 15px;'>
        <tr>
            <td align='center'>
                <table width='600' cellpadding='0' cellspacing='0' style='background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 4px 20px rgba(0,0,0,0.08);'>

                    <!-- Header -->
                    <tr>
                        <td style='background:linear-gradient(135deg,#1a3a5c,#2d6a8f);padding:30px 40px;text-align:center;'>
                            <h1 style='color:#ffffff;margin:0 0 5px;font-size:24px;font-weight:700;'>SUN HOTEL</h1>
                            <p style='color:#a8d4f0;margin:0;font-size:13px;letter-spacing:2px;'>PREMIUM ACCOMMODATION</p>
                        </td>
                    </tr>

                    <!-- Success badge -->
                    <tr>
                        <td style='padding:30px 40px 0;text-align:center;'>
                            <div style='display:inline-block;background:#e8f5e9;color:#2e7d32;font-size:13px;font-weight:700;padding:6px 18px;border-radius:20px;letter-spacing:1px;margin-bottom:15px;'>✓ THANH TOÁN THÀNH CÔNG</div>
                            <h2 style='color:#1a3a5c;margin:0 0 5px;font-size:22px;'>Xin chào, <span style='color:#2d6a8f;'>{guestName}</span>!</h2>
                            <p style='color:#666;margin:5px 0 0;font-size:14px;'>Chúng tôi đã nhận được thanh toán của quý khách.</p>
                        </td>
                    </tr>

                    <!-- Invoice card -->
                    <tr>
                        <td style='padding:25px 40px 10px;'>
                            <table width='100%' cellpadding='0' cellspacing='0' style='background:#f8fafc;border-radius:10px;border:1px solid #e2e8f0;'>
                                <tr>
                                    <td colspan='2' style='padding:15px 20px 10px;border-bottom:1px solid #e2e8f0;'>
                                        <span style='color:#1a3a5c;font-weight:700;font-size:15px;'>Chi tiết hóa đơn</span>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Số hóa đơn</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:15px;font-weight:700;'>{invoice.InvoiceNumber}</p>
                                    </td>
                                    <td style='padding:10px 20px;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Loại phòng</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:15px;font-weight:700;'>{roomTypeName}</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Phòng</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:15px;font-weight:700;'>{roomNumber}</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Phương thức</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:14px;'>{paymentMethodDisplay}</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Tiền phòng</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:14px;'>{invoice.RoomCharge:N0} VND</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Tiền dịch vụ</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:14px;'>{invoice.ServiceCharge:N0} VND</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Giảm giá</p>
                                        <p style='margin:0;color:#e53935;font-size:14px;'>-{invoice.Discount:N0} VND</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                    </td>
                                </tr>
                                <tr>
                                    <td colspan='2' style='padding:15px 20px;border-top:2px solid #1a3a5c;background:#f0f7ff;'>
                                        <table width='100%' cellpadding='0' cellspacing='0'>
                                            <tr>
                                                <td style='color:#1a3a5c;font-size:14px;font-weight:600;'>Tổng thanh toán:</td>
                                                <td align='right' style='color:#d4af37;font-size:22px;font-weight:800;'>{invoice.TotalAmount:N0} VND</td>
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td style='background:#1a3a5c;padding:25px 40px;text-align:center;'>
                            <p style='color:#a8d4f0;margin:0 0 8px;font-size:13px;'>Cảm ơn quý khách đã sử dụng dịch vụ của <strong style='color:#ffffff;'>Sun Hotel</strong>!</p>
                            <p style='color:#6b9fc4;margin:0;font-size:12px;'>📞 Hotline: 1900 1234  |  📧 noreply@sunhotel.vn  |  📍 123 Nguyen Hue, Q1, TP.HCM</p>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>";

            var bookingEmail = invoice.Booking?.Email;
            if (!string.IsNullOrEmpty(bookingEmail))
            {
                await SendEmail(bookingEmail, subject, body, "payment_confirmation");
            }
        }

        public async Task SendCheckInReadyEmail(Invoice invoice, DateTime checkInTime)
        {
            try
            {
                var booking = invoice.Booking;
                if (booking == null) return;

            var guestName = booking.GuestName ?? "Khách hàng";
            var roomNumber = booking.Room?.RoomNumber ?? "Chưa xếp phòng";
            var roomTypeName = booking.RoomType?.Name ?? "Chưa xếp loại phòng";

            TimeZoneInfo vnZone;
            try
            {
                vnZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                try
                {
                    vnZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
                }
                catch (TimeZoneNotFoundException)
                {
                    vnZone = TimeZoneInfo.CreateCustomTimeZone("Vietnam_Standard_Time", TimeSpan.FromHours(7), "Vietnam Standard Time", "Vietnam Standard Time");
                }
            }

            DateTime vnCheckIn;
            if (checkInTime.Kind == DateTimeKind.Utc)
            {
                vnCheckIn = TimeZoneInfo.ConvertTimeFromUtc(checkInTime, vnZone);
            }
            else if (checkInTime.Kind == DateTimeKind.Local)
            {
                vnCheckIn = TimeZoneInfo.ConvertTime(checkInTime, vnZone);
            }
            else
            {
                // DateTimeKind.Unspecified: đã là giờ khách sạn (e.g. CheckInDate + 14:00)
                vnCheckIn = checkInTime;
            }

            var formattedTime = vnCheckIn.ToString("HH:mm");
            var formattedDate = vnCheckIn.ToString("dd/MM/yyyy");

            var subject = $"Hoàn tất thanh toán - Sẵn sàng check-in lúc {formattedTime}, {formattedDate}";

            var paymentMethodDisplay = invoice.PaymentMethod.ToString();

            var body = $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Hoàn tất thanh toán - Sun Hotel</title>
</head>
<body style='margin:0;padding:0;font-family:'Segoe UI',Tahoma,Geneva,Verdana,sans-serif;background-color:#f4f6f9;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background:#f4f6f9;padding:30px 15px;'>
        <tr>
            <td align='center'>
                <table width='600' cellpadding='0' cellspacing='0' style='background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 4px 20px rgba(0,0,0,0.08);'>

                    <!-- Header -->
                    <tr>
                        <td style='background:linear-gradient(135deg,#1a3a5c,#2d6a8f);padding:30px 40px;text-align:center;'>
                            <h1 style='color:#ffffff;margin:0 0 5px;font-size:24px;font-weight:700;'>SUN HOTEL</h1>
                            <p style='color:#a8d4f0;margin:0;font-size:13px;letter-spacing:2px;'>PREMIUM ACCOMMODATION</p>
                        </td>
                    </tr>

                    <!-- Success badge -->
                    <tr>
                        <td style='padding:30px 40px 0;text-align:center;'>
                            <div style='display:inline-block;background:#e8f5e9;color:#2e7d32;font-size:13px;font-weight:700;padding:6px 18px;border-radius:20px;letter-spacing:1px;margin-bottom:15px;'>✓ THANH TOÁN THÀNH CÔNG</div>
                            <h2 style='color:#1a3a5c;margin:0 0 5px;font-size:22px;'>Xin chào, <span style='color:#2d6a8f;'>{guestName}</span>!</h2>
                            <p style='color:#666;margin:5px 0 0;font-size:14px;'>Chúng tôi xác nhận quý khách đã hoàn tất thủ tục thanh toán và có thể tiếp tục check-in.</p>
                        </td>
                    </tr>

                    <!-- Check-in time highlight -->
                    <tr>
                        <td style='padding:20px 40px 0;'>
                            <div style='background:linear-gradient(135deg,#e3f2fd,#f3e5f5);border-radius:10px;padding:20px;text-align:center;border:1px solid #bbdefb;'>
                                <p style='margin:0 0 5px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:1px;'>Thời gian check-in</p>
                                <p style='margin:0;color:#1a3a5c;font-size:28px;font-weight:800;'>{formattedTime}</p>
                                <p style='margin:5px 0 0;color:#666;font-size:14px;'>{formattedDate}</p>
                            </div>
                        </td>
                    </tr>

                    <!-- Booking details card -->
                    <tr>
                        <td style='padding:25px 40px 10px;'>
                            <table width='100%' cellpadding='0' cellspacing='0' style='background:#f8fafc;border-radius:10px;border:1px solid #e2e8f0;'>
                                <tr>
                                    <td colspan='2' style='padding:15px 20px 10px;border-bottom:1px solid #e2e8f0;'>
                                        <span style='color:#1a3a5c;font-weight:700;font-size:15px;'>Chi tiết đặt phòng</span>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Mã đặt phòng</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{booking.BookingCode}</p>
                                    </td>
                                    <td style='padding:10px 20px;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Loại phòng</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{roomTypeName}</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Phòng</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{roomNumber}</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Số hóa đơn</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:15px;font-weight:700;'>{invoice.InvoiceNumber}</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Ngày nhận phòng</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:15px;font-weight:600;'>{booking.CheckInDate:dd/MM/yyyy}</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Ngày trả phòng</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:15px;font-weight:600;'>{booking.CheckOutDate:dd/MM/yyyy}</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Phương thức</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:14px;'>{paymentMethodDisplay}</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Tiền dịch vụ</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:14px;'>{invoice.ServiceCharge:N0} VND</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td colspan='2' style='padding:15px 20px;border-top:2px solid #1a3a5c;background:#f0f7ff;'>
                                        <table width='100%' cellpadding='0' cellspacing='0'>
                                            <tr>
                                                <td style='color:#1a3a5c;font-size:14px;font-weight:600;'>Tổng thanh toán:</td>
                                                <td align='right' style='color:#d4af37;font-size:22px;font-weight:800;'>{invoice.TotalAmount:N0} VND</td>
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Notice -->
                    <tr>
                        <td style='padding:15px 40px;'>
                            <div style='background:#fff8e1;border-left:4px solid #ffb300;padding:12px 16px;border-radius:0 6px 6px 0;'>
                                <p style='margin:0;color:#795548;font-size:13px;line-height:1.6;'>📋 <strong>Lưu ý:</strong> Vui lòng đến lễ tân trước <strong>{formattedTime}</strong> để nhận phòng và hoàn tất thủ tục check-in. Nếu cần hỗ trợ, vui lòng liên hệ <strong>Hotline: 1900 1234</strong>.</p>
                            </div>
                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td style='background:#1a3a5c;padding:25px 40px;text-align:center;'>
                            <p style='color:#a8d4f0;margin:0 0 8px;font-size:13px;'>Cảm ơn quý khách đã chọn <strong style='color:#ffffff;'>Sun Hotel</strong>! Chúng tôi rất mong được phục vụ quý khách.</p>
                            <p style='color:#6b9fc4;margin:0;font-size:12px;'>📞 Hotline: 1900 1234  |  📧 noreply@sunhotel.vn  |  📍 123 Nguyen Hue, Q1, TP.HCM</p>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>";

                if (!string.IsNullOrEmpty(booking.Email))
                {
                    await SendEmail(booking.Email, subject, body, "checkin_ready");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong quá trình gửi email sẵn sàng check-in cho hóa đơn {InvoiceNumber}", invoice.InvoiceNumber);
            }
        }

        /// <summary>
        /// Gửi email hóa đơn thanh toán khi check-out thành công (Giao diện chuẩn hóa đơn tối ưu điện thoại)
        /// </summary>
        public async Task SendCheckOutInvoiceReceiptEmail(Invoice invoice)
        {
            var booking = invoice.Booking;
            var guestName = booking?.GuestName ?? "Quý khách";
            var phone = booking?.Phone ?? "—";
            var roomNumber = booking?.Room?.RoomNumber ?? "—";
            var roomTypeName = booking?.RoomType?.Name ?? "Phòng khách sạn";
            var checkInStr = booking != null ? booking.CheckInDate.ToString("dd/MM/yyyy") : "—";
            var checkOutStr = booking != null ? booking.CheckOutDate.ToString("dd/MM/yyyy") : "—";
            var actualCheckOutStr = booking?.ActualCheckOut.HasValue == true
                ? booking.ActualCheckOut.Value.ToString("dd/MM/yyyy HH:mm")
                : DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            var nights = booking != null ? Math.Max(1, booking.CheckOutDate.DayNumber - booking.CheckInDate.DayNumber) : 1;

            var services = booking?.Services?.ToList() ?? new List<Service>();
            var servicesTotal = services.Sum(s => s.TotalAmount);
            var computedTotal = invoice.RoomCharge + servicesTotal - invoice.Discount;

            var serviceRows = new StringBuilder();
            if (services.Any())
            {
                foreach (var s in services)
                {
                    serviceRows.AppendLine($@"
                    <tr>
                        <td style='padding:6px 0;border-bottom:1px dashed #e5e7eb;font-size:12px;color:#374151;'>
                            <div style='font-weight:600;'>{s.ServiceName}</div>
                            <div style='font-size:11px;color:#6b7280;'>SL: {s.Quantity} x {s.UnitPrice:N0}₫</div>
                        </td>
                        <td style='padding:6px 0;border-bottom:1px dashed #e5e7eb;font-size:12px;font-weight:600;text-align:right;color:#111827;white-space:nowrap;'>
                            {s.TotalAmount:N0}₫
                        </td>
                    </tr>");
                }
            }

            var subject = $"[Hóa đơn #{invoice.InvoiceNumber}] Xác nhận Check-out thành công | Sun Hotel";

            var body = $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Hóa đơn thanh toán - Sun Hotel</title>
</head>
<body style='margin:0;padding:20px 10px;background-color:#eceef2;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Helvetica,Arial,sans-serif;'>
    <div style='max-width:380px;margin:0 auto;background:#ffffff;border-radius:14px;box-shadow:0 8px 30px rgba(0,0,0,0.12);overflow:hidden;border:1px solid #e2e8f0;'>
        
        <!-- Header Receipt -->
        <div style='background:linear-gradient(135deg,#0F2540,#1E3A5F);padding:24px 20px;text-align:center;color:#ffffff;'>
            <div style='font-size:11px;letter-spacing:3px;color:#D4AF37;text-transform:uppercase;font-weight:700;margin-bottom:4px;'>Sun Hotel & Resort</div>
            <h1 style='margin:0;font-size:20px;font-weight:800;letter-spacing:1px;'>PHIẾU THANH TOÁN</h1>
            <div style='font-size:12px;color:#94a3b8;margin-top:4px;'>Hóa đơn: #{invoice.InvoiceNumber}</div>
            <div style='display:inline-block;margin-top:10px;padding:3px 12px;background:rgba(212,175,55,0.2);border:1px solid #D4AF37;border-radius:20px;font-size:11px;color:#FDE68A;font-weight:600;'>
                ✓ CHECK-OUT HOÀN TẤT
            </div>
        </div>

        <!-- Receipt Body -->
        <div style='padding:20px 18px;'>
            
            <!-- Guest & Stay Meta -->
            <table width='100%' cellpadding='0' cellspacing='0' style='font-size:12px;color:#4b5563;margin-bottom:14px;'>
                <tr>
                    <td style='padding:3px 0;'>Khách hàng:</td>
                    <td style='padding:3px 0;text-align:right;font-weight:700;color:#111827;'>{guestName}</td>
                </tr>
                <tr>
                    <td style='padding:3px 0;'>Điện thoại:</td>
                    <td style='padding:3px 0;text-align:right;font-weight:600;color:#111827;'>{phone}</td>
                </tr>
                <tr>
                    <td style='padding:3px 0;'>Số phòng:</td>
                    <td style='padding:3px 0;text-align:right;font-weight:700;color:#0F2540;'>Phòng {roomNumber} ({roomTypeName})</td>
                </tr>
                <tr>
                    <td style='padding:3px 0;'>Lưu trú:</td>
                    <td style='padding:3px 0;text-align:right;color:#111827;'>{checkInStr} → {checkOutStr} ({nights} đêm)</td>
                </tr>
                <tr>
                    <td style='padding:3px 0;'>Thời gian trả phòng:</td>
                    <td style='padding:3px 0;text-align:right;color:#059669;font-weight:600;'>{actualCheckOutStr}</td>
                </tr>
            </table>

            <!-- Divider -->
            <div style='border-top:2px dashed #cbd5e1;margin:12px 0 16px;'></div>

            <!-- Itemized Table -->
            <div style='font-size:11px;text-transform:uppercase;color:#6b7280;font-weight:700;letter-spacing:1px;margin-bottom:8px;'>Chi tiết thanh toán</div>
            <table width='100%' cellpadding='0' cellspacing='0' style='font-size:12px;margin-bottom:12px;'>
                <!-- Tiền phòng -->
                <tr>
                    <td style='padding:6px 0;border-bottom:1px dashed #e5e7eb;color:#374151;'>
                        <div style='font-weight:600;'>Tiền phòng ({roomTypeName})</div>
                        <div style='font-size:11px;color:#6b7280;'>{nights} đêm x {(invoice.RoomCharge / nights):N0}₫</div>
                    </td>
                    <td style='padding:6px 0;border-bottom:1px dashed #e5e7eb;font-weight:600;text-align:right;color:#111827;white-space:nowrap;'>
                        {invoice.RoomCharge:N0}₫
                    </td>
                </tr>

                <!-- Dịch vụ đi kèm -->
                {serviceRows}

                <!-- Giảm giá (nếu có) -->
                {(invoice.Discount > 0 ? $@"
                <tr>
                    <td style='padding:6px 0;border-bottom:1px dashed #e5e7eb;color:#dc2626;'>
                        <div style='font-weight:600;'>Khuyến mãi / Giảm giá</div>
                    </td>
                    <td style='padding:6px 0;border-bottom:1px dashed #e5e7eb;font-weight:600;text-align:right;color:#dc2626;white-space:nowrap;'>
                        -{invoice.Discount:N0}₫
                    </td>
                </tr>" : "")}
            </table>

            <!-- Total Block -->
            <div style='background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;padding:14px;margin-top:10px;'>
                <table width='100%' cellpadding='0' cellspacing='0'>
                    <tr>
                        <td style='font-size:13px;font-weight:600;color:#475569;'>Tổng dịch vụ:</td>
                        <td style='font-size:13px;font-weight:600;text-align:right;color:#0284c7;'>{servicesTotal:N0}₫</td>
                    </tr>
                    <tr>
                        <td style='font-size:14px;font-weight:800;color:#0F2540;padding-top:8px;'>TỔNG THANH TOÁN:</td>
                        <td style='font-size:18px;font-weight:800;text-align:right;color:#b45309;padding-top:8px;'>{computedTotal:N0}₫</td>
                    </tr>
                </table>
            </div>

            <!-- Payment Status Pill -->
            <div style='margin-top:14px;text-align:center;'>
                <div style='display:inline-block;padding:6px 16px;background:#ecfdf5;border:1px solid #a7f3d0;border-radius:20px;color:#065f46;font-size:12px;font-weight:700;'>
                    ✓ ĐÃ THANH TOÁN ({invoice.PaymentMethod})
                </div>
            </div>

            <!-- Barcode Simulation -->
            <div style='margin-top:20px;text-align:center;padding:10px 0;border-top:1px dashed #cbd5e1;'>
                <div style='letter-spacing:4px;font-family:monospace;font-size:18px;font-weight:bold;color:#334155;'>
                    ||| | |||| | ||| || |||| | ||
                </div>
                <div style='font-size:10px;color:#94a3b8;margin-top:2px;'>AUTH: {invoice.InvoiceNumber} - {DateTime.UtcNow:yyyyMMddHHmmss}</div>
            </div>

            <p style='text-align:center;font-size:12px;color:#64748b;margin:14px 0 0;line-height:1.5;'>
                Kính chúc Quý khách một chuyến đi vui vẻ & thượng lộ bình an.<br>
                Sun Hotel rất hân hạnh được phục vụ Quý khách!
            </p>
        </div>

        <!-- Footer -->
        <div style='background:#f1f5f9;padding:12px 18px;text-align:center;font-size:11px;color:#64748b;border-top:1px solid #e2e8f0;'>
            Hotline 24/7: <strong>1900 1234</strong> | Email: <strong>support@sunhotel.vn</strong><br>
            Địa chỉ: 123 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh
        </div>
    </div>
</body>
</html>";

            var toEmail = invoice.Booking?.Email;
            if (!string.IsNullOrWhiteSpace(toEmail) && !toEmail.EndsWith("@sunhotel.guest"))
            {
                await SendEmail(toEmail, subject, body, "checkout_receipt");
            }
        }

        /// <summary>
        /// Gửi mã xác thực OTP qua Email để đăng nhập Cổng Khách Hàng (Customer Portal)
        /// </summary>
        public async Task<bool> SendCustomerPortalOtpEmail(string toEmail, string guestName, string otpCode)
        {
            var subject = $"Mã OTP xác thực Cổng Khách Hàng: {otpCode} | Sun Hotel";

            var body = $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Mã xác thực OTP - Sun Hotel</title>
</head>
<body style='margin:0;padding:20px 10px;background-color:#f4f6f9;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,sans-serif;'>
    <div style='max-width:440px;margin:0 auto;background:#ffffff;border-radius:14px;box-shadow:0 8px 30px rgba(0,0,0,0.08);overflow:hidden;border:1px solid #e2e8f0;'>
        
        <div style='background:linear-gradient(135deg,#0F2540,#1E3A5F);padding:24px 20px;text-align:center;color:#ffffff;'>
            <div style='font-size:11px;letter-spacing:3px;color:#D4AF37;text-transform:uppercase;font-weight:700;'>Sun Hotel Portal</div>
            <h1 style='margin:6px 0 0;font-size:20px;font-weight:700;'>XÁC THỰC TRUY CẬP</h1>
        </div>

        <div style='padding:26px 24px;'>
            <p style='color:#334155;font-size:14px;margin:0 0 16px;line-height:1.6;'>
                Kính chào Quý khách <strong>{guestName}</strong>,<br>
                Quý khách đang thực hiện đăng nhập vào <strong>Cổng Dịch Vụ Khách Hàng Sun Hotel</strong>.
            </p>

            <div style='background:#f0f9ff;border:2px dashed #0284c7;border-radius:10px;padding:18px;text-align:center;margin:20px 0;'>
                <div style='font-size:11px;text-transform:uppercase;color:#0369a1;font-weight:700;letter-spacing:1px;margin-bottom:8px;'>Mã xác thực OTP của Quý khách</div>
                <div style='font-size:34px;font-weight:800;letter-spacing:8px;color:#0F2540;font-family:monospace;'>
                    {otpCode}
                </div>
                <div style='font-size:11px;color:#64748b;margin-top:8px;'>Hiệu lực trong vòng <strong>10 phút</strong></div>
            </div>

            <div style='background:#fffbeb;border-left:4px solid #f59e0b;padding:10px 14px;border-radius:0 6px 6px 0;margin:18px 0;font-size:12px;color:#92400e;line-height:1.5;'>
                ⚠️ <strong>Lưu ý bảo mật:</strong> Không cung cấp mã OTP này cho bất kỳ ai, kể cả nhân viên khách sạn.
            </div>

            <p style='color:#64748b;font-size:12px;margin:20px 0 0;line-height:1.5;text-align:center;'>
                Sau khi đăng nhập, Quý khách có thể xem hóa đơn chi tiêu hiện tại, đặt món ăn 4 sao tại phòng và tạo ticket yêu cầu dịch vụ.
            </p>
        </div>

        <div style='background:#f8fafc;padding:14px 20px;text-align:center;font-size:11px;color:#94a3b8;border-top:1px solid #e2e8f0;'>
            Hotline hỗ trợ: <strong>1900 1234</strong> | Sun Hotel & Resort
        </div>
    </div>
</body>
</html>";

            return await SendEmail(toEmail, subject, body, "customer_portal_otp");
        }

        /// <summary>
        /// Self-check kết nối mạng và SMTP server trước khi thực hiện gửi/thử lại email
        /// </summary>
        public async Task<(bool isHealthy, string message)> CheckSmtpHealthAsync()
        {
            var host = _config["Email:Host"] ?? "smtp.gmail.com";
            var port = int.Parse(_config["Email:Port"] ?? "587");
            var user = _config["Email:User"];
            var pass = _config["Email:Password"];

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass) || user.StartsWith("${"))
            {
                return (false, "Thông tin cấu hình tài khoản gửi email (EMAIL_USER / EMAIL_PASSWORD) chưa được thiết lập.");
            }

            try
            {
                using var tcpClient = new System.Net.Sockets.TcpClient();
                var connectTask = tcpClient.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(3500);

                if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                {
                    return (false, $"Timeout 3.5s khi kết nối tới máy chủ SMTP ({host}:{port}). Vui lòng kiểm tra mạng backend.");
                }

                if (!tcpClient.Connected)
                {
                    return (false, $"Không thể kết nối đến máy chủ SMTP ({host}:{port}).");
                }

                return (true, $"Kết nối SMTP {host}:{port} bình thường.");
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi kết nối tới SMTP server ({host}:{port}): {ex.Message}");
            }
        }

        /// <summary>
        /// Thực hiện self-check toàn diện trước khi cho phép gửi lại email
        /// </summary>
        public async Task<EmailSelfCheckResult> SelfCheckBeforeRetry(EmailQueue email, bool isManual = false)
        {
            var result = new EmailSelfCheckResult();

            // 1. Kiểm tra trạng thái đã gửi & phòng ngừa gửi trùng lặp (Idempotency)
            if (email.Status == EmailStatus.Sent || email.SentAt != null)
            {
                result.IsDuplicateOrAlreadySent = true;
                result.CanSend = false;
                result.Summary = $"Email đã được gửi thành công đến '{email.ToEmail}' lúc {email.SentAt:dd/MM/yyyy HH:mm:ss} UTC. Tự động hủy để tránh gửi trùng lặp.";
                result.CheckMessages.Add("✓ Kiểm tra trùng lặp: Email này đã được gửi thành công trước đó.");
                return result;
            }
            result.CheckMessages.Add("✓ Kiểm tra trạng thái: Email chưa gửi thành công.");

            // 2. Kiểm tra tính hợp lệ của địa chỉ người nhận (RFC)
            if (string.IsNullOrWhiteSpace(email.ToEmail) || !MailAddress.TryCreate(email.ToEmail, out var parsedTo) || !email.ToEmail.Contains('@') || !email.ToEmail.Contains('.'))
            {
                result.IsRecipientValid = false;
                result.IsPermanentFailure = true;
                result.CanSend = false;
                result.RecipientDetail = $"Địa chỉ email '{email.ToEmail}' không đúng cú pháp.";
                result.Summary = $"Self-check thất bại: Địa chỉ người nhận '{email.ToEmail}' không hợp lệ. Đã hủy gửi lại vĩnh viễn.";
                result.CheckMessages.Add($"✗ Cú pháp email người nhận không hợp lệ: '{email.ToEmail}'. Hủy gửi để tránh bounce.");
                return result;
            }
            result.IsRecipientValid = true;
            result.CheckMessages.Add($"✓ Địa chỉ người nhận hợp lệ: '{email.ToEmail}'.");

            // 3. Kiểm tra tính toàn vẹn của nội dung email
            if (string.IsNullOrWhiteSpace(email.Subject))
            {
                result.IsContentValid = false;
                result.IsPermanentFailure = true;
                result.CanSend = false;
                result.ContentDetail = "Tiêu đề email bị rỗng.";
                result.Summary = "Self-check thất bại: Tiêu đề email rỗng. Đã hủy gửi lại.";
                result.CheckMessages.Add("✗ Tiêu đề email bị rỗng.");
                return result;
            }

            var templateData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(email.TemplateData ?? "{}");
            var body = templateData?.GetValueOrDefault("body") ?? "";
            if (string.IsNullOrWhiteSpace(body))
            {
                result.IsContentValid = false;
                result.IsPermanentFailure = true;
                result.CanSend = false;
                result.ContentDetail = "Nội dung (body) email bị rỗng.";
                result.Summary = "Self-check thất bại: Nội dung email rỗng. Đã hủy gửi lại.";
                result.CheckMessages.Add("✗ Nội dung email bị rỗng.");
                return result;
            }
            result.IsContentValid = true;
            result.CheckMessages.Add("✓ Nội dung và tiêu đề email đầy đủ, nguyên vẹn.");

            // 4. Kiểm tra số lần thử tối đa (Max Attempts)
            var maxAttempts = int.Parse(_config["Email:MaxRetryAttempts"] ?? "3");
            if (email.Attempts >= maxAttempts && !isManual)
            {
                result.IsWithinMaxAttempts = false;
                result.IsPermanentFailure = true;
                result.CanSend = false;
                result.Summary = $"Đã vượt quá số lần thử tối đa ({email.Attempts}/{maxAttempts}). Tự động dừng gửi lại.";
                result.CheckMessages.Add($"✗ Số lần thử đã đạt ngưỡng tối đa ({email.Attempts}/{maxAttempts}).");
                return result;
            }
            result.IsWithinMaxAttempts = true;
            result.CheckMessages.Add($"✓ Số lần thử còn trong giới hạn ({email.Attempts}/{maxAttempts}).");

            // 5. Kiểm tra Exponential Backoff Cooldown (tránh gửi dồn dập trong vài giây)
            if (!isManual && email.Attempts > 0)
            {
                var cooldownMinutes = email.Attempts switch
                {
                    1 => 1,
                    2 => 3,
                    _ => 10
                };

                var timeSinceCreated = DateTime.UtcNow - email.CreatedAt;
                var minWaitTime = TimeSpan.FromMinutes(cooldownMinutes);
                if (timeSinceCreated < minWaitTime)
                {
                    var remaining = minWaitTime - timeSinceCreated;
                    result.CooldownRemaining = remaining;
                    result.CanSend = false;
                    result.Summary = $"Đang trong thời gian giãn cách thử lại (Cooldown). Cần chờ thêm {remaining.TotalSeconds:N0} giây.";
                    result.CheckMessages.Add($"⏳ Đang chờ thời gian giãn cách ({remaining.TotalSeconds:N0}s còn lại).");
                    return result;
                }
            }
            result.CheckMessages.Add("✓ Đã qua thời gian chờ giãn cách (Cooldown).");

            // 6. Kiểm tra sức khỏe SMTP & Backend Network
            var (isHealthy, healthMessage) = await CheckSmtpHealthAsync();
            result.IsSmtpHealthy = isHealthy;
            result.SmtpHealthDetail = healthMessage;
            if (!isHealthy)
            {
                result.CanSend = false;
                result.Summary = $"Máy chủ SMTP hoặc mạng backend chưa sẵn sàng: {healthMessage}";
                result.CheckMessages.Add($"✗ Kết nối SMTP thất bại: {healthMessage}");
                return result;
            }
            result.CheckMessages.Add("✓ Kết nối tới SMTP Server sẵn sàng.");

            result.CanSend = true;
            result.Summary = "Self-check thành công 100%! Đủ điều kiện gửi email.";
            return result;
        }

        /// <summary>
        /// Xử lý tự động gửi lại các email lỗi/đang chờ sau khi Self-Check
        /// </summary>
        public async Task<int> ProcessEmailRetryQueueAsync(int maxProcess = 10)
        {
            var maxAttempts = int.Parse(_config["Email:MaxRetryAttempts"] ?? "3");

            // Trước khi quét queue, self-check kết nối SMTP trước.
            // Nếu backend mất mạng hoàn toàn, không quét để tránh spam log và tốn tài nguyên.
            var (isSmtpHealthy, healthMsg) = await CheckSmtpHealthAsync();
            if (!isSmtpHealthy)
            {
                _logger.LogWarning("[EmailRetryQueue] Tạm hoãn đợt tự động gửi lại email do kết nối SMTP chưa sẵn sàng: {Msg}", healthMsg);
                return 0;
            }

            var retryableEmails = await _db.EmailQueues
                .Where(e => (e.Status == EmailStatus.Pending || e.Status == EmailStatus.Failed) && e.Attempts < maxAttempts)
                .OrderBy(e => e.CreatedAt)
                .Take(maxProcess)
                .ToListAsync();

            if (!retryableEmails.Any())
                return 0;

            int sentCount = 0;
            foreach (var email in retryableEmails)
            {
                var check = await SelfCheckBeforeRetry(email, isManual: false);
                if (!check.CanSend)
                {
                    if (check.IsPermanentFailure)
                    {
                        email.Status = EmailStatus.Failed;
                        email.LastError = $"[SelfCheck Failed] {check.Summary}";
                        await _db.SaveChangesAsync();
                        _logger.LogWarning("Email id={Id} to={Email} bị hủy gửi lại: {Reason}", email.Id, email.ToEmail, check.Summary);
                    }
                    continue;
                }

                // Thực hiện gửi lại
                var templateData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(email.TemplateData ?? "{}");
                var body = templateData?.GetValueOrDefault("body") ?? "";
                var subject = templateData?.GetValueOrDefault("subject") ?? email.Subject;
                var (fromEmail, fromName) = GetSenderInfo();

                email.Attempts++;
                try
                {
                    await SendViaSmtp(
                        _config["Email:Host"]!,
                        int.Parse(_config["Email:Port"] ?? "587"),
                        _config["Email:User"]!,
                        _config["Email:Password"]!,
                        fromEmail,
                        fromName,
                        email.ToEmail,
                        subject,
                        body
                    );

                    email.Status = EmailStatus.Sent;
                    email.SentAt = DateTime.UtcNow;
                    email.LastError = null;
                    sentCount++;

                    _logger.LogInformation("Tự động gửi lại email thành công cho: {ToEmail}, Subject: {Subject}, Lần thử: {Attempts}",
                        email.ToEmail, subject, email.Attempts);
                }
                catch (Exception ex)
                {
                    email.LastError = ex.Message;
                    if (email.Attempts >= maxAttempts)
                    {
                        email.Status = EmailStatus.Failed;
                    }

                    _logger.LogWarning("Tự động gửi lại email thất bại cho: {ToEmail}, Lần thử: {Attempts}, Lỗi: {Error}",
                        email.ToEmail, email.Attempts, ex.Message);
                }

                await _db.SaveChangesAsync();
            }

            return sentCount;
        }

        public async Task ProcessEmailQueue(int maxProcess = 10)
        {
            await ProcessEmailRetryQueueAsync(maxProcess);
        }

        /// <summary>
        /// Kích hoạt gửi lại thủ công một email cụ thể sau khi Self-Check
        /// </summary>
        public async Task<EmailRetryResult> RetryEmailAsync(int emailQueueId, bool force = false)
        {
            var email = await _db.EmailQueues.FirstOrDefaultAsync(e => e.Id == emailQueueId);
            if (email == null)
            {
                return new EmailRetryResult { Success = false, Message = "Không tìm thấy bản ghi email trong hệ thống." };
            }

            var check = await SelfCheckBeforeRetry(email, isManual: force);
            if (!check.CanSend && (!force || check.IsDuplicateOrAlreadySent || !check.IsRecipientValid || !check.IsContentValid))
            {
                return new EmailRetryResult
                {
                    Success = false,
                    Message = check.Summary,
                    SelfCheckResult = check
                };
            }

            var templateData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(email.TemplateData ?? "{}");
            var body = templateData?.GetValueOrDefault("body") ?? "";
            var subject = templateData?.GetValueOrDefault("subject") ?? email.Subject;
            var (fromEmail, fromName) = GetSenderInfo();

            email.Attempts++;
            try
            {
                await SendViaSmtp(
                    _config["Email:Host"]!,
                    int.Parse(_config["Email:Port"] ?? "587"),
                    _config["Email:User"]!,
                    _config["Email:Password"]!,
                    fromEmail,
                    fromName,
                    email.ToEmail,
                    subject,
                    body
                );

                email.Status = EmailStatus.Sent;
                email.SentAt = DateTime.UtcNow;
                email.LastError = null;
                await _db.SaveChangesAsync();

                _logger.LogInformation("Gửi lại email thủ công thành công: {ToEmail}", email.ToEmail);
                return new EmailRetryResult
                {
                    Success = true,
                    Message = $"Gửi lại email thành công tới '{email.ToEmail}'.",
                    SelfCheckResult = check
                };
            }
            catch (Exception ex)
            {
                email.LastError = ex.Message;
                email.Status = EmailStatus.Failed;
                await _db.SaveChangesAsync();

                _logger.LogError(ex, "Gửi lại email thủ công thất bại: {ToEmail}", email.ToEmail);
                return new EmailRetryResult
                {
                    Success = false,
                    Message = $"Gửi email thất bại: {ex.Message}",
                    SelfCheckResult = check
                };
            }
        }

        /// <summary>
        /// Kiểm tra trạng thái đã gửi tới khách hàng hay chưa kèm kết quả self-check
        /// </summary>
        public async Task<EmailQueueItemViewModel?> CheckEmailDeliveryStatusAsync(int emailQueueId)
        {
            var email = await _db.EmailQueues.FirstOrDefaultAsync(e => e.Id == emailQueueId);
            if (email == null) return null;

            var check = await SelfCheckBeforeRetry(email, isManual: true);
            return new EmailQueueItemViewModel
            {
                Id = email.Id,
                ToEmail = email.ToEmail,
                Subject = email.Subject,
                Template = email.Template,
                Status = email.Status,
                Attempts = email.Attempts,
                LastError = email.LastError,
                SentAt = email.SentAt,
                CreatedAt = email.CreatedAt,
                SelfCheck = check
            };
        }

        /// <summary>
        /// Kiểm tra email gần nhất gửi tới khách hàng đã thành công chưa
        /// </summary>
        public async Task<EmailQueueItemViewModel?> GetLatestDeliveryStatusForCustomerAsync(string toEmail)
        {
            var email = await _db.EmailQueues
                .Where(e => e.ToEmail.ToLower() == toEmail.Trim().ToLower())
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync();

            if (email == null) return null;

            return await CheckEmailDeliveryStatusAsync(email.Id);
        }

        /// <summary>
        /// Lấy danh sách hàng đợi email phục vụ theo dõi và kiểm tra trạng thái
        /// </summary>
        public async Task<List<EmailQueueItemViewModel>> GetEmailQueueListAsync(string? searchEmail = null, EmailStatus? status = null, int limit = 100)
        {
            var query = _db.EmailQueues.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchEmail))
            {
                var term = searchEmail.Trim().ToLower();
                query = query.Where(e => e.ToEmail.ToLower().Contains(term) || e.Subject.ToLower().Contains(term));
            }

            if (status.HasValue)
            {
                query = query.Where(e => e.Status == status.Value);
            }

            var list = await query
                .OrderByDescending(e => e.CreatedAt)
                .Take(limit)
                .Select(e => new EmailQueueItemViewModel
                {
                    Id = e.Id,
                    ToEmail = e.ToEmail,
                    Subject = e.Subject,
                    Template = e.Template,
                    Status = e.Status,
                    Attempts = e.Attempts,
                    LastError = e.LastError,
                    SentAt = e.SentAt,
                    CreatedAt = e.CreatedAt
                })
                .ToListAsync();

            return list;
        }

        /// <summary>
        /// Gửi lại toàn bộ email đang ở trạng thái lỗi
        /// </summary>
        public async Task<int> RetryAllFailedEmailsAsync()
        {
            var failedEmails = await _db.EmailQueues
                .Where(e => e.Status == EmailStatus.Failed)
                .OrderByDescending(e => e.CreatedAt)
                .Take(50)
                .ToListAsync();

            int successCount = 0;
            foreach (var email in failedEmails)
            {
                var result = await RetryEmailAsync(email.Id, force: true);
                if (result.Success)
                    successCount++;
            }
            return successCount;
        }
    }
}
