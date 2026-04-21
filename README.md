# Sun Hotel - Hệ Thống Quản Lý Khách Sạn

Dự án web quản lý khách sạn viết bằng ASP.NET Core 9 MVC, kết nối SQL Server. Được xây dựng cho mục đích học tập và thực hành, bao gồm đầy đủ các nghiệp vụ cơ bản của một khách sạn vừa và nhỏ.

---

## Tính năng chính

- **Đặt phòng**: Khách hàng tự đặt phòng online, tra cứu booking theo mã
- **Lễ tân**: Check-in / Check-out, tạo booking tại quầy
- **Quản lý phòng**: Theo dõi tình trạng phòng, lịch dọn phòng
- **Hóa đơn & Thanh toán**: VietQR, SePay, MoMo
- **Báo cáo doanh thu**: Thống kê theo ngày/tháng/năm
- **Ticket bảo trì**: Nhân viên gửi yêu cầu sửa chữa, theo dõi tiến độ
- **Quản lý người dùng**: Phân quyền theo vai trò (Admin / Manager / Receptionist / Housekeeping / Maintenance)
- **Gửi email**: Xác nhận booking, thông báo tự động qua Gmail SMTP

---

## Công nghệ sử dụng

| Thành phần | Chi tiết |
|---|---|
| Backend | ASP.NET Core 9 MVC |
| Database | MS SQL Server 2022 |
| ORM | Entity Framework Core 9 |
| Auth | Cookie Authentication + BCrypt |
| Logging | Serilog (Console + File) |
| Container | Docker + Docker Compose |

---

## Cài đặt

### Yêu cầu

- [Docker](https://www.docker.com/get-started) và Docker Compose
- Git

### Chạy bằng Docker (khuyến nghị)

**1. Clone repo về máy**

```bash
git clone https://github.com/kaedee206/QuanLyKhachSan.git
cd QuanLyKhachSan
```

**2. Tạo file cấu hình**

Sao chép file mẫu `.env.example` thành `.env`, sau đó điền các giá trị thực vào:

```bash
cp .env.example .env
```

Mở `.env` và chỉnh sửa:

```env
SA_PASSWORD=MatKhauManhCho@SqlServer123   # Mật khẩu SA của SQL Server
JWT_SECRET=ChuoiSecretDaiIt32KyTu!        # Secret cho JWT token
APP_BASE_URL=https://your-domain.com      # Domain thực tế của bạn

SEPAY_MERCHANT_ID=SP-...
SEPAY_SECRET_KEY=spsk_...

EMAIL_USER=your@gmail.com
EMAIL_PASSWORD=xxxx xxxx xxxx xxxx        # App Password của Gmail, không phải mật khẩu thường
```

Ngoài ra, tạo file `appsettings.json` từ file mẫu:

```bash
cp appsettings.Template.json appsettings.json
```

Sau đó điền connection string và các key vào `appsettings.json` tương tự.

**3. Build và chạy**

```bash
docker compose up --build
```

Lần đầu chạy sẽ mất vài phút vì cần pull image SQL Server và build ứng dụng. Sau khi khởi động xong, truy cập:

```
http://localhost:5000
```

Dữ liệu mẫu (phòng, tài khoản nhân viên) sẽ được tạo tự động khi app khởi động lần đầu.

**4. Dừng ứng dụng**

```bash
docker compose down
```

Dữ liệu SQL Server được lưu trong Docker volume, không mất khi dừng container.

---

### Chạy thủ công (không dùng Docker)

Yêu cầu thêm: [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) và SQL Server đang chạy.

```bash
# Cài dependencies
dotnet restore

# Tạo appsettings.json từ template và điền thông tin database
cp appsettings.Template.json appsettings.json

# Chạy app
dotnet run
```

---

## Tài khoản mặc định (sau khi seed)

| Username | Vai trò | Trang mặc định sau đăng nhập |
|---|---|---|
| `admin` | Admin | `/Admin/Index` |
| `manager` | Manager | `/Report/Revenue` |
| `reception1` | Lễ tân | `/Booking/Index` |
| `housekeep1` | Buồng phòng | `/Room/Cleaning` |
| `maintenance1` | Bảo trì | `/Ticket/Index` |

Mật khẩu xem trong file `test_accounts.txt` (file này không được commit lên git, chỉ có ở local).

---

## Cấu trúc thư mục

```
QuanLyKhachSan/
├── Controllers/        # Xử lý request
├── Models/
│   ├── Entities/       # Entity của database
│   ├── Enums/          # Các enum dùng chung
│   └── ViewModels/     # ViewModel cho View
├── Views/              # Giao diện Razor
├── Services/           # Business logic
├── Data/               # DbContext + SeedData
├── BackgroundServices/ # Các task chạy nền (SePay polling)
├── wwwroot/            # Static files (CSS, JS, ảnh)
├── Dockerfile
├── docker-compose.yml
├── appsettings.Template.json   # Template config, không có key thật
└── .env.example                # Template biến môi trường
```

---

## Lưu ý quan trọng

**Bảo mật:**

- File `appsettings.json` và `appsettings.Development.json` bị loại khỏi git (xem `.gitignore`). Khi clone về, bạn phải tự tạo lại từ `appsettings.Template.json`
- File `.env` cũng không được commit. Chỉ có `.env.example` (không chứa key thật) là được push lên
- File `test_accounts.txt` chứa mật khẩu test — không bao giờ commit file này

**Gmail App Password:**

Để gửi email hoạt động, bạn cần bật 2FA cho Gmail rồi tạo App Password tại [myaccount.google.com/apppasswords](https://myaccount.google.com/apppasswords). Điền App Password vào `Email__Password`, không phải mật khẩu Gmail thông thường.

**SQL Server trên Docker (Linux/Mac):**

Image `mcr.microsoft.com/mssql/server:2022-latest` yêu cầu ít nhất 2GB RAM. Nếu container SQL Server bị crash ngay khi start, hãy kiểm tra lại RAM được cấp cho Docker Desktop.

**SePay / MoMo:**

Nếu không dùng thanh toán online, có thể để trống các key này. Hệ thống vẫn chạy bình thường, chỉ tắt tính năng thanh toán tự động.

---

## Liên hệ

Mọi góp ý hoặc báo lỗi vui lòng tạo Issue trên GitHub.
