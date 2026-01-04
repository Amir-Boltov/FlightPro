using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlightPro.Models; // וודא שהמודלים האחרים שלך נמצאים בתיקייה הזו

public class myBook
{
    // --- שדות שקיימים בטבלה (המידע הגולמי) ---
    [Key]
    public int Id { get; set; }
    public int? UserId { get; set; }
    public int? PackageId { get; set; }
    public int? PackageDateId { get; set; }

    // שינינו מ-Adults ל-Amount לפי השיחה הקודמת
    public int? Amount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? TotalPrice { get; set; }

    [StringLength(100)]
    public string TransactionId { get; set; }
    public bool? IsPaid { get; set; }
    [StringLength(50)]
    public string Status { get; set; }
    public DateTime? CreatedAt { get; set; }

    // --- שדות וירטואליים לתצוגה בלבד (לא נשמרים ב-Bookings DB) ---

    [NotMapped]
    public PackageModel Package { get; set; } // יחזיק את הכותרת, תמונה ויעד

    [NotMapped]
    public PackageDateModel PackageDate { get; set; } // יחזיק את תאריכי ההתחלה והסיום
}