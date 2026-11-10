using Cupcakes.Api.Contracts;
using Cupcakes.Api.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cupcakes.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(RegistrationService registration, UserManager<IdentityUser> users,
    SignInManager<IdentityUser> signIn) : ControllerBase
{
    [HttpGet("antiforgery")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<AntiforgeryResponse> GetAntiforgery([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new AntiforgeryResponse(tokens.RequestToken!));
    }

    [HttpGet("me")]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionResponse>> GetSession()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();

        return Ok(new SessionResponse(user.Id, user.Email!, (await users.GetRolesAsync(user)).ToArray()));
    }

    [HttpPost("logout")]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    [HttpPost("login")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is not null)
        {
            var result = await signIn.PasswordSignInAsync(user, request.Password,
                isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                var roles = await users.GetRolesAsync(user);
                return Ok(new LoginResponse(user.Id, user.Email!, roles.ToArray()));
            }
        }

        return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Não foi possível entrar.",
            detail: "Confira e-mail e senha ou tente novamente mais tarde.");
    }

    [HttpPost("register")]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await registration.RegisterAsync(request, cancellationToken);
        if (result.User is { } user)
        {
            return StatusCode(StatusCodes.Status201Created, new RegisterResponse(user.Id, user.Email!, "Conta criada com sucesso."));
        }

        if (result.Result.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Este e-mail já está cadastrado.");
        }

        var errors = result.Result.Errors
            .GroupBy(error => error.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "email")
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray());
        return ValidationProblem(new ValidationProblemDetails(errors));
    }
}
