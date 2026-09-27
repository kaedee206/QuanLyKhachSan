using System.Collections.Generic;
using QuanLyKhachSan.Models;
using QuanLyKhachSan.Models.AI;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Services.AI
{
    public class AiToolRegistry
    {
        public static string GetTierName(UserRole role) => role switch
        {
            UserRole.Admin => "ADMIN",
            UserRole.Manager => "MANAGER",
            UserRole.Receptionist => "RECEPTIONIST",
            _ => "GUEST"
        };

        public List<AiToolDefinition> GetToolsForRole(UserRole role)
        {
            var tools = new List<AiToolDefinition>();
            
            // GUEST tools
            tools.Add(new AiToolDefinition { Name = "check_room_availability", Description = "Check if rooms are available", MinimumRole = "GUEST" });
            tools.Add(new AiToolDefinition { Name = "create_booking_draft", Description = "Create a draft booking", MinimumRole = "GUEST" });
            tools.Add(new AiToolDefinition { Name = "create_booking", Description = "Create a real hotel booking for a guest", MinimumRole = "GUEST" });
            tools.Add(new AiToolDefinition { Name = "lookup_booking", Description = "Lookup booking by ID", MinimumRole = "GUEST" });
            tools.Add(new AiToolDefinition { Name = "request_room_amenity", Description = "Request amenities for a room", MinimumRole = "GUEST" });

            if (role is UserRole.Receptionist or UserRole.Manager or UserRole.Admin)
            {
                tools.Add(new AiToolDefinition { Name = "search_reservation", Description = "Search for a reservation", MinimumRole = "RECEPTIONIST" });
                tools.Add(new AiToolDefinition { Name = "process_checkin", Description = "Process check-in", MinimumRole = "RECEPTIONIST" });
                tools.Add(new AiToolDefinition { Name = "process_checkout", Description = "Process check-out", MinimumRole = "RECEPTIONIST" });
                tools.Add(new AiToolDefinition { Name = "add_room_charge", Description = "Add a charge to a room", MinimumRole = "RECEPTIONIST" });
                tools.Add(new AiToolDefinition { Name = "check_housekeeping_status", Description = "Check room housekeeping status", MinimumRole = "RECEPTIONIST" });
            }

            if (role is UserRole.Manager or UserRole.Admin)
            {
                tools.Add(new AiToolDefinition { Name = "get_occupancy_and_revenue_metrics", Description = "Get metrics", MinimumRole = "MANAGER" });
                tools.Add(new AiToolDefinition { Name = "update_room_daily_rate", Description = "Update room rate", MinimumRole = "MANAGER" });
                tools.Add(new AiToolDefinition { Name = "get_staff_schedule", Description = "Get staff schedule", MinimumRole = "MANAGER" });
            }

            if (role is UserRole.Admin)
            {
                tools.Add(new AiToolDefinition { Name = "update_system_rag_knowledge", Description = "Update RAG", MinimumRole = "ADMIN" });
                tools.Add(new AiToolDefinition { Name = "get_audit_security_logs", Description = "Get logs", MinimumRole = "ADMIN" });
                tools.Add(new AiToolDefinition { Name = "manage_user_permissions", Description = "Manage permissions", MinimumRole = "ADMIN" });
            }

            return tools;
        }

        public bool IsToolAllowed(string toolName, UserRole role)
        {
            var allowedTools = GetToolsForRole(role);
            return allowedTools.Exists(t => t.Name == toolName);
        }
    }
}
