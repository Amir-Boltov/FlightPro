using System;
using System.Collections.Generic;

namespace FlightPro.Models
{
    public class AdminDashboardViewModel
    {
        public int TotalBookings { get; set; }
        public decimal TotalRevenue { get; set; }
        public int ActivePackages { get; set; }
        public int UsersCount { get; set; }

        // Lists for specific management sections
        public List<BookingViewModel> RecentBookings { get; set; } = new List<BookingViewModel>();
        public List<PackageModel> Packages { get; set; } = new List<PackageModel>();
        public int WaitingListCount { get; set; }
        public List<string> ChartLabels { get; set; }  // תאריכים
        public List<decimal> ChartData { get; set; }
        public List<PackageModel> AllPackages { get; set; }

        // נתונים לגרף (שמות החבילות + מספר המכירות)
        public List<string> TopPackageNames { get; set; }
        public List<int> TopPackageSales { get; set; }
    }

    public class BookingViewModel
    {
        public int Id { get; set; }
        public string CustFirstName { get; set; }
        public string CustLastName { get; set; }
        public string PackageTitle { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal PricePaid { get; set; }

        public string Status { get; set; }
    }
}