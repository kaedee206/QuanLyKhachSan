using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Services
{
    /// <summary>
    /// Service gửi email qua SFTP mail server
    /// Hỗ trợ: SMTP qua SSH tunnel (SFTP/SMTP proxy)
    /// </summary>
    public class EmailSftpService
    {
        private readonly SunHotelDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<EmailSftpService> _logger;

        public EmailSftpService(SunHotelDbContext db, IConfiguration config, ILogger<EmailSftpService> logger)
        {
            _db = db;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Gửi email qua SMTP server (có thể qua SFTP tunnel nếu cấu hình)
        /// </summary>
        public async Task<bool> SendEmail(string toEmail, string subject, string body, string template = "general")
        {
            var smtpHost = _config["Email:Host"];
            var smtpPort = int.Parse(_config["Email:Port"] ?? "587");
            var smtpUser = _config["Email:User"];
            var smtpPass = _config["Email:Password"];
            var fromEmail = _config["Email:From"] ?? "noreply@sunhotel.vn";
            var fromName = _config["Email:FromName"] ?? "Sun Hotel";
            var useSftp = bool.Parse(_config["Email:UseSftp"] ?? "false");
            var sftpHost = _config["Email:SftpHost"];
            var sftpPort = int.Parse(_config["Email:SftpPort"] ?? "22");
            var sftpUser = _config["Email:SftpUser"];
            var sftpPass = _config["Email:SftpPassword"];

            // Tạo email queue record
            var emailQueue = new EmailQueue
            {
                ToEmail = toEmail,
                Subject = subject,
                Template = template,
                TemplateData = System.Text.Json.JsonSerializer.Serialize(new { body, subject }),
                Status = EmailStatus.Pending,
                Attempts = 0
            };
            _db.EmailQueues.Add(emailQueue);
            await _db.SaveChangesAsync();

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

                // Cập nhật trạng thái thành công
                emailQueue.Status = EmailStatus.Sent;
                emailQueue.SentAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                _logger.LogInformation("Email sent successfully to: {ToEmail}, Subject: {Subject}", toEmail, subject);
                return true;
            }
            catch (Exception ex)
            {
                // Cập nhật trạng thái thất bại
                emailQueue.Status = EmailStatus.Failed;
                emailQueue.Attempts++;
                emailQueue.LastError = ex.Message;
                await _db.SaveChangesAsync();

                _logger.LogError(ex, "Failed to send email to: {ToEmail}", toEmail);
                return false;
            }
        }

        /// <summary>
        /// Gửi email trực tiếp qua SMTP (không qua SFTP)
        /// </summary>
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
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            message.To.Add(toEmail);

            await client.SendMailAsync(message);
        }

        /// <summary>
        /// Gửi email qua SMTP thông qua SFTP/SSH tunnel (port forwarding)
        /// Sử dụng Renci.SshNet để tạo SSH tunnel đến mail server
        /// </summary>
        private async Task SendViaSftpTunnel(string smtpHost, int smtpPort, string smtpUser, string smtpPassword, string fromEmail, string fromName, string toEmail, string subject, string body)
        {
            var sftpHost = _config["Email:SftpHost"]!;
            var sftpPort = int.Parse(_config["Email:SftpPort"] ?? "22");
            var sftpUser = _config["Email:SftpUser"]!;
            var sftpPass = _config["Email:SftpPassword"] ?? "";
            var localPort = int.Parse(_config["Email:LocalSmtpPort"] ?? "1025");

            // Tạo SSH forward connection để tunnel SMTP (local port forwarding)
            using var client = new SshClient(sftpHost, sftpPort, sftpUser, sftpPass);
            client.Connect();

            if (!client.IsConnected)
                throw new InvalidOperationException("Khong the ket noi SFTP server");

            // Tạo port forwarding (SSH tunnel) - dùng ForwardedPortLocal
            var forwardedPort = new Renci.SshNet.ForwardedPortLocal("127.0.0.1", (uint)localPort, smtpHost, (uint)smtpPort);
            client.AddForwardedPort(forwardedPort);
            forwardedPort.Start();

            try
            {
                // Gửi email qua tunnel
                using var smtpClient = new SmtpClient("127.0.0.1", localPort)
                {
                    EnableSsl = false, // SSH tunnel đã mã hóa
                    Credentials = new NetworkCredential(smtpUser, smtpPassword),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(fromEmail, fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                message.To.Add(toEmail);

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

        /// <summary>
        /// Gửi email xác nhận booking cho khách hàng (thiết kế HTML/CSS đẹp)
        /// </summary>
        public async Task SendBookingConfirmationEmail(Booking booking)
        {
            var subject = $"Xac nhan dat phong - Ma {booking.BookingCode} | Sun Hotel";
            var nights = booking.CheckOutDate.DayNumber - booking.CheckInDate.DayNumber;

            var body = $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Xac nhan dat phong - Sun Hotel</title>
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
                            <div style='display:inline-block;background:#e8f5e9;color:#2e7d32;font-size:13px;font-weight:700;padding:6px 18px;border-radius:20px;letter-spacing:1px;margin-bottom:15px;'>✓ DAT PHONG THANH CONG</div>
                            <h2 style='color:#1a3a5c;margin:0 0 5px;font-size:22px;'>Xin chao, <span style='color:#2d6a8f;'>{booking.GuestName}</span>!</h2>
                            <p style='color:#666;margin:5px 0 0;font-size:14px;'>Chung toi xac nhan dat phong cua quy khach thanh cong.<br>Don cua quy khach dang duoc xu ly va cho thanh toan.</p>
                        </td>
                    </tr>

                    <!-- Booking details card -->
                    <tr>
                        <td style='padding:25px 40px 10px;'>
                            <table width='100%' cellpadding='0' cellspacing='0' style='background:#f8fafc;border-radius:10px;border:1px solid #e2e8f0;'>
                                <tr>
                                    <td colspan='2' style='padding:15px 20px 10px;border-bottom:1px solid #e2e8f0;'>
                                        <span style='color:#1a3a5c;font-weight:700;font-size:15px;'>Thong tin dat phong</span>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Ma dat phong</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{booking.BookingCode}</p>
                                    </td>
                                    <td style='padding:10px 20px;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Loai phong</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{booking.RoomType?.Name ?? "N/A"}</p>
                                    </td>
                                </tr>
                                <tr style='background:#ffffff;'>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Ngay nhan phong</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{booking.CheckInDate:dd/MM/yyyy}</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Ngay tra phong</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{booking.CheckOutDate:dd/MM/yyyy}</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>So dem</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{nights} dem</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>So khach</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{booking.NumGuests} nguoi</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td colspan='2' style='padding:15px 20px;border-top:2px solid #1a3a5c;background:#f0f7ff;'>
                                        <table width='100%' cellpadding='0' cellspacing='0'>
                                            <tr>
                                                <td style='color:#1a3a5c;font-size:14px;font-weight:600;'>Tong thanh toan:</td>
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
                                <p style='margin:0;color:#795548;font-size:13px;line-height:1.6;'>📋 <strong>Luu y:</strong> Don dat phong dang cho xu ly. Vui long thanh toan de xac nhan dat phong. Sau khi thanh toan, quy khach se nhan duoc email thong báo co the check-in.</p>
                            </div>
                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td style='background:#1a3a5c;padding:25px 40px;text-align:center;'>
                            <p style='color:#a8d4f0;margin:0 0 8px;font-size:13px;'>Cam on quy khach da chon <strong style='color:#ffffff;'>Sun Hotel</strong>!</p>
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

        /// <summary>
        /// Gửi email thông báo thanh toán thành công (thiết kế HTML/CSS đẹp)
        /// </summary>
        public async Task SendPaymentConfirmationEmail(Invoice invoice)
        {
            var booking = invoice.Booking;
            var guestName = booking?.GuestName ?? "Khach hang";
            var roomNumber = booking?.Room?.RoomNumber ?? "Chua xep phong";
            var roomTypeName = booking?.RoomType?.Name ?? "Chua xep loai phong";

            var subject = $"Thanh toan thanh cong - Hoa don {invoice.InvoiceNumber} | Sun Hotel";

            var paymentMethodDisplay = invoice.PaymentMethod.ToString();

            var body = $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Thanh toan thanh cong - Sun Hotel</title>
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
                            <div style='display:inline-block;background:#e8f5e9;color:#2e7d32;font-size:13px;font-weight:700;padding:6px 18px;border-radius:20px;letter-spacing:1px;margin-bottom:15px;'>✓ THANH TOAN THANH CONG</div>
                            <h2 style='color:#1a3a5c;margin:0 0 5px;font-size:22px;'>Xin chao, <span style='color:#2d6a8f;'>{guestName}</span>!</h2>
                            <p style='color:#666;margin:5px 0 0;font-size:14px;'>Chung toi da nhan duoc thanh toan cua quy khach.</p>
                        </td>
                    </tr>

                    <!-- Invoice card -->
                    <tr>
                        <td style='padding:25px 40px 10px;'>
                            <table width='100%' cellpadding='0' cellspacing='0' style='background:#f8fafc;border-radius:10px;border:1px solid #e2e8f0;'>
                                <tr>
                                    <td colspan='2' style='padding:15px 20px 10px;border-bottom:1px solid #e2e8f0;'>
                                        <span style='color:#1a3a5c;font-weight:700;font-size:15px;'>Chi tiet hoa don</span>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>So hoa don</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:15px;font-weight:700;'>{invoice.InvoiceNumber}</p>
                                    </td>
                                    <td style='padding:10px 20px;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Loai phong</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:15px;font-weight:700;'>{roomTypeName}</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Phong</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:15px;font-weight:700;'>{roomNumber}</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Phuong thuc</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:14px;'>{paymentMethodDisplay}</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Tien phong</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:14px;'>{invoice.RoomCharge:N0} VND</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Tien dich vu</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:14px;'>{invoice.ServiceCharge:N0} VND</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Giam gia</p>
                                        <p style='margin:0;color:#e53935;font-size:14px;'>-{invoice.Discount:N0} VND</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                    </td>
                                </tr>
                                <tr>
                                    <td colspan='2' style='padding:15px 20px;border-top:2px solid #1a3a5c;background:#f0f7ff;'>
                                        <table width='100%' cellpadding='0' cellspacing='0'>
                                            <tr>
                                                <td style='color:#1a3a5c;font-size:14px;font-weight:600;'>Tong thanh toan:</td>
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
                            <p style='color:#a8d4f0;margin:0 0 8px;font-size:13px;'>Cam on quy khach da su dung dich vu cua <strong style='color:#ffffff;'>Sun Hotel</strong>!</p>
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

        /// <summary>
        /// Gui email thong bao da hoan tat thu tuc thanh toan va co the check-in
        /// Thoi gian check-in cu the: gio:phut, ngay/thang/nam (VN timezone)
        /// </summary>
        public async Task SendCheckInReadyEmail(Invoice invoice, DateTime checkInTime)
        {
            var booking = invoice.Booking;
            if (booking == null) return;

            var guestName = booking.GuestName ?? "Khach hang";
            var roomNumber = booking.Room?.RoomNumber ?? "Chua xep phong";
            var roomTypeName = booking.RoomType?.Name ?? "Chua xep loai phong";

            // Dinh dang thoi gian check-in: gio:phut, ngay/thang/nam (VN timezone)
            var vnZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            var vnCheckIn = TimeZoneInfo.ConvertTimeFromUtc(checkInTime, vnZone);
            var formattedTime = vnCheckIn.ToString("HH:mm");
            var formattedDate = vnCheckIn.ToString("dd/MM/yyyy");

            var subject = $"Hoan tat thanh toan - San sang check-in luc {formattedTime}, {formattedDate}";

            var paymentMethodDisplay = invoice.PaymentMethod.ToString();

            var body = $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Hoan tat thanh toan - Sun Hotel</title>
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
                            <div style='display:inline-block;background:#e8f5e9;color:#2e7d32;font-size:13px;font-weight:700;padding:6px 18px;border-radius:20px;letter-spacing:1px;margin-bottom:15px;'>✓ THANH TOAN THANH CONG</div>
                            <h2 style='color:#1a3a5c;margin:0 0 5px;font-size:22px;'>Xin chao, <span style='color:#2d6a8f;'>{guestName}</span>!</h2>
                            <p style='color:#666;margin:5px 0 0;font-size:14px;'>Chung toi xac nhan quy khach da hoan tat thu tuc thanh toan va co the tiep tuc check-in.</p>
                        </td>
                    </tr>

                    <!-- Check-in time highlight -->
                    <tr>
                        <td style='padding:20px 40px 0;'>
                            <div style='background:linear-gradient(135deg,#e3f2fd,#f3e5f5);border-radius:10px;padding:20px;text-align:center;border:1px solid #bbdefb;'>
                                <p style='margin:0 0 5px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:1px;'>Thoi gian check-in</p>
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
                                        <span style='color:#1a3a5c;font-weight:700;font-size:15px;'>Chi tiet dat phong</span>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Ma dat phong</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{booking.BookingCode}</p>
                                    </td>
                                    <td style='padding:10px 20px;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Loai phong</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{roomTypeName}</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Phong</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:16px;font-weight:700;'>{roomNumber}</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>So hoa don</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:15px;font-weight:700;'>{invoice.InvoiceNumber}</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Ngay nhan phong</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:15px;font-weight:600;'>{booking.CheckInDate:dd/MM/yyyy}</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Ngay tra phong</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:15px;font-weight:600;'>{booking.CheckOutDate:dd/MM/yyyy}</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 20px;width:50%;border-right:1px solid #e2e8f0;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Phuong thuc</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:14px;'>{paymentMethodDisplay}</p>
                                    </td>
                                    <td style='padding:10px 20px;border-top:1px solid #e2e8f0;vertical-align:top;'>
                                        <p style='margin:0 0 4px;color:#888;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;'>Tien dich vu</p>
                                        <p style='margin:0;color:#1a3a5c;font-size:14px;'>{invoice.ServiceCharge:N0} VND</p>
                                    </td>
                                </tr>
                                <tr>
                                    <td colspan='2' style='padding:15px 20px;border-top:2px solid #1a3a5c;background:#f0f7ff;'>
                                        <table width='100%' cellpadding='0' cellspacing='0'>
                                            <tr>
                                                <td style='color:#1a3a5c;font-size:14px;font-weight:600;'>Tong thanh toan:</td>
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
                                <p style='margin:0;color:#795548;font-size:13px;line-height:1.6;'>📋 <strong>Luu y:</strong> Vui long den le tan truoc <strong>{formattedTime}</strong> de nhan phong va hoan tat thu tuc check-in. Neu can ho tro, vui long lien he <strong>Hotline: 1900 1234</strong>.</p>
                            </div>
                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td style='background:#1a3a5c;padding:25px 40px;text-align:center;'>
                            <p style='color:#a8d4f0;margin:0 0 8px;font-size:13px;'>Cam on quy khach da chon <strong style='color:#ffffff;'>Sun Hotel</strong>! Chung toi rat mong duoc phuc vu quy khach.</p>
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

        /// <summary>
        /// Xử lý hàng đợi email (chạy background hoặc scheduled)
        /// </summary>
        public async Task ProcessEmailQueue(int maxProcess = 10)
        {
            var pendingEmails = await _db.EmailQueues
                .Where(e => e.Status == EmailStatus.Pending && e.Attempts < 3)
                .OrderBy(e => e.CreatedAt)
                .Take(maxProcess)
                .ToListAsync();

            foreach (var email in pendingEmails)
            {
                var templateData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(email.TemplateData ?? "{}");
                var body = templateData?.GetValueOrDefault("body") ?? "";
                var subject = templateData?.GetValueOrDefault("subject") ?? email.Subject;

                email.Attempts++;
                try
                {
                    await SendViaSmtp(
                        _config["Email:Host"]!,
                        int.Parse(_config["Email:Port"] ?? "587"),
                        _config["Email:User"]!,
                        _config["Email:Password"]!,
                        _config["Email:From"] ?? "noreply@sunhotel.vn",
                        _config["Email:FromName"] ?? "Sun Hotel",
                        email.ToEmail,
                        subject,
                        body
                    );

                    email.Status = EmailStatus.Sent;
                    email.SentAt = DateTime.UtcNow;
                    _logger.LogInformation("Email queue processed: {ToEmail}", email.ToEmail);
                }
                catch (Exception ex)
                {
                    email.LastError = ex.Message;
                    if (email.Attempts >= 3)
                        email.Status = EmailStatus.Failed;

                    _logger.LogWarning("Email queue retry failed: {ToEmail}, Attempt: {Attempt}, Error: {Error}",
                        email.ToEmail, email.Attempts, ex.Message);
                }

                await _db.SaveChangesAsync();
            }
        }
    }
}