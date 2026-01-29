// Models/LateFeeConfig.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagement.Models
{
    public class LateFeeConfig
    {
        [Key]
        public int ConfigId { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal FeePerDay { get; set; } = 5000;

        [Column(TypeName = "decimal(10,2)")]
        public decimal? MaxLateFee { get; set; }

        [StringLength(255)]
        public string? Description { get; set; }
    }
}