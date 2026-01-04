namespace FlightPro.Models
{
    public class UserViewModel
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Role { get; set; } = "";   // "Admin" or "Customer"
        public string Status { get; set; } = ""; // e.g., "Active", "Suspended"

        // Helper to show full name in views easily
        public string FullName => $"{FirstName} {LastName}";
    }
}