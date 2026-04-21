using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;

namespace QuanLyKhachSan.Services
{
    public class UserService
    {
        private readonly SunHotelDbContext _db;
        private readonly ILogger<UserService> _logger;

        public UserService(SunHotelDbContext db, ILogger<UserService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<User> CreateUser(CreateUserViewModel model)
        {
            if (await _db.Users.AnyAsync(u => u.Username == model.Username))
                throw new InvalidOperationException("Tên đăng nhập đã tồn tại");

            if (!string.IsNullOrEmpty(model.Email) && await _db.Users.AnyAsync(u => u.Email == model.Email))
                throw new InvalidOperationException("Email đã tồn tại");

            var user = new User
            {
                Username = model.Username,
                Password = BCrypt.Net.BCrypt.HashPassword(model.Password, 10),
                FullName = model.FullName,
                Role = model.Role,
                Email = model.Email,
                Phone = model.Phone,
                IsActive = true
            };

            _db.Users.Add(user);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "CREATE_USER",
                EntityType = "user",
                NewValue = Newtonsoft.Json.JsonConvert.SerializeObject(new { username = model.Username, role = model.Role.ToString() })
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("User created: {Username}, Role: {Role}", model.Username, model.Role);
            return user;
        }

        public async Task<PaginatedList<User>> GetUsers(string? search = null, UserRole? role = null, int page = 1, int pageSize = 20)
        {
            var query = _db.Users.AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                var s = search.ToLower();
                query = query.Where(u =>
                    u.Username.ToLower().Contains(s) ||
                    u.FullName.ToLower().Contains(s) ||
                    (u.Email != null && u.Email.ToLower().Contains(s)));
            }

            if (role.HasValue)
                query = query.Where(u => u.Role == role.Value);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PaginatedList<User>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            };
        }

        public async Task<User?> GetUserById(int id)
        {
            return await _db.Users.FindAsync(id);
        }

        public async Task<User> UpdateUser(int id, EditUserViewModel model)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) throw new InvalidOperationException("Không tìm thấy user");

            if (!string.IsNullOrEmpty(model.Email) && model.Email != user.Email)
            {
                if (await _db.Users.AnyAsync(u => u.Email == model.Email && u.Id != id))
                    throw new InvalidOperationException("Email đã tồn tại");
            }

            user.FullName = model.FullName;
            user.Role = model.Role;
            user.Email = model.Email;
            user.Phone = model.Phone;
            user.IsActive = model.IsActive;

            await _db.SaveChangesAsync();
            _logger.LogInformation("User updated: {UserId}", id);
            return user;
        }

        public async Task<bool> DeleteUser(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) return false;

            user.IsActive = false; // Soft delete
            await _db.SaveChangesAsync();
            _logger.LogInformation("User soft-deleted: {UserId}", id);
            return true;
        }

        public async Task<bool> ResetPassword(int userId, string newPassword)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return false;

            user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword, 10);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Password reset for user: {UserId}", userId);
            return true;
        }

        public async Task<List<User>> GetActiveUsers()
        {
            return await _db.Users.Where(u => u.IsActive).OrderBy(u => u.FullName).ToListAsync();
        }
    }
}
