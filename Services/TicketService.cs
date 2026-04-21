using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;
using Newtonsoft.Json;

namespace QuanLyKhachSan.Services
{
    public class TicketService
    {
        private readonly SunHotelDbContext _db;
        private readonly ILogger<TicketService> _logger;

        public TicketService(SunHotelDbContext db, ILogger<TicketService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Ticket> CreateTicket(CreateTicketViewModel model, int userId)
        {
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var count = await _db.Tickets.CountAsync(t => t.CreatedAt.Date == DateTime.UtcNow.Date) + 1;
            var ticketNumber = $"TK-{today}-{count:D3}";

            var ticket = new Ticket
            {
                TicketNumber = ticketNumber,
                RoomId = model.RoomId,
                Type = model.Type,
                Description = model.Description,
                Priority = model.Priority,
                Status = TicketStatus.Open,
                ReportedById = userId
            };

            _db.Tickets.Add(ticket);

            var room = await _db.Rooms.FindAsync(model.RoomId);
            if (room != null)
            {
                if (model.Type == TicketType.Maintenance)
                    room.Status = RoomStatus.Maintenance;
                else if (model.Type == TicketType.Housekeeping)
                    room.Status = RoomStatus.Cleaning;
            }

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "CREATE_TICKET",
                EntityType = "ticket",
                NewValue = JsonConvert.SerializeObject(new
                {
                    room_id = model.RoomId,
                    type = model.Type.ToString(),
                    priority = model.Priority.ToString()
                })
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("Ticket created: {TicketNumber}", ticketNumber);
            return ticket;
        }

        public async Task<Ticket> UpdateTicket(int ticketId, UpdateTicketViewModel model, int userId)
        {
            var ticket = await _db.Tickets
                .Include(t => t.Room)
                .FirstOrDefaultAsync(t => t.Id == ticketId);
            if (ticket == null) throw new InvalidOperationException("Ticket không tồn tại");

            if (model.Status.HasValue)
            {
                ticket.Status = model.Status.Value;

                if (model.Status == TicketStatus.Resolved)
                {
                    ticket.ResolvedAt = DateTime.UtcNow;
                    if (ticket.Type == TicketType.Maintenance)
                        ticket.Room.Status = RoomStatus.Cleaning;
                    else if (ticket.Type == TicketType.Housekeeping)
                        ticket.Room.Status = RoomStatus.Available;
                }

                if (model.Status == TicketStatus.Closed)
                    ticket.ClosedAt = DateTime.UtcNow;
            }

            if (model.AssignedToId.HasValue)
                ticket.AssignedToId = model.AssignedToId;

            if (!string.IsNullOrEmpty(model.ResolutionNotes))
                ticket.ResolutionNotes = model.ResolutionNotes;

            await _db.SaveChangesAsync();
            _logger.LogInformation("Ticket updated: {TicketId}", ticketId);
            return ticket;
        }

        public async Task<PaginatedList<Ticket>> GetTickets(TicketFilterViewModel filter)
        {
            var query = _db.Tickets
                .Include(t => t.Room).ThenInclude(r => r.RoomType)
                .Include(t => t.Reporter)
                .Include(t => t.Assignee)
                .AsQueryable();

            if (filter.Status.HasValue)
                query = query.Where(t => t.Status == filter.Status.Value);
            if (filter.Type.HasValue)
                query = query.Where(t => t.Type == filter.Type.Value);
            if (filter.Priority.HasValue)
                query = query.Where(t => t.Priority == filter.Priority.Value);
            if (filter.RoomId.HasValue)
                query = query.Where(t => t.RoomId == filter.RoomId.Value);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(t => t.CreatedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            return new PaginatedList<Ticket>
            {
                Items = items,
                Page = filter.Page,
                PageSize = filter.PageSize,
                TotalCount = total
            };
        }

        public async Task<Ticket?> GetTicketById(int id)
        {
            return await _db.Tickets
                .Include(t => t.Room).ThenInclude(r => r.RoomType)
                .Include(t => t.Reporter)
                .Include(t => t.Assignee)
                .FirstOrDefaultAsync(t => t.Id == id);
        }

        public async Task<int> GetOpenTicketsCount()
        {
            return await _db.Tickets.CountAsync(t =>
                t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress);
        }
    }
}
