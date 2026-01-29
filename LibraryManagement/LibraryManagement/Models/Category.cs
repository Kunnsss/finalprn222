// Models/Category.cs
using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Models
{
    public class Category
    {
        [Key]
        public int CategoryId { get; set; }

        [Required]
        [StringLength(100)]
        public string CategoryName { get; set; }

        [StringLength(255)]
        public string? Description { get; set; }

        public virtual ICollection<Book>? Books { get; set; }
    }
}