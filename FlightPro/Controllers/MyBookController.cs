using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using FlightPro.Models;
using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace FlightPro.Controllers
{
    public class MyBookController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public MyBookController(IConfiguration configuration, IWebHostEnvironment webHostEnvironment)
        {
            _configuration = configuration;
            _webHostEnvironment = webHostEnvironment;
        }

        // ==========================================
        // חלק 1: סל הקניות (Shopping Cart)
        // ==========================================

        // דף הסל - מציג הזמנות בסטטוס InCart
        public IActionResult Index()
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            // 1. CLEANUP: Remove items that expired before we load the page
            CleanupExpiredCartItems(userId.Value);

            List<myBook> cartItems = new List<myBook>();
            string connectionString = _configuration.GetConnectionString("myConnect");

            // REMOVED: int timeoutMinutes = 15; (This was dead code)

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                // We fetch GETDATE() as 'CurrentDbTime' to ensure the timer matches the database clock exactly
                string sql = @"
            SELECT 
                b.Id, b.UserId, b.PackageId, b.PackageDateId, b.Amount, b.TotalPrice, b.Status, b.CreatedAt,
                p.Title, p.Description, p.ExpiryMinutes,
                d.Name AS CityName, d.Country,
                pd.StartDate, pd.EndDate,
                (SELECT TOP 1 Url FROM PackageImages WHERE PackageId = p.Id AND IsPrimary=1) AS MainImageUrl,
                GETDATE() as CurrentDbTime 
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
                                Status = reader["Status"].ToString(),
                                CreatedAt = (DateTime)reader["CreatedAt"]
                            };

                            // SAFER: Handle nulls for ExpiryMinutes (default to 15 if missing)
                            int expiryLimit = reader["ExpiryMinutes"] != DBNull.Value ? (int)reader["ExpiryMinutes"] : 15;

                            // SAFER: Use the DB time, not the Web Server time
                            DateTime dbNow = (DateTime)reader["CurrentDbTime"];

                            // Fix: Use .Value to get the actual DateTime
                            DateTime expiryTime = bookItem.CreatedAt.Value.AddMinutes(expiryLimit);
                            TimeSpan diff = expiryTime - dbNow;

                            bookItem.RemainingSeconds = diff.TotalSeconds > 0 ? (int)diff.TotalSeconds : 0;

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
            if (userId == null) return RedirectToAction("ViewLogin", "User");

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
            if (userId == null) return RedirectToAction("ViewLogin", "User");

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
            if (userId == null) return RedirectToAction("ViewLogin", "User");

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

        public IActionResult DownloadTicket(int bookingId)
        {
            string passengerName = HttpContext.Session.GetString("UserName") ?? "Guest";
            // Default values
            string bookingRef = bookingId.ToString();
            string status = "Confirmed";
            string destinationCity = "Unknown";
            string destinationCountry = "";
            string packageTitle = "";
            string packageDescription = "";
            string startDate = "";
            string endDate = "";
            string cancellationDeadline = "";
            string amount = "";

            byte[] logoBytes = null;
            byte[] promoBytes = null;

            string connStr = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                // Note: I added 'b.Amount' to your select list just in case
                string sql = @"
                SELECT b.Id AS BookingId, b.Status, u.FirstName, u.LastName, p.Title, p.Description,
                       d.Name AS City, d.Country, pd.StartDate, pd.EndDate, b.Amount
                FROM Bookings b
                JOIN Users u ON b.UserId = u.Id
                JOIN Packages p ON b.PackageId = p.Id
                JOIN Destinations d ON p.DestinationId = d.Id
                JOIN PackageDates pd ON b.PackageDateId = pd.Id
                WHERE b.Id = @id";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", bookingId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string first = reader["FirstName"].ToString();
                            string last = reader["LastName"].ToString();
                            passengerName = $"{first} {last}";
                            bookingRef = reader["BookingId"].ToString();
                            status = reader["Status"] != DBNull.Value ? reader["Status"].ToString() : "Confirmed";
                            packageTitle = reader["Title"].ToString();
                            packageDescription = reader["Description"].ToString();
                            destinationCity = reader["City"].ToString();
                            destinationCountry = reader["Country"].ToString();
                            amount = reader["Amount"].ToString();

                            if (reader["StartDate"] != DBNull.Value)
                            {
                                DateTime dtStart = Convert.ToDateTime(reader["StartDate"]);
                                startDate = dtStart.ToString("dd MMM yyyy");
                                cancellationDeadline = dtStart.AddDays(-7).ToString("dd MMM yyyy");
                            }
                            if (reader["EndDate"] != DBNull.Value)
                                endDate = Convert.ToDateTime(reader["EndDate"]).ToString("dd MMM yyyy");
                        }
                    }
                }
            }

            // --- Load Images ---
            try
            {
                string wroot = _webHostEnvironment.WebRootPath;
                // Ensure you have these images in your wwwroot/img folder
                string logoPath = Path.Combine(wroot, "img", "logo.jpg");
                if (System.IO.File.Exists(logoPath)) logoBytes = System.IO.File.ReadAllBytes(logoPath);

                string promoPath = Path.Combine(wroot, "img", "promo.jpeg");
                if (System.IO.File.Exists(promoPath)) promoBytes = System.IO.File.ReadAllBytes(promoPath);
            }
            catch { }

            // --- Generate PDF ---
            QuestPDF.Settings.License = LicenseType.Community;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    page.Content().Column(col =>
                    {
                        // Header
                        col.Item().Row(row =>
                        {
                            if (logoBytes != null)
                                row.RelativeItem().AlignLeft().Width(60).Image(logoBytes);
                            else
                                row.RelativeItem().Text("FlightPro").FontSize(24).Bold().FontColor("#0097A7");

                            row.RelativeItem().AlignRight().AlignMiddle().Text("Itinerary Receipt").FontSize(16).Bold().FontColor("#007ACC");
                        });
                        col.Item().PaddingTop(5).Height(3).Background("#FFC107");
                        col.Item().PaddingBottom(10);

                        // Booking Details
                        col.Item().Text("Booking Details").FontSize(12).Bold().FontColor("#007ACC");
                        col.Item().PaddingTop(2).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(t => { t.Span("Status: ").Bold(); t.Span(status).FontColor(Colors.Green.Medium); });
                                c.Item().Text($"Date Issued: {DateTime.Now:dd MMM yyyy}");
                            });
                            row.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().Text("BOOKING REF:").FontSize(8).FontColor(Colors.Grey.Darken2);
                                c.Item().Text(bookingRef).FontSize(20).ExtraBold().FontColor(Colors.Black);
                            });
                        });
                        col.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        // Guest Details
                        col.Item().PaddingTop(5).Text("Guest Details").FontSize(12).Bold().FontColor("#007ACC");
                        col.Item().PaddingTop(2).Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(80); });
                            table.Header(h => {
                                h.Cell().Text("Passenger Name").Bold().FontSize(9).FontColor(Colors.Grey.Darken2);
                                h.Cell().AlignRight().Text("Tickets Qty").Bold().FontSize(9).FontColor(Colors.Grey.Darken2);
                            });
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(2).Text(passengerName).FontSize(11).Bold();
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(2).AlignRight().Text(amount).FontSize(11);
                        });

                        col.Item().PaddingVertical(10).LineHorizontal(1.5f).LineColor("#FFC107");

                        // Flight Info
                        col.Item().Text("Flight / Package").FontSize(12).Bold().FontColor("#007ACC");
                        col.Item().PaddingTop(5).Table(table =>
                        {
                            table.ColumnsDefinition(columns => { columns.RelativeColumn(2); columns.RelativeColumn(1); columns.RelativeColumn(2); columns.RelativeColumn(2); });
                            table.Header(header => {
                                header.Cell().Text("Route").Bold(); header.Cell().Text("Code").Bold();
                                header.Cell().Text("Departure").Bold(); header.Cell().Text("Return").Bold();
                            });
                            table.Cell().PaddingTop(2).Text($"{destinationCity}, {destinationCountry}");
                            table.Cell().PaddingTop(2).Text($"PKG-{bookingRef}");
                            table.Cell().PaddingTop(2).Column(c => { c.Item().Text(startDate).Bold(); c.Item().Text("Check-in").FontSize(8).FontColor(Colors.Grey.Medium); });
                            table.Cell().PaddingTop(2).Column(c => { c.Item().Text(endDate).Bold(); c.Item().Text("Check-out"); });
                        });

                        // Description
                        col.Item().PaddingTop(10).Background(Colors.Grey.Lighten4).Padding(10).Column(c =>
                        {
                            c.Item().Text("INFO").Bold().FontSize(10).FontColor("#0097A7");
                            c.Item().PaddingTop(2).Text(packageTitle).Bold();
                            c.Item().Text(packageDescription).FontSize(9);
                        });

                        // Footer Image
                        col.Item().PaddingTop(15);
                        if (promoBytes != null)
                        {
                            col.Item().Image(promoBytes).FitWidth();
                        }
                    });

                    page.Footer().PaddingTop(5).AlignRight().Text(x =>
                    {
                        x.Span("Page "); x.CurrentPageNumber();
                    });
                });
            });

            var stream = new MemoryStream(document.GeneratePdf());
            // Reset stream position just in case
            stream.Position = 0;
            return File(stream, "application/pdf", $"Itinerary_{bookingRef}.pdf");
        }


        private void CleanupExpiredCartItems(int userId)
        {
            string connectionString = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // 1. Find expired items dynamically based on their Package's ExpiryMinutes
                        // Logic: Calculate the expiration time (CreatedAt + ExpiryMinutes) and check if it is in the past.
                        string findExpiredSql = @"
                    SELECT b.Id, b.PackageDateId, b.Amount
                    FROM Bookings b
                    JOIN Packages p ON b.PackageId = p.Id
                    WHERE b.UserId = @UserId 
                      AND b.Status = 'InCart'
                      AND DATEADD(minute, p.ExpiryMinutes, b.CreatedAt) < GETDATE()";

                        // We use a simple list of objects to hold the data we need to process
                        var expiredItems = new List<(int Id, int PackageDateId, int Amount)>();

                        using (SqlCommand cmd = new SqlCommand(findExpiredSql, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@UserId", userId);

                            using (SqlDataReader reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    expiredItems.Add((
                                        (int)reader["Id"],
                                        (int)reader["PackageDateId"],
                                        (int)reader["Amount"]
                                    ));
                                }
                            }
                        }

                        // 2. Loop through expired items: Return stock and Delete booking
                        foreach (var item in expiredItems)
                        {
                            // A. Return Stock
                            string returnStockSql = "UPDATE PackageDates SET AvailableRooms = AvailableRooms + @Amount WHERE Id = @DateId";
                            using (SqlCommand cmdStock = new SqlCommand(returnStockSql, conn, transaction))
                            {
                                cmdStock.Parameters.AddWithValue("@Amount", item.Amount);
                                cmdStock.Parameters.AddWithValue("@DateId", item.PackageDateId);
                                cmdStock.ExecuteNonQuery();
                            }

                            // B. Delete Booking
                            string deleteSql = "DELETE FROM Bookings WHERE Id = @Id";
                            using (SqlCommand cmdDelete = new SqlCommand(deleteSql, conn, transaction))
                            {
                                cmdDelete.Parameters.AddWithValue("@Id", item.Id);
                                cmdDelete.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                    }
                }
            }
        }

        [HttpPost]
        public IActionResult RemoveExpiredItem(int bookingId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Json(new { success = false, message = "Not logged in" });

            string connectionString = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // 1. Get Details to return stock
                        int packageDateId = 0;
                        int amountToReturn = 0;

                        string findOrderSql = "SELECT PackageDateId, Amount FROM Bookings WHERE Id = @Id AND UserId = @UserId";
                        using (SqlCommand cmdFind = new SqlCommand(findOrderSql, conn, transaction))
                        {
                            cmdFind.Parameters.AddWithValue("@Id", bookingId);
                            cmdFind.Parameters.AddWithValue("@UserId", userId);
                            using (SqlDataReader reader = cmdFind.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    packageDateId = reader["PackageDateId"] != DBNull.Value ? (int)reader["PackageDateId"] : 0;
                                    amountToReturn = reader["Amount"] != DBNull.Value ? (int)reader["Amount"] : 0;
                                }
                            }
                        }

                        // 2. Return Stock
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

                        // 3. Delete the Booking
                        string deleteSql = "DELETE FROM Bookings WHERE Id = @Id AND UserId = @UserId";
                        using (SqlCommand cmdDelete = new SqlCommand(deleteSql, conn, transaction))
                        {
                            cmdDelete.Parameters.AddWithValue("@Id", bookingId);
                            cmdDelete.Parameters.AddWithValue("@UserId", userId);
                            cmdDelete.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        return Json(new { success = true });
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return Json(new { success = false, message = ex.Message });
                    }
                }
            }
        }
    }
}