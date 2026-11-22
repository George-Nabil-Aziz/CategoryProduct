using System.ComponentModel.DataAnnotations;

namespace CategoryProduct.API.Dtos
{
    public record ChangePasswordRequestDto(
        [Required(AllowEmptyStrings = false)] string CurrentPassword,
        [Required(AllowEmptyStrings = false)] [MinLength(8)] string NewPassword);

    public record ForgotPasswordRequestDto(
        [Required(AllowEmptyStrings = false)] [StringLength(254)] string UserNameOrEmail);

    public record ResetPasswordRequestDto(
        [Required(AllowEmptyStrings = false, ErrorMessage = "The reset link is invalid or has expired.")] string Token,
        [Required(AllowEmptyStrings = false)] [MinLength(8)] string NewPassword);
}
