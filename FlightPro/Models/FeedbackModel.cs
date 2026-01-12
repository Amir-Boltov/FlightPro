using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FlightPro.Models 
{
    public class Feedback
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }


        [Range(1, 5)]
        public int Rating { get; set; }

        public string Comment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey("BookingId")]
        public virtual myBook Booking { get; set; }// בשביל שנקשר אותו להזמנה 
    }
}