using FlightPro.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc.Rendering;

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
            // 1. Check if user is logged in. If not, they can't see "My Reviews"
            int? currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null)
            {
                return RedirectToAction("ViewLogin", "User");
            }

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

                // 2. Fetch ONLY THIS USER'S Site Reviews
                string sqlSite = "SELECT * FROM SiteReviews WHERE UserId = @UserId ORDER BY Date DESC";
                using (SqlCommand command = new SqlCommand(sqlSite, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
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
                }

                // 3. Fetch ONLY THIS USER'S Package Reviews
                string sqlPackage = @"
                    SELECT pr.*, p.Title as PackageTitle 
                    FROM PackageReviews pr
                    JOIN Packages p ON pr.PackageId = p.Id
                    WHERE pr.UserId = @UserId  -- Filter by User
                    ORDER BY pr.Date DESC";

                using (SqlCommand command = new SqlCommand(sqlPackage, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
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
                }

                // 4. Dropdown Logic: Only Booked Packages where Trip has ENDED
                string sqlUserPackages = @"
                    SELECT DISTINCT p.Id, p.Title 
                    FROM Bookings b
                    JOIN PackageDates pd ON b.PackageDateId = pd.Id
                    JOIN Packages p ON pd.PackageId = p.Id
                    WHERE b.UserId = @UserId
                    AND b.Status = 'Confirmed'
                    AND pd.EndDate < GETDATE()"; // Ensures trip is in the past

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
}