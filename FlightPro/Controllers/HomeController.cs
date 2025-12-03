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
            // Fetch top 3 trips that have a discount, ordered by best price
            string sql = @"
                SELECT TOP 3 p.Id, p.Title, p.Price, p.DiscountedPrice, 
                       d.Name as Destination, d.Country, pi.Url as MainImageUrl
                FROM Packages p
                JOIN Destinations d ON p.DestinationId = d.Id
                LEFT JOIN PackageImages pi ON pi.PackageId = p.Id AND pi.IsPrimary = 1
                WHERE p.DiscountedPrice IS NOT NULL 
                AND p.DiscountEndDate > GETDATE()
                AND p.AvailableRooms > 0
                ORDER BY (p.Price - p.DiscountedPrice) DESC";

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
                            Price = (decimal)reader["Price"],
                            DiscountedPrice = reader["DiscountedPrice"] as decimal?,
                            Destination = reader["Destination"].ToString(),
                            Country = reader["Country"].ToString(),
                            MainImageUrl = reader["MainImageUrl"] != DBNull.Value ? reader["MainImageUrl"].ToString() : "/images/default.jpg"
                        });
                    }
                }
            }
        }
        return View(featuredPackages);
    }
}