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
    public IActionResult Edit(int id)
    {
        // 1. Security Check
        if (HttpContext.Session.GetString("UserRole") != "Admin")
        {
            return RedirectToAction("Index", "Home");
        }

        PackageModel package = null;
        string connectionString = _configuration.GetConnectionString("myConnect");

        using (var conn = new SqlConnection(connectionString))
        {
            conn.Open();
            // Fetch existing data to fill the boxes
            string sql = @"SELECT p.*, pi.Url as MainImageUrl 
                       FROM Packages p 
                       LEFT JOIN PackageImages pi ON pi.PackageId = p.Id AND pi.IsPrimary = 1
                       WHERE p.Id = @id";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        package = new PackageModel
                        {
                            Id = (int)reader["Id"],
                            Title = reader["Title"].ToString(),
                            Description = reader["Description"].ToString(),
                            Price = (decimal)reader["Price"],
                            DiscountedPrice = reader["DiscountedPrice"] as decimal?,
                            AvailableRooms = (int)reader["AvailableRooms"],
                            StartDate = (DateTime)reader["StartDate"],
                            EndDate = (DateTime)reader["EndDate"],
                            MainImageUrl = reader["MainImageUrl"]?.ToString() // Load existing URL
                        };
                    }
                }
            }
        }
        return View(package);
    }
    [HttpPost]
    public IActionResult Edit(PackageModel model)
    {
        if (HttpContext.Session.GetString("UserRole") != "Admin") return RedirectToAction("Index");

        string connectionString = _configuration.GetConnectionString("myConnect");

        using (var conn = new SqlConnection(connectionString))
        {
            conn.Open();
            string sql = @"
            UPDATE Packages 
            SET Title = @Title, 
                Description = @Description, 
                Price = @Price, 
                DiscountedPrice = @DiscountedPrice,
                AvailableRooms = @AvailableRooms,
                StartDate = @StartDate,
                EndDate = @EndDate
            WHERE Id = @Id;

            -- Update Image URL if provided
            UPDATE PackageImages SET Url = @Url WHERE PackageId = @Id AND IsPrimary = 1;
            ";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", model.Id);
                cmd.Parameters.AddWithValue("@Title", model.Title);
                cmd.Parameters.AddWithValue("@Description", model.Description);
                cmd.Parameters.AddWithValue("@Price", model.Price);
                // Handle Nullable Discount
                cmd.Parameters.AddWithValue("@DiscountedPrice", model.DiscountedPrice ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@AvailableRooms", model.AvailableRooms);
                cmd.Parameters.AddWithValue("@StartDate", model.StartDate);
                cmd.Parameters.AddWithValue("@EndDate", model.EndDate);
                cmd.Parameters.AddWithValue("@Url", model.MainImageUrl);

                cmd.ExecuteNonQuery();
            }
        }

        return RedirectToAction("Index");
    }
    [HttpPost]
    public IActionResult Delete(int id)
    {
        if (HttpContext.Session.GetString("UserRole") != "Admin") return RedirectToAction("Index");

        string connectionString = _configuration.GetConnectionString("myConnect");

        using (var conn = new SqlConnection(connectionString))
        {
            conn.Open();

            // We must delete Child records (Images, WaitingList) first to avoid SQL Errors!
            // Note: If there are Bookings, this might fail unless you delete those too.
            string sql = @"
            DELETE FROM PackageImages WHERE PackageId = @id;
            DELETE FROM WaitingList WHERE PackageId = @id;
            DELETE FROM Packages WHERE Id = @id;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (SqlException ex)
                {
                    // Error 547 = Foreign Key Conflict (likely has Bookings)
                    if (ex.Number == 547)
                    {
                        TempData["Error"] = "Cannot delete this trip because users have already booked it!";
                    }
                }
            }
        }

        return RedirectToAction("Index");
    }
    public IActionResult Details(int id)
    {
        PackageModel package = null;
        string connectionString = _configuration.GetConnectionString("myConnect");

        using (var conn = new SqlConnection(connectionString))
        {
            conn.Open();
            string sql = @"
            SELECT p.Id, p.Title, p.Description, p.Price, p.DiscountedPrice, 
                   p.Category, p.AvailableRooms, p.TotalRooms, 
                   p.StartDate, p.EndDate, p.CancellationDeadlineDays,
                   d.Name AS Destination, d.Country,
                   ISNULL(pi.Url, '/images/default-placeholder.jpg') AS MainImageUrl
            FROM Packages p
            INNER JOIN Destinations d ON p.DestinationId = d.Id
            LEFT JOIN PackageImages pi ON pi.PackageId = p.Id AND pi.IsPrimary = 1
            WHERE p.Id = @id"; // <--- Vital: Select only ONE trip

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        package = new PackageModel
                        {
                            Id = (int)reader["Id"],
                            Title = reader["Title"].ToString(),
                            Description = reader["Description"].ToString(),
                            Price = (decimal)reader["Price"],
                            DiscountedPrice = reader["DiscountedPrice"] as decimal?,
                            Category = reader["Category"].ToString(),
                            AvailableRooms = (int)reader["AvailableRooms"],
                            StartDate = (DateTime)reader["StartDate"],
                            EndDate = (DateTime)reader["EndDate"],
                            Destination = reader["Destination"].ToString(),
                            Country = reader["Country"].ToString(),
                            MainImageUrl = reader["MainImageUrl"].ToString()
                            // If you added CancellationDeadlineDays to your model, map it here too
                        };
                    }
                }
            }
        }

        if (package == null)
        {
            return NotFound(); // Returns a 404 if ID doesn't exist
        }

        return View(package);
    }

    public IActionResult Index(string search)
    {
        var packages = new List<PackageModel>();
        string connectionString = _configuration.GetConnectionString("myConnect");

        using (var conn = new SqlConnection(connectionString))
        {
            conn.Open();

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
