using Microsoft.AspNetCore.Mvc;
using QuanLyKhachSan.Models;
using QuanLyKhachSan.Models.AI;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Services.AI;
using System.Threading.Tasks;
using UserRole = QuanLyKhachSan.Models.Enums.UserRole;

namespace QuanLyKhachSan.Controllers
{
    [ApiController]
    [Route("api/ai/chat")]
    public class AiChatController : ControllerBase
    {
        private readonly AiChatService _chatService;
        private readonly AiSecurityGuard _securityGuard;

        public AiChatController(AiChatService chatService, AiSecurityGuard securityGuard)
        {
            _chatService = chatService;
            _securityGuard = securityGuard;
        }

        private UserRole GetCurrentUserRole(string? requestedRole = null)
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin")) return UserRole.Admin;
                if (User.IsInRole("Manager")) return UserRole.Manager;
                if (User.IsInRole("Receptionist")) return UserRole.Receptionist;
                if (User.IsInRole("Housekeeping")) return UserRole.Housekeeping;
                if (User.IsInRole("Maintenance")) return UserRole.Maintenance;

                var roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                             ?? User.FindFirst("role")?.Value;

                if (!string.IsNullOrEmpty(roleClaim) && System.Enum.TryParse<UserRole>(roleClaim, true, out var role))
                {
                    return role;
                }
            }

            if (!string.IsNullOrWhiteSpace(requestedRole) && System.Enum.TryParse<UserRole>(requestedRole, true, out var rRole))
            {
                return rRole;
            }

            return UserRole.Customer;
        }

        [HttpPost]
        public async Task<IActionResult> Chat([FromBody] AiChatRequest request)
        {
            if (!_securityGuard.IsInputSafe(request.Message))
            {
                return BadRequest(new { error = "Invalid input detected." });
            }

            var role = GetCurrentUserRole(request.Role);
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int? userId = int.TryParse(userIdClaim, out var uid) ? uid : null;

            var response = await _chatService.ProcessChatAsync(request, role, userId);
            response.Reply = _securityGuard.FilterOutput(response.Reply);
            
            return Ok(response);
        }

        [HttpPost("new-session")]
        public IActionResult CreateNewSession([FromQuery] string? requestedRole = null)
        {
            var role = GetCurrentUserRole(requestedRole);
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int? userId = int.TryParse(userIdClaim, out var uid) ? uid : null;

            var session = _chatService.CreateSession(role, userId);
            return Ok(new { sessionId = session.SessionId });
        }

        [HttpGet("history")]
        public IActionResult GetHistory([FromQuery] string sessionId)
        {
            var session = _chatService.GetSession(sessionId);
            if (session == null) return NotFound();
            return Ok(session.Messages);
        }

        [HttpDelete("session")]
        public IActionResult DeleteSession([FromQuery] string sessionId)
        {
            if (_chatService.DeleteSession(sessionId))
            {
                return Ok();
            }
            return NotFound();
        }
    }
}
