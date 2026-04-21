using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;
using Newtonsoft.Json;

namespace QuanLyKhachSan.Services
{
    public class AuthService
    {
        private readonly SunHotelDbContext _db;
        private readonly ILogger<AuthService> _logger;

        public AuthService(SunHotelDbContext db, ILogger<AuthService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<User?> ValidateUser(string username, string password)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null || !user.IsActive)
            {
                _logger.LogWarning("Login failed: user not found or inactive - {Username}", username);
                return null;
            }

            if (!BCrypt.Net.BCrypt.Verify(password, user.Password))
            {
                _logger.LogWarning("Login failed: invalid password - {Username}", username);
                return null;
            }

            // Update last login
            user.LastLogin = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // Audit log
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = user.Id,
                Action = "LOGIN",
                EntityType = "user",
                EntityId = user.Id
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation("User logged in: {Username} (ID: {UserId})", username, user.Id);
            return user;
        }

        public async Task LogLogout(int userId)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "LOGOUT",
                EntityType = "user",
                EntityId = userId
            });
            await _db.SaveChangesAsync();
        }

        public async Task<User?> GetProfile(int userId)
        {
            return await _db.Users.FindAsync(userId);
        }

        public async Task<bool> ChangePassword(int userId, string currentPassword, string newPassword)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return false;

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.Password))
                return false;

            user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword, 10);
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "CHANGE_PASSWORD",
                EntityType = "user",
                EntityId = userId
            });
            await _db.SaveChangesAsync();
            return true;
        }
    }
}
