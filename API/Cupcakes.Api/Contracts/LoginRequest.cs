using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Cupcakes.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LoginRequest
{
    private string email = string.Empty;

    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [StringLength(256, ErrorMessage = "O e-mail deve ter no máximo 256 caracteres.")]
    public string Email
    {
        get => email;
        init => email = value?.Trim() ?? string.Empty;
    }

    [Required(ErrorMessage = "Informe a senha.")]
    [StringLength(128, ErrorMessage = "A senha deve ter no máximo 128 caracteres.")]
    public string Password { get; init; } = string.Empty;
}
