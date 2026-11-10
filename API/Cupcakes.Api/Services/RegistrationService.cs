using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cupcakes.Api.Services;

public sealed class RegistrationService(UserManager<IdentityUser> users, CupcakesDbContext db)
{
    public async Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = new IdentityUser { Email = request.Email, UserName = request.Email };
            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                return new RegistrationResult(created, null);
            }

            var assigned = await users.AddToRoleAsync(user, ApplicationRoles.Customer);
            if (!assigned.Succeeded)
            {
                throw new InvalidOperationException("Não foi possível atribuir o papel do cliente.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new RegistrationResult(IdentityResult.Success, user);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
               { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "EmailIndex" or "UserNameIndex" })
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RegistrationResult(IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateEmail",
                Description = "Este e-mail já está cadastrado."
            }), null);
        }
    }
}

public sealed record RegistrationResult(IdentityResult Result, IdentityUser? User);
