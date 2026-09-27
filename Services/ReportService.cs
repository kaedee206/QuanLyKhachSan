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

        public async Task<DashboardViewModel> GetDashboardSummary(string range = "7days")
        {
            var nowUtc = DateTime.UtcNow;
            var todayUtc = DateTime.SpecifyKind(nowUtc.Date, DateTimeKind.Utc);
            var tomorrowUtc = todayUtc.AddDays(1);

            // Múi giờ VN (UTC+7)
            var vnNow = nowUtc.AddHours(7);
            var todayDate = DateOnly.FromDateTime(vnNow);
            var todayDateUtc = DateOnly.FromDateTime(nowUtc);

            var startOfVnDayUtc = DateTime.SpecifyKind(todayDate.ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);
            var endOfVnDayUtc = startOfVnDayUtc.AddDays(1);

            var minStart = startOfVnDayUtc < todayUtc ? startOfVnDayUtc : todayUtc;
            var maxEnd = endOfVnDayUtc > tomorrowUtc ? endOfVnDayUtc : tomorrowUtc;

            var todayBookings = await _db.Bookings
                .CountAsync(b => b.CreatedAt >= minStart && b.CreatedAt < maxEnd);

            var todayRevenue = await _db.Invoices
                .Where(i => i.PaymentStatus == PaymentStatus.Paid && i.PaymentDate != null && i.PaymentDate.Value >= minStart && i.PaymentDate.Value < maxEnd)
                .SumAsync(i => (decimal?)i.TotalAmount) ?? 0m;

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

            var todayArrivals = await _db.Bookings
                .Include(b => b.RoomType)
                .Include(b => b.Room)
                .Where(b => (b.CheckInDate == todayDate || b.CheckInDate == todayDateUtc) &&
                            (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Pending))
                .OrderBy(b => b.Id)
                .Take(10)
                .ToListAsync();

            var todayDepartures = await _db.Bookings
                .Include(b => b.RoomType)
                .Include(b => b.Room)
                .Where(b => (b.CheckOutDate == todayDate || b.CheckOutDate == todayDateUtc) &&
                            b.Status == BookingStatus.CheckedIn)
                .OrderBy(b => b.Id)
                .Take(10)
                .ToListAsync();

            var todayExpectedCheckIns = await _db.Bookings.CountAsync(b =>
                (b.CheckInDate == todayDate || b.CheckInDate == todayDateUtc) &&
                (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Pending));

            var todayExpectedCheckOuts = await _db.Bookings.CountAsync(b =>
                (b.CheckOutDate == todayDate || b.CheckOutDate == todayDateUtc) &&
                b.Status == BookingStatus.CheckedIn);

            var recentBookings = await _db.Bookings
                .Include(b => b.RoomType)
                .Include(b => b.Room)
                .OrderByDescending(b => b.CreatedAt)
                .Take(10)
                .ToListAsync();

            var recentTickets = await _db.Tickets
                .Include(t => t.Room)
                .Include(t => t.Reporter)
                .Include(t => t.Assignee)
                .OrderByDescending(t => t.CreatedAt)
                .Take(15)
                .ToListAsync();

                // Chart Calculation Logic
            var endDateChart = DateTime.UtcNow.Date;
            var startDateChart = endDateChart.AddDays(-6);
            bool groupByMonth = false;

            if (range == "1month") startDateChart = endDateChart.AddMonths(-1);
            else if (range == "1quarter") { startDateChart = endDateChart.AddMonths(-3); groupByMonth = true; }
            else if (range == "6months") { startDateChart = endDateChart.AddMonths(-6); groupByMonth = true; }
            else if (range == "1year") { startDateChart = endDateChart.AddYears(-1); groupByMonth = true; }
            else { range = "7days"; }

            var chartLabels = new List<string>();
            var revenueData = new List<decimal>();
            var bookingsData = new List<int>();

            var allInvoices = await _db.Invoices
                .Where(i => i.PaymentStatus == PaymentStatus.Paid && i.PaymentDate != null && i.PaymentDate >= startDateChart)
                .ToListAsync();
            var allBookings = await _db.Bookings
                .Where(b => b.CreatedAt >= startDateChart)
                .ToListAsync();

            if (groupByMonth)
            {
                var currentMonth = new DateTime(startDateChart.Year, startDateChart.Month, 1);
                var endMonth = new DateTime(endDateChart.Year, endDateChart.Month, 1);
                
                while (currentMonth <= endMonth)
                {
                    chartLabels.Add($"Tháng {currentMonth.Month}/{currentMonth.Year}");
                    
                    var rev = allInvoices.Where(i => i.PaymentDate!.Value.Year == currentMonth.Year && i.PaymentDate!.Value.Month == currentMonth.Month).Sum(i => i.TotalAmount);
                    var bks = allBookings.Count(b => b.CreatedAt.Year == currentMonth.Year && b.CreatedAt.Month == currentMonth.Month);
                    
                    revenueData.Add(rev);
                    bookingsData.Add(bks);
                    
                    currentMonth = currentMonth.AddMonths(1);
                }
            }
            else
            {
                for (var date = startDateChart; date <= endDateChart; date = date.AddDays(1))
                {
                    chartLabels.Add(date.ToString("dd/MM"));
                    
                    var rev = allInvoices.Where(i => i.PaymentDate!.Value.Date == date).Sum(i => i.TotalAmount);
                    var bks = allBookings.Count(b => b.CreatedAt.Date == date);
                    
                    revenueData.Add(rev);
                    bookingsData.Add(bks);
                }
            }

            return new DashboardViewModel
            {
                TodayNewBookings = todayBookings,
                TodayRevenue = todayRevenue,
                RoomStats = roomStats,
                OccupancyRate = occupancyRate,
                PendingTickets = pendingTickets,
                ActiveBookings = activeBookings,
                PendingPayments = pendingPayments,
                TodayExpectedCheckIns = todayExpectedCheckIns,
                TodayExpectedCheckOuts = todayExpectedCheckOuts,
                TodayArrivals = todayArrivals,
                TodayDepartures = todayDepartures,
                RecentBookings = recentBookings,
                RecentTickets = recentTickets,
                CurrentRange = range,
                ChartLabels = chartLabels,
                RevenueData = revenueData,
                BookingsData = bookingsData
            };
        }

        public async Task<RevenueReportViewModel> GetRevenueReport(ReportFilterViewModel filter)
        {
            var startDate = DateTime.SpecifyKind(filter.StartDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var endDate = DateTime.SpecifyKind(filter.EndDate.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

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
                        ServiceRevenue = g.Sum(i => i.ServiceCharge),
                        Discount = g.Sum(i => i.Discount)
                    }).OrderBy(b => b.Period).ToList(),
                "room_type" => invoices.GroupBy(i => i.Booking.RoomType.Name)
                    .Select(g => new RevenueBreakdown
                    {
                        Period = g.Key,
                        InvoiceCount = g.Count(),
                        Revenue = g.Sum(i => i.TotalAmount),
                        RoomRevenue = g.Sum(i => i.RoomCharge),
                        ServiceRevenue = g.Sum(i => i.ServiceCharge),
                        Discount = g.Sum(i => i.Discount)
                    }).OrderByDescending(b => b.Revenue).ToList(),
                _ => invoices.GroupBy(i => i.PaymentDate!.Value.ToString("yyyy-MM-dd"))
                    .Select(g => new RevenueBreakdown
                    {
                        Period = g.Key,
                        InvoiceCount = g.Count(),
                        Revenue = g.Sum(i => i.TotalAmount),
                        RoomRevenue = g.Sum(i => i.RoomCharge),
                        ServiceRevenue = g.Sum(i => i.ServiceCharge),
                        Discount = g.Sum(i => i.Discount)
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
