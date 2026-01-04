using Microsoft.AspNetCore.Mvc;
using FlightPro.Models;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public class HomeController : Controller
{
    private readonly IConfiguration _configuration;

    public HomeController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IActionResult Index()
    {
        var featuredPackages = new List<PackageModel>();
        string connectionString = _configuration.GetConnectionString("myConnect");

        using (var conn = new SqlConnection(connectionString))
        {
            conn.Open();

            // UPDATED SQL:
            // 1. Join PackageDates (pd) to get the Price/Discount info
            // 2. Filter for Future Dates only
            // 3. Sort by the biggest cash saving (Price - DiscountedPrice)

            string sql = @"
            SELECT TOP 3 
                p.Id, 
                p.Title, 
                d.Name as Destination, 
                d.Country, 
                ISNULL(pi.Url, '/images/default.jpg') as MainImageUrl,
                pd.Price, 
                pd.DiscountedPrice,
                pd.StartDate
            FROM Packages p
            INNER JOIN PackageDates pd ON p.Id = pd.PackageId
            INNER JOIN Destinations d ON p.DestinationId = d.Id
            LEFT JOIN PackageImages pi ON pi.PackageId = p.Id AND pi.IsPrimary = 1
            WHERE pd.DiscountedPrice IS NOT NULL 
              AND pd.StartDate > GETDATE()
              AND pd.AvailableRooms > 0
            ORDER BY (pd.Price - pd.DiscountedPrice) DESC";

            using (var cmd = new SqlCommand(sql, conn))
            {
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        featuredPackages.Add(new PackageModel
                        {
                            Id = (int)reader["Id"],
                            Title = reader["Title"].ToString(),
                            Destination = reader["Destination"].ToString(),
                            Country = reader["Country"].ToString(),
                            MainImageUrl = reader["MainImageUrl"].ToString(),

                            // Map the specific deal we found to the Model properties
                            // If you are using the new 'DisplayPrice' property, use that:
                            DisplayPrice = (decimal)reader["Price"],

                            // If your View still uses 'DiscountedPrice', map it here:
                            DisplayDiscountedPrice = reader["DiscountedPrice"] as decimal?,

                            // Use DisplayDate to show which specific date this deal is for
                            DisplayStartDate = (DateTime)reader["StartDate"]
                        });
                    }
                }
            }
        }
        return View(featuredPackages);
    }
}