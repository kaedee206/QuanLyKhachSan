using System.ComponentModel.DataAnnotations;
using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Models.ViewModels
{
    public class AddServiceViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập mã đặt phòng")]
        [Display(Name = "Mã đặt phòng")]
        public string BookingCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập tên dịch vụ")]
        [MaxLength(100)]
        [Display(Name = "Tên dịch vụ")]
        public string ServiceName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Loại dịch vụ")]
        public ServiceType ServiceType { get; set; }

        [Range(1, 100)]
        [Display(Name = "Số lượng")]
        public int Quantity { get; set; } = 1;

        [Required]
        [Range(0, double.MaxValue)]
        [Display(Name = "Đơn giá")]
        public decimal UnitPrice { get; set; }

        [Display(Name = "Ghi chú")]
        public string? Notes { get; set; }

        public string? ReturnInvoiceNumber { get; set; }
    }
}
