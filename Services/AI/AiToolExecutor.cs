using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models;
using QuanLyKhachSan.Models.AI;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;

namespace QuanLyKhachSan.Services.AI
{
    public class AiToolExecutor
    {
        private readonly SunHotelDbContext _context;
        private readonly ILogger<AiToolExecutor> _logger;
        private readonly AiToolRegistry _registry;
        private readonly BookingService _bookingService;
        private readonly ReportService _reportService;
        private readonly EmailSftpService _emailService;

        public AiToolExecutor(
            SunHotelDbContext context, 
            ILogger<AiToolExecutor> logger, 
            AiToolRegistry registry,
            BookingService bookingService,
            ReportService reportService,
            EmailSftpService emailService)
        {
            _context = context;
            _logger = logger;
            _registry = registry;
            _bookingService = bookingService;
            _reportService = reportService;
            _emailService = emailService;
        }

        public async Task<AiFunctionCallResult> ExecuteToolAsync(string toolName, object? parameters, UserRole role, int? userId = null)
        {
            if (!_registry.IsToolAllowed(toolName, role))
            {
                _logger.LogWarning("Unauthorized tool execution attempt: {ToolName} by role {Role}", toolName, role);
                return new AiFunctionCallResult { FunctionName = toolName, Success = false, ErrorMessage = "Unauthorized access to tool." };
            }

            _logger.LogInformation("Executing AI Tool: {ToolName} by role {Role}", toolName, role);

            try
            {
                switch (toolName)
                {
                    case "check_room_availability":
                        return await CheckRoomAvailabilityAsync(parameters);

                    case "lookup_booking":
                        return await LookupBookingAsync(parameters);

                    case "create_booking":
                    case "create_booking_draft":
                        return await CreateBookingAsync(parameters, userId);

                    case "get_occupancy_and_revenue_metrics":
                        return await GetOccupancyAndRevenueMetricsAsync(parameters);

                    default:
                        return new AiFunctionCallResult
                        {
                            FunctionName = toolName,
                            Success = true,
                            Data = new { Message = $"Tool {toolName} executed successfully" }
                        };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while executing AI tool {ToolName}", toolName);
                return new AiFunctionCallResult
                {
                    FunctionName = toolName,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<AiFunctionCallResult> CheckRoomAvailabilityAsync(object? parameters)
        {
            int? roomTypeId = null;
            DateOnly? checkInDate = null;
            DateOnly? checkOutDate = null;
            int numGuests = 0;

            if (parameters != null)
            {
                if (parameters is JsonElement elem && elem.ValueKind == JsonValueKind.Object)
                {
                    if (elem.TryGetProperty("roomTypeId", out var rtp) && rtp.TryGetInt32(out var rtid)) roomTypeId = rtid;
                    if (elem.TryGetProperty("checkInDate", out var cid) && DateOnly.TryParse(cid.GetString(), out var cin)) checkInDate = cin;
                    if (elem.TryGetProperty("checkOutDate", out var cod) && DateOnly.TryParse(cod.GetString(), out var cout)) checkOutDate = cout;
                    if (elem.TryGetProperty("numGuests", out var ng) && ng.TryGetInt32(out var n)) numGuests = n;
                }
                else if (parameters is IDictionary<string, object> dict)
                {
                    if (dict.TryGetValue("roomTypeId", out var rto) && int.TryParse(rto?.ToString(), out var rtid)) roomTypeId = rtid;
                    if (dict.TryGetValue("checkInDate", out var cido) && DateOnly.TryParse(cido?.ToString(), out var cin)) checkInDate = cin;
                    if (dict.TryGetValue("checkOutDate", out var codo) && DateOnly.TryParse(codo?.ToString(), out var cout)) checkOutDate = cout;
                    if (dict.TryGetValue("numGuests", out var ngo) && int.TryParse(ngo?.ToString(), out var n)) numGuests = n;
                }
            }

            var query = _context.Rooms
                .Include(r => r.RoomType)
                .Where(r => r.RoomType.IsActive);

            if (roomTypeId.HasValue && roomTypeId.Value > 0)
                query = query.Where(r => r.RoomTypeId == roomTypeId.Value);

            if (numGuests > 0)
                query = query.Where(r => r.RoomType.MaxGuests >= numGuests);

            var rooms = await query
                .OrderBy(r => r.RoomType.BasePrice)
                .ThenBy(r => r.RoomNumber)
                .ToListAsync();

            var effectiveCheckIn = checkInDate ?? DateOnly.FromDateTime(DateTime.Today);
            var effectiveCheckOut = checkOutDate ?? effectiveCheckIn.AddDays(1);

            var bookedRoomIds = await _context.Bookings
                .Where(b => b.RoomId != null &&
                            b.Status != BookingStatus.Cancelled &&
                            b.Status != BookingStatus.NoShow &&
                            b.CheckInDate < effectiveCheckOut &&
                            b.CheckOutDate > effectiveCheckIn)
                .Select(b => b.RoomId!.Value)
                .Distinct()
                .ToListAsync();

            var roomTypes = await _context.RoomTypes
                .Where(rt => rt.IsActive)
                .OrderBy(rt => rt.BasePrice)
                .ToListAsync();

            var roomTypeSummaries = roomTypes
                .Where(rt => !roomTypeId.HasValue || rt.Id == roomTypeId.Value)
                .Select(rt =>
                {
                    var matchingRooms = rooms.Where(r => r.RoomTypeId == rt.Id).ToList();
                    var availableRooms = matchingRooms
                        .Where(r => r.Status == RoomStatus.Available && !bookedRoomIds.Contains(r.Id))
                        .ToList();

                    return new
                    {
                        RoomTypeId = rt.Id,
                        RoomTypeName = rt.Name,
                        BasePrice = rt.BasePrice,
                        MaxGuests = rt.MaxGuests,
                        Description = rt.Description,
                        TotalRooms = matchingRooms.Count,
                        AvailableCount = availableRooms.Count,
                        AvailableRoomNumbers = availableRooms.Select(r => r.RoomNumber).ToList()
                    };
                })
                .ToList();

            int totalAvailable = roomTypeSummaries.Sum(s => s.AvailableCount);
            int totalRooms = roomTypeSummaries.Sum(s => s.TotalRooms);

            return new AiFunctionCallResult
            {
                FunctionName = "check_room_availability",
                Success = true,
                Data = new
                {
                    TotalRooms = totalRooms,
                    TotalAvailable = totalAvailable,
                    CheckInDate = effectiveCheckIn.ToString("yyyy-MM-dd"),
                    CheckOutDate = effectiveCheckOut.ToString("yyyy-MM-dd"),
                    RoomTypes = roomTypeSummaries
                }
            };
        }

        private async Task<AiFunctionCallResult> LookupBookingAsync(object? parameters)
        {
            string? bookingCode = null;
            string? phone = null;

            if (parameters is JsonElement elem && elem.ValueKind == JsonValueKind.Object)
            {
                if (elem.TryGetProperty("bookingCode", out var bc)) bookingCode = bc.GetString();
                if (elem.TryGetProperty("phone", out var p)) phone = p.GetString();
            }

            if (string.IsNullOrWhiteSpace(bookingCode) && string.IsNullOrWhiteSpace(phone))
            {
                return new AiFunctionCallResult
                {
                    FunctionName = "lookup_booking",
                    Success = false,
                    ErrorMessage = "Vui lòng cung cấp mã đặt phòng hoặc số điện thoại để tra cứu."
                };
            }

            var query = _context.Bookings
                .Include(b => b.RoomType)
                .Include(b => b.Room)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(bookingCode))
                query = query.Where(b => b.BookingCode == bookingCode.Trim());
            if (!string.IsNullOrWhiteSpace(phone))
                query = query.Where(b => b.Phone == phone.Trim());

            var booking = await query.OrderByDescending(b => b.CreatedAt).FirstOrDefaultAsync();
            if (booking == null)
            {
                return new AiFunctionCallResult
                {
                    FunctionName = "lookup_booking",
                    Success = false,
                    ErrorMessage = "Không tìm thấy hồ sơ đặt phòng phù hợp."
                };
            }

            return new AiFunctionCallResult
            {
                FunctionName = "lookup_booking",
                Success = true,
                Data = new
                {
                    BookingCode = booking.BookingCode,
                    GuestName = booking.GuestName,
                    RoomTypeName = booking.RoomType?.Name,
                    RoomNumber = booking.Room?.RoomNumber ?? "Chưa chỉ định",
                    CheckInDate = booking.CheckInDate.ToString("yyyy-MM-dd"),
                    CheckOutDate = booking.CheckOutDate.ToString("yyyy-MM-dd"),
                    TotalAmount = booking.TotalAmount,
                    Status = booking.Status.ToString()
                }
            };
        }

        private async Task<AiFunctionCallResult> CreateBookingAsync(object? parameters, int? userId)
        {
            int roomTypeId = 0;
            string guestName = string.Empty;
            string phone = string.Empty;
            string? email = null;
            string? paymentMethod = null;
            DateOnly checkInDate = DateOnly.FromDateTime(DateTime.Today);
            DateOnly checkOutDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            int numGuests = 1;
            string? notes = null;

            if (parameters != null)
            {
                if (parameters is JsonElement elem && elem.ValueKind == JsonValueKind.Object)
                {
                    if (elem.TryGetProperty("roomTypeId", out var rtp) && rtp.TryGetInt32(out var rtid)) roomTypeId = rtid;
                    if (elem.TryGetProperty("guestName", out var gn)) guestName = gn.GetString() ?? string.Empty;
                    if (elem.TryGetProperty("phone", out var p)) phone = p.GetString() ?? string.Empty;
                    if (elem.TryGetProperty("email", out var em)) email = em.GetString();
                    if (elem.TryGetProperty("paymentMethod", out var pm)) paymentMethod = pm.GetString();
                    if (elem.TryGetProperty("payment_method", out var pmu) && string.IsNullOrWhiteSpace(paymentMethod)) paymentMethod = pmu.GetString();
                    if (elem.TryGetProperty("checkInDate", out var cid) && DateOnly.TryParse(cid.GetString(), out var cin)) checkInDate = cin;
                    if (elem.TryGetProperty("checkOutDate", out var cod) && DateOnly.TryParse(cod.GetString(), out var cout)) checkOutDate = cout;
                    if (elem.TryGetProperty("numGuests", out var ng) && ng.TryGetInt32(out var n)) numGuests = n;
                    if (elem.TryGetProperty("notes", out var nt)) notes = nt.GetString();
                }
                else if (parameters is IDictionary<string, object> dict)
                {
                    if (dict.TryGetValue("roomTypeId", out var rto) && int.TryParse(rto?.ToString(), out var rtid)) roomTypeId = rtid;
                    if (dict.TryGetValue("guestName", out var gno)) guestName = gno?.ToString() ?? string.Empty;
                    if (dict.TryGetValue("phone", out var po)) phone = po?.ToString() ?? string.Empty;
                    if (dict.TryGetValue("email", out var emo)) email = emo?.ToString();
                    if (dict.TryGetValue("paymentMethod", out var pmo)) paymentMethod = pmo?.ToString();
                    if (dict.TryGetValue("payment_method", out var pmuo) && string.IsNullOrWhiteSpace(paymentMethod)) paymentMethod = pmuo?.ToString();
                    if (dict.TryGetValue("checkInDate", out var cido) && DateOnly.TryParse(cido?.ToString(), out var cin)) checkInDate = cin;
                    if (dict.TryGetValue("checkOutDate", out var codo) && DateOnly.TryParse(codo?.ToString(), out var cout)) checkOutDate = cout;
                    if (dict.TryGetValue("numGuests", out var ngo) && int.TryParse(ngo?.ToString(), out var n)) numGuests = n;
                    if (dict.TryGetValue("notes", out var nto)) notes = nto?.ToString();
                }
            }

            // Fallback nếu chưa có roomTypeId: chọn hạng phòng đang khả dụng
            if (roomTypeId <= 0)
            {
                var defaultRoomType = await _context.RoomTypes
                    .Where(rt => rt.IsActive)
                    .OrderBy(rt => rt.BasePrice)
                    .FirstOrDefaultAsync();
                if (defaultRoomType != null) roomTypeId = defaultRoomType.Id;
            }

            var roomType = await _context.RoomTypes.FindAsync(roomTypeId);
            if (roomType == null)
            {
                return new AiFunctionCallResult
                {
                    FunctionName = "create_booking",
                    Success = false,
                    ErrorMessage = "Hạng phòng không tồn tại trong hệ thống."
                };
            }

            if (string.IsNullOrWhiteSpace(guestName))
            {
                return new AiFunctionCallResult
                {
                    FunctionName = "create_booking",
                    Success = false,
                    ErrorMessage = "Vui lòng cung cấp Họ và tên người nhận phòng."
                };
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                return new AiFunctionCallResult
                {
                    FunctionName = "create_booking",
                    Success = false,
                    ErrorMessage = "Vui lòng cung cấp Số điện thoại liên hệ để nhân viên tiện liên lạc."
                };
            }

            if (checkOutDate <= checkInDate)
            {
                checkOutDate = checkInDate.AddDays(1);
            }

            if (numGuests <= 0) numGuests = 1;
            if (numGuests > roomType.MaxGuests)
            {
                return new AiFunctionCallResult
                {
                    FunctionName = "create_booking",
                    Success = false,
                    ErrorMessage = $"Hạng phòng {roomType.Name} phục vụ tối đa {roomType.MaxGuests} khách. Quý khách vui lòng chọn loại phòng lớn hơn hoặc đặt thêm phòng."
                };
            }

            var paymentTag = !string.IsNullOrWhiteSpace(paymentMethod) ? $" | HTTT: {paymentMethod.Trim()}" : "";
            var noteContent = string.IsNullOrWhiteSpace(notes) ? $"Đặt qua Tiếp tân ảo AI Agent{paymentTag}" : $"[AI Agent] {notes.Trim()}{paymentTag}";

            var viewModel = new CreateBookingViewModel
            {
                RoomTypeId = roomTypeId,
                GuestName = guestName.Trim(),
                Phone = phone.Trim(),
                Email = string.IsNullOrWhiteSpace(email) ? $"{phone.Trim()}@sunhotel.guest" : email.Trim(),
                CheckInDate = checkInDate,
                CheckOutDate = checkOutDate,
                NumGuests = numGuests,
                Notes = noteContent
            };

            var booking = await _bookingService.CreateBooking(viewModel, userId);
            int nights = booking.CheckOutDate.DayNumber - booking.CheckInDate.DayNumber;

            _logger.LogInformation("AI Agent created booking {BookingCode} for {GuestName} ({Phone}) - HTTT: {PaymentMethod}", 
                booking.BookingCode, booking.GuestName, booking.Phone, paymentMethod ?? "N/A");

            // Tự động kích hoạt gửi Email xác nhận tới khách hàng nếu có email thật
            if (!string.IsNullOrWhiteSpace(booking.Email) && !booking.Email.EndsWith("@sunhotel.guest", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await _emailService.SendBookingConfirmationEmail(booking);
                    _logger.LogInformation("Đã kích hoạt gửi email xác nhận đặt phòng qua AI tới {Email}", booking.Email);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Lỗi khi kích hoạt gửi email xác nhận đặt phòng tới {Email}", booking.Email);
                }
            }

            return new AiFunctionCallResult
            {
                FunctionName = "create_booking",
                Success = true,
                Data = new
                {
                    BookingId = booking.Id,
                    BookingCode = booking.BookingCode,
                    GuestName = booking.GuestName,
                    Phone = booking.Phone,
                    Email = booking.Email,
                    PaymentMethod = paymentMethod ?? "Thanh toán tại quầy",
                    RoomTypeId = booking.RoomTypeId,
                    RoomTypeName = roomType.Name,
                    CheckInDate = booking.CheckInDate.ToString("yyyy-MM-dd"),
                    CheckOutDate = booking.CheckOutDate.ToString("yyyy-MM-dd"),
                    Nights = nights,
                    NumGuests = booking.NumGuests,
                    TotalAmount = booking.TotalAmount,
                    Status = booking.Status.ToString(),
                    ConfirmationUrl = $"/Booking/Confirmation?code={booking.BookingCode}",
                    PaymentUrl = $"/Payment?code={booking.BookingCode}"
                }
            };
        }

        private async Task<AiFunctionCallResult> GetOccupancyAndRevenueMetricsAsync(object? parameters)
        {
            string range = "7days";
            if (parameters != null)
            {
                if (parameters is JsonElement elem && elem.ValueKind == JsonValueKind.Object)
                {
                    if (elem.TryGetProperty("range", out var rp) && !string.IsNullOrWhiteSpace(rp.GetString()))
                    {
                        range = rp.GetString()!;
                    }
                }
                else if (parameters is IDictionary<string, object> dict)
                {
                    if (dict.TryGetValue("range", out var ro) && ro != null && !string.IsNullOrWhiteSpace(ro.ToString()))
                    {
                        range = ro.ToString()!;
                    }
                }
            }

            var summary = await _reportService.GetDashboardSummary(range);

            decimal totalPeriodRevenue = summary.RevenueData?.Sum() ?? 0m;
            int totalPeriodBookings = summary.BookingsData?.Sum() ?? 0;

            var metricsData = new
            {
                TodayRevenue = summary.TodayRevenue,
                TodayNewBookings = summary.TodayNewBookings,
                OccupancyRate = summary.OccupancyRate,
                TotalRooms = summary.RoomStats.Total,
                AvailableRooms = summary.RoomStats.Available,
                OccupiedRooms = summary.RoomStats.Occupied,
                CleaningRooms = summary.RoomStats.Cleaning,
                MaintenanceRooms = summary.RoomStats.Maintenance,
                ActiveBookings = summary.ActiveBookings,
                PendingPayments = summary.PendingPayments,
                PendingTickets = summary.PendingTickets,
                TodayExpectedCheckIns = summary.TodayExpectedCheckIns,
                TodayExpectedCheckOuts = summary.TodayExpectedCheckOuts,
                CurrentRange = summary.CurrentRange,
                TotalPeriodRevenue = totalPeriodRevenue,
                TotalPeriodBookings = totalPeriodBookings,
                ChartLabels = summary.ChartLabels,
                RevenueBreakdownByDay = summary.ChartLabels != null && summary.RevenueData != null
                    ? summary.ChartLabels.Zip(summary.RevenueData, (label, rev) => new { Date = label, Revenue = rev }).ToList()
                    : null
            };

            return new AiFunctionCallResult
            {
                FunctionName = "get_occupancy_and_revenue_metrics",
                Success = true,
                Data = metricsData
            };
        }
    }
}
