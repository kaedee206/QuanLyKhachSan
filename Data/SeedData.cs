using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Helpers;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using System.IO;
using System.Text;

namespace QuanLyKhachSan.Data
{
    /// <summary>
    /// Lớp khởi tạo dữ liệu mẫu cho cơ sở dữ liệu
    /// Chạy lần đầu khi ứng dụng khởi động
    /// </summary>
    public static class SeedData
    {
        /// <summary>
        /// Khởi tạo toàn bộ dữ liệu mẫu
        /// </summary>
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SunHotelDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<SunHotelDbContext>>();

            try
            {
                // Đảm bảo database đã được tạo
                await context.Database.EnsureCreatedAsync();

                // Kiểm tra đã có dữ liệu chưa
                if (await context.Users.AnyAsync())
                {
                    // Database đã có user -> chỉ fix role sai (chạy 1 lần duy nhất)
                    await FixUserRolesAsync(context, logger);
                    return;
                }

                logger.LogInformation("Bắt đầu khởi tạo dữ liệu mẫu...");

                // ════════════════════════════════════════════════════════
                // 1. TẠO LOẠI PHÒNG (RoomTypes)
                // ════════════════════════════════════════════════════════
                var roomTypes = new List<RoomType>
                {
                    new RoomType
                    {
                        Name = "Standard",
                        Description = "Phòng Standard với các tiện nghi cơ bản, phù hợp cho 1-2 người",
                        BasePrice = 450000m,
                        MaxGuests = 2,
                        Amenities = "[\"Wifi\",\"Điều hòa\",\"TV\",\"Nước uống miễn phí\"]",
                        ImageUrl = "/images/rooms/single-1.jpg",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new RoomType
                    {
                        Name = "Superior",
                        Description = "Phòng Superior rộng rãi hơn, có ban công, view đẹp",
                        BasePrice = 650000m,
                        MaxGuests = 2,
                        Amenities = "[\"Wifi\",\"Điều hòa\",\"TV\",\"Mini bar\",\"Bàn làm việc\",\"Ban công\"]",
                        ImageUrl = "/images/rooms/double-1.jpg",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new RoomType
                    {
                        Name = "Deluxe",
                        Description = "Phòng Deluxe cao cấp, diện tích lớn, có phòng khách riêng",
                        BasePrice = 950000m,
                        MaxGuests = 3,
                        Amenities = "[\"Wifi\",\"Điều hòa\",\"Smart TV\",\"Mini bar\",\"Phòng khách\",\"Bồn tắm\"]",
                        ImageUrl = "/images/rooms/family-1.jpg",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new RoomType
                    {
                        Name = "Suite",
                        Description = "Phòng Suite sang trọng, có phòng khách, phòng ngủ riêng, jacuzzi",
                        BasePrice = 1500000m,
                        MaxGuests = 4,
                        Amenities = "[\"Wifi\",\"Điều hòa\",\"Smart TV 65\\\"\",\"Mini bar\",\"Phòng khách\",\"Phòng ngủ riêng\",\"Jacuzzi\",\"Bồn tắm cao cấp\"]",
                        ImageUrl = "/images/rooms/vip-1.jpg",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new RoomType
                    {
                        Name = "President Suite",
                        Description = "Phòng President xa hoa nhất, view tuyệt đẹp, dịch vụ VIP",
                        BasePrice = 3600000m,
                        MaxGuests = 6,
                        Amenities = "[\"Wifi tốc độ cao\",\"Điều hòa\",\"Smart TV 75\\\"\",\"Mini bar\",\"Phòng khách lớn\",\"2 Phòng ngủ\",\"Jacuzzi\",\"Sauna\",\"Butler 24/7\"]",
                        ImageUrl = "/images/hotel-assets/rooms/vip-room.png",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Phòng VIP Luxury - hạ tầng sang trọng, giá 6.7 triệu/đêm
                    new RoomType
                    {
                        Name = "VIP Luxury",
                        Description = "Phòng VIP Luxury hạng sang, có view tuyệt đẹp, dịch vụ cao cấp 5 sao",
                        BasePrice = 6700000m,
                        MaxGuests = 4,
                        Amenities = "[\"Wifi tốc độ cao\",\"Điều hòa\",\"Smart TV 75 inch\",\"Mini bar\",\"Phòng khách\",\"Phòng ngủ\",\"Jacuzzi\",\"Butler 24/7\",\"Champagne miễn phí\"]",
                        ImageUrl = "/images/hotel-assets/rooms/vip-luxury.png",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Phòng test giá 10,000 VND - chỉ dùng để test thanh toán SePay
                    new RoomType
                    {
                        Name = "Test Room",
                        Description = "Phòng test giá 10,000 VND - chỉ dùng để test thanh toán",
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
                logger.LogInformation("Đã tạo {Count} loại phòng", roomTypes.Count);

                // ════════════════════════════════════════════════════════
                // 2. TẠO PHÒNG (Rooms) - mỗi tầng 4 phòng
                // ════════════════════════════════════════════════════════
                var rooms = new List<Room>();
                var roomNumbers = new[]
                {
                    // Tầng 1 - Standard
                    ("101", 1, 1, RoomStatus.Available),
                    ("102", 1, 1, RoomStatus.Available),
                    ("103", 1, 2, RoomStatus.Available),
                    ("104", 1, 2, RoomStatus.Occupied),
                    // Tầng 2 - Superior
                    ("201", 2, 2, RoomStatus.Available),
                    ("202", 2, 2, RoomStatus.Cleaning),
                    ("203", 2, 3, RoomStatus.Available),
                    ("204", 2, 3, RoomStatus.Occupied),
                    // Tầng 3 - Deluxe
                    ("301", 3, 3, RoomStatus.Available),
                    ("302", 3, 3, RoomStatus.Available),
                    ("303", 3, 4, RoomStatus.Maintenance),
                    ("304", 3, 4, RoomStatus.Occupied),
                    // Tầng 4 - Suite
                    ("401", 4, 4, RoomStatus.Available),
                    ("402", 4, 4, RoomStatus.Available),
                    ("403", 4, 5, RoomStatus.Available),
                    ("404", 4, 5, RoomStatus.Occupied),

                    // Tầng 5 - President Suite (3 phòng)
                    ("501", 5, 5, RoomStatus.Available),
                    ("502", 5, 5, RoomStatus.Available),
                    ("503", 5, 5, RoomStatus.Occupied),
                    // Tầng 6 - VIP Luxury (3 phòng)
                    ("601", 6, 6, RoomStatus.Available),
                    ("602", 6, 6, RoomStatus.Available),
                    ("603", 6, 6, RoomStatus.Occupied),
                    // Tầng 7 - Test Room
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
                logger.LogInformation("Đã tạo {Count} phòng", rooms.Count);

                // ════════════════════════════════════════════════════════
                // 4. TẠO NGƯỜI DÙNG (Users) với mật khẩu ngẫu nhiên 32 ký tự
                // ════════════════════════════════════════════════════════
                var credsBuilder = new StringBuilder();
                credsBuilder.AppendLine("================================================================================");
                credsBuilder.AppendLine($"SUN HOTEL - GENERATED SEED CREDENTIALS ({DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)");
                credsBuilder.AppendLine("SECURITY NOTICE: 32-character randomized CSPRNG passwords generated for all accounts.");
                credsBuilder.AppendLine("Upper + Lower + Numbers + Special Characters. DO NOT COMMIT TO VERSION CONTROL.");
                credsBuilder.AppendLine("================================================================================");

                string CreateSeedPassword(string username, string role)
                {
                    string pass = PasswordGenerator.Generate(32);
                    credsBuilder.AppendLine($"Role: {role,-14} | Username: {username,-15} | Password: {pass}");
                    return pass;
                }

                var users = new List<User>
                {
                    // Admin - Lường Minh Hiếu
                    new User
                    {
                        Username = "admin",
                        Password = BCrypt.Net.BCrypt.HashPassword(CreateSeedPassword("admin", "Admin"), 10),
                        FullName = "Lường Minh Hiếu",
                        Role = UserRole.Admin,
                        Email = "nguyenthaitrunghieu123@gmail.com",
                        Phone = "0342144054",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Manager
                    new User
                    {
                        Username = "manager",
                        Password = BCrypt.Net.BCrypt.HashPassword(CreateSeedPassword("manager", "Manager"), 10),
                        FullName = "Trần Thị Mai Anh",
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
                        Password = BCrypt.Net.BCrypt.HashPassword(CreateSeedPassword("reception1", "Receptionist"), 10),
                        FullName = "Lê Thị Thùy Linh",
                        Role = UserRole.Receptionist,
                        Email = "reception1@sunhotel.vn",
                        Phone = "0903456789",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new User
                    {
                        Username = "reception2",
                        Password = BCrypt.Net.BCrypt.HashPassword(CreateSeedPassword("reception2", "Receptionist"), 10),
                        FullName = "Nguyễn Văn Tuấn",
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
                        Password = BCrypt.Net.BCrypt.HashPassword(CreateSeedPassword("housekeep1", "Housekeeping"), 10),
                        FullName = "Phạm Thị Hồng",
                        Role = UserRole.Housekeeping,
                        Email = "housekeep1@sunhotel.vn",
                        Phone = "0905678901",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new User
                    {
                        Username = "housekeep2",
                        Password = BCrypt.Net.BCrypt.HashPassword(CreateSeedPassword("housekeep2", "Housekeeping"), 10),
                        FullName = "Hoàng Văn Dũng",
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
                        Password = BCrypt.Net.BCrypt.HashPassword(CreateSeedPassword("maintenance1", "Maintenance"), 10),
                        FullName = "Võ Văn Kỷ",
                        Role = UserRole.Maintenance,
                        Email = "maintenance@sunhotel.vn",
                        Phone = "0907890123",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Kitchen (Bếp)
                    new User
                    {
                        Username = "bep1",
                        Password = BCrypt.Net.BCrypt.HashPassword(CreateSeedPassword("bep1", "Kitchen"), 10),
                        FullName = "Nguyễn Hùng Cường",
                        Role = UserRole.Kitchen,
                        Email = "bep1@sunhotel.vn",
                        Phone = "0908123456",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Bar (Quầy bar)
                    new User
                    {
                        Username = "bar1",
                        Password = BCrypt.Net.BCrypt.HashPassword(CreateSeedPassword("bar1", "Bar"), 10),
                        FullName = "Đinh Gia Huy",
                        Role = UserRole.Bar,
                        Email = "bar1@sunhotel.vn",
                        Phone = "0908234567",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Inventory (Kho & Vật tư)
                    new User
                    {
                        Username = "kho1",
                        Password = BCrypt.Net.BCrypt.HashPassword(CreateSeedPassword("kho1", "Inventory"), 10),
                        FullName = "Phan Thị Thu Hà",
                        Role = UserRole.Inventory,
                        Email = "kho1@sunhotel.vn",
                        Phone = "0908345678",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Customer accounts
                    new User
                    {
                        Username = "customer001",
                        Password = BCrypt.Net.BCrypt.HashPassword(CreateSeedPassword("customer001", "Customer"), 10),
                        FullName = "Trần Văn Khánh",
                        Role = UserRole.Customer,
                        Email = "khach1@email.com",
                        Phone = "0912345678",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new User
                    {
                        Username = "customer002",
                        Password = BCrypt.Net.BCrypt.HashPassword(CreateSeedPassword("customer002", "Customer"), 10),
                        FullName = "Hoàng Thị Minh",
                        Role = UserRole.Customer,
                        Email = "khach2@email.com",
                        Phone = "0923456789",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    }
                };
                context.Users.AddRange(users);
                await context.SaveChangesAsync();

                try
                {
                    var credsPath = Path.Combine(Directory.GetCurrentDirectory(), "credentials.generated.txt");
                    await File.WriteAllTextAsync(credsPath, credsBuilder.ToString());
                    logger.LogWarning("Đã sinh mật khẩu ngẫu nhiên 32 ký tự cho tất cả tài khoản mẫu và lưu vào: {Path}", credsPath);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Không thể ghi file credentials.generated.txt. Hãy xem thông tin tài khoản qua log console.");
                    Console.WriteLine(credsBuilder.ToString());
                }

                logger.LogInformation("Đã tạo {Count} người dùng với mật khẩu ngẫu nhiên 32 ký tự", users.Count);

                // ════════════════════════════════════════════════════════
                // 5. TẠO BOOKING MẪU
                // ════════════════════════════════════════════════════════
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var adminUser = users[0];
                var receptionUser = users[2];

                var bookings = new List<Booking>
                {
                    // Booking đã check-in (đang ở)
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
                    // Booking đã check-in 2
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
                    // Booking confirmed (sắp đến)
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
                logger.LogInformation("Đã tạo {Count} booking", bookings.Count);

                // ════════════════════════════════════════════════════════
                // 5. TẠO HÓA ĐƠN MẪU
                // ════════════════════════════════════════════════════════
                var invoices = new List<Invoice>
                {
                    // Invoice đã thanh toán cho booking 1
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
                    // Invoice đã thanh toán cho booking 2
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
                    // Invoice chưa thanh toán cho booking 3
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
                    // Invoice chưa thanh toán cho booking 5
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
                logger.LogInformation("Đã tạo {Count} hóa đơn", invoices.Count);

                // ════════════════════════════════════════════════════════
                // 6. TẠO DỊCH VỤ MẪU
                // ════════════════════════════════════════════════════════
                var services = new List<Service>
                {
                    // Dịch vụ cho booking 1 (đang ở)
                    new Service
                    {
                        BookingId = bookings[0].Id,
                        ServiceName = "Giặt ủi",
                        ServiceType = ServiceType.Laundry,
                        Quantity = 3,
                        UnitPrice = 15000m,
                        TotalAmount = 45000m,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    },
                    new Service
                    {
                        BookingId = bookings[0].Id,
                        ServiceName = "Nước uống minibar",
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
                        ServiceName = "Buffet sáng",
                        ServiceType = ServiceType.Breakfast,
                        Quantity = 4,
                        UnitPrice = 180000m,
                        TotalAmount = 720000m,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    },
                    new Service
                    {
                        BookingId = bookings[1].Id,
                        ServiceName = "Di chuyển sân bay",
                        ServiceType = ServiceType.Transport,
                        Quantity = 1,
                        UnitPrice = 250000m,
                        TotalAmount = 250000m,
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    }
                };
                context.Services.AddRange(services);
                await context.SaveChangesAsync();
                logger.LogInformation("Đã tạo {Count} dịch vụ", services.Count);

                // ════════════════════════════════════════════════════════
                // 7. TẠO TICKET MẪU
                // ════════════════════════════════════════════════════════
                var housekeepingUser = users[4];
                var maintenanceUser = users[6];

                var tickets = new List<Ticket>
                {
                    new Ticket
                    {
                        TicketNumber = "TK-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-001",
                        RoomId = rooms[5].Id, // Room 202 - đang dọn dẹp
                        Type = TicketType.Housekeeping,
                        Description = "Dọn dẹp phòng sau khi khách trả",
                        Priority = TicketPriority.Medium,
                        Status = TicketStatus.Open,
                        ReportedById = receptionUser.Id,
                        AssignedToId = housekeepingUser.Id,
                        CreatedAt = DateTime.UtcNow.AddHours(-3)
                    },
                    new Ticket
                    {
                        TicketNumber = "TK-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-002",
                        RoomId = rooms[10].Id, // Room 303 - bảo trì
                        Type = TicketType.Maintenance,
                        Description = "Máy lạnh không lạnh, cần kiểm tra",
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
                        Description = "Đèn phòng chập, cần thay bóng",
                        Priority = TicketPriority.Low,
                        Status = TicketStatus.Resolved,
                        ReportedById = receptionUser.Id,
                        AssignedToId = maintenanceUser.Id,
                        ResolutionNotes = "Đã thay bóng đèn mới",
                        ResolvedAt = DateTime.UtcNow.AddHours(-2),
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    }
                };
                context.Tickets.AddRange(tickets);
                await context.SaveChangesAsync();
                logger.LogInformation("Đã tạo {Count} ticket", tickets.Count);

                // ════════════════════════════════════════════════════════
                // 8. TẠO AUDIT LOG MẪU
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
                logger.LogInformation("Đã tạo {Count} audit log", auditLogs.Count);

                logger.LogInformation("Hoàn tất khởi tạo dữ liệu mẫu!");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi khi khởi tạo SeedData.");
                throw;
            }
        }

        /// <summary>
        /// Fix user roles in existing database - chỉ chạy 1 lần khi phát hiện role sai
        /// Nếu phát hiện role không khớp -> throw exception ngay lập tức để stop app
        /// </summary>
        private static async Task FixUserRolesAsync(SunHotelDbContext context, ILogger logger)
        {
            // Cập nhật thông tin admin theo yêu cầu mới nhất (Lường Minh Hiếu)
            var adminUser = await context.Users.FirstOrDefaultAsync(u => u.Username == "admin");
            if (adminUser != null)
            {
                adminUser.FullName = "Lường Minh Hiếu";
                adminUser.Phone = "0342144054";
                adminUser.Email = "nguyenthaitrunghieu123@gmail.com";
                adminUser.Role = UserRole.Admin;
                adminUser.IsActive = true;
            }

            // Đảm bảo các role mới có tài khoản mẫu: bep1 (Kitchen), bar1 (Bar), kho1 (Inventory)
            if (!await context.Users.AnyAsync(u => u.Username == "bep1"))
            {
                context.Users.Add(new User
                {
                    Username = "bep1",
                    Password = BCrypt.Net.BCrypt.HashPassword("Bep@SunHotel2026", 10),
                    FullName = "Nguyễn Hùng Cường",
                    Role = UserRole.Kitchen,
                    Email = "bep1@sunhotel.vn",
                    Phone = "0908123456",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (!await context.Users.AnyAsync(u => u.Username == "bar1"))
            {
                context.Users.Add(new User
                {
                    Username = "bar1",
                    Password = BCrypt.Net.BCrypt.HashPassword("Bar@SunHotel2026", 10),
                    FullName = "Đinh Gia Huy",
                    Role = UserRole.Bar,
                    Email = "bar1@sunhotel.vn",
                    Phone = "0908234567",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (!await context.Users.AnyAsync(u => u.Username == "kho1"))
            {
                context.Users.Add(new User
                {
                    Username = "kho1",
                    Password = BCrypt.Net.BCrypt.HashPassword("Kho@SunHotel2026", 10),
                    FullName = "Phan Thị Thu Hà",
                    Role = UserRole.Inventory,
                    Email = "kho1@sunhotel.vn",
                    Phone = "0908345678",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await context.SaveChangesAsync();

            var roleMapping = new Dictionary<string, UserRole>
            {
                { "admin", UserRole.Admin },
                { "manager", UserRole.Manager },
                { "reception1", UserRole.Receptionist },
                { "reception2", UserRole.Receptionist },
                { "housekeep1", UserRole.Housekeeping },
                { "housekeep2", UserRole.Housekeeping },
                { "maintenance1", UserRole.Maintenance },
                { "bep1", UserRole.Kitchen },
                { "bar1", UserRole.Bar },
                { "kho1", UserRole.Inventory },
                { "customer001", UserRole.Customer },
                { "customer002", UserRole.Customer }
            };

            var usersToFix = await context.Users.Where(u => roleMapping.Keys.Contains(u.Username)).ToListAsync();
            var invalidUsers = usersToFix.Where(u => u.Role != roleMapping[u.Username]).ToList();
            if (invalidUsers.Any())
            {
                foreach (var user in invalidUsers)
                {
                    var oldRole = user.Role.ToString();
                    user.Role = roleMapping[user.Username];
                    logger.LogWarning("Auto-fixed role: {Username}: {OldRole} -> {NewRole}",
                        user.Username, oldRole, user.Role);
                }
                await context.SaveChangesAsync();
                logger.LogInformation("Đã tự động fix {Count} tài khoản có role sai", invalidUsers.Count);
            }

            logger.LogInformation("Tất cả tài khoản hệ thống đã được cập nhật vai trò và dữ liệu chuẩn xác.");
        }
    }
}
