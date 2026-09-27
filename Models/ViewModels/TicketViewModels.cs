using System.ComponentModel.DataAnnotations;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.ViewModels
{
    public class CreateTicketViewModel
    {
        [Required(ErrorMessage = "Vui lòng chọn phòng")]
        [Display(Name = "Phòng")]
        public int RoomId { get; set; }

        [Required]
        [Display(Name = "Loại ticket")]
        public TicketType Type { get; set; } = TicketType.Maintenance;

        [Required(ErrorMessage = "Vui lòng nhập mô tả")]
        [Display(Name = "Mô tả")]
        public string Description { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Mức độ ưu tiên")]
        public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    }

    public class UpdateTicketViewModel
    {
        public TicketStatus? Status { get; set; }
        public int? AssignedToId { get; set; }
        public string? ResolutionNotes { get; set; }
    }

    public class TicketFilterViewModel
    {
        public TicketStatus? Status { get; set; }
        public TicketType? Type { get; set; }
        public TicketPriority? Priority { get; set; }
        public int? RoomId { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
