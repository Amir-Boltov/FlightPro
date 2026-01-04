namespace FlightPro.Models
{
    public class PackageDateModel
    {
        public int Id { get; set; }
        public int PackageId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal Price { get; set; }
        public decimal? DiscountedPrice { get; set; }
        public int AvailableRooms { get; set; }
        public DateTime? DiscountEndDate { get; set; }
    }
}