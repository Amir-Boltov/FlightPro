using Microsoft.AspNetCore.Mvc;
using FlightPro.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http; // Required for Session
using System;
using Microsoft.AspNetCore.Identity;

namespace FlightPro.Controllers
{
    public class UserController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly EmailService _emailService;

        public UserController(IConfiguration configuration, EmailService emailService)
        {
            _configuration = configuration;
            _emailService = emailService;
        }

        // Register Page (GET)
        public IActionResult ViewRegister()
        {
            return View();
        }


        // Register Logic (POST)

        [HttpPost]
        public async Task<IActionResult> Register(UserModel user) // Changed to Async
        {
            if (!ModelState.IsValid)
            {
                return View("ViewRegister", user);
            }

            string passwordHash = BCrypt.Net.BCrypt.HashPassword(user.Password);
            string connectionString = _configuration.GetConnectionString("myConnect");

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                string query = @"
            INSERT INTO Users (FirstName, LastName, Email, PasswordHash, Role, Status) 
            VALUES (@FirstName, @LastName, @Email, @PasswordHash, 'User', 'Active')";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@FirstName", user.FirstName);
                    command.Parameters.AddWithValue("@LastName", user.LastName);
                    command.Parameters.AddWithValue("@Email", user.Email);
                    command.Parameters.AddWithValue("@PasswordHash", passwordHash);

                    try
                    {
                        connection.Open();
                        command.ExecuteNonQuery(); // Execute the INSERT

                        // --- NEW EMAIL LOGIC START ---
                        string subject = "Welcome to FlightPro!";
                        string body = $@"
                    <div style='font-family: Arial, sans-serif; padding: 20px;'>
                        <h2>Welcome, {user.FirstName}!</h2>
                        <p>Thank you for registering with <strong>FlightPro</strong>.</p>
                        <p>Your account has been successfully created.</p>
                        <p>You can now log in to book your next adventure!</p>
                    </div>";

                        // We await this so the user doesn't get redirected until the email is sent
                        await _emailService.SendEmailAsync(user.Email, subject, body);
                        // --- NEW EMAIL LOGIC END ---

                        return RedirectToAction("ViewLogin");
                    }
                    catch (SqlException ex)
                    {
                        // Check for duplicate Email (Error 2627 or 2601)
                        if (ex.Number == 2627 || ex.Number == 2601)
                        {
                            ViewBag.Error = "This email is already registered.";
                        }
                        else
                        {
                            ViewBag.Error = "Database error: " + ex.Message;
                        }
                        return View("ViewRegister", user);
                    }
                }
            }
        }
        public IActionResult ViewLogin()
        {
            return View();
        }
        [HttpPost]
        public IActionResult Login(string email, string password)
        {
            string connectionString = _configuration.GetConnectionString("myConnect");

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                // 1. UPDATE QUERY: Add 'Status' to the SELECT list
                string query = "SELECT Id, FirstName, Email, Role, PasswordHash, Status FROM Users WHERE Email = @Email";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Email", email);

                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string dbPasswordHash = reader["PasswordHash"].ToString();

                            bool isPasswordValid = BCrypt.Net.BCrypt.Verify(password, dbPasswordHash);

                            if (isPasswordValid)
                            {
                                // 2. RETRIEVE STATUS
                                string status = reader["Status"] != DBNull.Value ? reader["Status"].ToString() : "Active";

                                // 3. CHECK STATUS: If Suspended, stop here
                                if (status == "Suspended")
                                {
                                    ViewBag.Error = "Your account has been suspended. Please contact support.";
                                    return View("ViewLogin");
                                }

                                // --- Login Success Logic ---
                                int userId = (int)reader["Id"];
                                string firstName = reader["FirstName"].ToString();
                                string role = reader["Role"].ToString();

                                HttpContext.Session.SetInt32("UserId", userId);
                                HttpContext.Session.SetString("UserEmail", email);
                                HttpContext.Session.SetString("UserName", firstName);
                                HttpContext.Session.SetString("UserRole", role);

                                return RedirectToAction("Index", "Home");
                            }
                        }

                        // If we get here, either email not found or password incorrect
                        ViewBag.Error = "Invalid email or password.";
                        return View("ViewLogin");
                    }
                }
            }
        }
        // ==========================================
        // Logout
        // ==========================================
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }
    }
}