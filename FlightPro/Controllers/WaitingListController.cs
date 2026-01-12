using FlightPro.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace FlightPro.Controllers
{
    public class WaitingListController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly WaitlistService _waitlistService;

        public WaitingListController(IConfiguration configuration, WaitlistService waitlistService)
        {
            _configuration = configuration;
            _waitlistService = waitlistService;
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
        public IActionResult Join(int packageId, int packageDateId, int requestedAmount)
        {
            // 1. Ensure User is Logged In
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return RedirectToAction("ViewLogin", "User");
            }

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("myConnect")))
            {
                conn.Open();

                // 2. Check for Duplicates (Prevent joining the same list twice)
                // Note: Using your table name 'WaitingList' (singular)
                string checkSql = "SELECT COUNT(1) FROM WaitingList WHERE UserId = @UserId AND PackageDateId = @DateId";
                using (SqlCommand checkCmd = new SqlCommand(checkSql, conn))
                {
                    checkCmd.Parameters.AddWithValue("@UserId", userId);
                    checkCmd.Parameters.AddWithValue("@DateId", packageDateId);
                    int count = (int)checkCmd.ExecuteScalar();

                    if (count > 0)
                    {
                        // Already on the list? Just show success.
                        return RedirectToAction("JoinSuccess");
                    }
                }

                // 3. Insert Record
                // Using your columns: JoinedAt, IsNotified
                string insertSql = @"
                    INSERT INTO WaitingList 
                    (UserId, PackageId, PackageDateId, RequestedAmount, JoinedAt, IsNotified)
                    VALUES 
                    (@UserId, @PackageId, @PackageDateId, @RequestedAmount, GETDATE(), 0)";

                using (SqlCommand cmd = new SqlCommand(insertSql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@PackageId", packageId);
                    cmd.Parameters.AddWithValue("@PackageDateId", packageDateId);
                    cmd.Parameters.AddWithValue("@RequestedAmount", requestedAmount);
                    // JoinedAt is handled by GETDATE()
                    // IsNotified is set to 0 (false) by default
                    // NotificationExpiresAt is left null until we actually notify them later

                    cmd.ExecuteNonQuery();
                }
            }

            // 4. Redirect
            return RedirectToAction("JoinSuccess");
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