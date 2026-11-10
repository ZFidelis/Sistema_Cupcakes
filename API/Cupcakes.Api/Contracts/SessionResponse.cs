namespace Cupcakes.Api.Contracts;

public sealed record SessionResponse(string Id, string Email, string[] Roles);
