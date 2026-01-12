namespace FlightPro.Models
{
    public class AdminWaitlistViewModel
    {
        public int PackageDateId { get; set; }
        public string PackageTitle { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<WaitlistEntry> Entries { get; set; } = new List<WaitlistEntry>();
    }

    public class WaitlistEntry
    {
        public int WaitlistId { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string UserEmail { get; set; }
        public DateTime JoinedAt { get; set; }
        public int RequestedAmount { get; set; }
        public bool IsNotified { get; set; }
    }
}