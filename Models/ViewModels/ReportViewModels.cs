using System.ComponentModel.DataAnnotations;
using QuanLyKhachSan.Models.Entities;

namespace QuanLyKhachSan.Models.ViewModels
{
    public class ReportFilterViewModel
    {
        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Từ ngày")]
        public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddMonths(-1));

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Đến ngày")]
        public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        public string GroupBy { get; set; } = "day";
    }

    public class DashboardViewModel
    {
        public int TodayNewBookings { get; set; }
        public decimal TodayRevenue { get; set; }
        public RoomSummary RoomStats { get; set; } = new();
        public decimal OccupancyRate { get; set; }
        public int PendingTickets { get; set; }
        public int ActiveBookings { get; set; }
        public int PendingPayments { get; set; }
        public int TodayExpectedCheckIns { get; set; }
        public int TodayExpectedCheckOuts { get; set; }
        public List<Booking> TodayArrivals { get; set; } = new();
        public List<Booking> TodayDepartures { get; set; } = new();
        public List<FloorData> Floors { get; set; } = new();
        public List<Booking> RecentBookings { get; set; } = new();
        public List<RoomGridItem> RoomGrid { get; set; } = new();
        public List<Ticket> RecentTickets { get; set; } = new();

        // Chart Data
        public string CurrentRange { get; set; } = "7days";
        public List<string> ChartLabels { get; set; } = new();
        public List<decimal> RevenueData { get; set; } = new();
        public List<int> BookingsData { get; set; } = new();
    }

    public class RevenueReportViewModel
    {
        public ReportFilterViewModel Filter { get; set; } = new();
        public decimal TotalRevenue { get; set; }
        public decimal RoomRevenue { get; set; }
        public decimal ServiceRevenue { get; set; }
        public decimal TotalDiscount { get; set; }
        public int TotalInvoices { get; set; }
        public decimal AvgInvoice { get; set; }
        public List<RevenueBreakdown> Breakdown { get; set; } = new();
        public List<PaymentMethodStat> PaymentMethods { get; set; } = new();
    }

    public class RevenueBreakdown
    {
        public string Period { get; set; } = string.Empty;
        public int InvoiceCount { get; set; }
        public decimal Revenue { get; set; }
        public decimal RoomRevenue { get; set; }
        public decimal ServiceRevenue { get; set; }
        public decimal Discount { get; set; }
    }

    public class PaymentMethodStat
    {
        public string Method { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal Total { get; set; }
    }
}
