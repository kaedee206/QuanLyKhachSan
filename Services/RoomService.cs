using Microsoft.EntityFrameworkCore;
using QuanLyKhachSan.Data;
using QuanLyKhachSan.Models.Entities;
using QuanLyKhachSan.Models.Enums;
using QuanLyKhachSan.Models.ViewModels;
using Newtonsoft.Json;

namespace QuanLyKhachSan.Services
{
    public class RoomService
    {
        private readonly SunHotelDbContext _db;
        private readonly ILogger<RoomService> _logger;

        public RoomService(SunHotelDbContext db, ILogger<RoomService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Tìm phòng trống theo ngày và loại phòng
        /// </summary>
        public async Task<List<Room>> FindAvailableRooms(RoomSearchViewModel search)
        {
            var query = _db.Rooms
                .Include(r => r.RoomType)
                .Where(r => r.Status == RoomStatus.Available && r.RoomType.IsActive);

            if (search.RoomTypeId.HasValue)
                query = query.Where(r => r.RoomTypeId == search.RoomTypeId.Value);

            if (search.NumGuests > 0)
                query = query.Where(r => r.RoomType.MaxGuests >= search.NumGuests);

            var rooms = await query.OrderBy(r => r.Floor).ThenBy(r => r.RoomNumber).ToListAsync();

            // Filter by date conflicts if dates are provided
            if (search.CheckInDate.HasValue && search.CheckOutDate.HasValue)
            {
                var checkIn = search.CheckInDate.Value;
                var checkOut = search.CheckOutDate.Value;

                var bookedRoomIds = await _db.Bookings
                    .Where(b =>
                        b.RoomId != null &&
                        b.Status != BookingStatus.Cancelled &&
                        b.Status != BookingStatus.NoShow &&
                        b.CheckInDate < checkOut &&
                        b.CheckOutDate > checkIn)
                    .Select(b => b.RoomId!.Value)
                    .Distinct()
                    .ToListAsync();

                rooms = rooms.Where(r => !bookedRoomIds.Contains(r.Id)).ToList();
            }

            return rooms;
        }

        /// <summary>
        /// Lấy room grid data cho dashboard
        /// </summary>
        public async Task<RoomGridViewModel> GetRoomGrid()
        {
            var rooms = await _db.Rooms
                .Include(r => r.RoomType)
                .OrderBy(r => r.Floor)
                .ThenBy(r => r.RoomNumber)
                .ToListAsync();

            // Get active bookings for occupied rooms
            var activeBookings = await _db.Bookings
                .Where(b => b.RoomId != null &&
                       (b.Status == BookingStatus.Pending ||
                        b.Status == BookingStatus.Confirmed ||
                        b.Status == BookingStatus.CheckedIn))
                .ToDictionaryAsync(b => b.RoomId!.Value);

            var floors = rooms
                .GroupBy(r => r.Floor)
                .Select(g => new FloorData
                {
                    Floor = g.Key,
                    Rooms = g.Select(r =>
                    {
                        activeBookings.TryGetValue(r.Id, out var booking);
                        return new RoomGridItem
                        {
                            Id = r.Id,
                            RoomNumber = r.RoomNumber,
                            Status = r.Status,
                            RoomTypeName = r.RoomType.Name,
                            BasePrice = r.RoomType.BasePrice,
                            GuestName = booking?.GuestName,
                            BookingCode = booking?.BookingCode,
                            CheckInDate = booking?.CheckInDate,
                            CheckOutDate = booking?.CheckOutDate
                        };
                    }).ToList(),
                    Summary = new RoomSummary
                    {
                        Total = g.Count(),
                        Available = g.Count(r => r.Status == RoomStatus.Available),
                        Occupied = g.Count(r => r.Status == RoomStatus.Occupied),
                        Cleaning = g.Count(r => r.Status == RoomStatus.Cleaning),
                        Maintenance = g.Count(r => r.Status == RoomStatus.Maintenance)
                    }
                })
                .ToList();

            return new RoomGridViewModel
            {
                Floors = floors,
                Summary = new RoomSummary
                {
                    Total = rooms.Count,
                    Available = rooms.Count(r => r.Status == RoomStatus.Available),
                    Occupied = rooms.Count(r => r.Status == RoomStatus.Occupied),
                    Cleaning = rooms.Count(r => r.Status == RoomStatus.Cleaning),
                    Maintenance = rooms.Count(r => r.Status == RoomStatus.Maintenance)
                }
            };
        }

        /// <summary>
        /// Cập nhật trạng thái phòng
        /// </summary>
        public async Task<Room> UpdateRoomStatus(int roomId, RoomStatus newStatus, int? userId = null)
        {
            var room = await _db.Rooms.Include(r => r.RoomType).FirstOrDefaultAsync(r => r.Id == roomId);
            if (room == null) throw new InvalidOperationException("Phòng không tồn tại");

            var oldStatus = room.Status;
            if (!IsValidStatusTransition(oldStatus, newStatus))
                throw new InvalidOperationException($"Không thể chuyển trạng thái phòng từ {oldStatus} sang {newStatus}");

            room.Status = newStatus;

            if (userId.HasValue)
            {
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "UPDATE_ROOM_STATUS",
                    EntityType = "room",
                    EntityId = roomId,
                    OldValue = JsonConvert.SerializeObject(new { status = oldStatus.ToString() }),
                    NewValue = JsonConvert.SerializeObject(new { status = newStatus.ToString() })
                });
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Room {RoomNumber} status: {OldStatus} -> {NewStatus}",
                room.RoomNumber, oldStatus, newStatus);
            return room;
        }

