using System.ComponentModel.DataAnnotations;

namespace FlightPro.Models
{
    public class UserModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "First Name is required")]
        [Display(Name = "First Name")]
        public string FirstName { get; set; }

        [Required(ErrorMessage = "Last Name is required")]
        [Display(Name = "Last Name")]
        public string LastName { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid Email Address")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        public string Password { get; set; } // Will be saved into 'PasswordHash' column

        // These are set automatically by the controller, not the user
        public string? Role { get; set; }
        public string? Status { get; set; }
    }
}