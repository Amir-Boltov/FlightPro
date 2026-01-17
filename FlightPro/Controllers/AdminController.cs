using FlightPro.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using FlightPro.Attributes;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Json;
using System.Globalization;



namespace FlightPro.Controllers
{
    [AdminOnly]
    public class AdminController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly EmailService _emailService;

        public AdminController(IConfiguration configuration, EmailService emailService)
        {
            _configuration = configuration;
            _emailService = emailService;
        }

        // GET: Admin Dashboard
        public IActionResult Index()
        {
            // 1. אתחול המודל והרשימות (חשוב מאוד כדי למנוע שגיאות Null)
            var model = new AdminDashboardViewModel
            {
                RecentBookings = new List<BookingViewModel>(),
                ChartLabels = new List<string>(),
                ChartData = new List<decimal>() // במודל שלך זה decimal
            };

            string connStr = _configuration.GetConnectionString("myConnect");

            try
            {
                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    conn.Open();

                    // --- חלק 1: סטטיסטיקות כלליות (הקוביות למעלה) ---
                    string statsSql = @"
                SELECT 
                    (SELECT COUNT(*) FROM Bookings) as TotalBookings,
                    (SELECT ISNULL(SUM(TotalPrice),0) FROM Bookings) as TotalRevenue,
                    (SELECT COUNT(*) FROM Packages) as ActivePackages,
                    (SELECT COUNT(*) FROM Users) as UsersCount";

                    using (SqlCommand cmd = new SqlCommand(statsSql, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            model.TotalBookings = Convert.ToInt32(reader["TotalBookings"]);
                            model.TotalRevenue = Convert.ToDecimal(reader["TotalRevenue"]);
                            model.ActivePackages = Convert.ToInt32(reader["ActivePackages"]);
                            model.UsersCount = Convert.ToInt32(reader["UsersCount"]);
                        }
                    }

                    // --- חלק 2: טבלה (הזמנות אחרונות) ---
                    string bookingSql = @"
                SELECT TOP 10 b.Id, b.CreatedAt, b.TotalPrice, u.FirstName, u.LastName, p.Title, b.Status
                FROM Bookings b
                JOIN Users u ON b.UserId = u.Id
                JOIN PackageDates pd ON b.PackageDateId = pd.Id
                JOIN Packages p ON pd.PackageId = p.Id
                ORDER BY b.CreatedAt DESC";

                    using (SqlCommand cmd = new SqlCommand(bookingSql, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            model.RecentBookings.Add(new BookingViewModel
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                CreatedAt = Convert.ToDateTime(reader["CreatedAt"]),
                                PricePaid = Convert.ToDecimal(reader["TotalPrice"]),
                                CustFirstName = reader["FirstName"].ToString(),
                                CustLastName = reader["LastName"].ToString(),
                                PackageTitle = reader["Title"].ToString(),
                                Status = reader["Status"].ToString()
                            });
                        }
                    }

                    string chartSql = @"
                SELECT TOP 5 p.Title, COUNT(b.Id) as BookingCount
                FROM Bookings b
                JOIN PackageDates pd ON b.PackageDateId = pd.Id
                JOIN Packages p ON pd.PackageId = p.Id
                GROUP BY p.Title
                ORDER BY BookingCount DESC";

                    using (SqlCommand cmd = new SqlCommand(chartSql, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // 1. שם החבילה הולך ל-Labels
                            model.ChartLabels.Add(reader["Title"].ToString());

                            // 2. הכמות הולכת ל-Data
                            // בגלל שבמודל הגדרת decimal, אנחנו ממירים את המספר ל-decimal
                            int count = Convert.ToInt32(reader["BookingCount"]);
                            model.ChartData.Add((decimal)count);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in Admin Dashboard: " + ex.Message);
            }

            return View(model);
        }
        public IActionResult Packages()
        {
            // 1. יצירת המודל המאוחד (שים לב לשם המחלקה שיצרנו קודם)
            var viewModel = new AdminDashboardViewModel
            {
                AllPackages = new List<PackageModel>(),
                TopPackageNames = new List<string>(),
                TopPackageSales = new List<int>()
            };

            string connStr = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // --- חלק א': שליפת כל החבילות לטבלה ---
                string sqlPackages = @"
            SELECT p.Id, p.Title, p.Category, p.MinAge, d.Name as City, d.Country
            FROM Packages p
            JOIN Destinations d ON p.DestinationId = d.Id";

                using (SqlCommand cmd = new SqlCommand(sqlPackages, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        viewModel.AllPackages.Add(new PackageModel
                        {
                            Id = (int)reader["Id"],
                            Title = reader["Title"].ToString(),
                            Category = reader["Category"].ToString(),
                            MinAge = (int)reader["MinAge"],
                            Destination = reader["City"].ToString(),
                            Country = reader["Country"].ToString()
                        });
                    }
                }

                // --- חלק ב': שליפת נתונים לגרף (5 החבילות הנמכרות ביותר) ---
                // אנו עושים JOIN בין Bookings -> PackageDates -> Packages כדי לקבל את השם
                string sqlTop = @"
            SELECT TOP 5 p.Title, COUNT(b.Id) as SalesCount
            FROM Bookings b
            JOIN PackageDates pd ON b.PackageDateId = pd.Id
            JOIN Packages p ON pd.PackageId = p.Id
            GROUP BY p.Title
            ORDER BY SalesCount DESC";

                using (SqlCommand cmd = new SqlCommand(sqlTop, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // הוספת הנתונים לרשימות של הגרף
                        viewModel.TopPackageNames.Add(reader["Title"].ToString());
                        viewModel.TopPackageSales.Add((int)reader["SalesCount"]);
                    }
                }
            }

            return View(viewModel);
        }
        [HttpGet]
        public IActionResult CreatePackage()
        {
            ViewBag.DestinationsJson = GetDestinationsJson(); // Reuse the helper from previous step
            return View(new PackageModel());
        }

