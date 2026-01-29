using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagement.Models
{
    [Table("ReadingHistory")]
    public class ReadingHistory
    {
        [Key]
        public int HistoryId { get; set; }

        public int UserId { get; set; }

        public int BookId { get; set; }

        public DateTime ViewDate { get; set; } = DateTime.Now;

        public int? ViewDuration { get; set; } // Thời gian xem (giây)

        public bool IsCompleted { get; set; } = false;

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual User? User { get; set; }

        [ForeignKey("BookId")]
        public virtual Book? Book { get; set; }
    }
}