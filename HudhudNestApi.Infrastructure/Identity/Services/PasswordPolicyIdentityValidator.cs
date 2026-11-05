using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Services;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Identity.Services;

/// <summary>
/// Runs the project's own local password rules (<see cref="PasswordPolicy"/>) inside ASP.NET Identity,
/// so that EVERY path that ends in <c>UserManager</c> -- e-mail registration, phone registration, e-mail
/// and phone password reset, change password -- enforces the same policy. Before this, only the e-mail
/// registration / reset / change validators called <see cref="IPasswordSecurityService"/>; the phone paths
/// relied on Identity's options alone and accepted passwords the e-mail paths refuse.
///
/// Network-bound breach screening stays in <see cref="IPasswordSecurityService"/> (a validator that
/// calls out would run twice on e-mail paths); this validator only needs the database.
///
/// The personal details come from the user being saved: its e-mail / phone, and the first and last name
/// from the profile row when one exists (a user being created has none yet; the registration paths pass
/// the names to <see cref="IPasswordSecurityService"/> themselves).
/// </summary>
public sealed class PasswordPolicyIdentityValidator : IPasswordValidator<ApplicationUser>
{
    private readonly AppDbContext _db;

    public PasswordPolicyIdentityValidator(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password)
    {
        var errors = PasswordPolicy.ValidateBasicRequirements(password);

        if (errors.Count == 0)
        {
            var name = user.Id == Guid.Empty
                ? null
                : await _db.UserAccounts
                    .AsNoTracking()
                    .Where(account => account.Id == user.Id)
                    .Select(account => new { account.FirstName, account.LastName })
                    .FirstOrDefaultAsync();

            var context = new PasswordCheckContext(
                FirstName: name?.FirstName,
                LastName: name?.LastName,
                Email: user.Email,
                PhoneNumber: user.PhoneNumber,
                UserName: user.UserName);

            errors = PasswordPolicy.ValidatePatterns(password!, context);
        }

        return errors.Count == 0
            ? IdentityResult.Success
            : IdentityResult.Failed(errors
                .Select(error => new IdentityError { Code = error.Code, Description = error.Message })
                .ToArray());
    }
}
