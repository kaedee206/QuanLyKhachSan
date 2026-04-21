using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;
using Newtonsoft.Json;

namespace QuanLyKhachSan.Services
{
    public class BookingService
    {
        private readonly SunHotelDbContext _db;
        private readonly ILogger<BookingService> _logger;

        public BookingService(SunHotelDbContext db, ILogger<BookingService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Booking> CreateBooking(CreateBookingViewModel model, int? userId = null)
        {
            var roomType = await _db.RoomTypes.FindAsync(model.RoomTypeId);
            if (roomType == null)
                throw new InvalidOperationException("Loại phòng không tồn tại");

            if (model.NumGuests > roomType.MaxGuests)
                throw new InvalidOperationException($"Số khách tối đa cho loại phòng này là {roomType.MaxGuests}");

            if (model.CheckOutDate <= model.CheckInDate)
                throw new InvalidOperationException("Ngày trả phòng phải sau ngày nhận phòng");

            var nights = model.CheckOutDate.DayNumber - model.CheckInDate.DayNumber;
            var totalAmount = nights * roomType.BasePrice;

            var bookingCode = await GenerateBookingCode();

            var booking = new Booking
            {
                BookingCode = bookingCode,
                RoomTypeId = model.RoomTypeId,
                GuestName = model.GuestName,
                Phone = model.Phone,
                Email = model.Email,
                IdNumber = model.IdNumber,
                CheckInDate = model.CheckInDate,
                CheckOutDate = model.CheckOutDate,
                NumGuests = model.NumGuests,
                TotalAmount = totalAmount,
                Notes = model.Notes,
                Status = BookingStatus.Pending,
                CreatedBy = userId
            };

            _db.Bookings.Add(booking);

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "CREATE_BOOKING",
                EntityType = "booking",
                NewValue = JsonConvert.SerializeObject(new
                {
                    booking_code = bookingCode,
                    guest_name = model.GuestName,
                    check_in_date = model.CheckInDate.ToString(),
                    check_out_date = model.CheckOutDate.ToString(),
                    total_amount = totalAmount
                })
            });

            await _db.SaveChangesAsync();

            _logger.LogInformation("Booking created: {BookingCode} by {GuestName}",
                bookingCode, model.GuestName);

            return booking;
        }

        public async Task<Booking?> LookupBooking(string bookingCode, string phone)
        {
            return await _db.Bookings
                .Include(b => b.RoomType)
                .Include(b => b.Room)
                .Include(b => b.Services)
                .Include(b => b.Invoice)
                .FirstOrDefaultAsync(b => b.BookingCode == bookingCode && b.Phone == phone);
        }

