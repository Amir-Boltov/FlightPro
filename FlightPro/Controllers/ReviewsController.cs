using FlightPro.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace FlightPro.Controllers
{
    public class ReviewsController : Controller
    {
        private readonly IConfiguration _configuration;

        public ReviewsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // === דף הראשי של הביקורות ===
        public IActionResult Index()
        {
            string connectionString = _configuration.GetConnectionString("myConnect");

            var viewModel = new ReviewsPageViewModel
            {
                GeneralReviews = new List<SiteReviewModel>(),
                PackageReviews = new List<PackageReviewModel>()
            };

            List<SelectListItem> packagesList = new List<SelectListItem>();

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // 1. שליפת ביקורות כלליות על האתר
                string sqlSite = "SELECT * FROM SiteReviews ORDER BY Date DESC";
                using (SqlCommand command = new SqlCommand(sqlSite, connection))
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        viewModel.GeneralReviews.Add(new SiteReviewModel
                        {
                            Id = (int)reader["Id"],
                            UserName = reader["UserName"].ToString(),
                            Comment = reader["Comment"].ToString(),
                            Rating = (int)reader["Rating"],
                            Date = (DateTime)reader["Date"]
                        });
                    }
                }

                // 2. שליפת ביקורות על חבילות (להצגה בדף)
                string sqlPackage = @"
                    SELECT pr.*, p.Title as PackageTitle 
                    FROM PackageReviews pr
                    JOIN Packages p ON pr.PackageId = p.Id
                    ORDER BY pr.Date DESC";

                using (SqlCommand command = new SqlCommand(sqlPackage, connection))
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        viewModel.PackageReviews.Add(new PackageReviewModel
                        {
                            Id = (int)reader["Id"],
                            UserName = reader["UserName"].ToString(),
                            PackageId = (int)reader["PackageId"],
                            PackageTitle = reader["PackageTitle"].ToString(),
                            Comment = reader["Comment"].ToString(),
                            Rating = (int)reader["Rating"],
                            Date = (DateTime)reader["Date"]
                        });
                    }
                }

                // 3. שליפת חבילות ל-Dropdown (מותאם לטבלאות Bookings ו-PackageDates)
                int? currentUserId = HttpContext.Session.GetInt32("UserId");

                if (currentUserId != null)
                {
                    // הסבר לשאילתה:
                    // 1. מתחילים ב-Bookings (מה המשתמש הזמין)
                    // 2. מחברים ל-PackageDates (כדי לבדוק מתי הטיול נגמר)
                    // 3. מחברים ל-Packages (כדי לקבל את שם הטיול)
                    // 4. תנאי: הטיול נגמר (EndDate < GETDATE)

                    string sqlUserPackages = @"
                        SELECT DISTINCT p.Id, p.Title 
                        FROM Bookings b
                        JOIN PackageDates pd ON b.PackageDateId = pd.Id
                        JOIN Packages p ON pd.PackageId = p.Id
                        WHERE b.UserId = @UserId 
                        AND pd.EndDate < GETDATE()";

                    using (SqlCommand command = new SqlCommand(sqlUserPackages, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", currentUserId);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                packagesList.Add(new SelectListItem
                                {
                                    Value = reader["Id"].ToString(),
                                    Text = reader["Title"].ToString()
                                });
                            }
                        }
                    }
                }
            }

            // מעבירים את הרשימה ל-View
            ViewBag.Packages = packagesList;

            return View(viewModel);
        }

        // === פונקציה לשמירת ביקורת על האתר ===
        [HttpPost]
        public IActionResult AddSiteReview(int rating, string comment)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            string userName = HttpContext.Session.GetString("UserName");

            if (userId == null)
            {
                return RedirectToAction("ViewLogin", "User");
            }

            string connectionString = _configuration.GetConnectionString("myConnect");
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string sql = "INSERT INTO SiteReviews (UserId, UserName, Comment, Rating) VALUES (@UserId, @UserName, @Comment, @Rating)";

                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@UserId", userId);
                    command.Parameters.AddWithValue("@UserName", userName);
                    command.Parameters.AddWithValue("@Comment", comment);
                    command.Parameters.AddWithValue("@Rating", rating);

                    command.ExecuteNonQuery();
                }
            }

            TempData["Message"] = "Thank you! Your site review has been posted.";
            return RedirectToAction("Index");
        }

        // === פונקציה לשמירת ביקורת על חבילה ===
        [HttpPost]
        public IActionResult AddPackageReview(int packageId, int rating, string comment)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            string userName = HttpContext.Session.GetString("UserName");

            if (userId == null)
            {
                return RedirectToAction("ViewLogin", "User");
            }

            string connectionString = _configuration.GetConnectionString("myConnect");
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string sql = "INSERT INTO PackageReviews (UserId, UserName, PackageId, Comment, Rating) VALUES (@UserId, @UserName, @PackageId, @Comment, @Rating)";

                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@UserId", userId);
                    command.Parameters.AddWithValue("@UserName", userName);
                    command.Parameters.AddWithValue("@PackageId", packageId);
                    command.Parameters.AddWithValue("@Comment", comment);
                    command.Parameters.AddWithValue("@Rating", rating);

                    command.ExecuteNonQuery();
                }
            }

            TempData["Message"] = "Thank you! Your trip review has been posted.";
            return RedirectToAction("Index");
        }
    }

    public class SelectListItem
    {
        public string Value { get; set; }
        public string Text { get; set; }
    }
}