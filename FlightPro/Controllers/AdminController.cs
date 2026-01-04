using FlightPro.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using FlightPro.Attributes;
using Microsoft.AspNetCore.Mvc.Rendering;



namespace FlightPro.Controllers
{
    [AdminOnly]
    public class AdminController : Controller
    {
        private readonly IConfiguration _configuration;

        public AdminController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // GET: Admin Dashboard
        public IActionResult Index()
        {
            var model = new AdminDashboardViewModel();
            string connStr = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
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
                        model.TotalBookings = (int)reader["TotalBookings"];
                        model.TotalRevenue = (decimal)reader["TotalRevenue"];
                        model.ActivePackages = (int)reader["ActivePackages"];
                        model.UsersCount = (int)reader["UsersCount"];
                    }
                }
                string bookingSql = @"
                    SELECT TOP 10 b.Id, b.CreatedAt, b.TotalPrice, u.FirstName,u.LastName, p.Title
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
                            Id = (int)reader["Id"],
                            CreatedAt = (DateTime)reader["CreatedAt"],
                            PricePaid = (decimal)reader["TotalPrice"],
                            CustFirstName = reader["FirstName"].ToString(),
                            CustLastName = reader["LastName"].ToString(),
                            PackageTitle = reader["Title"].ToString()
                        });
                    }
                }
            }
            return View(model);
        }
        public IActionResult Packages()
        {
            var list = new List<PackageModel>();
            string connStr = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                string sql = @"
                    SELECT p.Id, p.Title, p.Category, p.MinAge, d.Name as City, d.Country
                    FROM Packages p
                    JOIN Destinations d ON p.DestinationId = d.Id";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new PackageModel
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
            }
            return View(list);
        }
        // GET: Show the form (Load Destinations for the Dropdown)
        [HttpGet]
        public IActionResult CreatePackage()
        {
            // Fetch destinations for the dropdown
            string connStr = _configuration.GetConnectionString("myConnect");
            var destinations = new List<SelectListItem>();

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                string sql = "SELECT Id, Name, Country FROM Destinations ORDER BY Country, Name";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        destinations.Add(new SelectListItem
                        {
                            Value = reader["Id"].ToString(),
                            Text = $"{reader["Name"]}, {reader["Country"]}"
                        });
                    }
                }
            }

            ViewBag.Destinations = destinations; // Pass to View
            return View(new PackageModel());
        }

        // POST: Process the new package (Now saves DestinationId)
        [HttpPost]
        public IActionResult CreatePackage(PackageModel model, int DestinationId)
        {
            // 1. Basic Validation
            if (!ModelState.IsValid)
            {
                // Reload destinations if we have to return the view due to error
                // (Re-run the Fetch destinations logic here or extract it to a helper method)
                return CreatePackage();
            }

            string connStr = _configuration.GetConnectionString("myConnect");
            int newPackageId = 0;

            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // 2. Insert Package (ADDED DestinationId)
                string sql = @"
            INSERT INTO Packages (Title, Description, Category, DestinationId, MinAge, CancellationDeadlineDays) 
            VALUES (@Title, @Desc, @Cat, @DestId, @Age, @Cancel);
            SELECT CAST(scope_identity() AS int);";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Title", model.Title);
                    cmd.Parameters.AddWithValue("@Desc", model.Description);
                    cmd.Parameters.AddWithValue("@Cat", model.Category);
                    cmd.Parameters.AddWithValue("@DestId", DestinationId); // <--- ADDED
                    cmd.Parameters.AddWithValue("@Age", model.MinAge);
                    cmd.Parameters.AddWithValue("@Cancel", model.CancellationDeadlineDays);

                    newPackageId = (int)cmd.ExecuteScalar();
                }

                // 3. Insert Images
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

            TempData["Success"] = "Package created! Now you can add dates.";
            return RedirectToAction("EditPackage", new { id = newPackageId });
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
        public IActionResult AddPackageDate(int packageId, DateTime startDate, DateTime endDate, decimal price, int totalRooms)
        {
            string connStr = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // Validation: Ensure Start Date is in the future
                if (startDate < DateTime.Now)
                {
                    ModelState.AddModelError("", "Start date must be in the future.");
                    ViewBag.PackageId = packageId;
                    return View();
                }

                string sql = @"
            INSERT INTO PackageDates (PackageId, StartDate, EndDate, Price, AvailableRooms, TotalRooms)
            VALUES (@Pid, @Start, @End, @Price, @Rooms, @Rooms)"; // Start with Available = Total

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Pid", packageId);
                    cmd.Parameters.AddWithValue("@Start", startDate);
                    cmd.Parameters.AddWithValue("@End", endDate);
                    cmd.Parameters.AddWithValue("@Price", price);
                    cmd.Parameters.AddWithValue("@Rooms", totalRooms);

                    cmd.ExecuteNonQuery();
                }
            }

            TempData["Success"] = "New travel date added successfully!";
            return RedirectToAction("Packages");
        }
        // ==========================================
        // PART 1: EDIT MAIN PACKAGE DETAILS
        // ==========================================

        // GET: Edit Package
        // GET: Edit Package
        [HttpGet]
        public IActionResult EditPackage(int id)
        {
            PackageModel package = null;
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
            SELECT Id, StartDate, EndDate, Price, DiscountedPrice 
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
                                DiscountedPrice = reader["DiscountedPrice"] as decimal?
                            });
                        }
                    }
                }
            }

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
                       SET Title=@Title, Description=@Desc, Category=@Cat, MinAge=@Age, CancellationDeadlineDays=@Cancel
                       WHERE Id=@Id";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Title", model.Title);
                    cmd.Parameters.AddWithValue("@Desc", model.Description);
                    cmd.Parameters.AddWithValue("@Cat", model.Category);
                    cmd.Parameters.AddWithValue("@Age", model.MinAge);
                    cmd.Parameters.AddWithValue("@Cancel", model.CancellationDeadlineDays);
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
            // Fetch the single schedule row
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
                                AvailableRooms = (int)reader["AvailableRooms"],
                                DiscountedPrice = reader["DiscountedPrice"] as decimal?,
                                // Note: You need to add DiscountEndDate to your PackageDateModel if not there
                                DiscountEndDate = reader["DiscountEndDate"] as DateTime?
                            };
                        }
                    }
                }
            }
            return View(model);
        }

        [HttpPost]
        public IActionResult EditSchedule(int id, int packageId, decimal price, int rooms, decimal? discountPrice, DateTime? discountEndDate)
        {
            // --- REQUIREMENT: Discount last for a week at most ---
            if (discountPrice.HasValue && discountEndDate.HasValue)
            {
                // Calculate days between NOW and the Discount End Date
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
                string sql = @"
            UPDATE PackageDates 
            SET Price=@Price, AvailableRooms=@Rooms, DiscountedPrice=@DiscPrice, DiscountEndDate=@DiscEnd
            WHERE Id=@Id";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Price", price);
                    cmd.Parameters.AddWithValue("@Rooms", rooms);
                    // Handle NULLs for discount
                    cmd.Parameters.AddWithValue("@DiscPrice", (object)discountPrice ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@DiscEnd", (object)discountEndDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["Success"] = "Schedule updated successfully.";
            return RedirectToAction("EditPackage", new { id = packageId });
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
        public IActionResult CreateUser(string firstName, string lastName, string email, string password, bool isAdmin)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin") return RedirectToAction("ViewLogin", "User");

            string role = isAdmin ? "Admin" : "User";
            string status = "Active"; // Default status
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(password);
            string connStr = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();

                // Check duplication
                string checkSql = "SELECT COUNT(1) FROM Users WHERE Email = @Email";
                using (SqlCommand checkCmd = new SqlCommand(checkSql, conn))
                {
                    checkCmd.Parameters.AddWithValue("@Email", email);
                    if ((int)checkCmd.ExecuteScalar() > 0)
                    {
                        ModelState.AddModelError("Email", "Email already exists.");
                        return View();
                    }
                }

                // Insert
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
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["Success"] = "User created successfully!";
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

    }
}