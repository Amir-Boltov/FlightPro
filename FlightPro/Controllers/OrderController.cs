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
        private readonly PayPalService _payPalService;
        private readonly StripeService _stripeService;

        public OrderController(IConfiguration configuration, PayPalService payPalService, StripeService stripeService)
        {
            _configuration = configuration;
            _payPalService = payPalService;
            _stripeService = stripeService;
        }

        // 1. דף בחירת תשלום
        [HttpGet]
        public IActionResult Checkout()
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Users");

            decimal totalAmount = GetCartTotal(userId.Value);
            if (totalAmount == 0) return RedirectToAction("Index", "MyBook");

            var model = new CheckoutViewModel
            {
                TotalAmount = totalAmount,
                ItemCount = GetItemCount(userId.Value)
            };

            return View(model);
        }

        // 2. התחלת תשלום בפייפאל
        [HttpPost]
        public async Task<IActionResult> PayWithPayPal()
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            decimal totalAmount = GetCartTotal(userId.Value);
            if (totalAmount == 0) return RedirectToAction("Index", "MyBook");

            string returnUrl = Url.Action("PayPalCallback", "Order", null, Request.Scheme);
            string cancelUrl = Url.Action("Checkout", "Order", null, Request.Scheme);

            string approvalUrl = await _payPalService.CreateOrder(totalAmount, returnUrl, cancelUrl);

            return Redirect(approvalUrl);
        }

        // 3. חזרה מפייפאל (Callback)
        public async Task<IActionResult> PayPalCallback(string token)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");

            // חיוב סופי
            string transactionId = await _payPalService.CaptureOrder(token);

            if (!string.IsNullOrEmpty(transactionId))
            {
                MarkOrderAsPaid(userId.Value, transactionId, "PayPal");
                return RedirectToAction("Success");
            }
            return RedirectToAction("Checkout");
        }

        // 4. התחלת תשלום באשראי (Stripe)
        [HttpPost]
        public IActionResult PayWithStripe()
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            decimal totalAmount = GetCartTotal(userId.Value);
            if (totalAmount == 0) return RedirectToAction("Index", "MyBook");

            string returnUrl = Url.Action("StripeCallback", "Order", null, Request.Scheme);
            string cancelUrl = Url.Action("Checkout", "Order", null, Request.Scheme);

            string paymentUrl = _stripeService.CreateCheckoutSession(totalAmount, returnUrl, cancelUrl);

            return Redirect(paymentUrl);
        }

        // 5. חזרה מסטרייפ (Callback)
        public IActionResult StripeCallback(string session_id)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");

            // כאן ההנחה היא שאם חזרנו לכאן הכל תקין.
            // במערכת גדולה בודקים את ה-session_id מול סטרייפ שוב.

            MarkOrderAsPaid(userId.Value, session_id, "Credit Card");
            return RedirectToAction("Success");
        }

        // 6. דף תודה
        public IActionResult Success()
        {
            return View();
        }

        //  פונקציות עזר (SQL) 

        private decimal GetCartTotal(int userId)
        {
            decimal total = 0;
            string connectionString = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string sql = "SELECT SUM(TotalPrice) FROM Bookings WHERE UserId = @UserId AND Status = 'InCart'";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    object result = cmd.ExecuteScalar();
                    if (result != DBNull.Value) total = (decimal)result;
                }
            }
            return total;
        }

        private int GetItemCount(int userId)
        {
            // סתם פונקציה שתחזיר כמה פריטים יש להציג ב-View
            int count = 0;
            string connectionString = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connectionString))
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
            string connectionString = _configuration.GetConnectionString("myConnect");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                // עדכון הסטטוס, שמירת מספר העסקה ותאריך
                string sql = @"
                    UPDATE Bookings 
                    SET IsPaid = 1, 
                        Status = 'Confirmed', 
                        TransactionId = @TxId,
                        CreatedAt = GETDATE() 
                    WHERE UserId = @UserId AND Status = 'InCart'";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@TxId", txId);
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}