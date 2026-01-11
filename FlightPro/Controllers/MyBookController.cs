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
            WHERE b.UserId = @UserId AND b.Status IN ('InCart', 'Reserved')";

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

        [HttpPost]
        public IActionResult AddToBasket(int packageId, int packageDateId, int amount)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Json(new { success = false, requireLogin = true });

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("myConnect")))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // ---------------------------------------------------------
                        // STEP 1: Get Price & Discount Info
                        // ---------------------------------------------------------
                        // We Join Packages to get the DiscountPrice.
                        string checkSql = @"
                    SELECT d.AvailableRooms, d.Price, d.DiscountedPrice 
                    FROM PackageDates d
                    JOIN Packages p ON d.PackageId = p.Id
                    WHERE d.Id = @DateId";

                        int availableRooms = 0;
                        decimal finalPrice = 0;

                        using (SqlCommand checkCmd = new SqlCommand(checkSql, conn, transaction))
                        {
                            checkCmd.Parameters.AddWithValue("@DateId", packageDateId);
                            using (SqlDataReader reader = checkCmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    availableRooms = (int)reader["AvailableRooms"];
                                    decimal basePrice = (decimal)reader["Price"];

                                    // LOGIC: Use DiscountPrice if it exists and is greater than 0
                                    decimal discountPrice = reader["DiscountedPrice"] != DBNull.Value
                                                            ? (decimal)reader["DiscountedPrice"]
                                                            : 0;

                                    finalPrice = (discountPrice > 0) ? discountPrice : basePrice;
                                }
                                else
                                {
                                    return Json(new { success = false, message = "Date not found." });
                                }
                            }
                        }

                        // ---------------------------------------------------------
                        // STEP 2: Stock Check
                        // ---------------------------------------------------------
                        if (availableRooms < amount)
                        {
                            return Json(new { success = false, isFull = true, packageId, packageDateId, requestedAmount = amount });
                        }

                        // ---------------------------------------------------------
                        // STEP 3: Deduct Stock
                        // ---------------------------------------------------------
                        string updateStockSql = "UPDATE PackageDates SET AvailableRooms = AvailableRooms - @Amount WHERE Id = @DateId";
                        using (SqlCommand stockCmd = new SqlCommand(updateStockSql, conn, transaction))
                        {
                            stockCmd.Parameters.AddWithValue("@Amount", amount);
                            stockCmd.Parameters.AddWithValue("@DateId", packageDateId);
                            stockCmd.ExecuteNonQuery();
                        }

                        // ---------------------------------------------------------
                        // STEP 4: Check for Existing Cart Item (Merge Logic)
                        // ---------------------------------------------------------
                        string checkExistingSql = @"
                    SELECT Id, Amount 
                    FROM Bookings 
                    WHERE UserId = @UserId AND PackageDateId = @DateId AND Status = 'InCart'";

                        int existingBookingId = 0;
                        int existingAmount = 0;

                        using (SqlCommand existingCmd = new SqlCommand(checkExistingSql, conn, transaction))
                        {
                            existingCmd.Parameters.AddWithValue("@UserId", userId);
                            existingCmd.Parameters.AddWithValue("@DateId", packageDateId);
                            using (SqlDataReader r = existingCmd.ExecuteReader())
                            {
                                if (r.Read())
                                {
                                    existingBookingId = (int)r["Id"];
                                    existingAmount = (int)r["Amount"];
                                }
                            }
                        }

                        int resultId = 0;

                        if (existingBookingId > 0)
                        {
                            // CASE A: UPDATE EXISTING ROW
                            // We add the new amount to the old amount, and update the TotalPrice
                            string updateBookingSql = @"
                        UPDATE Bookings 
                        SET Amount = Amount + @NewAmount, 
                            TotalPrice = (Amount + @NewAmount) * @PricePerUnit
                        WHERE Id = @Id";

                            using (SqlCommand updateCmd = new SqlCommand(updateBookingSql, conn, transaction))
                            {
                                updateCmd.Parameters.AddWithValue("@NewAmount", amount);
                                updateCmd.Parameters.AddWithValue("@PricePerUnit", finalPrice);
                                updateCmd.Parameters.AddWithValue("@Id", existingBookingId);
                                updateCmd.ExecuteNonQuery();
                            }
                            resultId = existingBookingId;
                        }
                        else
                        {
                            // CASE B: INSERT NEW ROW
                            string insertSql = @"
                        INSERT INTO Bookings (UserId, PackageId, PackageDateId, Amount, TotalPrice, Status, CreatedAt, IsPaid)
                        OUTPUT INSERTED.Id 
                        VALUES (@UserId, @PkgId, @DateId, @Amount, @Total, 'InCart', GETDATE(), 0)";

                            using (SqlCommand insertCmd = new SqlCommand(insertSql, conn, transaction))
                            {
                                insertCmd.Parameters.AddWithValue("@UserId", userId);
                                insertCmd.Parameters.AddWithValue("@PkgId", packageId);
                                insertCmd.Parameters.AddWithValue("@DateId", packageDateId);
                                insertCmd.Parameters.AddWithValue("@Amount", amount);
                                insertCmd.Parameters.AddWithValue("@Total", finalPrice * amount);
                                resultId = (int)insertCmd.ExecuteScalar();
                            }
                        }

                        transaction.Commit();

                        return Json(new { success = true, bookingId = resultId });
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return Json(new { success = false, message = "Error: " + ex.Message });
                    }
                }
            }
        }

        [HttpPost]
        public IActionResult JoinWaitingList(int packageId, int packageDateId, int amount)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Json(new { success = false, message = "Login required." });

            try
            {
                string connectionString = _configuration.GetConnectionString("myConnect");
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    // Check if already in waiting list to avoid duplicates
                    string checkSql = "SELECT COUNT(*) FROM WaitingList WHERE UserId = @UserId AND PackageDateId = @DateId AND IsNotified = 0";
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

                    // Insert into Waiting List
                    string sql = @"INSERT INTO WaitingList (UserId, PackageId, PackageDateId, RequestedAmount) 
                           VALUES (@UserId, @PackageId, @DateId, @Amount)";

                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@PackageId", packageId);
                        cmd.Parameters.AddWithValue("@DateId", packageDateId);
                        cmd.Parameters.AddWithValue("@Amount", amount);
                        cmd.ExecuteNonQuery();
                    }
                }
                return Json(new { success = true, message = "You have been added to the waiting list! We will notify you if a spot opens." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error joining list: " + ex.Message });
            }
        }
        // הסרה מהסל (פעולה למוצרים שעדיין לא שולמו)
        public IActionResult RemoveFromBasket(int bookingId)
        {
            string connectionString = _configuration.GetConnectionString("myConnect");
            int dateIdToPromote = 0;

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
                        dateIdToPromote = packageDateId;

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
                    if (dateIdToPromote > 0)
                    {
                        TryPromoteFromWaitlist(dateIdToPromote);
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
            int dateIdToPromote = 0;

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("myConnect")))
            {
                conn.Open();

                // FIX 1: Wrap in Transaction for safety
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        int packageDateId = 0;
                        int amountToReturn = 0;

                        // FIX 2: Check Status != 'Canceled' to prevent double-refunds
                        string getSql = @"SELECT PackageDateId, Amount 
                                  FROM Bookings 
                                  WHERE Id = @Id AND UserId = @UserId AND Status != 'Canceled'";

                        using (SqlCommand cmd = new SqlCommand(getSql, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@Id", bookingId);
                            cmd.Parameters.AddWithValue("@UserId", userId);
                            using (SqlDataReader reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    packageDateId = (int)reader["PackageDateId"];
                                    amountToReturn = reader["Amount"] != DBNull.Value ? (int)reader["Amount"] : 0;
                                }
                                else
                                {
                                    // If we find no rows, it means it doesn't exist OR is already canceled.
                                    // We stop here to protect the data.
                                    return RedirectToAction("OrderHistory");
                                }
                            }
                        }
                        dateIdToPromote = packageDateId;

                        // 2. Update Status
                        string updateBooking = "UPDATE Bookings SET Status = 'Canceled' WHERE Id = @Id";
                        using (SqlCommand cmd = new SqlCommand(updateBooking, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@Id", bookingId);
                            cmd.ExecuteNonQuery();
                        }

                        // 3. Return Stock
                        if (packageDateId > 0 && amountToReturn > 0)
                        {
                            string returnStock = "UPDATE PackageDates SET AvailableRooms = AvailableRooms + @Amount WHERE Id = @DateId";
                            using (SqlCommand cmd = new SqlCommand(returnStock, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@DateId", packageDateId);
                                cmd.Parameters.AddWithValue("@Amount", amountToReturn);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        // Handle error
                    }
                }
            }
            if (dateIdToPromote > 0)
            {
                TryPromoteFromWaitlist(dateIdToPromote);
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
            HashSet<int> dateIdsToCheck = new HashSet<int>();

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
                            dateIdsToCheck.Add(item.PackageDateId);

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
            foreach (int dateId in dateIdsToCheck)
            {
                TryPromoteFromWaitlist(dateId);
            }
        }

        [HttpPost]
        public IActionResult RemoveExpiredItem(int bookingId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Json(new { success = false, message = "Not logged in" });
            int dateIdToPromote = 0;

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
                        dateIdToPromote = packageDateId;
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
                        
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return Json(new { success = false, message = ex.Message });
                    }
                }
                if (dateIdToPromote > 0)
                {
                    TryPromoteFromWaitlist(dateIdToPromote);
                }

                return Json(new { success = true });
            }
        }
        private void TryPromoteFromWaitlist(int packageDateId)
        {
            string connectionString = _configuration.GetConnectionString("myConnect");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // 1. Check current stock
                        int currentStock = 0;
                        string checkStockSql = "SELECT AvailableRooms FROM PackageDates WHERE Id = @DateId";
                        using (SqlCommand cmdStock = new SqlCommand(checkStockSql, conn, transaction))
                        {
                            cmdStock.Parameters.AddWithValue("@DateId", packageDateId);
                            object result = cmdStock.ExecuteScalar();
                            currentStock = result != DBNull.Value ? (int)result : 0;
                        }

                        // 2. Find the correct candidate (User B) AND the Price
                        string findWaitlistSql = @"
                    SELECT TOP 1 
                        w.Id, w.UserId, w.PackageId, w.RequestedAmount, 
                        pd.Price, pd.DiscountedPrice
                    FROM WaitingList w
                    JOIN PackageDates pd ON w.PackageDateId = pd.Id
                    WHERE w.PackageDateId = @DateId 
                    AND w.RequestedAmount <= @CurrentStock 
                    ORDER BY w.JoinedAt ASC";

                        int waitlistId = 0;
                        int userIdToPromote = 0;
                        int packageId = 0;
                        int amountNeeded = 0;
                        decimal finalPerPersonPrice = 0;

                        using (SqlCommand cmdFind = new SqlCommand(findWaitlistSql, conn, transaction))
                        {
                            cmdFind.Parameters.AddWithValue("@DateId", packageDateId);
                            cmdFind.Parameters.AddWithValue("@CurrentStock", currentStock);

                            using (SqlDataReader reader = cmdFind.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    waitlistId = (int)reader["Id"];
                                    userIdToPromote = (int)reader["UserId"]; // <--- IMPORTANT: This is User B
                                    packageId = (int)reader["PackageId"];
                                    amountNeeded = (int)reader["RequestedAmount"];

                                    decimal regularPrice = reader["Price"] != DBNull.Value ? Convert.ToDecimal(reader["Price"]) : 0;
                                    decimal discountPrice = reader["DiscountedPrice"] != DBNull.Value ? Convert.ToDecimal(reader["DiscountedPrice"]) : 0;

                                    // If discount exists and is valid (greater than 0), use it. Otherwise use regular.
                                    if (discountPrice > 0)
                                    {
                                        finalPerPersonPrice = discountPrice;
                                    }
                                    else
                                    {
                                        finalPerPersonPrice = regularPrice;
                                    }

                                }
                            }
                        }

                        if (waitlistId > 0)
                        {
                            // Calculate the total price so it doesn't show as empty/zero in the cart
                            decimal finalTotalPrice = finalPerPersonPrice * amountNeeded;

                            // 3. Create the Booking
                            // We use 'Reserved' because your Index action supports it
                            string createBookingSql = @"
                        INSERT INTO Bookings (UserId, PackageId, PackageDateId, Amount, TotalPrice, Status, CreatedAt)
                        VALUES (@UserId, @PackageId, @DateId, @Amount, @TotalPrice, 'Reserved', GETDATE())";

                            using (SqlCommand cmdBook = new SqlCommand(createBookingSql, conn, transaction))
                            {
                                // BUG FIX: Use 'userIdToPromote' (User B), NOT the current Session User
                                cmdBook.Parameters.AddWithValue("@UserId", userIdToPromote);

                                cmdBook.Parameters.AddWithValue("@PackageId", packageId);
                                cmdBook.Parameters.AddWithValue("@DateId", packageDateId);
                                cmdBook.Parameters.AddWithValue("@Amount", amountNeeded);

                                // BUG FIX: Insert the calculated TotalPrice
                                cmdBook.Parameters.AddWithValue("@TotalPrice", finalTotalPrice);

                                cmdBook.ExecuteNonQuery();
                            }

                            // 4. Decrease Stock (so we don't accidentally create rooms out of thin air)
                            string reduceStockSql = "UPDATE PackageDates SET AvailableRooms = AvailableRooms - @Amount WHERE Id = @DateId";
                            using (SqlCommand cmdReduce = new SqlCommand(reduceStockSql, conn, transaction))
                            {
                                cmdReduce.Parameters.AddWithValue("@Amount", amountNeeded);
                                cmdReduce.Parameters.AddWithValue("@DateId", packageDateId);
                                cmdReduce.ExecuteNonQuery();
                            }

                            // 5. Remove from Waitlist
                            string deleteWaitlistSql = "DELETE FROM WaitingList WHERE Id = @Id";
                            using (SqlCommand cmdDel = new SqlCommand(deleteWaitlistSql, conn, transaction))
                            {
                                cmdDel.Parameters.AddWithValue("@Id", waitlistId);
                                cmdDel.ExecuteNonQuery();
                            }

                            transaction.Commit();
                        }
                        else
                        {
                            // No candidate found to fill the spot
                            transaction.Rollback();
                        }
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        System.Diagnostics.Debug.WriteLine("PROMOTION ERROR: " + ex.Message);
                    }
                }
            }
        }
    }
}