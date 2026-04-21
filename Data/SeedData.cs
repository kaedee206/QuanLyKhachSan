using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Data
{
    /// <summary>
    /// Lop khoi tao du lieu mau cho co so du lieu
    /// Chay lan dau khi ung dung khoi dong
    /// </summary>
    public static class SeedData
    {
        /// <summary>
        /// Khoi tao toan bo du lieu mau
        /// </summary>
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SunHotelDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<SunHotelDbContext>>();

            try
            {
                // Dam bao database da duoc tao
                await context.Database.EnsureCreatedAsync();

                // Kiem tra da co du lieu chua
                if (await context.Users.AnyAsync())
                {
                    // Database da co user -> chi fix role sai (chay 1 lan duy nhat)
                    await FixUserRolesAsync(context, logger);
                    return;
                }

                logger.LogInformation("Bat dau khoi tao du lieu mau...");

                // ════════════════════════════════════════════════════════
                // 1. TAO LOAI PHONG (RoomTypes)
                // ════════════════════════════════════════════════════════
                var roomTypes = new List<RoomType>
                {
                    new RoomType
                    {
                        Name = "Standard",
                        Description = "Phong Standard voi cac tien nghi co ban, phu hop cho 1-2 nguoi",
                        BasePrice = 450000m,
                        MaxGuests = 2,
                        Amenities = "[\"Wifi\",\"Dieu hoa\",\"TV\",\"Nuoc uong mien phi\"]",
                        ImageUrl = "/images/rooms/single-1.jpg",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new RoomType
                    {
                        Name = "Superior",
                        Description = "Phong Superior rong rai hon, co ban cong, view dep",
                        BasePrice = 650000m,
                        MaxGuests = 2,
                        Amenities = "[\"Wifi\",\"Dieu hoa\",\"TV\",\"Mini bar\",\"Ban lam viec\",\"Bancong\"]",
                        ImageUrl = "/images/rooms/double-1.jpg",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new RoomType
                    {
                        Name = "Deluxe",
                        Description = "Phong Deluxe cao cap, dien tich lon, co phong khach rieng",
                        BasePrice = 950000m,
                        MaxGuests = 3,
                        Amenities = "[\"Wifi\",\"Dieu hoa\",\"Smart TV\",\"Mini bar\",\"Phong khach\",\"Bao hoa\"]",
                        ImageUrl = "/images/rooms/family-1.jpg",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new RoomType
                    {
                        Name = "Suite",
                        Description = "Phong Suite sang trong, co phong khach, phong ngu rieng, jacuzzi",
                        BasePrice = 1500000m,
                        MaxGuests = 4,
                        Amenities = "[\"Wifi\",\"Dieu hoa\",\"Smart TV 65\\\"\",\"Mini bar\",\"Phong khach\",\"Phong ngu rieng\",\"Jacuzzi\",\"Bao hoa cao cap\"]",
                        ImageUrl = "/images/rooms/vip-1.jpg",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new RoomType
                    {
                        Name = "President Suite",
                        Description = "Phong President xa hoa nhat, view tuyen dep, dich vu VIP",
                        BasePrice = 3600000m,
                        MaxGuests = 6,
                        Amenities = "[\"Wifi toc do cao\",\"Dieu hoa\",\"Smart TV 75\\\"\",\"Mini bar\",\"Phong khach lon\",\"2 Phong ngu\",\"Jacuzzi\",\"Sauna\",\"Butler 24/7\"]",
                        ImageUrl = "/images/hotel-assets/rooms/vip-room.png",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Phong VIP Luxury - ha tang sang trong, gia 6.7 trieu/doi
                    new RoomType
                    {
                        Name = "VIP Luxury",
                        Description = "Phong VIP Luxury ha dang, co view tuyen dep, dich vu cao cap 5 sao",
                        BasePrice = 6700000m,
                        MaxGuests = 4,
                        Amenities = "[\"Wifi toc do cao\",\"Dieu hoa\",\"Smart TV 75 inch\",\"Mini bar\",\"Phong khach\",\"Phong ngu\",\"Jacuzzi\",\"Butler 24/7\",\"Champagne mien phi\"]",
                        ImageUrl = "/images/hotel-assets/rooms/vip-luxury.png",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Phong test gia 10,000 VND - chi dung de test thanh toan SePay
                    new RoomType
                    {
                        Name = "Test Room",
                        Description = "Phong test gia 10,000 VND - chi dung de test thanh toan",
                        BasePrice = 10000m,
                        MaxGuests = 1,
                        Amenities = "[\"Wifi\"]",
                        ImageUrl = "/images/rooms/single-1.jpg",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    }
                };
                context.RoomTypes.AddRange(roomTypes);
                await context.SaveChangesAsync();
                logger.LogInformation("Da tao {Count} loai phong", roomTypes.Count);

                // ════════════════════════════════════════════════════════
                // 2. TAO PHONG (Rooms) - moi tang 4 phong
                // ════════════════════════════════════════════════════════
                var rooms = new List<Room>();
                var roomNumbers = new[]
                {
                    // Tang 1 - Standard
                    ("101", 1, 1, RoomStatus.Available),
                    ("102", 1, 1, RoomStatus.Available),
                    ("103", 1, 2, RoomStatus.Available),
                    ("104", 1, 2, RoomStatus.Occupied),
                    // Tang 2 - Superior
                    ("201", 2, 2, RoomStatus.Available),
                    ("202", 2, 2, RoomStatus.Cleaning),
                    ("203", 2, 3, RoomStatus.Available),
                    ("204", 2, 3, RoomStatus.Occupied),
                    // Tang 3 - Deluxe
                    ("301", 3, 3, RoomStatus.Available),
                    ("302", 3, 3, RoomStatus.Available),
                    ("303", 3, 4, RoomStatus.Maintenance),
                    ("304", 3, 4, RoomStatus.Occupied),
                    // Tang 4 - Suite
                    ("401", 4, 4, RoomStatus.Available),
                    ("402", 4, 4, RoomStatus.Available),
                    ("403", 4, 5, RoomStatus.Available),
                    ("404", 4, 5, RoomStatus.Occupied),

                    // Tang 5 - President Suite (3 phong)
                    ("501", 5, 5, RoomStatus.Available),
                    ("502", 5, 5, RoomStatus.Available),
                    ("503", 5, 5, RoomStatus.Occupied),
                    // Tang 6 - VIP Luxury (3 phong)
                    ("601", 6, 6, RoomStatus.Available),
                    ("602", 6, 6, RoomStatus.Available),
                    ("603", 6, 6, RoomStatus.Occupied),
                    // Tang 7 - Test Room
                    ("701", 7, 7, RoomStatus.Available),
                };

                foreach (var (number, floor, typeId, status) in roomNumbers)
                {
                    rooms.Add(new Room
                    {
                        RoomNumber = number,
                        Floor = floor,
                        RoomTypeId = typeId,
                        Status = status,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                context.Rooms.AddRange(rooms);
                await context.SaveChangesAsync();
                logger.LogInformation("Da tao {Count} phong", rooms.Count);

                // ════════════════════════════════════════════════════════
                // 4. TAO NGUOI DUNG (Users)
                // ════════════════════════════════════════════════════════
                var users = new List<User>
                {
                    // Admin
                    new User
                    {
                        Username = "admin",
                        Password = BCrypt.Net.BCrypt.HashPassword("Admin@123456", 10),
                        FullName = "Nguyen Van Admin",
                        Role = UserRole.Admin,
                        Email = "admin@sunhotel.vn",
                        Phone = "0901234567",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Manager
                    new User
                    {
                        Username = "manager",
                        Password = BCrypt.Net.BCrypt.HashPassword("Manager@123", 10),
                        FullName = "Tran Thi Quan Ly",
                        Role = UserRole.Manager,
                        Email = "manager@sunhotel.vn",
                        Phone = "0902345678",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Receptionists
                    new User
                    {
                        Username = "reception1",
                        Password = BCrypt.Net.BCrypt.HashPassword("Ltan@123456", 10),
                        FullName = "Le Thi Le Tan",
                        Role = UserRole.Receptionist,
                        Email = "reception1@sunhotel.vn",
                        Phone = "0903456789",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new User
                    {
                        Username = "reception2",
                        Password = BCrypt.Net.BCrypt.HashPassword("Nva@12345678", 10),
                        FullName = "Nguyen Van A",
                        Role = UserRole.Receptionist,
                        Email = "reception2@sunhotel.vn",
                        Phone = "0904567890",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Housekeeping
                    new User
                    {
                        Username = "housekeep1",
                        Password = BCrypt.Net.BCrypt.HashPassword("Hkeep@123456", 10),
                        FullName = "Pham Thi Buong",
                        Role = UserRole.Housekeeping,
                        Email = "housekeep1@sunhotel.vn",
                        Phone = "0905678901",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new User
                    {
                        Username = "housekeep2",
                        Password = BCrypt.Net.BCrypt.HashPassword("Hkeep2@123456", 10),
                        FullName = "Hoang Van Dung",
                        Role = UserRole.Housekeeping,
                        Email = "housekeep2@sunhotel.vn",
                        Phone = "0906789012",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Maintenance
                    new User
                    {
                        Username = "maintenance1",
                        Password = BCrypt.Net.BCrypt.HashPassword("Maint@123456", 10),
                        FullName = "Vo Van Ky",
                        Role = UserRole.Maintenance,
                        Email = "maintenance@sunhotel.vn",
                        Phone = "0907890123",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Customer accounts
                    new User
                    {
                        Username = "customer001",
                        Password = BCrypt.Net.BCrypt.HashPassword("Cust@123456", 10),
                        FullName = "Tran Van Khach",
                        Role = UserRole.Customer,
                        Email = "khach1@email.com",
                        Phone = "0912345678",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new User
                    {
                        Username = "customer002",
                        Password = BCrypt.Net.BCrypt.HashPassword("Cust2@123456", 10),
                        FullName = "Hoang Thi Minh",
                        Role = UserRole.Customer,
                        Email = "khach2@email.com",
                        Phone = "0923456789",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    }
                };
                context.Users.AddRange(users);
                await context.SaveChangesAsync();
                logger.LogInformation("Da tao {Count} nguoi dung", users.Count);

                // ════════════════════════════════════════════════════════
                // 5. TAO BOOKING MAU
                // ════════════════════════════════════════════════════════
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var adminUser = users[0];
                var receptionUser = users[2];

                var bookings = new List<Booking>
                {
                    // Booking da check-in (dang o)
                    new Booking
                    {
                        BookingCode = "BK" + DateTime.UtcNow.ToString("yyMMdd") + "001",
                        RoomId = rooms[3].Id,  // Room 104 - Occupied
                        RoomTypeId = 1,
                        GuestName = "Nguyen Van Lam",
                        Phone = "0934567890",
                        Email = "lamnv@email.com",
                        IdNumber = "001234567890",
                        CheckInDate = today.AddDays(-2),
                        CheckOutDate = today.AddDays(1),
                        ActualCheckIn = DateTime.UtcNow.AddDays(-2),
                        NumGuests = 2,
                        Status = BookingStatus.CheckedIn,
                        TotalAmount = 3 * 450000m,
                        CreatedBy = receptionUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-3)
                    },
                    // Booking da check-in 2
                    new Booking
                    {
                        BookingCode = "BK" + DateTime.UtcNow.ToString("yyMMdd") + "002",
                        RoomId = rooms[7].Id,  // Room 204 - Occupied
                        RoomTypeId = 2,
                        GuestName = "Le Thi Mai",
                        Phone = "0945678901",
                        Email = "maile@email.com",
                        IdNumber = "002345678901",
                        CheckInDate = today.AddDays(-1),
                        CheckOutDate = today.AddDays(3),
                        ActualCheckIn = DateTime.UtcNow.AddDays(-1),
                        NumGuests = 2,
                        Status = BookingStatus.CheckedIn,
                        TotalAmount = 4 * 650000m,
                        CreatedBy = receptionUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    },
                    // Booking confirmed
                    new Booking
                    {
                        BookingCode = "BK" + DateTime.UtcNow.ToString("yyMMdd") + "003",
                        RoomId = rooms[11].Id, // Room 304 - Occupied
                        RoomTypeId = 3,
                        GuestName = "Tran Van Hai",
                        Phone = "0956789012",
                        Email = "hai.tv@email.com",
                        IdNumber = "003456789012",
                        CheckInDate = today,
                        CheckOutDate = today.AddDays(2),
                        NumGuests = 3,
                        Status = BookingStatus.CheckedIn,
                        TotalAmount = 2 * 950000m,
                        CreatedBy = receptionUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    },
                    // Booking pending
                    new Booking
                    {
                        BookingCode = "BK" + DateTime.UtcNow.ToString("yyMMdd") + "004",
                        RoomTypeId = 2,
                        GuestName = "Pham Thi Hoa",
                        Phone = "0967890123",
                        Email = "hoa.pham@email.com",
                        CheckInDate = today.AddDays(2),
                        CheckOutDate = today.AddDays(5),
                        NumGuests = 2,
                        Status = BookingStatus.Pending,
                        TotalAmount = 3 * 650000m,
                        CreatedBy = receptionUser.Id,
                        CreatedAt = DateTime.UtcNow.AddHours(-5)
                    },
                    // Booking confirmed (sap den)
                    new Booking
                    {
                        BookingCode = "BK" + DateTime.UtcNow.ToString("yyMMdd") + "005",
                        RoomId = rooms[15].Id, // Room 404 - Occupied
                        RoomTypeId = 4,
                        GuestName = "Hoang Van Phuc",
                        Phone = "0978901234",
                        Email = "phuc.hoang@email.com",
                        IdNumber = "004567890123",
                        CheckInDate = today,
                        CheckOutDate = today.AddDays(4),
                        ActualCheckIn = DateTime.UtcNow.AddHours(-2),
                        NumGuests = 4,
                        Status = BookingStatus.CheckedIn,
                        TotalAmount = 4 * 1500000m,
                        CreatedBy = receptionUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    },
                    // Booking cancelled
                    new Booking
                    {
                        BookingCode = "BK" + DateTime.UtcNow.ToString("yyMMdd") + "006",
                        RoomTypeId = 1,
                        GuestName = "Dao Van Tuan",
                        Phone = "0989012345",
                        Email = "tuan.dao@email.com",
                        CheckInDate = today.AddDays(1),
                        CheckOutDate = today.AddDays(3),
                        NumGuests = 1,
                        Status = BookingStatus.Cancelled,
                        TotalAmount = 2 * 450000m,
                        CreatedBy = receptionUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    }
                };
                context.Bookings.AddRange(bookings);
                await context.SaveChangesAsync();
                logger.LogInformation("Da tao {Count} booking", bookings.Count);

                // ════════════════════════════════════════════════════════
                // 5. TAO HOA DON MAU
                // ════════════════════════════════════════════════════════
                var invoices = new List<Invoice>
                {
                    // Invoice da thanh toan cho booking 1
                    new Invoice
                    {
                        BookingId = bookings[0].Id,
                        InvoiceNumber = "INV-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-1001",
                        RoomCharge = 3 * 450000m,
                        ServiceCharge = 50000m,
                        Discount = 0,
                        TotalAmount = 3 * 450000m + 50000m,
                        PaymentMethod = PaymentMethod.Cash,
                        PaymentStatus = PaymentStatus.Paid,
                        PaymentDate = DateTime.UtcNow.AddDays(-2),
                        CreatedById = receptionUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    },
                    // Invoice da thanh toan cho booking 2
                    new Invoice
                    {
                        BookingId = bookings[1].Id,
                        InvoiceNumber = "INV-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-1002",
                        RoomCharge = 4 * 650000m,
                        ServiceCharge = 150000m,
                        Discount = 65000m,
                        TotalAmount = 4 * 650000m + 150000m - 65000m,
                        PaymentMethod = PaymentMethod.VietQR,
                        PaymentStatus = PaymentStatus.Paid,
                        PaymentDate = DateTime.UtcNow.AddDays(-1),
                        CreatedById = receptionUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    },
                    // Invoice chua thanh toan cho booking 3
                    new Invoice
                    {
                        BookingId = bookings[2].Id,
                        InvoiceNumber = "INV-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-1003",
                        RoomCharge = 2 * 950000m,
                        ServiceCharge = 0,
                        Discount = 0,
                        TotalAmount = 2 * 950000m,
                        PaymentMethod = PaymentMethod.Cash,
                        PaymentStatus = PaymentStatus.Unpaid,
                        CreatedById = receptionUser.Id,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Invoice chua thanh toan cho booking 5
                    new Invoice
                    {
                        BookingId = bookings[4].Id,
                        InvoiceNumber = "INV-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-1004",
                        RoomCharge = 4 * 1500000m,
                        ServiceCharge = 300000m,
                        Discount = 0,
                        TotalAmount = 4 * 1500000m + 300000m,
                        PaymentMethod = PaymentMethod.MoMo,
                        PaymentStatus = PaymentStatus.Pending,
                        CreatedById = receptionUser.Id,
                        CreatedAt = DateTime.UtcNow
                    }
                };
                context.Invoices.AddRange(invoices);
                await context.SaveChangesAsync();
                logger.LogInformation("Da tao {Count} hoa don", invoices.Count);

                // ════════════════════════════════════════════════════════
                // 6. TAO DICH VU MAU
                // ════════════════════════════════════════════════════════
                var services = new List<Service>
                {
                    // Dịch vụ cho booking 1 (đang ở)
                    new Service
                    {
                        BookingId = bookings[0].Id,
                        ServiceName = "Giat uoc",
                        ServiceType = ServiceType.Laundry,
                        Quantity = 3,
                        UnitPrice = 15000m,
                        TotalAmount = 45000m,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    },
                    new Service
                    {
                        BookingId = bookings[0].Id,
                        ServiceName = "Nuoc uong minibar",
                        ServiceType = ServiceType.Minibar,
                        Quantity = 2,
                        UnitPrice = 25000m,
                        TotalAmount = 50000m,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    },
                    // Dịch vụ cho booking 2
                    new Service
                    {
                        BookingId = bookings[1].Id,
                        ServiceName = " buffet sang",
                        ServiceType = ServiceType.Breakfast,
                        Quantity = 4,
                        UnitPrice = 180000m,
                        TotalAmount = 720000m,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    },
                    new Service
                    {
                        BookingId = bookings[1].Id,
                        ServiceName = "Di chuyen san bay",
                        ServiceType = ServiceType.Transport,
                        Quantity = 1,
                        UnitPrice = 250000m,
                        TotalAmount = 250000m,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    }
                };
                context.Services.AddRange(services);
                await context.SaveChangesAsync();
                logger.LogInformation("Da tao {Count} dich vu", services.Count);

                // ════════════════════════════════════════════════════════
                // 7. TAO TICKET MAU
                // ════════════════════════════════════════════════════════
                var housekeepingUser = users[4];
                var maintenanceUser = users[6];

                var tickets = new List<Ticket>
                {
                    new Ticket
                    {
                        TicketNumber = "TK-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-001",
                        RoomId = rooms[5].Id, // Room 202 - dang don dep
                        Type = TicketType.Housekeeping,
                        Description = "Don dep phong sau khi khach tra",
                        Priority = TicketPriority.Medium,
                        Status = TicketStatus.Open,
                        ReportedById = receptionUser.Id,
                        AssignedToId = housekeepingUser.Id,
                        CreatedAt = DateTime.UtcNow.AddHours(-3)
                    },
                    new Ticket
                    {
                        TicketNumber = "TK-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-002",
                        RoomId = rooms[10].Id, // Room 303 - bao tri
                        Type = TicketType.Maintenance,
                        Description = "May lanh khong lanh, can kiểm tra",
                        Priority = TicketPriority.High,
                        Status = TicketStatus.InProgress,
                        ReportedById = receptionUser.Id,
                        AssignedToId = maintenanceUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    },
                    new Ticket
                    {
                        TicketNumber = "TK-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-003",
                        RoomId = rooms[0].Id, // Room 101
                        Type = TicketType.Maintenance,
                        Description = "Den phong chạp, can thay bong",
                        Priority = TicketPriority.Low,
                        Status = TicketStatus.Resolved,
                        ReportedById = receptionUser.Id,
                        AssignedToId = maintenanceUser.Id,
                        ResolutionNotes = "Da thay bong den moi",
                        ResolvedAt = DateTime.UtcNow.AddHours(-2),
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    }
                };
                context.Tickets.AddRange(tickets);
                await context.SaveChangesAsync();
                logger.LogInformation("Da tao {Count} ticket", tickets.Count);

                // ════════════════════════════════════════════════════════
                // 8. TAO AUDIT LOG MAU
                // ════════════════════════════════════════════════════════
                var auditLogs = new List<AuditLog>
                {
                    new AuditLog
                    {
                        UserId = adminUser.Id,
                        Action = "CREATE_USER",
                        EntityType = "user",
                        EntityId = users[2].Id,
                        IpAddress = "127.0.0.1",
                        CreatedAt = DateTime.UtcNow.AddDays(-7)
                    },
                    new AuditLog
                    {
                        UserId = receptionUser.Id,
                        Action = "CREATE_BOOKING",
                        EntityType = "booking",
                        EntityId = bookings[0].Id,
                        IpAddress = "127.0.0.1",
                        CreatedAt = DateTime.UtcNow.AddDays(-3)
                    },
                    new AuditLog
                    {
                        UserId = receptionUser.Id,
                        Action = "CHECK_IN",
                        EntityType = "booking",
                        EntityId = bookings[0].Id,
                        IpAddress = "127.0.0.1",
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    },
                    new AuditLog
                    {
                        UserId = receptionUser.Id,
                        Action = "CONFIRM_PAYMENT",
                        EntityType = "invoice",
                        EntityId = invoices[0].Id,
                        IpAddress = "127.0.0.1",
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    }
                };
                context.AuditLogs.AddRange(auditLogs);
                await context.SaveChangesAsync();
                logger.LogInformation("Da tao {Count} audit log", auditLogs.Count);

                logger.LogInformation("Hoan tat khoi tao du lieu mau!");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Loi khi khoi tao SeedData. Thu fix schema va thu lai...");
                // Thử fix schema bằng raw SQL rồi khởi tạo lại
                try
                {
#pragma warning disable EF1002 // Chi hardcode table/column name, khong co user input
                    await context.Database.ExecuteSqlRawAsync(@"
                        DECLARE @sql NVARCHAR(MAX) = N'';
                        SELECT @sql += N'ALTER TABLE [dbo].[booking] DROP CONSTRAINT ' + QUOTENAME(dc.name) + ';'
                        FROM sys.default_constraints dc
                        JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                        WHERE dc.parent_object_id = OBJECT_ID('booking') AND c.name = 'booking_code';
                        EXEC sp_executesql @sql;
                        ALTER TABLE [dbo].[booking] ALTER COLUMN booking_code NVARCHAR(20) NOT NULL;
                    ");
#pragma warning restore EF1002
                    logger.LogInformation("Da fix booking_code column");

#pragma warning disable EF1002 // Chi hardcode table/column name, khong co user input
                    await context.Database.ExecuteSqlRawAsync(@"
                        DECLARE @sql NVARCHAR(MAX) = N'';
                        SELECT @sql += N'ALTER TABLE [dbo].[ticket] DROP CONSTRAINT ' + QUOTENAME(dc.name) + ';'
                        FROM sys.default_constraints dc
                        JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                        WHERE dc.parent_object_id = OBJECT_ID('ticket') AND c.name = 'ticket_number';
                        EXEC sp_executesql @sql;
                        ALTER TABLE [dbo].[ticket] ALTER COLUMN ticket_number NVARCHAR(30) NOT NULL;
                    ");
#pragma warning restore EF1002
                    logger.LogInformation("Da fix ticket_number column");

                    // Retry initialization from the beginning
                    return; // Exit gracefully - let next run create proper data
                }
                catch (Exception fixEx)
                {
                    logger.LogError(fixEx, "Khong the fix schema, can xoa database thu cong");
                }
            }
        }

        /// <summary>
        /// Fix user roles in existing database - chi chay 1 lan khi phat hien role sai
        /// Neu phat hien role khong khop -> throw exception ngay lap tuc de stop app
        /// </summary>
        private static async Task FixUserRolesAsync(SunHotelDbContext context, ILogger logger)
        {
            var roleMapping = new Dictionary<string, UserRole>
            {
                { "admin", UserRole.Admin },
                { "manager", UserRole.Manager },
                { "reception1", UserRole.Receptionist },
                { "reception2", UserRole.Receptionist },
                { "housekeep1", UserRole.Housekeeping },
                { "housekeep2", UserRole.Housekeeping },
                { "maintenance1", UserRole.Maintenance },
                { "customer001", UserRole.Customer },
                { "customer002", UserRole.Customer }
            };

            var usersToFix = context.Users.Where(u => roleMapping.Keys.Contains(u.Username)).ToList();
            if (!usersToFix.Any())
            {
                logger.LogWarning("CRITICAL: Khong tim thay bat ky tai khoan test nao trong database!");
                throw new InvalidOperationException(
                    "CRITICAL: Database thieu tai khoan test! " +
                    "Vui long chay SeedData moi lan dau tien bang cach xoa toan bo bang User.");
            }

            var invalidUsers = usersToFix.Where(u => u.Role != roleMapping[u.Username]).ToList();
            if (invalidUsers.Any())
            {
                // Tu dong fix role sai thay vi throw - dam bao app van chay duoc
                foreach (var user in invalidUsers)
                {
                    var oldRole = user.Role.ToString();
                    user.Role = roleMapping[user.Username];
                    logger.LogWarning("Auto-fixed role: {Username}: {OldRole} -> {NewRole}",
                        user.Username, oldRole, user.Role);
                }
                await context.SaveChangesAsync();
                logger.LogInformation("Da tu dong fix {Count} tai khoan co role sai", invalidUsers.Count);
            }

            logger.LogInformation("All {Count} test user roles are correct", usersToFix.Count);
            await Task.CompletedTask;
        }
    }
}