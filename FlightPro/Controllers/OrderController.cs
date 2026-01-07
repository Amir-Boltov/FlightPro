using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using FlightPro.Models;
using System.Threading.Tasks;
using System;

namespace FlightPro.Controllers
{
    public class OrderController : Controller
    {
        private readonly IConfiguration _configuration;
        // Assuming you have these services registered in Program.cs
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

            // 1. Create a specific booking with status 'PendingDirect'.
            //    This isolates it from the 'InCart' items.
            int bookingId = CreateDirectBooking(userId.Value, packageId, packageDateId, amount);

            if (bookingId == 0)
            {
                // Handle error (e.g., date not found)
                return RedirectToAction("Index", "Trips");
            }

            // 2. Redirect to Checkout, passing the specific Booking ID
            return RedirectToAction("Checkout", new { directBookingId = bookingId });
        }

        // ==========================================
        //              2. UNIFIED CHECKOUT
        // ==========================================
        [HttpGet]
        public IActionResult Checkout(int? directBookingId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            decimal totalAmount = 0;
            int itemCount = 0;

            if (directBookingId.HasValue && directBookingId.Value > 0)
            {
                // -- PATH A: Direct Buy Now --
                // Only fetch the total for this specific booking
                totalAmount = GetBookingTotal(directBookingId.Value, userId.Value);
                itemCount = 1;
            }
            else
            {
                // -- PATH B: Standard Shopping Cart --
                // Only fetch items strictly marked as 'InCart'
                totalAmount = GetCartTotal(userId.Value);
                itemCount = GetItemCount(userId.Value);
            }

            // If nothing to pay, go back
            if (totalAmount == 0) return RedirectToAction("Index", "MyBook");

            var model = new CheckoutViewModel
            {
                TotalAmount = totalAmount,
                ItemCount = itemCount,
                DirectBookingId = directBookingId // Important: View must submit this back
            };

            return View(model);
        }

        // ==========================================
        //              3. PAYPAL FLOW
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> PayWithPayPal(int? directBookingId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            // Determine amount based on whether we are paying for a specific ID or the whole cart
            decimal totalAmount = (directBookingId.HasValue && directBookingId.Value > 0)
                ? GetBookingTotal(directBookingId.Value, userId.Value)
                : GetCartTotal(userId.Value);

            if (totalAmount == 0) return RedirectToAction("Index", "MyBook");

            // Pass the ID in the ReturnURL so we don't lose context after PayPal redirects back
            string returnUrl = Url.Action("PayPalCallback", "Order", new { directBookingId = directBookingId }, Request.Scheme);
            string cancelUrl = Url.Action("Checkout", "Order", new { directBookingId = directBookingId }, Request.Scheme);

            try
            {
                string approvalUrl = await _payPalService.CreateOrder(totalAmount, returnUrl, cancelUrl);
                return Redirect(approvalUrl);
            }
            catch (Exception)
            {
                // Log error
                return RedirectToAction("Checkout", new { directBookingId = directBookingId });
            }
        }

        public async Task<IActionResult> PayPalCallback(string token, int? directBookingId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            string transactionId = await _payPalService.CaptureOrder(token);

            if (!string.IsNullOrEmpty(transactionId))
            {
                if (directBookingId.HasValue && directBookingId.Value > 0)
                {
                    // PATH A: Mark ONLY the direct booking as paid
                    MarkBookingAsPaid(directBookingId.Value, transactionId, "PayPal");
                }
                else
                {
                    // PATH B: Mark ALL 'InCart' items as paid
                    MarkOrderAsPaid(userId.Value, transactionId, "PayPal");
                }
                return RedirectToAction("Success");
            }

            // If capture failed, go back to checkout
            return RedirectToAction("Checkout", new { directBookingId = directBookingId });
        }

        // ==========================================
        //              4. STRIPE FLOW
        // ==========================================
        [HttpPost]
        public IActionResult PayWithStripe(int? directBookingId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            decimal totalAmount = (directBookingId.HasValue && directBookingId.Value > 0)
                ? GetBookingTotal(directBookingId.Value, userId.Value)
                : GetCartTotal(userId.Value);

            if (totalAmount == 0) return RedirectToAction("Index", "MyBook");

            // 1. Create the base URL (This might already contain a '?' if directBookingId is present)
            string returnUrl = Url.Action("StripeCallback", "Order", new { directBookingId = directBookingId }, Request.Scheme);

            // 2. FIX: Smartly append the Stripe ID placeholder
            // If the URL already has a '?', we must use '&'
            if (returnUrl.Contains("?"))
            {
                returnUrl += "&session_id={CHECKOUT_SESSION_ID}";
            }
            else
            {
                returnUrl += "?session_id={CHECKOUT_SESSION_ID}";
            }

            string cancelUrl = Url.Action("Checkout", "Order", new { directBookingId = directBookingId }, Request.Scheme);

            string paymentUrl = _stripeService.CreateCheckoutSession(totalAmount, returnUrl, cancelUrl);

            return Redirect(paymentUrl);
        }

        public IActionResult StripeCallback(string session_id, int? directBookingId)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("ViewLogin", "User");

            if (string.IsNullOrEmpty(session_id))
            {
                return RedirectToAction("Checkout", new { directBookingId = directBookingId });
            }

