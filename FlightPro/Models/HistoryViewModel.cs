using System;

namespace FlightPro.Models
{
    public class HistoryViewModel
    {
        public int BookingId { get; set; }      // מזהה ההזמנה (בשביל הביטול)
        public string Title { get; set; }       // שם החבילה
        public string Destination { get; set; } // יעד
        public string ImageUrl { get; set; }    // תמונה
        public DateTime StartDate { get; set; } // תאריך התחלה
        public DateTime EndDate { get; set; }   // תאריך סיום
        public decimal TotalPrice { get; set; } // כמה שולם
        public string Status { get; set; }      // הסטטוס: Confirmed או Canceled

        // זה שדה מיוחד שעוזר לנו לדעת אם הטיול בעתיד או בעבר
        public bool IsUpcoming => StartDate > DateTime.Now;
    }
}
