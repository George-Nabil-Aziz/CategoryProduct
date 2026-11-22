using System.ComponentModel.DataAnnotations;

namespace CategoryProduct.MVC.Models
{
    public class ProfileViewModel
    {
        [EmailAddress]
        [StringLength(254)]
        [Display(Name = "Email")]
        public string? Email { get; set; }
    }

    /// <summary>The signed-in user's own profile as returned by api/account/me.</summary>
    public record ProfileItem(int Id, string UserName, string Role, string? Email);
}
