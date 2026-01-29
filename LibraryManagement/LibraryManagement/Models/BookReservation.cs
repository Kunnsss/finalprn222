// Models/BookReservation.cs 
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagement.Models
{
    public class BookReservation
    {
        [Key]
        public int ReservationId { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        public int BookId { get; set; }
        [ForeignKey("BookId")]
        public virtual Book Book { get; set; }

        public DateTime ReservationDate { get; set; } = DateTime.Now;
        public DateTime ExpiryDate { get; set; }
        public DateTime? NotifiedDate { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Ready, Completed, Cancelled, Expired

        public int Priority { get; set; } // Thứ tự ưu tiên
    }
}