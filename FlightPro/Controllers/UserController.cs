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

        public UserController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // ==========================================
        // Register Page (GET)
        // ==========================================
        public IActionResult ViewRegister()
        {
            return View();
        }

        // ==========================================
        // Register Logic (POST)
        // ==========================================
        [HttpPost]
        public IActionResult Register(UserModel user)
        {
            if (!ModelState.IsValid)
            {
                return View("ViewRegister", user);
            }
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(user.Password);
            string connectionString = _configuration.GetConnectionString("myConnect");

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                // Query matches your new SQL Schema
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

                        // Success -> Go to Login
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

        // ==========================================
        // Login Page (GET)
        // ==========================================
        public IActionResult ViewLogin()
        {
            return View();
        }

        // ==========================================
        // Login Logic (POST)
        // ==========================================
        [HttpPost]
        public IActionResult Login(string email, string password)
        {
            string connectionString = _configuration.GetConnectionString("myConnect");

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                // 1. תיקון השאילתה:
                // א. שולפים רק לפי אימייל (בלי לבדוק סיסמה ב-SQL)
                // ב. חייבים לשלוף את PasswordHash כדי שנוכל לבדוק אותו בקוד
                string query = "SELECT Id, FirstName, Email, Role, PasswordHash FROM Users WHERE Email = @Email";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Email", email);

                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read()) // המשתמש נמצא לפי האימייל
                        {
                            // 2. שליפת ה-Hash המוצפן מהמסד
                            string dbPasswordHash = reader["PasswordHash"].ToString();

                            // 3. אימות הסיסמה:
                            // הפונקציה Verify לוקחת את הסיסמה הרגילה (password)
                            // ובודקת אם היא מתאימה למוצפנת (dbPasswordHash)
                            bool isPasswordValid = BCrypt.Net.BCrypt.Verify(password, dbPasswordHash);

                            if (isPasswordValid)
                            {
                                // סיסמה נכונה! - נכנסים
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

                        // אם הגענו לפה - או שהאימייל לא קיים, או שה-Verify החזיר false
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