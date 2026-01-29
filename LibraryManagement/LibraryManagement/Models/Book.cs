// Models/Book.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagement.Models
{
    public class Book
    {
        [Key]
        public int BookId { get; set; }

        [Required]
        [StringLength(255)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Author { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Publisher { get; set; }

        public int? PublishedYear { get; set; }

        public int? CategoryId { get; set; }

        [ForeignKey("CategoryId")]
        public virtual Category? Category { get; set; }

        [StringLength(50)]
        public string? ISBN { get; set; }

        public int Quantity { get; set; } = 0;

        public int AvailableQuantity { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        public decimal RentalPrice { get; set; }

        public bool IsPhysical { get; set; } = true;

        [StringLength(500)]
        public string? OnlineLink { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal? OnlinePrice { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        [StringLength(255)]
        public string? CoverImage { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public virtual ICollection<RentalTransaction>? RentalTransactions { get; set; }

        public virtual ICollection<OnlineRentalTransaction>? OnlineRentalTransactions { get; set; }
    }
}