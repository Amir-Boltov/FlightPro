using System.Collections.Generic;

namespace FlightPro.Models
{
    public class TripsIndexViewModel
    {
        // The Results
        public List<PackageModel> Trips { get; set; } = new List<PackageModel>();

        // The Search/Filter Inputs (What the user typed/selected)
        public string SearchQuery { get; set; }
        public string SelectedCountry { get; set; }
        public string SelectedCity { get; set; }
        public string SelectedCategory { get; set; }
        public bool OnlyDiscounted { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string SortOrder { get; set; }

        // Data for the Dropdowns (So the user doesn't have to type "France")
        public List<string> AllCountries { get; set; } = new List<string>();
        public List<string> AllCities { get; set; } = new List<string>();
        public List<string> AllCategories { get; set; } = new List<string>();
    }
}