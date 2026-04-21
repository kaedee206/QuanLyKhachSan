# Changelog - Sun Hotel Management System

Tất cả thay đổi quan trọng của dự án sẽ được ghi lại tại đây.

---

## [2026-04-11] - Cập nhật hệ thống đăng nhập nhân viên với vai trò

### Tính năng mới
- Thêm dropdown chọn vai trò trên trang đăng nhập nhân viên (`/Auth/Login`)
- Thay nút "Đăng nhập" trên navbar thành "Bạn là Nhân Viên Khách Sạn, Đăng Nhập Tại Đây"
- Tự động điều hướng (auto-redirect) khi nhân viên đã đăng nhập

### Các file đã sửa đổi

| File | Mô tả |
|------|--------|
| `Models/ViewModels/ViewModels.cs` | Thêm property `SelectedRole` vào `LoginViewModel` |
| `Views/Auth/Login.cshtml` | Thiết kế lại trang login: thêm dropdown vai trò, đổi tiêu đề, xóa link test_account.txt |
| `Controllers/AuthController.cs` | Cập nhật action Login (GET: auto-redirect, POST: validate role), mở rộng `RedirectByRole()` |
| `Views/Shared/_Layout.cshtml` | Thay nút đăng nhập thành "Bạn là Nhân Viên Khách Sạn, Đăng Nhập Tại Đây" |

### Chi tiết thay đổi

#### 1. LoginViewModel (`Models/ViewModels/ViewModels.cs`)
- Thêm property `SelectedRole` kiểu `string?` để lưu vai trò được chọn khi đăng nhập

#### 2. Trang đăng nhập (`Views/Auth/Login.cshtml`)
- Tiêu đề mới: "Sun Hotel - Trang Đăng Nhập Nhân Viên Khách Sạn"
- Mô tả: "Bạn là Nhân Viên Khách Sạn, Đăng Nhập Tại Đây"
- Dropdown chọn vai trò với 5 tùy chọn:
  - Lễ Tân (Receptionist)
  - Nhân Viên Buồng Phòng (Housekeeping)
  - Bảo Trì (Maintenance)
  - Quản Lý (Manager)
  - Admin
- Xóa phần hiển thị link file test_account.txt

#### 3. AuthController (`Controllers/AuthController.cs`)
- **Login (GET)**: Khi user đã đăng nhập rồi, tự động detect role và redirect đến dashboard phù hợp
- **Login (POST)**: Validate vai trò nếu người dùng chọn vai trò cụ thể. Nếu tài khoản không có vai trò được chọn, báo lỗi
- **RedirectByRole()**: Mở rộng ánh xạ đầy đủ các vai trò:

| Vai trò | Điều hướng đến | Mô tả |
|---------|-----------------|--------|
| Admin | `/Admin/Index` | Dashboard tổng quan |
| Manager | `/Report/Revenue` | Trang báo cáo doanh thu |
| Receptionist | `/Booking/Index` | Trang quản lý booking |
| Housekeeping | `/Room/Cleaning` | Danh sách phòng cần dọn |
| Maintenance | `/Ticket/Index` | Danh sách ticket bảo trì |

#### 4. Layout (`Views/Shared/_Layout.cshtml`)
- Thay nút đăng nhập trên navbar từ style trong suốt thành gold gradient button với text "Bạn là Nhân Viên Khách Sạn, Đăng Nhập Tại Đây"

### Workflow mới

```
Trang chủ (chưa đăng nhập)
    -> Nhấn "Bạn là Nhân Viên Khách Sạn, Đăng Nhập Tại Đây"
    -> Trang /Auth/Login
        -> Chọn vai trò từ dropdown
        -> Nhập username + password
        -> Nhấn "Đăng nhập"
            -> Validate credentials
            -> Validate vai trò (nếu có chọn)
            -> Redirect theo vai trò:
                Admin -> /Admin/Index
                Manager -> /Report/Revenue
                Receptionist -> /Booking/Index
                Housekeeping -> /Room/Cleaning
                Maintenance -> /Ticket/Index
```

---

## [2026-04-10] - Khởi tạo dự án
- Khởi tạo dự án ASP.NET Core MVC
- Cấu hình Entity Framework Core với SQL Server
- Triển khai các module: Booking, Room, Invoice, Service, Ticket, User, Report
- Tích hợp thanh toán: VietQR, MoMo, SePay
- Hệ thống xác thực Cookie Authentication
