using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Azure.Cosmos;

namespace FlightPro.Models
{
    public class Reviews
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        

        [Range(1, 5)]
        public int Rating { get; set; }

        [Required]
        public string Content { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey("UserId")]// קישור משתמש לביקורת
        public virtual User User { get; set; }
    }
}