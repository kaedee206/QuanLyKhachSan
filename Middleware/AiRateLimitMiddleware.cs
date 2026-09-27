using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models;

namespace QuanLyKhachSan.Middleware
{
    public class AiRateLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private static readonly ConcurrentDictionary<string, (int count, DateTime resetTime)> _limits = new();

        public AiRateLimitMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments("/api/ai"))
            {
                string ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var role = UserRole.Customer;

                if (context.User?.Identity?.IsAuthenticated == true)
                {
                    var roleClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                    if (System.Enum.TryParse<UserRole>(roleClaim, out var parsedRole))
                    {
                        role = parsedRole;
                    }
                }

                int limit = role switch
                {
                    UserRole.Admin => 100,
                    UserRole.Manager => 60,
                    UserRole.Receptionist => 40,
                    _ => 20 // Guest / Customer
                };

                var now = DateTime.UtcNow;
                var (count, resetTime) = _limits.GetOrAdd(ip, _ => (0, now.AddMinutes(1)));

                if (now > resetTime)
                {
                    _limits[ip] = (1, now.AddMinutes(1));
                }
                else if (count >= limit)
                {
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers.Add("Retry-After", "60");
                    await context.Response.WriteAsync("Too Many Requests");
                    return;
                }
                else
                {
                    _limits[ip] = (count + 1, resetTime);
                }
            }

            await _next(context);
        }
    }
}
