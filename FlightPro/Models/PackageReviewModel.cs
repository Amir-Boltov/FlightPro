using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FlightPro.Models 
{
    public class PackageReviewModel
    {
            public int Id { get; set; }

            public int UserId { get; set; } // אופציונלי: אם תרצה לדעת מי המשתמש מאחורי הקלעים

            public string UserName { get; set; } // השם שמוצג בתגובה

            public int PackageId { get; set; } // המזהה של החבילה

            public string PackageTitle { get; set; } // שם החבילה (מגיע מה-JOIN עם טבלת Packages)

            public string Comment { get; set; } // תוכן הביקורת

            public int Rating { get; set; } // דירוג (למשל 1-5)

            public DateTime Date { get; set; } // תאריך כתיבת הביקורת
        }
    }

