using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;

namespace QuanLyKhachSan.Services
{
    public class ReportService
    {
        private readonly SunHotelDbContext _db;
        private readonly ILogger<ReportService> _logger;

        public ReportService(SunHotelDbContext db, ILogger<ReportService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<DashboardViewModel> GetDashboardSummary()
        {
            var today = DateTime.UtcNow.Date;

            var todayBookings = await _db.Bookings.CountAsync(b => b.CreatedAt.Date == today);
            var todayRevenue = await _db.Invoices
                .Where(i => i.PaymentStatus == PaymentStatus.Paid && i.PaymentDate != null && i.PaymentDate.Value.Date == today)
                .SumAsync(i => i.TotalAmount);

            var rooms = await _db.Rooms.ToListAsync();
            var roomStats = new RoomSummary
            {
                Total = rooms.Count,
                Available = rooms.Count(r => r.Status == RoomStatus.Available),
                Occupied = rooms.Count(r => r.Status == RoomStatus.Occupied),
                Cleaning = rooms.Count(r => r.Status == RoomStatus.Cleaning),
                Maintenance = rooms.Count(r => r.Status == RoomStatus.Maintenance)
            };

            var occupancyRate = roomStats.Total > 0
                ? Math.Round((decimal)roomStats.Occupied / roomStats.Total * 100, 2)
                : 0;

            var pendingTickets = await _db.Tickets.CountAsync(t =>
                t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress);

            var activeBookings = await _db.Bookings.CountAsync(b =>
                b.Status == BookingStatus.Pending ||
                b.Status == BookingStatus.Confirmed ||
                b.Status == BookingStatus.CheckedIn);

            var pendingPayments = await _db.Invoices.CountAsync(i =>
                i.PaymentStatus == PaymentStatus.Unpaid || i.PaymentStatus == PaymentStatus.Pending);

            var recentBookings = await _db.Bookings
                .Include(b => b.RoomType)
                .Include(b => b.Room)
                .OrderByDescending(b => b.CreatedAt)
                .Take(10)
                .ToListAsync();

            return new DashboardViewModel
            {
                TodayNewBookings = todayBookings,
                TodayRevenue = todayRevenue,
                RoomStats = roomStats,
                OccupancyRate = occupancyRate,
                PendingTickets = pendingTickets,
                ActiveBookings = activeBookings,
                PendingPayments = pendingPayments,
                RecentBookings = recentBookings
            };
        }

        public async Task<RevenueReportViewModel> GetRevenueReport(ReportFilterViewModel filter)
        {
            var startDate = filter.StartDate.ToDateTime(TimeOnly.MinValue);
            var endDate = filter.EndDate.ToDateTime(TimeOnly.MaxValue);

            var invoices = await _db.Invoices
                .Include(i => i.Booking).ThenInclude(b => b.RoomType)
                .Where(i => i.PaymentStatus == PaymentStatus.Paid &&
                            i.PaymentDate != null &&
                            i.PaymentDate >= startDate &&
                            i.PaymentDate <= endDate)
                .ToListAsync();

            var totalRevenue = invoices.Sum(i => i.TotalAmount);
            var roomRevenue = invoices.Sum(i => i.RoomCharge);
            var serviceRevenue = invoices.Sum(i => i.ServiceCharge);
            var totalDiscount = invoices.Sum(i => i.Discount);

            var breakdown = filter.GroupBy switch
            {
                "month" => invoices.GroupBy(i => i.PaymentDate!.Value.ToString("yyyy-MM"))
                    .Select(g => new RevenueBreakdown
                    {
                        Period = g.Key,
                        InvoiceCount = g.Count(),
                        Revenue = g.Sum(i => i.TotalAmount),
                        RoomRevenue = g.Sum(i => i.RoomCharge),
                        ServiceRevenue = g.Sum(i => i.ServiceCharge)
                    }).OrderBy(b => b.Period).ToList(),
                "room_type" => invoices.GroupBy(i => i.Booking.RoomType.Name)
                    .Select(g => new RevenueBreakdown
                    {
                        Period = g.Key,
                        InvoiceCount = g.Count(),
                        Revenue = g.Sum(i => i.TotalAmount),
                        RoomRevenue = g.Sum(i => i.RoomCharge),
                        ServiceRevenue = g.Sum(i => i.ServiceCharge)
                    }).OrderByDescending(b => b.Revenue).ToList(),
                _ => invoices.GroupBy(i => i.PaymentDate!.Value.ToString("yyyy-MM-dd"))
                    .Select(g => new RevenueBreakdown
                    {
                        Period = g.Key,
                        InvoiceCount = g.Count(),
                        Revenue = g.Sum(i => i.TotalAmount),
                        RoomRevenue = g.Sum(i => i.RoomCharge),
                        ServiceRevenue = g.Sum(i => i.ServiceCharge)
                    }).OrderBy(b => b.Period).ToList()
            };

            var paymentMethods = invoices
                .GroupBy(i => i.PaymentMethod)
                .Select(g => new PaymentMethodStat
                {
                    Method = g.Key.ToString(),
                    Count = g.Count(),
                    Total = g.Sum(i => i.TotalAmount)
                }).ToList();

            return new RevenueReportViewModel
            {
                Filter = filter,
                TotalRevenue = totalRevenue,
                RoomRevenue = roomRevenue,
                ServiceRevenue = serviceRevenue,
                TotalDiscount = totalDiscount,
                TotalInvoices = invoices.Count,
                AvgInvoice = invoices.Count > 0 ? totalRevenue / invoices.Count : 0,
                Breakdown = breakdown,
                PaymentMethods = paymentMethods
            };
        }
    }
}
