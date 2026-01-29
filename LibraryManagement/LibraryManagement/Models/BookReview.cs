// Models/BookReview.cs - Model cho đánh giá sách
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagement.Models
{
    [Table("BookReviews")]
    public class BookReview
    {
        [Key]
        public int ReviewId { get; set; }

        [Required]
        public int BookId { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [Range(1, 5, ErrorMessage = "Rating phải từ 1 đến 5 sao")]
        public int Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }

        [Required]
        public DateTime ReviewDate { get; set; }

        public int HelpfulCount { get; set; } = 0;

        // Navigation Properties
        [ForeignKey("BookId")]
        public virtual Book Book { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; }
    }
}
