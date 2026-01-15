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
        // ????? 1: ?????? ?????
        List<PackageModel> hotDeals = new List<PackageModel>();
        // ????? 2: ????????
        List<PackageReviewModel> allReviews = new List<PackageReviewModel>();

        string connectionString = _configuration.GetConnectionString("myConnect");

        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();

            // --- ?????? 1: ????? ?????? (Hot Deals) ---
            string dealsSql = @"
            SELECT TOP 3 p.Id, p.Title, d.Name as City, d.Country, 
                   pd.Price, pd.DiscountedPrice, pi.Url
            FROM Packages p
            JOIN Destinations d ON p.DestinationId = d.Id
            JOIN PackageDates pd ON p.Id = pd.PackageId
            OUTER APPLY (SELECT TOP 1 Url FROM PackageImages WHERE PackageId = p.Id AND IsPrimary = 1) pi
            WHERE pd.DiscountedPrice IS NOT NULL AND pd.StartDate > GETDATE()
            ORDER BY pd.StartDate ASC";

            using (SqlCommand cmd = new SqlCommand(dealsSql, conn))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        hotDeals.Add(new PackageModel
                        {
                            Id = (int)reader["Id"],
                            Title = reader["Title"].ToString(),
                            Destination = reader["City"].ToString(),
                            Country = reader["Country"].ToString(),
                            MainImageUrl = reader["Url"] != DBNull.Value ? reader["Url"].ToString() : "/img/default.jpg",
                            DisplayPrice = (decimal)reader["Price"],
                            DisplayDiscountedPrice = (decimal)reader["DiscountedPrice"]
                        });
                    }
                }
            }

            // --- ?????? 2: ????? ?? ???????? (Reviews) ---
            // ????? ?? ?-6 ????????
            string reviewsSql = @"
            SELECT TOP 6 r.UserName, r.Rating, r.Comment, r.Date
            FROM SiteReviews r
            ORDER BY r.Date DESC";

            using (SqlCommand cmd = new SqlCommand(reviewsSql, conn))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        allReviews.Add(new PackageReviewModel
                        {
                            UserName = reader["UserName"].ToString(),
                            Rating = (int)reader["Rating"],
                            Comment = reader["Comment"].ToString(),
                            Date = (DateTime)reader["Date"]
                        });
                    }
                }
            }
        }

        // ????? ?-Tuple (?????? ???????)
        // Item1 = hotDeals
        // Item2 = allReviews
        var model = Tuple.Create(hotDeals, allReviews);

        return View(model);
    }
}