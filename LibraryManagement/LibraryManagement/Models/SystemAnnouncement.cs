using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagement.Models
{
    [Table("SystemAnnouncements")]
    public class SystemAnnouncement
    {
        [Key]
        public int AnnouncementId { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(1000)]
        public string Content { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Type { get; set; } = "Info"; // Info, Warning, Alert

        public int CreatedBy { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? ExpiryDate { get; set; }

        public bool IsActive { get; set; } = true;

        // Navigation properties
        [ForeignKey("CreatedBy")]
        public virtual User? Creator { get; set; }
    }
}