namespace FlightPro.Models
{
    public class MyWaitingListItem
    {
        public int Id { get; set; } // The ID from the WaitingList table
        public string Title { get; set; }
        public string MainImageUrl { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int RequestedAmount { get; set; }
        public DateTime JoinedAt { get; set; }
        public bool IsNotified { get; set; }
    }
}