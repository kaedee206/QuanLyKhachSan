using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;
using Newtonsoft.Json;

namespace QuanLyKhachSan.Services
{
    public class ServiceManagementService
    {
        private readonly SunHotelDbContext _db;
        private readonly ILogger<ServiceManagementService> _logger;

        public ServiceManagementService(SunHotelDbContext db, ILogger<ServiceManagementService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Service> AddService(AddServiceViewModel model, int userId)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingCode == model.BookingCode);
            if (booking == null)
                throw new InvalidOperationException("Không tìm thấy booking");

            if (booking.Status != BookingStatus.CheckedIn && booking.Status != BookingStatus.Confirmed)
                throw new InvalidOperationException("Chỉ có thể thêm dịch vụ cho booking đang hoạt động");

            var totalAmount = model.Quantity * model.UnitPrice;

            var service = new Service
            {
                BookingId = booking.Id,
                ServiceName = model.ServiceName,
                ServiceType = model.ServiceType,
                Quantity = model.Quantity,
                UnitPrice = model.UnitPrice,
                TotalAmount = totalAmount,
                Notes = model.Notes
            };

            _db.Services.Add(service);

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "ADD_SERVICE",
                EntityType = "service",
                NewValue = JsonConvert.SerializeObject(new
                {
                    booking_code = model.BookingCode,
                    service_name = model.ServiceName,
                    total_amount = totalAmount
                })
            });

            await _db.SaveChangesAsync();

            await RecalculateInvoiceTotalAsync(booking.Id);

            _logger.LogInformation("Service added: {ServiceName} for booking {BookingCode}",
                model.ServiceName, model.BookingCode);
            return service;
        }

        public async Task<(List<Service> services, decimal total, Booking booking)> GetServicesByBookingCode(string bookingCode)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingCode == bookingCode);
            if (booking == null)
                throw new InvalidOperationException("Không tìm thấy booking");

            var services = await _db.Services
                .Where(s => s.BookingId == booking.Id)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var total = services.Sum(s => s.TotalAmount);
            return (services, total, booking);
        }

        public async Task<Service?> GetServiceById(int id)
        {
            return await _db.Services
                .Include(s => s.Booking)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<bool> DeleteService(int serviceId, int userId)
        {
            var service = await _db.Services.FindAsync(serviceId);
            if (service == null) return false;

            var bookingId = service.BookingId;

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "DELETE_SERVICE",
                EntityType = "service",
                EntityId = serviceId,
                OldValue = JsonConvert.SerializeObject(new
                {
                    service_name = service.ServiceName,
                    total_amount = service.TotalAmount
                })
            });

            _db.Services.Remove(service);
            await _db.SaveChangesAsync();

            await RecalculateInvoiceTotalAsync(bookingId);
            return true;
        }

        private async Task RecalculateInvoiceTotalAsync(int bookingId)
        {
            var servicesTotal = await _db.Services
                .Where(s => s.BookingId == bookingId)
                .SumAsync(s => s.TotalAmount);

            var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.BookingId == bookingId);
            if (invoice == null) return;

            invoice.ServiceCharge = servicesTotal;
            invoice.TotalAmount = invoice.RoomCharge + servicesTotal - invoice.Discount;
            invoice.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        public async Task<PaginatedList<Service>> GetAllServices(int page = 1, int pageSize = 20)
        {
            var query = _db.Services
                .Include(s => s.Booking)
                .OrderByDescending(s => s.CreatedAt);

            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PaginatedList<Service>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            };
        }
    }
}
