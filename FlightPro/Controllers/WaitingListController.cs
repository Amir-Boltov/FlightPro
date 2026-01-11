using FlightPro.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace FlightPro.Controllers
{
    public class WaitingListController : Controller
    {
        private readonly IConfiguration _configuration;

        public WaitingListController(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        // GET: WaitingList/Index
        public IActionResult Index()
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            List<MyWaitingListItem> myLists = new List<MyWaitingListItem>();

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("myConnect")))
            {
                conn.Open();
                string sql = @"
            SELECT w.Id, w.RequestedAmount, w.JoinedAt, w.IsNotified,
                   p.Title, 
                   d.StartDate, d.EndDate,
                   img.Url AS MainImageUrl
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
                                IsNotified = (bool)reader["IsNotified"]
                            });
                        }
                    }
                }
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