namespace FlightPro.Models
{
    public class PackageModel
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public decimal? DiscountedPrice { get; set; }
        public string Category { get; set; }
        public int AvailableRooms { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string MainImageUrl { get; set; }
        public string Destination { get; set; }
        public string Country { get; set; }

        public decimal FinalPrice => DiscountedPrice.HasValue ? DiscountedPrice.Value : Price;
    }
}