        public async Task<Booking> ConfirmBooking(string bookingCode, int roomId, int userId)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingCode == bookingCode);
            if (booking == null) throw new InvalidOperationException("Không tìm thấy booking");
            if (booking.Status != BookingStatus.Pending)
                throw new InvalidOperationException($"Không thể xác nhận booking với trạng thái: {booking.Status}");

            var room = await _db.Rooms.Include(r => r.RoomType).FirstOrDefaultAsync(r => r.Id == roomId);
            if (room == null) throw new InvalidOperationException("Phòng không tồn tại");
            if (room.RoomTypeId != booking.RoomTypeId)
                throw new InvalidOperationException("Loại phòng không khớp");
            if (room.Status != RoomStatus.Available)
                throw new InvalidOperationException("Phòng không có sẵn");

            var hasConflict = await _db.Bookings.AnyAsync(b =>
                b.RoomId == roomId &&
                b.Id != booking.Id &&
                b.Status != BookingStatus.Cancelled &&
                b.Status != BookingStatus.NoShow &&
                b.CheckInDate < booking.CheckOutDate &&
                b.CheckOutDate > booking.CheckInDate);
            if (hasConflict)
                throw new InvalidOperationException("Phòng đã có booking khác trong khoảng thời gian này");

            booking.RoomId = roomId;
            booking.Status = BookingStatus.Confirmed;

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "CONFIRM_BOOKING",
                EntityType = "booking",
                EntityId = booking.Id,
                OldValue = JsonConvert.SerializeObject(new { status = "Pending", room_id = (int?)null }),
                NewValue = JsonConvert.SerializeObject(new { status = "Confirmed", room_id = roomId })
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("Booking confirmed: {BookingCode}, Room: {RoomId}", bookingCode, roomId);
            return booking;
        }

        public async Task<(Booking booking, Invoice invoice)> CheckIn(string bookingCode, CheckInViewModel model, int userId)
        {
            var booking = await _db.Bookings
                .Include(b => b.Services)
                .Include(b => b.Invoice)
                .FirstOrDefaultAsync(b => b.BookingCode == bookingCode);
            if (booking == null) throw new InvalidOperationException("Không tìm thấy booking");
            if (booking.Status != BookingStatus.Confirmed && booking.Status != BookingStatus.Pending)
                throw new InvalidOperationException($"Không thể check-in booking với trạng thái: {booking.Status}");

            var finalRoomId = model.RoomId ?? booking.RoomId;
            if (finalRoomId == null)
                throw new InvalidOperationException("Cần chọn phòng để check-in");

            var room = await _db.Rooms.FindAsync(finalRoomId.Value);
            if (room != null)
                room.Status = RoomStatus.Occupied;

            booking.RoomId = finalRoomId;
            booking.ActualCheckIn = model.ActualCheckIn ?? DateTime.UtcNow;
            booking.Status = BookingStatus.CheckedIn;

            var invoice = booking.Invoice;
            if (invoice == null)
            {
                var existingInvoice = await _db.Invoices.FirstOrDefaultAsync(i => i.BookingId == booking.Id);
                if (existingInvoice != null)
                {
                    invoice = existingInvoice;
                }
                else
                {
                    var servicesTotal = booking.Services.Sum(s => s.TotalAmount);
                    var invoiceNumber = await GenerateInvoiceNumber();
                    invoice = new Invoice
                    {
                        BookingId = booking.Id,
                        InvoiceNumber = invoiceNumber,
                        RoomCharge = booking.TotalAmount,
                        ServiceCharge = servicesTotal,
                        TotalAmount = booking.TotalAmount + servicesTotal,
                        PaymentMethod = PaymentMethod.Cash,
                        PaymentStatus = PaymentStatus.Unpaid,
                        CreatedById = userId
                    };
                    _db.Invoices.Add(invoice);
                }
            }

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "CHECK_IN",
                EntityType = "booking",
                EntityId = booking.Id,
                OldValue = JsonConvert.SerializeObject(new { status = booking.Status.ToString() }),
                NewValue = JsonConvert.SerializeObject(new { status = "CheckedIn", room_id = finalRoomId })
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("Check-in: {BookingCode}, Room: {RoomId}", bookingCode, finalRoomId);
            return (booking, invoice);
        }

        public async Task<(Booking booking, Invoice invoice)> CheckOut(string bookingCode, CheckOutViewModel model, int userId)
        {
            var booking = await _db.Bookings
                .Include(b => b.Services)
                .Include(b => b.Invoice)
                .FirstOrDefaultAsync(b => b.BookingCode == bookingCode);
            if (booking == null) throw new InvalidOperationException("Không tìm thấy booking");
            if (booking.Status != BookingStatus.CheckedIn)
                throw new InvalidOperationException($"Không thể check-out booking với trạng thái: {booking.Status}");

            var servicesTotal = booking.Services.Sum(s => s.TotalAmount);
            var roomCharge = booking.TotalAmount;
            var discountAmount = (roomCharge + servicesTotal) * (model.Discount / 100);
            var finalTotal = roomCharge + servicesTotal - discountAmount;

            booking.ActualCheckOut = model.ActualCheckOut ?? DateTime.UtcNow;
            booking.Status = BookingStatus.CheckedOut;
            booking.Notes = model.Notes ?? booking.Notes;

            if (booking.RoomId.HasValue)
            {
                var room = await _db.Rooms.FindAsync(booking.RoomId.Value);
                if (room != null)
                    room.Status = RoomStatus.Cleaning;
            }

            var invoice = booking.Invoice;
            if (invoice == null)
            {
                invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.BookingId == booking.Id);
            }
            if (invoice == null)
            {
                invoice = new Invoice
                {
                    BookingId = booking.Id,
                    InvoiceNumber = await GenerateInvoiceNumber(),
                    RoomCharge = roomCharge,
                    ServiceCharge = servicesTotal,
                    Discount = discountAmount,
                    TotalAmount = finalTotal,
                    PaymentMethod = model.PaymentMethod,
                    PaymentStatus = PaymentStatus.Unpaid,
                    CreatedById = userId
                };
                _db.Invoices.Add(invoice);
            }
            else
            {
                invoice.ServiceCharge = servicesTotal;
                invoice.Discount = discountAmount;
                invoice.TotalAmount = finalTotal;
                invoice.PaymentMethod = model.PaymentMethod;
            }

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "CHECK_OUT",
                EntityType = "booking",
                EntityId = booking.Id,
                OldValue = JsonConvert.SerializeObject(new { status = "CheckedIn" }),
                NewValue = JsonConvert.SerializeObject(new { status = "CheckedOut", discount = model.Discount })
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("Check-out: {BookingCode}, Total: {Total}", bookingCode, finalTotal);
            return (booking, invoice);
        }

        public async Task<Booking> CancelBooking(string bookingCode, int userId)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingCode == bookingCode);
            if (booking == null) throw new InvalidOperationException("Không tìm thấy booking");

            var nonCancellable = new[] { BookingStatus.CheckedOut, BookingStatus.Cancelled, BookingStatus.NoShow };
            if (nonCancellable.Contains(booking.Status))
                throw new InvalidOperationException($"Không thể hủy booking với trạng thái: {booking.Status}");

            if (booking.RoomId.HasValue)
            {
                var room = await _db.Rooms.FindAsync(booking.RoomId.Value);
                if (room != null)
                    room.Status = RoomStatus.Available;
            }

            var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.BookingId == booking.Id);
            if (invoice != null && invoice.PaymentStatus != PaymentStatus.Paid)
            {
                invoice.PaymentStatus = PaymentStatus.Cancelled;
            }

            booking.Status = BookingStatus.Cancelled;

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "CANCEL_BOOKING",
                EntityType = "booking",
                EntityId = booking.Id
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("Booking cancelled: {BookingCode}", bookingCode);
            return booking;
        }

        public async Task<Booking> MarkNoShow(string bookingCode, int userId)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingCode == bookingCode);
            if (booking == null) throw new InvalidOperationException("Không tìm thấy booking");

            var invalid = new[] { BookingStatus.CheckedIn, BookingStatus.CheckedOut, BookingStatus.Cancelled, BookingStatus.NoShow };
            if (invalid.Contains(booking.Status))
                throw new InvalidOperationException($"Không thể đánh dấu no-show với trạng thái: {booking.Status}");

            if (booking.RoomId.HasValue)
            {
                var room = await _db.Rooms.FindAsync(booking.RoomId.Value);
                if (room != null)
                    room.Status = RoomStatus.Available;
            }

            booking.Status = BookingStatus.NoShow;

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "MARK_NOSHOW",
                EntityType = "booking",
                EntityId = booking.Id
            });

            await _db.SaveChangesAsync();
            return booking;
        }

        public async Task<PaginatedList<Booking>> GetBookings(BookingFilterViewModel filter)
        {
            var query = _db.Bookings
                .Include(b => b.RoomType)
                .Include(b => b.Room)
                .Include(b => b.Invoice)
                .Include(b => b.Services)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filter.Search))
            {
                var search = filter.Search.ToLower();
                query = query.Where(b =>
                    b.BookingCode.ToLower().Contains(search) ||
                    b.GuestName.ToLower().Contains(search) ||
                    b.Phone.Contains(search));
            }

            if (filter.Status.HasValue)
                query = query.Where(b => b.Status == filter.Status.Value);

            if (filter.FromDate.HasValue)
                query = query.Where(b => b.CheckInDate >= filter.FromDate.Value);

            if (filter.ToDate.HasValue)
                query = query.Where(b => b.CheckOutDate <= filter.ToDate.Value);

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(b => b.CreatedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            return new PaginatedList<Booking>
            {
                Items = items,
                Page = filter.Page,
                PageSize = filter.PageSize,
                TotalCount = totalCount
            };
        }

        public async Task<BookingDetailViewModel?> GetBookingDetail(int id)
        {
            var booking = await _db.Bookings
                .Include(b => b.RoomType)
                .Include(b => b.Room)
                .Include(b => b.Services)
                .Include(b => b.Invoice)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return null;

            var availableRooms = await _db.Rooms
                .Include(r => r.RoomType)
                .Where(r => r.Status == RoomStatus.Available && r.RoomTypeId == booking.RoomTypeId)
                .ToListAsync();

            return new BookingDetailViewModel
            {
                Booking = booking,
                Services = booking.Services.ToList(),
                Invoice = booking.Invoice,
                AvailableRooms = availableRooms
            };
        }

        public async Task<BookingDetailViewModel?> GetBookingDetailByCode(string code)
        {
            var booking = await _db.Bookings
                .Include(b => b.RoomType)
                .Include(b => b.Room)
                .Include(b => b.Services)
                .Include(b => b.Invoice)
                .FirstOrDefaultAsync(b => b.BookingCode == code);

            if (booking == null) return null;

            var availableRooms = await _db.Rooms
                .Include(r => r.RoomType)
                .Where(r => r.Status == RoomStatus.Available && r.RoomTypeId == booking.RoomTypeId)
                .ToListAsync();

            return new BookingDetailViewModel
            {
                Booking = booking,
                Services = booking.Services.ToList(),
                Invoice = booking.Invoice,
                AvailableRooms = availableRooms
            };
        }

        private async Task<string> GenerateBookingCode()
        {
            string code;
            do
            {
                code = "BK" + DateTime.UtcNow.ToString("yyMMdd") +
                       Random.Shared.Next(100, 999).ToString();
            } while (await _db.Bookings.AnyAsync(b => b.BookingCode == code));
            return code;
        }

        private async Task<string> GenerateInvoiceNumber()
        {
            string number;
            do
            {
                number = "INV-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" +
                         Random.Shared.Next(1000, 9999).ToString();
            } while (await _db.Invoices.AnyAsync(i => i.InvoiceNumber == number));
            return number;
        }
    }
}
