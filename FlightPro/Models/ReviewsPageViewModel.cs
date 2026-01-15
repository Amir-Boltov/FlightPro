using System.Collections.Generic;

namespace FlightPro.Models
{
    public class ReviewsPageViewModel
    {
        // רשימה לביקורות כלליות
        public List<SiteReviewModel> GeneralReviews { get; set; }

        // רשימה לביקורות על חבילות
        public List<PackageReviewModel> PackageReviews { get; set; }

        // בנאי לאתחול הרשימות (מונע שגיאת Null Reference)
        public ReviewsPageViewModel()
        {
            GeneralReviews = new List<SiteReviewModel>();
            PackageReviews = new List<PackageReviewModel>();
        }
    }
}