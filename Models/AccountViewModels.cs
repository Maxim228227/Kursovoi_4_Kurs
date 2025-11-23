using System.ComponentModel.DataAnnotations;

namespace Kursovoi.Models
{
    public class LoginViewModel
    {
        [Required]
        public string Login { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterViewModel
    {
        [Required]
        public string Login { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare("Password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Phone]
        public string Phone { get; set; } = string.Empty;
    }
}
