using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.ViewModels
{
    // ViewModels/RentalViewModel.cs
    public class RentalViewModel
    {
        public int BookId { get; set; }
        public string BookTitle { get; set; }
        public decimal RentalPrice { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn số ngày thuê")]
        [Range(1, 90, ErrorMessage = "Số ngày thuê từ 1 đến 90 ngày")]
        public int RentalDays { get; set; }

        public DateTime DueDate => DateTime.Now.AddDays(RentalDays);
        public decimal TotalAmount => RentalPrice * RentalDays;
    }
}
