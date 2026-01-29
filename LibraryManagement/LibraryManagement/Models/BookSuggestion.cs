using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagement.Models
{
    [Table("BookSuggestions")]
    public class BookSuggestion
    {
        [Key]
        public int SuggestionId { get; set; }

        public int UserId { get; set; }

        [Required]
        [MaxLength(255)]
        public string BookTitle { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Author { get; set; }

        [MaxLength(100)]
        public string? Publisher { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        public DateTime SuggestionDate { get; set; } = DateTime.Now;

        [MaxLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected

        [MaxLength(500)]
        public string? AdminNote { get; set; }

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual User? User { get; set; }
    }
}