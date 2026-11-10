namespace Cupcakes.Api.Contracts;

public sealed record LoginResponse(string Id, string Email, string[] Roles);
