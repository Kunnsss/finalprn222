// Models/OnlineRentalTransaction.cs
using LibraryManagement.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagement.Models
{
    public class OnlineRentalTransaction
    {
        [Key]
        public int OnlineTransactionId { get; set; }

        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        public int BookId { get; set; }

        [ForeignKey("BookId")]
        public virtual Book Book { get; set; }

        public DateTime PurchaseDate { get; set; } = DateTime.Now;

        [Required]
        public DateTime ExpiryDate { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal Amount { get; set; }

        [StringLength(500)]
        public string? AccessLink { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Active"; // Active, Expired

        [StringLength(20)]
        public string PaymentStatus { get; set; } = "Pending"; // Pending, Paid, Cancelled

        public long? PayOSOrderCode { get; set; }

        [StringLength(100)]
        public string? PayOSPaymentLinkId { get; set; }
    }
}