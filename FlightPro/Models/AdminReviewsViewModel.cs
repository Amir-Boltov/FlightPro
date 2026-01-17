namespace FlightPro.Models
{
    public class AdminReviewsViewModel
    {
        public List<FlightPro.Models.SiteReviewModel> SiteReviews { get; set; } = new List<SiteReviewModel>();
        public List<FlightPro.Models.PackageReviewModel> PackageReviews { get; set; } = new List<PackageReviewModel>();
    }
}
