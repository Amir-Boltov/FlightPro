namespace FlightPro.Models
{
    public class CheckoutViewModel
    {
        // הסכום הכולל לתשלום (לתצוגה בלבד)
        public decimal TotalAmount { get; set; }

        // כמות הפריטים בסל
        public int ItemCount { get; set; }
        public int? DirectBookingId { get; set; }
        public List<myBook> Items { get; set; } = new List<myBook>();
    }
}