        // POST: Process logic
        [HttpPost]
        public IActionResult CreatePackage(PackageModel model, string CountryName, string CityName)
        {
            // 1. Basic Model Validation (Excluding DestinationId since we calculate it manually)
            if (!ModelState.IsValid)
            {
                // If Model is invalid, reload the list and return view
                ViewBag.DestinationsJson = GetDestinationsJson();
                return View(model);
            }

            string connStr = _configuration.GetConnectionString("myConnect");
            int destinationId = 0;
            int newPackageId = 0;

            // Standardize input (e.g., "paris" becomes "Paris")
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            CountryName = textInfo.ToTitleCase(CountryName.Trim());
            CityName = textInfo.ToTitleCase(CityName.Trim());

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // ---------------------------------------------------------
                // 2. HANDLE DESTINATION (Find Existing OR Create New)
                // ---------------------------------------------------------

                // Check if this Country+City combo already exists
                string checkDestSql = "SELECT Id FROM Destinations WHERE Country = @C AND Name = @N";
                using (SqlCommand cmd = new SqlCommand(checkDestSql, conn))
                {
                    cmd.Parameters.AddWithValue("@C", CountryName);
                    cmd.Parameters.AddWithValue("@N", CityName);
                    object result = cmd.ExecuteScalar();

                    if (result != null)
                    {
                        // Found existing ID
                        destinationId = (int)result;
                    }
                    else
                    {
                        string insertDestSql = "INSERT INTO Destinations (Country, Name) VALUES (@C, @N); SELECT CAST(scope_identity() AS int);";
                        using (SqlCommand insertCmd = new SqlCommand(insertDestSql, conn))
                        {
                            insertCmd.Parameters.AddWithValue("@C", CountryName);
                            insertCmd.Parameters.AddWithValue("@N", CityName);
                            destinationId = (int)insertCmd.ExecuteScalar();
                        }
                    }
                }

                // ---------------------------------------------------------
                // 3. INSERT PACKAGE
                // ---------------------------------------------------------
                string pkgSql = @"
            INSERT INTO Packages (Title, Description, Category, DestinationId, MinAge, CancellationDeadlineDays, ExpiryMinutes) 
            VALUES (@Title, @Desc, @Cat, @DestId, @Age, @Cancel, @Expiry);
            SELECT CAST(scope_identity() AS int);";

                using (SqlCommand cmd = new SqlCommand(pkgSql, conn))
                {
                    cmd.Parameters.AddWithValue("@Title", model.Title);
                    cmd.Parameters.AddWithValue("@Desc", model.Description);
                    cmd.Parameters.AddWithValue("@Cat", model.Category);
                    cmd.Parameters.AddWithValue("@DestId", destinationId); // Use the ID we found or created
                    cmd.Parameters.AddWithValue("@Age", model.MinAge);
                    cmd.Parameters.AddWithValue("@Cancel", model.CancellationDeadlineDays);
                    cmd.Parameters.AddWithValue("@Expiry", model.ExpiryMinutes);

                    newPackageId = (int)cmd.ExecuteScalar();
                }

                // ---------------------------------------------------------
                // 4. INSERT IMAGES
                // ---------------------------------------------------------
                void InsertImage(string url, bool isPrimary)
                {
                    if (!string.IsNullOrEmpty(url))
                    {
                        string imgSql = "INSERT INTO PackageImages (PackageId, Url, IsPrimary) VALUES (@Pid, @Url, @IsPrim)";
                        using (SqlCommand cmd = new SqlCommand(imgSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@Pid", newPackageId);
                            cmd.Parameters.AddWithValue("@Url", url);
                            cmd.Parameters.AddWithValue("@IsPrim", isPrimary);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                InsertImage(model.MainImageUrl, true);
                InsertImage(model.ImageUrl2, false);
                InsertImage(model.ImageUrl3, false);
                InsertImage(model.ImageUrl4, false);
            }

            TempData["Success"] = "Package created successfully!";
            return RedirectToAction("EditPackage", new { id = newPackageId });
        }

        private string GetDestinationsJson()
        {
            var list = new List<object>();
            string connStr = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                string sql = "SELECT Id, Name, Country FROM Destinations ORDER BY Country, Name";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new { Id = reader["Id"], Name = reader["Name"], Country = reader["Country"] });
                    }
                }
            }
            return JsonSerializer.Serialize(list);
        }

        // POST: Delete Package
        [HttpPost]
        public IActionResult DeletePackage(int id)
        {
            string connStr = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                // Note: Real deletion logic needs to handle foreign keys (Bookings/Images)
                string sql = "DELETE FROM Packages WHERE Id = @Id";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    try { cmd.ExecuteNonQuery(); }
                    catch { /* Handle FK constraint errors here */ }
                }
            }
            return RedirectToAction("Packages");
        }
        /*
        public IActionResult Users()
        {
            // Similar logic to fetch AspNetUsers table
            return View();
        }
        */
        [HttpPost]
        public IActionResult SendReminders()
        {
            // Logic to find trips starting in 5 days and send emails
            TempData["Message"] = "Reminders sent successfully to upcoming travelers.";
            return RedirectToAction("Index");
        }
        // GET: Show the form to add a date
        [HttpGet]
        public IActionResult AddPackageDate(int packageId)
        {
            // We pass the ID to the view so we know which package we are adding dates to
            ViewBag.PackageId = packageId;
            return View();
        }

        // POST: Save the new date
        [HttpPost]
        public IActionResult AddPackageDate(int packageId, DateTime startDate, DateTime endDate,DateTime bookingEndDate, decimal price, int totalRooms)
        {
            if (startDate < DateTime.Now)
            {
                ModelState.AddModelError("", "Start date must be in the future.");
                ViewBag.PackageId = packageId;
                return View();
            }
            if (endDate < startDate)
            {
                ModelState.AddModelError("endDate", "End date cannot be before start date.");
                ViewBag.PackageId = packageId;
                return View();
            }
            if (bookingEndDate > startDate)
            {
                ModelState.AddModelError("bookingEndDate", "Booking deadline cannot be after the trip starts.");
            }
            if (bookingEndDate < DateTime.Now.Date)
            {
                ModelState.AddModelError("bookingEndDate", "Booking deadline cannot be in the past.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.PackageId = packageId;
                return View();
            }
            string connStr = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                // Updated SQL to include BookingEndDate
                string sql = @"
            INSERT INTO PackageDates 
            (PackageId, StartDate, EndDate, BookingEndDate, Price, TotalRooms, AvailableRooms)
            VALUES 
            (@Pid, @Start, @End, @BookEnd, @Price, @Total, @Total)";
                // Note: AvailableRooms starts equal to TotalRooms

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Pid", packageId);
                    cmd.Parameters.AddWithValue("@Start", startDate);
                    cmd.Parameters.AddWithValue("@End", endDate);

                    // New Parameter
                    cmd.Parameters.AddWithValue("@BookEnd", bookingEndDate);

                    cmd.Parameters.AddWithValue("@Price", price);
                    cmd.Parameters.AddWithValue("@Total", totalRooms);
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["Success"] = "New schedule added successfully.";
            return RedirectToAction("EditPackage", new { id = packageId });
        }
        // ==========================================
        // PART 1: EDIT MAIN PACKAGE DETAILS
        // ==========================================

        // GET: Edit Package
        [HttpGet]
        public IActionResult EditPackage(int id)
        {
            PackageModel package = null;
            var categoryList = new List<string>();
            string connStr = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // 1. Fetch Basic Package Info
                string sql = "SELECT * FROM Packages WHERE Id = @Id";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
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
                                ExpiryMinutes = (int)reader["ExpiryMinutes"],
                                // IMPORTANT: Initialize the list so we can add to it later
                                AvailableSchedules = new List<PackageDateModel>()
                            };
                        }
                    }
                }

                if (package == null) return NotFound();

                // 2. Fetch Images (Map to properties)
                string imgSql = "SELECT Url, IsPrimary FROM PackageImages WHERE PackageId = @Id ORDER BY IsPrimary DESC";
                using (SqlCommand cmd = new SqlCommand(imgSql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        List<string> extras = new List<string>();
                        while (reader.Read())
                        {
                            string url = reader["Url"].ToString();
                            if ((bool)reader["IsPrimary"]) package.MainImageUrl = url;
                            else extras.Add(url);
                        }
                        // Assign extras
                        if (extras.Count > 0) package.ImageUrl2 = extras[0];
                        if (extras.Count > 1) package.ImageUrl3 = extras[1];
                        if (extras.Count > 2) package.ImageUrl4 = extras[2];
                    }
                }

                // 3. FETCH SCHEDULES (The part you were missing)
                string dateSql = @"
            SELECT Id, StartDate, EndDate, Price, DiscountedPrice, DiscountEndDate
            FROM PackageDates 
            WHERE PackageId = @Id 
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
                                Price = (decimal)reader["Price"],
                                DiscountedPrice = reader["DiscountedPrice"] as decimal?,
                                DiscountEndDate = reader["DiscountEndDate"] as DateTime?
                            });
                        }
                    }
                }
                string catSql = "SELECT DISTINCT Category FROM Packages WHERE Category IS NOT NULL AND Category <> '' ORDER BY Category";

                using (SqlCommand cmd = new SqlCommand(catSql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        categoryList.Add(reader["Category"].ToString());
                    }
                }
            }
            ViewBag.Categories = categoryList;
            return View(package);
        }
        // POST: Save Package and Images
        [HttpPost]
        public IActionResult EditPackage(PackageModel model)
        {
            string connStr = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // 1. Update Basic Info
                string sql = @"UPDATE Packages 
                       SET Title=@Title, Description=@Desc, Category=@Cat, MinAge=@Age, CancellationDeadlineDays=@Cancel, ExpiryMinutes=@Expiry
                       WHERE Id=@Id";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Title", model.Title);
                    cmd.Parameters.AddWithValue("@Desc", model.Description);
                    cmd.Parameters.AddWithValue("@Cat", model.Category);
                    cmd.Parameters.AddWithValue("@Age", model.MinAge);
                    cmd.Parameters.AddWithValue("@Cancel", model.CancellationDeadlineDays);
                    cmd.Parameters.AddWithValue("@Expiry", model.ExpiryMinutes);
                    cmd.Parameters.AddWithValue("@Id", model.Id);

                    cmd.ExecuteNonQuery();
                }

                // 2. Handle Images (The "Wipe and Replace" Strategy)
                // First, remove ALL images for this package
                string deleteSql = "DELETE FROM PackageImages WHERE PackageId = @Id";
                using (SqlCommand cmd = new SqlCommand(deleteSql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", model.Id);
                    cmd.ExecuteNonQuery();
                }

                // Helper function to insert image
                void InsertImage(string url, bool isPrimary)
                {
                    if (!string.IsNullOrEmpty(url))
                    {
                        string insertSql = "INSERT INTO PackageImages (PackageId, Url, IsPrimary) VALUES (@Pid, @Url, @IsPrim)";
                        using (SqlCommand cmd = new SqlCommand(insertSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@Pid", model.Id);
                            cmd.Parameters.AddWithValue("@Url", url);
                            cmd.Parameters.AddWithValue("@IsPrim", isPrimary);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                // Insert the new values from the textboxes
                InsertImage(model.MainImageUrl, true);  // Main
                InsertImage(model.ImageUrl2, false);    // Extra 1
                InsertImage(model.ImageUrl3, false);    // Extra 2
                InsertImage(model.ImageUrl4, false);    // Extra 3
            }

            TempData["Success"] = "Package and Images updated successfully.";
            return RedirectToAction("EditPackage", new { id = model.Id });
        }

        // ==========================================
        // PART 2: EDIT SPECIFIC DATE (SCHEDULE)
        // ==========================================

        [HttpGet]
        public IActionResult EditSchedule(int id)
        {
            PackageDateModel model = null;
            string connStr = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                string sql = "SELECT * FROM PackageDates WHERE Id = @Id";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            model = new PackageDateModel
                            {
                                Id = (int)reader["Id"],
                                PackageId = (int)reader["PackageId"],
                                StartDate = (DateTime)reader["StartDate"],
                                EndDate = (DateTime)reader["EndDate"],
                                Price = (decimal)reader["Price"],
                                // IMPORTANT: For editing, we load TotalRooms, but call it AvailableRooms in the model 
                                // if that's how your model is defined, otherwise adjust property name.
                                AvailableRooms = (int)reader["TotalRooms"],

                                DiscountedPrice = reader["DiscountedPrice"] as decimal?,
                                DiscountEndDate = reader["DiscountEndDate"] as DateTime?,
                                BookingEndDate = reader["BookingEndDate"] as DateTime?
                            };
                        }
                    }
                }
            }

            if (model == null) return NotFound();
            return View(model);
        }

        [HttpPost]
        public IActionResult EditSchedule(int id, int packageId, decimal price, int rooms, decimal? discountPrice, DateTime? discountEndDate, DateTime? bookingEndDate)
        {
            // 1. SANITIZE DISCOUNT INPUTS
            // If the user clears the price OR the date (or leaves both blank), we treat it as "Remove Discount"
            if (discountPrice == null || discountEndDate == null)
            {
                discountPrice = null;
                discountEndDate = null;
            }

            // 2. VALIDATE DISCOUNT DURATION (Only run if we actually have a NEW discount)
            if (discountPrice.HasValue && discountEndDate.HasValue)
            {
                var daysUntilEnd = (discountEndDate.Value - DateTime.Now).TotalDays;
                if (daysUntilEnd > 7)
                {
                    TempData["Error"] = "Error: Discounts cannot last longer than 7 days from today.";
                    return RedirectToAction("EditSchedule", new { id = id });
                }
            }

            string connStr = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // 3. FETCH CURRENT DATA (For Capacity & Date Validation)
                string checkSql = "SELECT TotalRooms, StartDate, BookingEndDate FROM PackageDates WHERE Id = @Id";

                int currentTotal = 0;
                DateTime startDate = DateTime.MinValue;
                DateTime? currentBookingEnd = null;

                using (SqlCommand checkCmd = new SqlCommand(checkSql, conn))
                {
                    checkCmd.Parameters.AddWithValue("@Id", id);
                    using (SqlDataReader reader = checkCmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            currentTotal = (int)reader["TotalRooms"];
                            startDate = (DateTime)reader["StartDate"];
                            currentBookingEnd = reader["BookingEndDate"] as DateTime?;
                        }
                    }
                }

                // VALIDATION: Capacity (Cannot reduce)
                if (rooms < currentTotal)
                {
                    TempData["Error"] = $"Error: You cannot reduce the room count. The current total is {currentTotal}.";
                    return RedirectToAction("EditSchedule", new { id = id });
                }

                // VALIDATION: Booking End Date
                if (bookingEndDate.HasValue)
                {
                    // Rule A: Cannot be after the trip Start Date
                    if (bookingEndDate.Value > startDate)
                    {
                        TempData["Error"] = $"Error: Booking Deadline ({bookingEndDate.Value:yyyy-MM-dd}) cannot be after the Trip Start Date ({startDate:yyyy-MM-dd}).";
                        return RedirectToAction("EditSchedule", new { id = id });
                    }

                    // Rule B: Can only extend (move closer to departure), not shrink
                    if (currentBookingEnd.HasValue && bookingEndDate.Value < currentBookingEnd.Value)
                    {
                        TempData["Error"] = $"Error: You can only extend the booking window. The new date must be on or after the current deadline ({currentBookingEnd.Value:yyyy-MM-dd}).";
                        return RedirectToAction("EditSchedule", new { id = id });
                    }
                }

                // 4. CALCULATE CAPACITY DIFFERENCE
                int capacityDifference = rooms - currentTotal;

                // 5. UPDATE DATABASE
                string sql = @"
            UPDATE PackageDates 
            SET Price=@Price, 
                TotalRooms=@NewTotal,
                AvailableRooms = AvailableRooms + @Diff, 
                DiscountedPrice=@DiscPrice, 
                DiscountEndDate=@DiscEnd,
                BookingEndDate=@BookEnd
            WHERE Id=@Id";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Price", price);
                    cmd.Parameters.AddWithValue("@NewTotal", rooms);
                    cmd.Parameters.AddWithValue("@Diff", capacityDifference);
                    cmd.Parameters.AddWithValue("@DiscPrice", (object)discountPrice ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@DiscEnd", (object)discountEndDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@BookEnd", (object)bookingEndDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Id", id);

                    cmd.ExecuteNonQuery();
                }
            }

            TempData["Success"] = "Schedule updated successfully.";
            // Ensure you redirect to the correct action (likely Details in Trips or Admin controller)
            return RedirectToAction("Details", "Trips", new { id = packageId });
        }
        [HttpPost]
        public IActionResult DeleteSchedule(int dateId, int packageId)
        {
            string connStr = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // 1. SAFETY CHECK: Ensure no bookings exist for this specific date
                // NOTE: Replace 'PackageDateId' with the actual column name in your Bookings table if it's different.
                string checkSql = "SELECT COUNT(*) FROM Bookings WHERE PackageDateId = @DateId";

                using (SqlCommand cmd = new SqlCommand(checkSql, conn))
                {
                    cmd.Parameters.AddWithValue("@DateId", dateId);
                    int bookingCount = (int)cmd.ExecuteScalar();

                    if (bookingCount > 0)
                    {
                        TempData["Error"] = $"Cannot delete this date. There are {bookingCount} existing booking(s) associated with it.";
                        // Redirect back to the Edit Package page
                        return RedirectToAction("EditPackage", new { id = packageId });
                    }
                }

                // 2. DELETE: If count is 0, it is safe to delete
                string deleteSql = "DELETE FROM PackageDates WHERE Id = @DateId";

                using (SqlCommand cmd = new SqlCommand(deleteSql, conn))
                {
                    cmd.Parameters.AddWithValue("@DateId", dateId);
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["Success"] = "Date deleted successfully.";

            // Redirect back to the Edit Package page so the user sees the updated list
            return RedirectToAction("EditPackage", new { id = packageId });
        }
        // GET: List all destinations
        [HttpGet]
        public IActionResult Destinations()
        {
            var destinations = new List<DestinationModel>();
            string connStr = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                // Fixed: Select Name instead of City
                string sql = "SELECT * FROM Destinations ORDER BY Country, Name";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        destinations.Add(new DestinationModel
                        {
                            Id = (int)reader["Id"],
                            Name = reader["Name"].ToString(), // Fixed column mapping
                            Country = reader["Country"].ToString()
                        });
                    }
                }
            }
            return View(destinations);
        }

        // GET: Show the form
        [HttpGet]
        public IActionResult CreateDestination()
        {
            return View();
        }

        // POST: Save new destination
        [HttpPost]
        public IActionResult CreateDestination(DestinationModel model)
        {
            // 1. Validation: Use Name instead of City
            if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Country))
            {
                TempData["Error"] = "Name and Country are required.";
                return View(model);
            }

            string connStr = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // 2. DUPLICATE CHECK
                // Fixed: Check against 'Name' column
                string checkSql = "SELECT COUNT(*) FROM Destinations WHERE Name = @Name AND Country = @Country";
                using (SqlCommand cmd = new SqlCommand(checkSql, conn))
                {
                    cmd.Parameters.AddWithValue("@Name", model.Name.Trim());
                    cmd.Parameters.AddWithValue("@Country", model.Country.Trim());
                    int exists = (int)cmd.ExecuteScalar();

                    if (exists > 0)
                    {
                        TempData["Error"] = $"The destination {model.Name}, {model.Country} already exists.";
                        return View(model);
                    }
                }

                // 3. INSERT
                // Fixed: Removed @Url and matched parameters to columns exactly
                string sql = "INSERT INTO Destinations (Name, Country) VALUES (@Name, @Country)";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Name", model.Name.Trim());
                    cmd.Parameters.AddWithValue("@Country", model.Country.Trim());

                    cmd.ExecuteNonQuery();
                }
            }

            TempData["Success"] = "Destination added successfully!";
            return RedirectToAction("Destinations");
        }

        // POST: Delete destination
        [HttpPost]
        public IActionResult DeleteDestination(int id)
        {
            string connStr = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // 1. SAFETY CHECK (Checks Packages table)
                string checkSql = "SELECT COUNT(*) FROM Packages WHERE DestinationId = @Id";

                using (SqlCommand cmd = new SqlCommand(checkSql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    int count = (int)cmd.ExecuteScalar();

                    if (count > 0)
                    {
                        TempData["Error"] = $"Cannot delete. Attached to {count} package(s).";
                        return RedirectToAction("Destinations");
                    }
                }

                // 2. DELETE
                string deleteSql = "DELETE FROM Destinations WHERE Id = @Id";
                using (SqlCommand cmd = new SqlCommand(deleteSql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["Success"] = "Destination deleted successfully.";
            return RedirectToAction("Destinations");
        }
        // // ==========================================
        // USER MANAGEMENT SECTION
        // ==========================================

        // 1. LIST ALL USERS (With Search)
        public IActionResult Users(string searchQuery)
        {
            // UPDATED SECURITY CHECK
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                return RedirectToAction("ViewLogin", "User");
            }

            var users = new List<UserViewModel>();
            string connStr = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                string sql = "SELECT Id, FirstName, LastName, Email, Role, Status FROM Users";

                if (!string.IsNullOrEmpty(searchQuery))
                {
                    sql += " WHERE FirstName LIKE @Query OR LastName LIKE @Query OR Email LIKE @Query";
                }

                // Order by Role (Admins first), then Name
                sql += " ORDER BY CASE WHEN Role = 'Admin' THEN 1 ELSE 2 END, FirstName ASC";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    if (!string.IsNullOrEmpty(searchQuery))
                    {
                        cmd.Parameters.AddWithValue("@Query", "%" + searchQuery + "%");
                    }

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            users.Add(new UserViewModel
                            {
                                Id = (int)reader["Id"],
                                FirstName = reader["FirstName"].ToString(),
                                LastName = reader["LastName"].ToString(),
                                Email = reader["Email"].ToString(),
                                Role = reader["Role"].ToString(),
                                Status = reader["Status"].ToString()
                            });
                        }
                    }
                }
            }

            ViewBag.CurrentSearch = searchQuery;
            return View(users);
        }

        // 2. CREATE USER - GET
        [HttpGet]
        public IActionResult CreateUser()
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin") return RedirectToAction("ViewLogin", "User");
            return View();
        }

        // 2. CREATE USER - POST
        [HttpPost]
        public async Task<IActionResult> CreateUser(string firstName, string lastName, string email, string password, bool isAdmin)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return RedirectToAction("ViewLogin", "User");

            string role = isAdmin ? "Admin" : "User";
            string status = "Active";
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(password);
            string connStr = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // 1. Check duplication
                string checkSql = "SELECT COUNT(1) FROM Users WHERE Email = @Email";
                using (SqlCommand checkCmd = new SqlCommand(checkSql, conn))
                {
                    checkCmd.Parameters.AddWithValue("@Email", email);
                    if ((int)checkCmd.ExecuteScalar() > 0)
                    {
                        ModelState.AddModelError("Email", "Email already exists.");
                        // Note: Depending on your view structure, you might need to reload a list here 
                        // or redirect back to show the error properly.
                        return RedirectToAction("Users");
                    }
                }

                // 2. Insert User
                string sql = @"INSERT INTO Users (FirstName, LastName, Email, PasswordHash, Role, Status) 
                       VALUES (@Fn, @Ln, @Email, @Pass, @Role, @Status)";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Fn", firstName);
                    cmd.Parameters.AddWithValue("@Ln", lastName);
                    cmd.Parameters.AddWithValue("@Email", email);
                    cmd.Parameters.AddWithValue("@Pass", passwordHash);
                    cmd.Parameters.AddWithValue("@Role", role);
                    cmd.Parameters.AddWithValue("@Status", status);

                    cmd.ExecuteNonQuery(); // Execute the INSERT
                }
            }

            // 3. Send "Account Created" Email (After connection closes)
            string subject = "Welcome to FlightPro - Account Created";
            string body = $@"
        <div style='font-family: Arial, sans-serif; padding: 20px;'>
            <h2>Hello {firstName},</h2>
            <p>An account has been created for you by our administrator.</p>
            <p>Here are your login details:</p>
            <ul>
                <li><strong>Email:</strong> {email}</li>
                <li><strong>Password:</strong> {password}</li>
            </ul>
            <p><em>Please log in and change your password as soon as possible for security.</em></p>
        </div>";

            try
            {
                await _emailService.SendEmailAsync(email, subject, body);
                TempData["Success"] = "User created and email sent successfully!";
            }
            catch
            {
                // If email fails, we still consider the user created, but warn the admin
                TempData["Success"] = "User created, but the email notification failed.";
            }

            return RedirectToAction("Users");
        }

        // 3. USER DETAILS (Profile + History)
        public IActionResult UserDetails(int id)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin") return RedirectToAction("ViewLogin", "User");

            var model = new UserDetailsViewModel();
            string connStr = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // A. Fetch Profile
                string userSql = "SELECT Id, FirstName, LastName, Email, Role, Status FROM Users WHERE Id = @Id";
                using (SqlCommand cmd = new SqlCommand(userSql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            model.Id = (int)r["Id"];
                            model.FirstName = r["FirstName"].ToString();
                            model.LastName = r["LastName"].ToString();
                            model.Email = r["Email"].ToString();
                            model.Role = r["Role"].ToString();
                            model.Status = r["Status"].ToString();
                        }
                        else return NotFound();
                    }
                }

                // B. Fetch History

                string historySql = @"
            SELECT b.Id, b.CreatedAt, b.TotalPrice, b.Status, 
                   p.Title, p.Id AS PackageId, 
                   pd.StartDate
            FROM Bookings b
            JOIN PackageDates pd ON b.PackageDateId = pd.Id 
            JOIN Packages p ON pd.PackageId = p.Id
            WHERE b.UserId = @UserId
            ORDER BY b.CreatedAt DESC";

                using (SqlCommand cmd = new SqlCommand(historySql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", id);
                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            model.BookingHistory.Add(new UserBookingModel
                            {
                                BookingId = (int)r["Id"],
                                PackageId = (int)r["PackageId"],
                                BookedAt = (DateTime)r["CreatedAt"],
                                PricePaid = (decimal)r["TotalPrice"],
                                Status = r["Status"].ToString(),
                                PackageTitle = r["Title"].ToString(),
                                TripDate = (DateTime)r["StartDate"]
                            });
                        }
                    }
                }
            }
            return View(model);
        }

        // 4. UPDATE USER
        [HttpPost]
        public IActionResult UpdateUser(int id, string firstName, string lastName, string email, string status)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin") return RedirectToAction("ViewLogin", "User");

            string connStr = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                string sql = "UPDATE Users SET FirstName=@Fn, LastName=@Ln, Email=@Email, Status=@Status WHERE Id=@Id";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Fn", firstName);
                    cmd.Parameters.AddWithValue("@Ln", lastName);
                    cmd.Parameters.AddWithValue("@Email", email);
                    cmd.Parameters.AddWithValue("@Status", status);
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }
            }
            TempData["Success"] = "User updated.";
            return RedirectToAction("UserDetails", new { id = id });
        }

        // 5. DELETE USER
        [HttpPost]
        public IActionResult DeleteUser(int id)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin") return RedirectToAction("ViewLogin", "User");

            // Don't delete yourself
            // Note: If you store UserId in session as string, parse it. 
            // Assuming here you might have SetInt32("UserId", ...)
            // If not, adapt this check.

            string connStr = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                try
                {
                    string sql = "DELETE FROM Users WHERE Id = @Id";
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        cmd.ExecuteNonQuery();
                    }
                    TempData["Success"] = "User deleted.";
                }
                catch
                {
                    TempData["Error"] = "Cannot delete user (likely has bookings).";
                }
            }
            return RedirectToAction("Users");
        }
        
        
        // 6. TOGGLE ROLE
        [HttpPost]
        public IActionResult ToggleRole(int id)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin") return RedirectToAction("ViewLogin", "User");

            string connStr = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // SQL Case Statement to swap string values
                string sql = @"
            UPDATE Users 
            SET Role = CASE WHEN Role = 'Admin' THEN 'User' ELSE 'Admin' END 
            WHERE Id = @Id";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }
            }
            TempData["Success"] = "User role updated.";
            return RedirectToAction("Users");
        }
        public IActionResult Waitlists()
        {
            var model = new List<AdminWaitlistViewModel>();

            string connStr = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                string sql = @"
            SELECT 
                w.Id AS WaitlistId, 
                w.JoinedAt, 
                w.RequestedAmount, 
                w.IsNotified,
                u.Id AS UserId,        -- FIX: Changed from u.UserId to u.Id
                u.FirstName, 
                u.LastName, 
                u.Email,
                pd.Id AS DateId, 
                pd.StartDate, 
                pd.EndDate,
                p.Title
            FROM WaitingList w
            JOIN Users u ON w.UserId = u.Id  -- FIX: Joined WaitingList.UserId with Users.Id
            JOIN PackageDates pd ON w.PackageDateId = pd.Id
            JOIN Packages p ON pd.PackageId = p.Id
            ORDER BY p.Title, pd.StartDate, w.JoinedAt";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int dateId = (int)reader["DateId"];

                        var group = model.FirstOrDefault(x => x.PackageDateId == dateId);
                        if (group == null)
                        {
                            group = new AdminWaitlistViewModel
                            {
                                PackageDateId = dateId,
                                PackageTitle = reader["Title"].ToString(),
                                StartDate = (DateTime)reader["StartDate"],
                                EndDate = (DateTime)reader["EndDate"]
                            };
                            model.Add(group);
                        }

                        group.Entries.Add(new WaitlistEntry
                        {
                            WaitlistId = (int)reader["WaitlistId"],
                            UserId = (int)reader["UserId"], // This now reads the alias 'UserId' which maps to u.Id
                            UserName = $"{reader["FirstName"]} {reader["LastName"]}",
                            UserEmail = reader["Email"].ToString(),
                            JoinedAt = (DateTime)reader["JoinedAt"],
                            RequestedAmount = (int)reader["RequestedAmount"],
                            IsNotified = (bool)reader["IsNotified"]
                        });
                    }
                }
            }

            return View(model);
        }

        [HttpPost]
        public IActionResult RemoveFromWaitlist(int waitlistId)
        {
            string connStr = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                // Changed table name from 'Waitlists' to 'WaitingList'
                string sql = "DELETE FROM WaitingList WHERE Id = @Id";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", waitlistId);
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["Success"] = "User removed from waitlist successfully.";
            return RedirectToAction("Waitlists");
        }

    }
}