using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using FlightPro.Models;

namespace FlightPro.Controllers
{
    public class MyBookController : Controller
    {
        private readonly IConfiguration _configuration;

        public MyBookController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // ==========================================
        // חלק 1: סל הקניות (Shopping Cart)
        // ==========================================

        // דף הסל - מציג הזמנות בסטטוס InCart
        public IActionResult Index()
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Users");

            List<myBook> cartItems = new List<myBook>();
            string connectionString = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string sql = @"
                    SELECT 
                        b.Id, b.UserId, b.PackageId, b.PackageDateId, b.Amount, b.TotalPrice, b.Status,
                        p.Title, p.Description,
                        d.Name AS CityName, d.Country,
                        pd.StartDate, pd.EndDate,
                        (SELECT TOP 1 Url FROM PackageImages WHERE PackageId = p.Id AND IsPrimary=1) AS MainImageUrl
                    FROM Bookings b
                    JOIN Packages p ON b.PackageId = p.Id
                    JOIN PackageDates pd ON b.PackageDateId = pd.Id
                    JOIN Destinations d ON p.DestinationId = d.Id
                    WHERE b.UserId = @UserId AND b.Status = 'InCart'";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var bookItem = new myBook
                            {
                                Id = (int)reader["Id"],
                                UserId = (int)reader["UserId"],
                                PackageId = (int)reader["PackageId"],
                                PackageDateId = (int)reader["PackageDateId"],
                                Amount = reader["Amount"] != DBNull.Value ? (int)reader["Amount"] : 1,
                                TotalPrice = reader["TotalPrice"] != DBNull.Value ? (decimal)reader["TotalPrice"] : 0,
                                Status = reader["Status"].ToString()
                            };

                            bookItem.Package = new PackageModel
                            {
                                Id = (int)reader["PackageId"],
                                Title = reader["Title"].ToString(),
                                Destination = $"{reader["CityName"]}, {reader["Country"]}",
                                MainImageUrl = reader["MainImageUrl"] != DBNull.Value ? reader["MainImageUrl"].ToString() : "/img/default.jpg"
                            };

                            bookItem.PackageDate = new PackageDateModel
                            {
                                Id = (int)reader["PackageDateId"],
                                StartDate = (DateTime)reader["StartDate"],
                                EndDate = (DateTime)reader["EndDate"]
                            };

