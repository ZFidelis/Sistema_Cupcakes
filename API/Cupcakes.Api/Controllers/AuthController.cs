using Cupcakes.Api.Contracts;
using Cupcakes.Api.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace Cupcakes.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(RegistrationService registration) : ControllerBase
{
    [HttpGet("antiforgery")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<AntiforgeryResponse> GetAntiforgery([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new AntiforgeryResponse(tokens.RequestToken!));
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
