using System;
using System.Collections.Generic;

namespace FlightPro.Models
{
    public class UserDetailsViewModel
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Role { get; set; } = "";
        public string Status { get; set; } = "";

        public List<UserBookingModel> BookingHistory { get; set; } = new List<UserBookingModel>();
    }

    public class UserBookingModel
    {
        public int BookingId { get; set; }
        public int PackageId { get; set; }
        public string PackageTitle { get; set; } = "";
        public DateTime TripDate { get; set; }
        public DateTime BookedAt { get; set; }
        public decimal PricePaid { get; set; }
        public string Status { get; set; } = "";
    }
}