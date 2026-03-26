using LibraryManagement.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagement.Models
{
    public class RentalTransaction
    {
        [Key]
        public int TransactionId { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        public int BookId { get; set; }
        [ForeignKey("BookId")]
        public virtual Book Book { get; set; }

        public DateTime RentalDate { get; set; } = DateTime.Now;

        [Required]
        public DateTime DueDate { get; set; }

        public DateTime? ReturnDate { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal RentalPrice { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal LateFee { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal TotalAmount { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Renting"; // Renting, Returned, Overdue, Lost, PendingPayment, Compensated, ...

        // PayOS - Initial / Extension payment
        public long? RentalPayOSOrderCode { get; set; }
        [StringLength(100)]
        public string? RentalPayOSLinkId { get; set; }
        [StringLength(20)]
        public string? RentalPaymentStatus { get; set; } = "Pending"; // Pending, Paid, Cancelled

        // PayOS - Late fee payment
        public long? LateFeePayOSOrderCode { get; set; }
        [StringLength(100)]
        public string? LateFeePayOSLinkId { get; set; }
        [StringLength(20)]
        public string? LateFeePaymentStatus { get; set; } // null, Pending, Paid, Cancelled

        // Calculated property - not mapped to database
        [NotMapped]
        public int RentalDays
        {
            get
            {
                if (ReturnDate.HasValue)
                {
                    // Nếu đã trả sách, tính số ngày từ ngày thuê đến ngày trả
                    return (ReturnDate.Value - RentalDate).Days;
                }
                else
                {
                    // Nếu chưa trả, tính số ngày từ ngày thuê đến hiện tại
                    return (DateTime.Now - RentalDate).Days;
                }
            }
        }
    }
}