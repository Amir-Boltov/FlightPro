using FlightPro.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace FlightPro.Controllers
{
    public class WaitingListController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly WaitlistService _waitlistService;
        private readonly EmailService _emailService;

        public WaitingListController(IConfiguration configuration, WaitlistService waitlistService, EmailService emailService)
        {
            _configuration = configuration;
            _waitlistService = waitlistService;
            _emailService = emailService;
        }
        // GET: WaitingList/Index
        public IActionResult Index()
        {
            // 1. Auth Check
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            string connectionString = _configuration.GetConnectionString("myConnect");

            // ---------------------------------------------------------
            // STEP 1: JIT CLEANUP (The "Just-In-Time" Fix)
            // Before showing the list, we check the packages the user is waiting for.
            // If a spot expired 1 second ago, we want to grab it NOW.
            // ---------------------------------------------------------

            List<int> packagesToRefresh = new List<int>();

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                // specific query to get only the packages relevant to this user
                string getIdsSql = "SELECT DISTINCT PackageId FROM WaitingList WHERE UserId = @UserId";
                using (SqlCommand cmd = new SqlCommand(getIdsSql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            packagesToRefresh.Add((int)reader["PackageId"]);
                        }
                    }
                }
            }

            // Run the logic: "Clean expired items -> If space opens, promote the next person"
            foreach (int pkgId in packagesToRefresh)
            {
                _waitlistService.CleanupAndPromoteForPackage(pkgId);
            }

            // ---------------------------------------------------------
            // STEP 2: FETCH THE DATA
            // Now that the DB is clean and promotions have happened, 
            // we fetch what is LEFT in the user's waitlist.
            // ---------------------------------------------------------

            List<MyWaitingListItem> myLists = new List<MyWaitingListItem>();

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                // Your existing SQL query (Perfect for this job)
                string sql = @"
                SELECT w.Id, w.RequestedAmount, w.JoinedAt, w.IsNotified,
                       p.Title, 
                       d.StartDate, d.EndDate,
                       img.Url AS MainImageUrl,
                       (
                           SELECT COUNT(*) + 1 
                           FROM WaitingList w2 
                           WHERE w2.PackageDateId = w.PackageDateId 
                           AND w2.JoinedAt < w.JoinedAt
                       ) AS QueuePosition
                FROM WaitingList w
                JOIN Packages p ON w.PackageId = p.Id
                JOIN PackageDates d ON w.PackageDateId = d.Id
                LEFT JOIN PackageImages img ON p.Id = img.PackageId AND img.IsPrimary = 1
                WHERE w.UserId = @UserId
                ORDER BY w.JoinedAt DESC";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            myLists.Add(new MyWaitingListItem
                            {
                                Id = (int)reader["Id"],
                                Title = reader["Title"].ToString(),
                                MainImageUrl = reader["MainImageUrl"] != DBNull.Value ? reader["MainImageUrl"].ToString() : "/images/default.jpg",
                                StartDate = (DateTime)reader["StartDate"],
                                EndDate = (DateTime)reader["EndDate"],
                                RequestedAmount = (int)reader["RequestedAmount"],
                                JoinedAt = (DateTime)reader["JoinedAt"],
                                IsNotified = (bool)reader["IsNotified"],
                                QueuePosition = reader["QueuePosition"] != DBNull.Value ? (int)reader["QueuePosition"] : 0
                            });
                        }
                    }
                }
            }

            // Optional: Check if the user was promoted during Step 1.
            // If the number of items fetched (myLists.Count) is LESS than the number of 
            // distinct packages we checked (packagesToRefresh.Count), it implies 
            // an item was promoted and removed from the Waitlist table.
            if (myLists.Count < packagesToRefresh.Count)
            {
                TempData["SuccessMessage"] = "Good news! One of your waitlist items has been secured. Check your Cart!";
            }

            return View(myLists);
        }

        // POST: WaitingList/Join
        [HttpPost]
        public async Task<IActionResult> Join(int packageId, int packageDateId, int requestedAmount)
        {
            // 1. Ensure User is Logged In
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                // Return JSON so the AJAX handler knows to redirect or show error
                return Json(new { success = false, message = "Login required." });
            }

            try
            {
                string connectionString = _configuration.GetConnectionString("myConnect");

                // Variables to hold info for the email
                string userEmail = null;
                string userName = null;
                string packageTitle = null;
                DateTime? tripDate = null;

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    // 2. Check for Duplicates
                    string checkSql = "SELECT COUNT(1) FROM WaitingList WHERE UserId = @UserId AND PackageDateId = @DateId";
                    using (SqlCommand checkCmd = new SqlCommand(checkSql, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@UserId", userId);
                        checkCmd.Parameters.AddWithValue("@DateId", packageDateId);
                        int count = (int)checkCmd.ExecuteScalar();

                        if (count > 0)
                        {
                            return Json(new { success = true, message = "You are already on the waiting list for this trip." });
                        }
                    }

                    // 3. Insert Record
                    string insertSql = @"
                INSERT INTO WaitingList 
                (UserId, PackageId, PackageDateId, RequestedAmount, JoinedAt, IsNotified)
                VALUES 
                (@UserId, @PackageId, @DateId, @Amount, GETDATE(), 0)";

                    using (SqlCommand cmd = new SqlCommand(insertSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@PackageId", packageId);
                        cmd.Parameters.AddWithValue("@DateId", packageDateId);
                        cmd.Parameters.AddWithValue("@Amount", requestedAmount);
                        cmd.ExecuteNonQuery();
                    }

                    // 4. Fetch Details for Email (User & Package Info)
                    string infoSql = @"
                SELECT u.Email, u.FirstName, p.Title, pd.StartDate
                FROM Users u
                JOIN Packages p ON p.Id = @PackageId
                JOIN PackageDates pd ON pd.Id = @DateId
                WHERE u.Id = @UserId";

                    using (SqlCommand infoCmd = new SqlCommand(infoSql, conn))
                    {
                        infoCmd.Parameters.AddWithValue("@UserId", userId);
                        infoCmd.Parameters.AddWithValue("@PackageId", packageId);
                        infoCmd.Parameters.AddWithValue("@DateId", packageDateId);

                        using (SqlDataReader reader = infoCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                userEmail = reader["Email"].ToString();
                                userName = reader["FirstName"].ToString();
                                packageTitle = reader["Title"].ToString();
                                tripDate = (DateTime)reader["StartDate"];
                            }
                        }
                    }
                }

                // 5. Send the Email
                if (!string.IsNullOrEmpty(userEmail))
                {
                    string subject = "FlightPro Waitlist Confirmation";
                    string body = $@"
                <div style='font-family: Arial, sans-serif; color: #333;'>
                    <h2>Hi {userName},</h2>
                    <p>You have been successfully added to the waiting list for:</p>
                    <div style='background: #f4f4f4; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                        <h3 style='margin: 0; color: #007bff;'>{packageTitle}</h3>
                        <p style='margin: 5px 0 0;'><strong>Date:</strong> {tripDate:MMMM dd, yyyy}</p>
                        <p style='margin: 5px 0 0;'><strong>Requested Seats:</strong> {requestedAmount}</p>
                    </div>
                    <p>If a spot opens up, we will notify you immediately via email.</p>
                    <p>Best regards,<br/>The FlightPro Team</p>
                </div>";

                    await _emailService.SendEmailAsync(userEmail, subject, body);
                }

                return Json(new { success = true, message = "You have been added to the waiting list! We sent you a confirmation email." });

            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }
        // POST: WaitingList/Leave
        [HttpPost]
        public IActionResult Leave(int id)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("myConnect")))
            {
                conn.Open();
                // Only delete if the ID belongs to the current user (security check)
                string sql = "DELETE FROM WaitingList WHERE Id = @Id AND UserId = @UserId";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.ExecuteNonQuery();
                }
            }

            return RedirectToAction("Index");
        }

        // GET: WaitingList/JoinSuccess
        public IActionResult JoinSuccess()
        {
            return View();
        }
    }
}