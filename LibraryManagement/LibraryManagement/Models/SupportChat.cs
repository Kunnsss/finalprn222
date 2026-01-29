using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagement.Models
{
    // Models/SupportChat.cs - Chat hỗ trợ
    public class SupportChat
    {
        [Key]
        public int ChatId { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        public int? LibrarianId { get; set; }
        [ForeignKey("LibrarianId")]
        public virtual User? Librarian { get; set; }

        [Required]
        [StringLength(1000)]
        public string Message { get; set; }

        public DateTime SentDate { get; set; } = DateTime.Now;

        public bool IsFromUser { get; set; } = true; // true: user gửi, false: librarian gửi

        public bool IsRead { get; set; } = false;
    }
}
