using FlightPro.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Linq;

public class TripsController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly WaitlistService _waitlistService;
    private readonly EmailService _emailService;

    public TripsController(IConfiguration configuration, WaitlistService waitlistService, EmailService emailService)
    {
        _configuration = configuration;
        _waitlistService = waitlistService;
        _emailService = emailService;
    }

    public IActionResult Index(
        string searchQuery,
        string country,
        string city,
        string category,
        bool onlyDiscounted,
        decimal? minPrice,
        decimal? maxPrice,
        DateTime? startDate,
        DateTime? endDate,
        string sortOrder)
    {
        var viewModel = new TripsIndexViewModel
        {
            SearchQuery = searchQuery,
            SelectedCountry = country,
            SelectedCity = city,
            SelectedCategory = category,
            OnlyDiscounted = onlyDiscounted,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            StartDate = startDate,
            EndDate = endDate,
            SortOrder = sortOrder
        };

        string connectionString = _configuration.GetConnectionString("myConnect");

        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();

            // 1. Fetch distinct values for Dropdowns
            viewModel.AllCountries = GetDistinctColumn(conn, "Destinations", "Country");
            viewModel.AllCities = GetDistinctColumn(conn, "Destinations", "Name");
            viewModel.AllCategories = GetDistinctColumn(conn, "Packages", "Category");

            // 2. Build the Main Query
            string sql = @"
                SELECT 
                    p.Id, p.Title, p.Description, p.Category, p.MinAge,
                    d.Name AS CityName, d.Country AS CountryName,
                    pi.Url AS ImageUrl,
                    pd.StartDate, pd.EndDate, 
                    pd.Price, 
                    
                    -- LOGIC: If discount expired, return NULL
                    CASE 
                        WHEN pd.DiscountEndDate < CAST(GETDATE() AS DATE) THEN NULL 
                        ELSE pd.DiscountedPrice 
                    END AS DiscountedPrice,

                    pd.DiscountEndDate, pd.AvailableRooms,
                    (SELECT COUNT(*) FROM Bookings b WHERE b.PackageDateId = pd.Id) as BookingCount,
                    
                    -- LOGIC: Calculate effective price checking expiry
                    COALESCE(
                        CASE 
                            WHEN pd.DiscountEndDate < CAST(GETDATE() AS DATE) THEN NULL 
                            ELSE pd.DiscountedPrice 
                        END, 
                        pd.Price
                    ) as EffectivePrice

                FROM Packages p
                JOIN Destinations d ON p.DestinationId = d.Id
                JOIN PackageDates pd ON p.Id = pd.PackageId
                OUTER APPLY (SELECT TOP 1 Url FROM PackageImages WHERE PackageId = p.Id AND IsPrimary = 1) pi
                
                -- STRICT FILTERING: Hide if booking closed OR trip started
                WHERE COALESCE(pd.BookingEndDate, pd.StartDate) >= CAST(GETDATE() AS DATE)";

            // 3. Apply Filters Dynamically
            var parameters = new List<SqlParameter>();

            if (!string.IsNullOrEmpty(searchQuery))
            {
                sql += " AND p.Title LIKE @SearchQuery ";
                parameters.Add(new SqlParameter("@SearchQuery", "%" + searchQuery + "%"));
            }
            if (!string.IsNullOrEmpty(country))
            {
                sql += " AND d.Country = @Country ";
                parameters.Add(new SqlParameter("@Country", country));
            }
            if (!string.IsNullOrEmpty(city))
            {
                sql += " AND d.Name = @City ";
                parameters.Add(new SqlParameter("@City", city));
            }
            if (!string.IsNullOrEmpty(category))
            {
                sql += " AND p.Category = @Category ";
                parameters.Add(new SqlParameter("@Category", category));
            }
            if (onlyDiscounted)
            {
                // Only show if discount exists AND is not expired
                sql += " AND pd.DiscountedPrice IS NOT NULL AND (pd.DiscountEndDate IS NULL OR pd.DiscountEndDate >= CAST(GETDATE() AS DATE)) ";
            }
            if (minPrice.HasValue)
            {
                sql += " AND COALESCE(CASE WHEN pd.DiscountEndDate < CAST(GETDATE() AS DATE) THEN NULL ELSE pd.DiscountedPrice END, pd.Price) >= @MinPrice ";
                parameters.Add(new SqlParameter("@MinPrice", minPrice.Value));
            }
            if (maxPrice.HasValue)
            {
                sql += " AND COALESCE(CASE WHEN pd.DiscountEndDate < CAST(GETDATE() AS DATE) THEN NULL ELSE pd.DiscountedPrice END, pd.Price) <= @MaxPrice ";
                parameters.Add(new SqlParameter("@MaxPrice", maxPrice.Value));
            }
            if (startDate.HasValue)
            {
                sql += " AND pd.StartDate >= @StartDate ";
                parameters.Add(new SqlParameter("@StartDate", startDate.Value));
            }
            if (endDate.HasValue)
            {
                sql += " AND pd.EndDate <= @EndDate ";
                parameters.Add(new SqlParameter("@EndDate", endDate.Value));
            }

            // 4. Apply Sorting
            switch (sortOrder)
            {
                case "price_asc":
                    sql += " ORDER BY EffectivePrice ASC";
                    break;
                case "price_desc":
                    sql += " ORDER BY EffectivePrice DESC";
                    break;
                case "date_asc":
                    sql += " ORDER BY pd.StartDate ASC";
                    break;
                case "date_desc":
                    sql += " ORDER BY pd.StartDate DESC";
                    break;
                case "popularity":
                    sql += " ORDER BY BookingCount DESC";
                    break;
                case "category":
                    sql += " ORDER BY p.Category ASC";
                    break;
                default:
                    sql += " ORDER BY pd.StartDate ASC";
                    break;
            }

            // 5. Execute and Map
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddRange(parameters.ToArray());
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        viewModel.Trips.Add(new PackageModel
                        {
                            Id = (int)reader["Id"],
                            Title = reader["Title"].ToString(),
                            Description = reader["Description"].ToString(),
                            Category = reader["Category"].ToString(),
                            MinAge = (int)reader["MinAge"],
                            Destination = reader["CityName"].ToString(),
                            Country = reader["CountryName"].ToString(),
                            MainImageUrl = reader["ImageUrl"] != DBNull.Value ? reader["ImageUrl"].ToString() : "/img/default.jpg",
                            DisplayStartDate = (DateTime)reader["StartDate"],
                            DisplayEndDate = (DateTime)reader["EndDate"],
                            DisplayPrice = (decimal)reader["Price"],
                            DisplayDiscountedPrice = reader["DiscountedPrice"] != DBNull.Value ? (decimal?)reader["DiscountedPrice"] : null,
                            DisplayDiscountEndDate = reader["DiscountEndDate"] != DBNull.Value ? (DateTime?)reader["DiscountEndDate"] : null,
                            DisplayAvailableRooms = (int)reader["AvailableRooms"]
                        });
                    }
                }
            }
        }

        return View(viewModel);
    }

    public IActionResult Details(int id)
    {
        PackageModel package = null;
        string connectionString = _configuration.GetConnectionString("myConnect");
        _waitlistService.CleanupAndPromoteForPackage(id);

        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();

            // 1. Fetch the General Package Info
            string pkgSql = @"
            SELECT 
                p.Id, p.Title, p.Description, p.Category, p.MinAge, p.CancellationDeadlineDays,
                d.Name AS CityName, d.Country AS CountryName,
                pi.Url AS MainImageUrl
            FROM Packages p
            JOIN Destinations d ON p.DestinationId = d.Id
            OUTER APPLY (SELECT TOP 1 Url FROM PackageImages WHERE PackageId = p.Id AND IsPrimary = 1) pi
            WHERE p.Id = @Id";

            using (SqlCommand cmd = new SqlCommand(pkgSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        package = new PackageModel
                        {
                            Id = (int)reader["Id"],
                            Title = reader["Title"].ToString(),
                            Description = reader["Description"].ToString(),
                            Category = reader["Category"].ToString(),
                            MinAge = (int)reader["MinAge"],
                            CancellationDeadlineDays = (int)reader["CancellationDeadlineDays"],
                            Destination = reader["CityName"].ToString(),
                            Country = reader["CountryName"].ToString(),
                            MainImageUrl = reader["MainImageUrl"] != DBNull.Value ? reader["MainImageUrl"].ToString() : "/img/default.jpg",
                            AvailableSchedules = new List<PackageDateModel>()
                        };
                    }
                }
            }

            if (package == null) return NotFound();

            // Fetch extra images
            string imgSql = "SELECT TOP 3 Url FROM PackageImages WHERE PackageId = @Id AND IsPrimary = 0 ORDER BY Id";
            using (SqlCommand cmd = new SqlCommand(imgSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    int count = 0;
                    while (reader.Read())
                    {
                        string url = reader["Url"].ToString();
                        if (count == 0) package.ImageUrl2 = url;
                        else if (count == 1) package.ImageUrl3 = url;
                        else if (count == 2) package.ImageUrl4 = url;
                        count++;
                    }
                }
            }

            // 2. Fetch all Available Dates (Schedules) for this Package
            // UPDATED LOGIC: Filter by BookingEndDate exactly like the Index
            string dateSql = @"
            SELECT 
                Id, StartDate, EndDate, BookingEndDate, Price, AvailableRooms,
                DiscountEndDate,
                CASE 
                    WHEN DiscountEndDate < CAST(GETDATE() AS DATE) THEN NULL 
                    ELSE DiscountedPrice 
                END AS DiscountedPrice
            FROM PackageDates 
            WHERE PackageId = @Id 
            AND COALESCE(BookingEndDate, StartDate) >= CAST(GETDATE() AS DATE)
            ORDER BY StartDate";

            using (SqlCommand cmd = new SqlCommand(dateSql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        package.AvailableSchedules.Add(new PackageDateModel
                        {
                            Id = (int)reader["Id"],
                            StartDate = (DateTime)reader["StartDate"],
                            EndDate = (DateTime)reader["EndDate"],
                            BookingEndDate = reader["BookingEndDate"] != DBNull.Value ? (DateTime?)reader["BookingEndDate"] : null,
                            Price = (decimal)reader["Price"],
                            DiscountedPrice = reader["DiscountedPrice"] != DBNull.Value ? (decimal?)reader["DiscountedPrice"] : null,
                            DiscountEndDate = reader["DiscountEndDate"] != DBNull.Value ? (DateTime?)reader["DiscountEndDate"] : null,
                            AvailableRooms = (int)reader["AvailableRooms"]
                        });
                    }
                }
            }
        }

        return View(package);
    }

    private List<string> GetDistinctColumn(SqlConnection conn, string tableName, string columnName)
    {
        var list = new List<string>();
        string sql = $"SELECT DISTINCT {columnName} FROM {tableName} ORDER BY {columnName}";
        using (SqlCommand cmd = new SqlCommand(sql, conn))
        using (SqlDataReader reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                if (reader[0] != DBNull.Value) list.Add(reader[0].ToString());
            }
        }
        return list;
    }
}