                            cartItems.Add(bookItem);
                        }
                    }
                }
            }

            return View(cartItems);
        }

        // הוספה לסל (POST)
        [HttpPost]
        public IActionResult AddToBasket(int packageId, int packageDateId, int amount)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Users");

            string connectionString = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // בדיקת מחיר ומלאי
                        string checkSql = "SELECT Price, AvailableRooms FROM PackageDates WITH (UPDLOCK) WHERE Id = @DateId";
                        decimal pricePerPerson = 0;
                        int availableRooms = 0;

                        using (SqlCommand cmdCheck = new SqlCommand(checkSql, conn, transaction))
                        {
                            cmdCheck.Parameters.AddWithValue("@DateId", packageDateId);
                            using (SqlDataReader reader = cmdCheck.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    pricePerPerson = (decimal)reader["Price"];
                                    availableRooms = reader["AvailableRooms"] != DBNull.Value ? (int)reader["AvailableRooms"] : 0;
                                }
                                else return RedirectToAction("Index", "Trips");
                            }
                        }

                        if (availableRooms < amount)
                        {
                            TempData["ErrorMessage"] = "Not enough rooms available.";
                            return RedirectToAction("Details", "Trips", new { id = packageId });
                        }

                        // בדיקה האם כבר קיים בסל
                        string checkExistingSql = "SELECT Id, Amount FROM Bookings WHERE UserId = @UserId AND PackageDateId = @DateId AND Status = 'InCart'";
                        int existingBookingId = 0;
                        int existingAmount = 0;

                        using (SqlCommand cmdExist = new SqlCommand(checkExistingSql, conn, transaction))
                        {
                            cmdExist.Parameters.AddWithValue("@UserId", userId);
                            cmdExist.Parameters.AddWithValue("@DateId", packageDateId);
                            using (SqlDataReader reader = cmdExist.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    existingBookingId = (int)reader["Id"];
                                    existingAmount = (int)reader["Amount"];
                                }
                            }
                        }

                        // עדכון או יצירה
                        if (existingBookingId > 0)
                        {
                            int newTotalAmount = existingAmount + amount;
                            decimal newTotalPrice = newTotalAmount * pricePerPerson;

                            string updateBookingSql = "UPDATE Bookings SET Amount = @NewAmount, TotalPrice = @NewPrice WHERE Id = @Id";
                            using (SqlCommand cmdUpdate = new SqlCommand(updateBookingSql, conn, transaction))
                            {
                                cmdUpdate.Parameters.AddWithValue("@NewAmount", newTotalAmount);
                                cmdUpdate.Parameters.AddWithValue("@NewPrice", newTotalPrice);
                                cmdUpdate.Parameters.AddWithValue("@Id", existingBookingId);
                                cmdUpdate.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            decimal totalPrice = (amount * pricePerPerson);
                            string insertSql = @"
                                INSERT INTO Bookings (UserId, PackageId, PackageDateId, Amount, TotalPrice, Status, CreatedAt, IsPaid) 
                                VALUES (@UserId, @PackageId, @PackageDateId, @Amount, @TotalPrice, 'InCart', GETDATE(), 0)";

                            using (SqlCommand cmdInsert = new SqlCommand(insertSql, conn, transaction))
                            {
                                cmdInsert.Parameters.AddWithValue("@UserId", userId);
                                cmdInsert.Parameters.AddWithValue("@PackageId", packageId);
                                cmdInsert.Parameters.AddWithValue("@PackageDateId", packageDateId);
                                cmdInsert.Parameters.AddWithValue("@Amount", amount);
                                cmdInsert.Parameters.AddWithValue("@TotalPrice", totalPrice);
                                cmdInsert.ExecuteNonQuery();
                            }
                        }

                        // הורדת המלאי
                        string updateStockSql = "UPDATE PackageDates SET AvailableRooms = AvailableRooms - @Amount WHERE Id = @DateId";
                        using (SqlCommand cmdStock = new SqlCommand(updateStockSql, conn, transaction))
                        {
                            cmdStock.Parameters.AddWithValue("@DateId", packageDateId);
                            cmdStock.Parameters.AddWithValue("@Amount", amount);
                            cmdStock.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                    }
                }
            }
            return RedirectToAction("Index");
        }

        // הסרה מהסל (פעולה למוצרים שעדיין לא שולמו)
        public IActionResult RemoveFromBasket(int bookingId)
        {
            string connectionString = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        int packageDateId = 0;
                        int amountToReturn = 0;

                        // 1. שליפת נתונים לפני מחיקה
                        string findOrderSql = "SELECT PackageDateId, Amount FROM Bookings WHERE Id = @Id";
                        using (SqlCommand cmdFind = new SqlCommand(findOrderSql, conn, transaction))
                        {
                            cmdFind.Parameters.AddWithValue("@Id", bookingId);
                            using (SqlDataReader reader = cmdFind.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    packageDateId = reader["PackageDateId"] != DBNull.Value ? (int)reader["PackageDateId"] : 0;
                                    amountToReturn = reader["Amount"] != DBNull.Value ? (int)reader["Amount"] : 0;
                                }
                            }
                        }

                        // 2. החזרת המלאי
                        if (packageDateId > 0 && amountToReturn > 0)
                        {
                            string returnStockSql = "UPDATE PackageDates SET AvailableRooms = AvailableRooms + @Amount WHERE Id = @DateId";
                            using (SqlCommand cmdReturn = new SqlCommand(returnStockSql, conn, transaction))
                            {
                                cmdReturn.Parameters.AddWithValue("@DateId", packageDateId);
                                cmdReturn.Parameters.AddWithValue("@Amount", amountToReturn);
                                cmdReturn.ExecuteNonQuery();
                            }
                        }

                        // 3. מחיקת השורה מהסל
                        string deleteSql = "DELETE FROM Bookings WHERE Id = @Id";
                        using (SqlCommand cmdDelete = new SqlCommand(deleteSql, conn, transaction))
                        {
                            cmdDelete.Parameters.AddWithValue("@Id", bookingId);
                            cmdDelete.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                    }
                }
            }

            return RedirectToAction("Index");
        }

        // ==========================================
        // חלק 2: היסטוריית הזמנות וביטולים
        // ==========================================

        // דף היסטוריה - מציג הזמנות ששולמו (IsPaid=1)
        public IActionResult OrderHistory()
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Users");

            List<HistoryViewModel> list = new List<HistoryViewModel>();
            string connectionString = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                // תיקון השאילתה:
                // 1. הוספת JOIN ל-Destinations כדי לקבל שם עיר ומדינה
                // 2. הוספת תת-שאילתה (Subquery) לשליפת התמונה הראשית מ-PackageImages
                string sql = @"
            SELECT 
                b.Id, 
                p.Title, 
                d.Name AS CityName, 
                d.Country,
                pd.StartDate, 
                pd.EndDate, 
                b.TotalPrice, 
                b.Status,
                (SELECT TOP 1 Url FROM PackageImages WHERE PackageId = p.Id AND IsPrimary = 1) AS MainImageUrl
            FROM Bookings b
            JOIN PackageDates pd ON b.PackageDateId = pd.Id
            JOIN Packages p ON pd.PackageId = p.Id
            JOIN Destinations d ON p.DestinationId = d.Id
            WHERE b.UserId = @UserId AND b.IsPaid = 1
            ORDER BY pd.StartDate DESC";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // הרכבת הנתונים מהשליפה החדשה
                            var historyItem = new HistoryViewModel
                            {
                                BookingId = (int)reader["Id"],
                                Title = reader["Title"].ToString(),

                                // חיבור שם העיר והמדינה למחרוזת אחת
                                Destination = $"{reader["CityName"]}, {reader["Country"]}",

                                // בדיקה אם חזרה תמונה (אם לא - שים תמונת ברירת מחדל)
                                ImageUrl = reader["MainImageUrl"] != DBNull.Value ? reader["MainImageUrl"].ToString() : "/images/default.jpg",

                                StartDate = (DateTime)reader["StartDate"],
                                EndDate = (DateTime)reader["EndDate"],
                                TotalPrice = (decimal)reader["TotalPrice"],
                                Status = reader["Status"].ToString()
                            };

                            list.Add(historyItem);
                        }
                    }
                }
            }

            return View("HistoryBook",list);
        }

        // ביטול הזמנה שכבר בוצעה (מההיסטוריה)
        [HttpPost]
        public IActionResult CancelBooking(int bookingId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Users");

            string connectionString = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                // 1. השגת ה-DateId והכמות כדי להחזיר למלאי
                int packageDateId = 0;
                int amountToReturn = 1; // ברירת מחדל 1, אם שמרת כמות אחרת צריך לשלוף אותה

                string getSql = "SELECT PackageDateId, Amount FROM Bookings WHERE Id = @Id AND UserId = @UserId";
                using (SqlCommand cmd = new SqlCommand(getSql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", bookingId);
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            packageDateId = (int)reader["PackageDateId"];
                            // אם יש לך עמודת Amount בטבלה, תוסיף את השורה הבאה:
                            amountToReturn = reader["Amount"] != DBNull.Value ? (int)reader["Amount"] : 1;
                        }
                    }
                }

                if (packageDateId > 0)
                {
                    // 2. עדכון הסטטוס ל-Canceled (אנחנו לא מוחקים מההיסטוריה, רק מסמנים כבטל)
                    string updateBooking = "UPDATE Bookings SET Status = 'Canceled' WHERE Id = @Id";
                    using (SqlCommand cmd = new SqlCommand(updateBooking, conn))
                    {
                        cmd.Parameters.AddWithValue("@Id", bookingId);
                        cmd.ExecuteNonQuery();
                    }

                    // 3. החזרת החדרים למלאי
                    string returnStock = "UPDATE PackageDates SET AvailableRooms = AvailableRooms + @Amount WHERE Id = @DateId";
                    using (SqlCommand cmd = new SqlCommand(returnStock, conn))
                    {
                        cmd.Parameters.AddWithValue("@DateId", packageDateId);
                        cmd.Parameters.AddWithValue("@Amount", amountToReturn);
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            return RedirectToAction("OrderHistory");
        }
    }
}