        /// <summary>
        /// Cập nhật trạng thái dọn phòng (Housekeeping)
        /// </summary>
        public async Task<Room> UpdateCleaningStatus(int roomId, RoomStatus status, int userId)
        {
            var room = await _db.Rooms.Include(r => r.RoomType).FirstOrDefaultAsync(r => r.Id == roomId);
            if (room == null) throw new InvalidOperationException("Phòng không tồn tại");

            if (room.Status != RoomStatus.Cleaning && room.Status != RoomStatus.Available)
                throw new InvalidOperationException("Phòng phải ở trạng thái Cleaning hoặc Available");

            if (status != RoomStatus.Cleaning && status != RoomStatus.Available)
                throw new InvalidOperationException("Trạng thái dọn phòng phải là Cleaning hoặc Available");

            var oldStatus = room.Status;
            room.Status = status;

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "UPDATE_CLEANING_STATUS",
                EntityType = "room",
                EntityId = roomId,
                OldValue = JsonConvert.SerializeObject(new { status = oldStatus.ToString() }),
                NewValue = JsonConvert.SerializeObject(new { status = status.ToString() })
            });

            await _db.SaveChangesAsync();
            return room;
        }

        public async Task<List<Room>> GetAllRooms()
        {
            return await _db.Rooms
                .Include(r => r.RoomType)
                .OrderBy(r => r.Floor)
                .ThenBy(r => r.RoomNumber)
                .ToListAsync();
        }

        public async Task<Room?> GetRoomById(int id)
        {
            return await _db.Rooms.Include(r => r.RoomType).FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<List<RoomType>> GetAllRoomTypes()
        {
            return await _db.RoomTypes.Where(rt => rt.IsActive).OrderBy(rt => rt.BasePrice).ToListAsync();
        }

        public async Task<RoomType?> GetRoomTypeById(int id)
        {
            return await _db.RoomTypes.FindAsync(id);
        }

        // ── Validation helpers ────────────────────────────────────
        private static bool IsValidStatusTransition(RoomStatus current, RoomStatus target)
        {
            var validTransitions = new Dictionary<RoomStatus, RoomStatus[]>
            {
                // Trống → Có khách (chỉ qua CheckIn) hoặc Dọn / Bảo trì
                { RoomStatus.Available, new[] { RoomStatus.Occupied, RoomStatus.Cleaning, RoomStatus.Maintenance } },
                // Có khách → Chỉ được chuyển sang Đang dọn (qua CheckOut) hoặc Bảo trì
                // KHÔNG cho phép chuyển trực tiếp sang Trống
                { RoomStatus.Occupied, new[] { RoomStatus.Cleaning, RoomStatus.Maintenance } },
                { RoomStatus.Cleaning, new[] { RoomStatus.Available, RoomStatus.Maintenance } },
                { RoomStatus.Maintenance, new[] { RoomStatus.Cleaning, RoomStatus.Available } }
            };

            return validTransitions.ContainsKey(current) && validTransitions[current].Contains(target);
        }
    }
}
