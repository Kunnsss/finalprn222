using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagement.Models
{
    [Table("EventRegistrations")]
    public class EventRegistration
    {
        [Key]
        public int RegistrationId { get; set; }

        public int EventId { get; set; }

        public int UserId { get; set; }

        public DateTime RegistrationDate { get; set; } = DateTime.Now;

        [MaxLength(20)]
        public string Status { get; set; } = "Registered"; // Registered, Attended, Cancelled

        // Navigation properties
        [ForeignKey("EventId")]
        public virtual LibraryEvent? Event { get; set; }

        [ForeignKey("UserId")]
        public virtual User? User { get; set; }
    }
}