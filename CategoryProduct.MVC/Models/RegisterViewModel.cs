using System.ComponentModel.DataAnnotations;

namespace CategoryProduct.MVC.Models
{
    public class RegisterViewModel
    {
        [Required]
        [StringLength(50, MinimumLength = 3)]
        [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Only letters, digits, '.', '_' and '-' are allowed.")]
        [Display(Name = "User name")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public record UserItem(int Id, string UserName, string Role);
}
