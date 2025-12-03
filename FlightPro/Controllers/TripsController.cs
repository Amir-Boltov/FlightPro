using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Data;
using FlightPro.Models;

public class TripsController : Controller
{
    private readonly IConfiguration _configuration;

    public TripsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IActionResult Index(string search)
    {
        var packages = new List<PackageModel>();
        string connectionString = _configuration.GetConnectionString("myConnect");

        using (var conn = new SqlConnection(connectionString))
        {
            conn.Open();

            // UPDATED SQL:
            // 1. Handles Discount Expiration Logic inside the query
            // 2. Handles NULL images with ISNULL
            string sql = @"
            SELECT p.Id, p.Title, p.Description, p.Price, 
                   
                   -- Logic: If discount expired, return NULL, otherwise return the discount
                   CASE 
                       WHEN p.DiscountEndDate < GETDATE() THEN NULL 
                       ELSE p.DiscountedPrice 
                   END AS ValidDiscountedPrice,

                   p.Category, p.AvailableRooms, p.StartDate, p.EndDate,
                   d.Name AS Destination, d.Country,
                   ISNULL(pi.Url, '/images/default-placeholder.jpg') AS MainImageUrl
            FROM Packages p
            INNER JOIN Destinations d ON p.DestinationId = d.Id
            LEFT JOIN PackageImages pi ON pi.PackageId = p.Id AND pi.IsPrimary = 1
            WHERE (@search IS NULL 
                    OR p.Title LIKE '%' + @search + '%' 
                    OR d.Name LIKE '%' + @search + '%'
                    OR d.Country LIKE '%' + @search + '%')
            ORDER BY p.StartDate";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@search", string.IsNullOrEmpty(search) ? (object)DBNull.Value : search);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        packages.Add(new PackageModel
                        {
                            Id = (int)reader["Id"],
                            Title = reader["Title"].ToString(),
                            Description = reader["Description"].ToString(),
                            Price = (decimal)reader["Price"],
                            // Read the VALIDATED discount from SQL
                            DiscountedPrice = reader["ValidDiscountedPrice"] as decimal?,
                            Category = reader["Category"].ToString(),
                            AvailableRooms = (int)reader["AvailableRooms"],
                            StartDate = (DateTime)reader["StartDate"],
                            EndDate = (DateTime)reader["EndDate"],
                            Destination = reader["Destination"].ToString(),
                            Country = reader["Country"].ToString(),
                            MainImageUrl = reader["MainImageUrl"].ToString()
                        });
                    }
                }
            }
        }

        ViewBag.Search = search;
        return View(packages);
    }
}
