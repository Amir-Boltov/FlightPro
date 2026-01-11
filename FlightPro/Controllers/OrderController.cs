using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using FlightPro.Models;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace FlightPro.Controllers
{
    public class OrderController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly PayPalService _payPalService;
        private readonly StripeService _stripeService;

        public OrderController(IConfiguration configuration, PayPalService payPalService, StripeService stripeService)
        {
            _configuration = configuration;
            _payPalService = payPalService;
            _stripeService = stripeService;
        }

        // ==========================================
        //              1. BUY NOW ACTION
        // ==========================================
        [HttpPost]
        public IActionResult BuyNow(int packageId, int packageDateId, int amount)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            // 1. Create Booking AND Deduct Stock
            int bookingId = CreateDirectBooking(userId.Value, packageId, packageDateId, amount);

            if (bookingId == 0)
            {
                // Likely means sold out or error
                TempData["Error"] = "Could not reserve this trip. It may be sold out.";
                return RedirectToAction("Index", "Trips"); // Or wherever you list trips
            }

            // 2. Go to Checkout with this specific ID
            return RedirectToAction("Checkout", new { bookingId = bookingId });
        }

        // ==========================================
        //              2. UNIFIED CHECKOUT
        // ==========================================
        // GET: Order/Checkout
        // GET: Order/Checkout
        public IActionResult Checkout(int? bookingId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            List<myBook> cartItems = new List<myBook>();
            decimal grandTotal = 0;

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("myConnect")))
            {
                conn.Open();

                // 1. Base query structure (Joins are the same for both cases)
                string baseSql = @"
            SELECT b.Id, b.Amount, b.TotalPrice, 
                   p.Title,
                   img.Url AS MainImageUrl,
                   d.StartDate, d.EndDate
            FROM Bookings b
            JOIN Packages p ON b.PackageId = p.Id
            JOIN PackageDates d ON b.PackageDateId = d.Id
            LEFT JOIN PackageImages img ON p.Id = img.PackageId AND img.IsPrimary = 1 ";

                string whereClause = "";

                // 2. C# DECIDES THE FILTER (Much safer than SQL logic)
                if (bookingId.HasValue && bookingId.Value > 0)
                {
                    // Case A: BUY NOW (Specific Item)
                    // We select by ID strictly. We don't care about status here.
                    whereClause = "WHERE b.UserId = @UserId AND b.Id = @SpecificId";
                }
                else
                {
                    // Case B: CART CHECKOUT
                    // We select everything that is currently in the cart.
                    whereClause = "WHERE b.UserId = @UserId AND b.Status = 'InCart'";
                }

                // Combine them
                string finalSql = baseSql + whereClause;

                using (SqlCommand cmd = new SqlCommand(finalSql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);

                    // Only add this parameter if we are in "Buy Now" mode
                    if (bookingId.HasValue && bookingId.Value > 0)
                    {
                        cmd.Parameters.AddWithValue("@SpecificId", bookingId.Value);
                    }

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var item = new myBook
                            {
                                Id = (int)reader["Id"],
                                Amount = (int)reader["Amount"],
                                TotalPrice = (decimal)reader["TotalPrice"],
                                Package = new PackageModel
                                {
                                    Title = reader["Title"].ToString(),
                                    MainImageUrl = reader["MainImageUrl"] != DBNull.Value ? reader["MainImageUrl"].ToString() : "/images/default.jpg"
                                },
                                PackageDate = new PackageDateModel
                                {
                                    StartDate = (DateTime)reader["StartDate"],
                                    EndDate = (DateTime)reader["EndDate"]
                                }
                            };
                            cartItems.Add(item);
                            grandTotal += item.TotalPrice ?? 0;
                        }
                    }
                }
            }

            var viewModel = new CheckoutViewModel
            {
                Items = cartItems,
                TotalAmount = grandTotal,
                ItemCount = cartItems.Count,
                DirectBookingId = bookingId
            };

            return View(viewModel);
        }

        // ==========================================
        //              3. PAYPAL FLOW
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> PayWithPayPal(int? bookingId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            decimal totalAmount = GetTotalAmount(userId.Value, bookingId);
            if (totalAmount == 0) return RedirectToAction("Index", "MyBook");

            string returnUrl = Url.Action("PayPalCallback", "Order", new { bookingId = bookingId }, Request.Scheme);
            string cancelUrl = Url.Action("Checkout", "Order", new { bookingId = bookingId }, Request.Scheme);

            try
            {
                string approvalUrl = await _payPalService.CreateOrder(totalAmount, returnUrl, cancelUrl);
                return Redirect(approvalUrl);
            }
            catch (Exception)
            {
                return RedirectToAction("Checkout", new { bookingId = bookingId });
            }
        }

        public async Task<IActionResult> PayPalCallback(string token, int? bookingId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            string transactionId = await _payPalService.CaptureOrder(token);

            if (!string.IsNullOrEmpty(transactionId))
            {
                // ONE method handles both cases now
                MarkAsPaid(userId.Value, transactionId, bookingId);
                return RedirectToAction("Success");
            }

            return RedirectToAction("Checkout", new { bookingId = bookingId });
        }

        // ==========================================
        //              4. STRIPE FLOW
        // ==========================================
        [HttpPost]
        public IActionResult PayWithStripe(int? bookingId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            decimal totalAmount = GetTotalAmount(userId.Value, bookingId);
            if (totalAmount == 0) return RedirectToAction("Index", "MyBook");

            string returnUrl = Url.Action("StripeCallback", "Order", new { bookingId = bookingId }, Request.Scheme);

            // Fix URL formatting
            returnUrl += returnUrl.Contains("?") ? "&session_id={CHECKOUT_SESSION_ID}" : "?session_id={CHECKOUT_SESSION_ID}";

            string cancelUrl = Url.Action("Checkout", "Order", new { bookingId = bookingId }, Request.Scheme);

            string paymentUrl = _stripeService.CreateCheckoutSession(totalAmount, returnUrl, cancelUrl);

            return Redirect(paymentUrl);
        }

        public IActionResult StripeCallback(string session_id, int? bookingId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            if (string.IsNullOrEmpty(session_id))
            {
                return RedirectToAction("Checkout", new { bookingId = bookingId });
            }

            string cleanTxId = session_id.Contains("?") ? session_id.Split('?')[0] : session_id;

            // ONE method handles both cases now
            MarkAsPaid(userId.Value, cleanTxId, bookingId);

            return RedirectToAction("Success");
        }

        public IActionResult Success()
        {
            return View();
        }

        // ==========================================
        //           5. SMART SQL HELPERS
        // ==========================================

        private decimal GetTotalAmount(int userId, int? bookingId)
        {
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("myConnect")))
            {
                conn.Open();
                string sql;

                if (bookingId.HasValue && bookingId.Value > 0)
                {
                    // Case A: Single Item
                    sql = "SELECT TotalPrice FROM Bookings WHERE Id = @Id AND UserId = @UserId";
                }
                else
                {
                    // Case B: Full Cart
                    sql = "SELECT SUM(TotalPrice) FROM Bookings WHERE UserId = @UserId AND Status = 'InCart'";
                }

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    if (bookingId.HasValue) cmd.Parameters.AddWithValue("@Id", bookingId.Value);

                    object result = cmd.ExecuteScalar();
                    return result != null && result != DBNull.Value ? (decimal)result : 0;
                }
            }
        }

        private int CreateDirectBooking(int userId, int packageId, int dateId, int amount)
        {
            int newId = 0;
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("myConnect")))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // 1. CHECK STOCK FIRST
                        string checkStockSql = "SELECT AvailableRooms, Price FROM PackageDates WHERE Id = @DateId";
                        int available = 0;
                        decimal price = 0;

                        using (SqlCommand checkCmd = new SqlCommand(checkStockSql, conn, transaction))
                        {
                            checkCmd.Parameters.AddWithValue("@DateId", dateId);
                            using (SqlDataReader r = checkCmd.ExecuteReader())
                            {
                                if (r.Read())
                                {
                                    available = (int)r["AvailableRooms"];
                                    price = (decimal)r["Price"];
                                }
                            }
                        }

                        if (available < amount) return 0; // Sold out

                        // 2. DEDUCT STOCK (Important!)
                        string updateStockSql = "UPDATE PackageDates SET AvailableRooms = AvailableRooms - @Amount WHERE Id = @DateId";
                        using (SqlCommand stockCmd = new SqlCommand(updateStockSql, conn, transaction))
                        {
                            stockCmd.Parameters.AddWithValue("@Amount", amount);
                            stockCmd.Parameters.AddWithValue("@DateId", dateId);
                            stockCmd.ExecuteNonQuery();
                        }

                        // 3. CREATE BOOKING
                        string insertSql = @"
                            INSERT INTO Bookings (UserId, PackageId, PackageDateId, Amount, TotalPrice, Status, CreatedAt, IsPaid)
                            OUTPUT INSERTED.Id
                            VALUES (@UserId, @PkgId, @DateId, @Amount, @Total, 'PendingDirect', GETDATE(), 0)";

                        using (SqlCommand insertCmd = new SqlCommand(insertSql, conn, transaction))
                        {
                            insertCmd.Parameters.AddWithValue("@UserId", userId);
                            insertCmd.Parameters.AddWithValue("@PkgId", packageId);
                            insertCmd.Parameters.AddWithValue("@DateId", dateId);
                            insertCmd.Parameters.AddWithValue("@Amount", amount);
                            insertCmd.Parameters.AddWithValue("@Total", price * amount); // Or logic for discount

                            newId = (int)insertCmd.ExecuteScalar();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        return 0;
                    }
                }
            }
            return newId;
        }

        private void MarkAsPaid(int userId, string txId, int? specificBookingId)
        {
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("myConnect")))
            {
                conn.Open();
                string sql = @"
                    UPDATE Bookings 
                    SET IsPaid = 1, 
                        Status = 'Confirmed', 
                        TransactionId = @TxId, 
                        CreatedAt = GETDATE() 
                    WHERE UserId = @UserId 
                    AND (
                        (@SpecificId IS NOT NULL AND Id = @SpecificId)
                        OR 
                        (@SpecificId IS NULL AND Status = 'InCart')
                    )";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@TxId", txId ?? "N/A");
                    cmd.Parameters.AddWithValue("@SpecificId", specificBookingId.HasValue ? (object)specificBookingId.Value : DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}