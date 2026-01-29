using LibraryManagement.Models;

namespace LibraryManagement.Models
{
    public class ReadingHistoryViewModel
    {
        public int BookId { get; set; }
        public Book? Book { get; set; }
        public DateTime ViewDate { get; set; }
        public int? ViewDuration { get; set; } // Thời gian xem (giây)
        public bool IsCompleted { get; set; }
        public string Source { get; set; } = string.Empty; // "ReadingHistory", "Online", "Physical"

        // Thông tin bổ sung từ OnlineRentalTransactions
        public DateTime? ExpiryDate { get; set; }

        // Thông tin bổ sung từ RentalTransactions
        public DateTime? DueDate { get; set; }
        public DateTime? ReturnDate { get; set; }

        // Trạng thái
        public string? Status { get; set; }
    }
}