            // FIX: Clean the ID. 
            // If session_id comes in as "cs_test_123?session_id=cs_test_123", this splits it and takes only the first part.
            string cleanTxId = session_id;
            if (cleanTxId.Contains("?"))
            {
                cleanTxId = cleanTxId.Split('?')[0];
            }

            if (directBookingId.HasValue && directBookingId.Value > 0)
            {
                MarkBookingAsPaid(directBookingId.Value, cleanTxId, "Credit Card");
            }
            else
            {
                MarkOrderAsPaid(userId.Value, cleanTxId, "Credit Card");
            }

            return RedirectToAction("Success");
        }

        public IActionResult Success()
        {
            return View();
        }

        // ==========================================
        //           5. SQL HELPER METHODS
        // ==========================================

        private string GetConnString()
        {
            return _configuration.GetConnectionString("myConnect");
        }

        // --- CART HELPERS ---

        private decimal GetCartTotal(int userId)
        {
            decimal total = 0;
            using (SqlConnection conn = new SqlConnection(GetConnString()))
            {
                conn.Open();
                // STRICTLY filter by 'InCart' so we don't accidentally pay for PendingDirect items
                string sql = "SELECT SUM(TotalPrice) FROM Bookings WHERE UserId = @UserId AND Status = 'InCart'";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    object result = cmd.ExecuteScalar();
                    if (result != DBNull.Value && result != null) total = (decimal)result;
                }
            }
            return total;
        }

        private int GetItemCount(int userId)
        {
            int count = 0;
            using (SqlConnection conn = new SqlConnection(GetConnString()))
            {
                conn.Open();
                string sql = "SELECT COUNT(*) FROM Bookings WHERE UserId = @UserId AND Status = 'InCart'";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    count = (int)cmd.ExecuteScalar();
                }
            }
            return count;
        }

        private void MarkOrderAsPaid(int userId, string txId, string method)
        {
            using (SqlConnection conn = new SqlConnection(GetConnString()))
            {
                conn.Open();
                string sql = @"UPDATE Bookings 
                       SET IsPaid = 1, Status = 'Confirmed', TransactionId = @TxId, CreatedAt = GETDATE() 
                       WHERE UserId = @UserId AND Status = 'InCart'";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    // Fix: Handle null explicitly
                    cmd.Parameters.AddWithValue("@TxId", (object)txId ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // --- DIRECT BOOKING HELPERS ---

        private int CreateDirectBooking(int userId, int packageId, int dateId, int amount)
        {
            int newId = 0;
            using (SqlConnection conn = new SqlConnection(GetConnString()))
            {
                conn.Open();
                // We use 'PendingDirect' status.
                // This is the key to separating it from the Cart.
                string sql = @"
                    INSERT INTO Bookings (UserId, PackageId, PackageDateId, Amount, TotalPrice, Status, CreatedAt, IsPaid)
                    OUTPUT INSERTED.Id
                    SELECT @UserId, @PkgId, @DateId, @Amount, (p.Price * @Amount), 'PendingDirect', GETDATE(), 0
                    FROM PackageDates p
                    WHERE p.Id = @DateId";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@PkgId", packageId);
                    cmd.Parameters.AddWithValue("@DateId", dateId);
                    cmd.Parameters.AddWithValue("@Amount", amount);

                    object result = cmd.ExecuteScalar();
                    if (result != null) newId = (int)result;
                }
            }
            return newId;
        }

        private decimal GetBookingTotal(int bookingId, int userId)
        {
            decimal total = 0;
            using (SqlConnection conn = new SqlConnection(GetConnString()))
            {
                conn.Open();
                // Get price for the specific single booking
                string sql = "SELECT TotalPrice FROM Bookings WHERE Id = @Id AND UserId = @UserId";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", bookingId);
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    object result = cmd.ExecuteScalar();
                    if (result != DBNull.Value && result != null) total = (decimal)result;
                }
            }
            return total;
        }

        private void MarkBookingAsPaid(int bookingId, string txId, string method)
        {
            using (SqlConnection conn = new SqlConnection(GetConnString()))
            {
                conn.Open();

                // We use a transaction to ensure stock is only deducted if the status update succeeds
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // Step A: Mark the booking as paid
                        string updateBookingSql = @"
                    UPDATE Bookings 
                    SET IsPaid = 1, 
                        Status = 'Confirmed', 
                        TransactionId = @TxId 
                    WHERE Id = @Id";

                        using (SqlCommand cmd = new SqlCommand(updateBookingSql, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@Id", bookingId);
                            cmd.Parameters.AddWithValue("@TxId", (object)txId ?? DBNull.Value);
                            cmd.ExecuteNonQuery();
                        }

                        // Step B: Deduct the spots from PackageDates
                        // This query finds the specific PackageDate for this booking and subtracts the Booking's Amount
                        string updateStockSql = @"
                    UPDATE P
                    SET P.AvailableRooms = P.AvailableRooms - B.Amount
                    FROM PackageDates P
                    INNER JOIN Bookings B ON P.Id = B.PackageDateId
                    WHERE B.Id = @Id";

                        using (SqlCommand cmd = new SqlCommand(updateStockSql, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@Id", bookingId);
                            cmd.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw; // Re-throw the error so we know something went wrong
                    }
                }
            }
        }
    }
}