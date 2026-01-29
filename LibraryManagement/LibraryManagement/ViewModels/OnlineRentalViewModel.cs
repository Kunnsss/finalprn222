using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.ViewModels
{
    // ViewModels/OnlineRentalViewModel.cs
    public class OnlineRentalViewModel
    {
        public int BookId { get; set; }
        public string BookTitle { get; set; }
        public decimal OnlinePrice { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn thời hạn truy cập")]
        [Range(1, 365, ErrorMessage = "Thời hạn từ 1 đến 365 ngày")]
        public int AccessDays { get; set; }

        public DateTime ExpiryDate => DateTime.Now.AddDays(AccessDays);
    }
}
