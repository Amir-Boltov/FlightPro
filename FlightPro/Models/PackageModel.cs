using System.ComponentModel.DataAnnotations;

namespace FlightPro.Models
{
    public class PackageModel
    {
        // FIELDS THAT EXIST IN THE DATEBASE
        public int Id { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string MainImageUrl { get; set; }
        public string? ImageUrl2 { get; set; }
        public string? ImageUrl3 { get; set; }
        public string? ImageUrl4 { get; set; }
        public string? Destination { get; set; }
        public int MinAge { get; set; }
        public string? Country { get; set; }
        public int CancellationDeadlineDays { get; set; }

        [Display(Name = "Cart Hold Time (Minutes)")]
        [Required]
        [Range(1, 60, ErrorMessage = "Time must be between 1 and 60 minutes")]
        public int ExpiryMinutes { get; set; }

        // DISPLAY FIELDS ONLY
        public DateTime? DisplayStartDate { get; set; }
        public DateTime? DisplayEndDate { get; set; }
        public decimal DisplayPrice { get; set; }
        public decimal? DisplayDiscountedPrice { get; set; }
        public int DisplayAvailableRooms { get; set; }
        public DateTime? DisplayDiscountEndDate { get; set; }
        // בתוך הקובץ PackageModel.cs
        public List<PackageReviewModel> Reviews { get; set; } = new List<PackageReviewModel>();

        public List<PackageDateModel> AvailableSchedules { get; set; } = new List<PackageDateModel>();
    }
}