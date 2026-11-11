using System.ComponentModel.DataAnnotations;
using Cupcakes.Api.Data;
using Cupcakes.Api.Identity;
using Microsoft.AspNetCore.Identity;

namespace Cupcakes.Api.Services;

public sealed class AdminProvisioningService(UserManager<IdentityUser> users,
    RoleManager<IdentityRole> roles, CupcakesDbContext db)
{
    public async Task<AdminProvisioningResult> ProvisionAsync(string? email, string? password,
        CancellationToken cancellationToken = default)
    {
        email = email?.Trim();
        if (string.IsNullOrEmpty(email) || email.Length > 256 || !new EmailAddressAttribute().IsValid(email)
            || string.IsNullOrEmpty(password) || password.Length > 128)
            return new(false, "Configure Administrator:Email e Administrator:Password com e-mail valido e senha de ate 128 caracteres.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
            return await users.IsInRoleAsync(existing, ApplicationRoles.Admin)
                ? new(true, "Administrador ja existente. Conta e senha preservadas.")
                : new(false, "E-mail pertence a uma conta sem papel administrativo. Escolha um e-mail novo.");

        var user = new IdentityUser { Email = email, UserName = email };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            return Failed(created);

        if (!await roles.RoleExistsAsync(ApplicationRoles.Admin))
        {
            var roleCreated = await roles.CreateAsync(new IdentityRole(ApplicationRoles.Admin));
            if (!roleCreated.Succeeded) return Failed(roleCreated);
        }

        var assigned = await users.AddToRoleAsync(user, ApplicationRoles.Admin);
        if (!assigned.Succeeded) return Failed(assigned);

        await transaction.CommitAsync(cancellationToken);
        return new(true, "Administrador criado. Use o login existente da aplicacao.");
    }

    private static AdminProvisioningResult Failed(IdentityResult result) =>
        new(false, "Nao foi possivel provisionar: " + string.Join(", ", result.Errors.Select(error => error.Code)) + ". Nenhuma alteracao foi confirmada.");
}

public sealed record AdminProvisioningResult(bool Succeeded, string Message);
