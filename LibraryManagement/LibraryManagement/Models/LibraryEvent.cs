using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagement.Models
{
    [Table("LibraryEvents")]
    public class LibraryEvent
    {
        [Key]
        public int EventId { get; set; }

        [Required]
        [MaxLength(200)]
        public string EventName { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        public DateTime EventDate { get; set; }

        [MaxLength(200)]
        public string? Location { get; set; }

        public int? MaxParticipants { get; set; }

        public int CurrentParticipants { get; set; } = 0;

        public int CreatedBy { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public bool IsActive { get; set; } = true;

        // Navigation properties
        [ForeignKey("CreatedBy")]
        public virtual User? Creator { get; set; }
    }
}