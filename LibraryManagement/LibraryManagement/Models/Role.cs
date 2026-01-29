// Models/Role.cs
using LibraryManagement.Models;
using System.ComponentModel.DataAnnotations;

namespace LibraryManagement.Models
{
    public class Role
    {
        [Key]
        public int RoleId { get; set; }

        [Required]
        [StringLength(50)]
        public string RoleName { get; set; }

        [StringLength(200)]
        public string? Description { get; set; }

        public virtual ICollection<User> Users { get; set; }
    }
}