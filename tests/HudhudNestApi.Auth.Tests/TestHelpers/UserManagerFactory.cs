using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using HudhudNestApi.Infrastructure.Identity.Entities;


namespace HudhudNestApi.Auth.Tests.TestHelpers;

public static class UserManagerFactory
{
    public static Mock<UserManager<ApplicationUser>> Create()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var options = Options.Create(new IdentityOptions());
        var passwordHasher = new PasswordHasher<ApplicationUser>();
        var userValidators = Array.Empty<IUserValidator<ApplicationUser>>();
        var passwordValidators = Array.Empty<IPasswordValidator<ApplicationUser>>();
        var normalizer = new Mock<ILookupNormalizer>();
        var describer = new IdentityErrorDescriber();
        var services = Mock.Of<IServiceProvider>();
        var logger = NullLogger<UserManager<ApplicationUser>>.Instance;

        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            options,
            passwordHasher,
            userValidators,
            passwordValidators,
            normalizer.Object,
            describer,
            services,
            logger);
    }

    public static Mock<UserManager<ApplicationUser>> CreateWithDefaults(
        ApplicationUser? userToReturn = null,
        bool createSucceeds = true)
    {
        var manager = Create();

        if (userToReturn is not null)
        {
            manager
                .Setup(m => m.FindByIdAsync(userToReturn.Id.ToString()))
                .ReturnsAsync(userToReturn);

            if (!string.IsNullOrWhiteSpace(userToReturn.PhoneNumber))
            {
                manager
                    .Setup(m => m.FindByNameAsync(userToReturn.PhoneNumber))
                    .ReturnsAsync(userToReturn);
            }
        }

        manager
            .Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(createSucceeds
                ? IdentityResult.Success
                : IdentityResult.Failed(new IdentityError
                {
                    Code = "CreateFailed",
                    Description = "Creation failed."
                }));

        manager
            .Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        manager
            .Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new[] { "ApplicationUser" });

        return manager;
    }
}

public static class UserBuilder
{
    public static ApplicationUser Valid(
        Guid? id = null,
        string phone = "+963911234567",
        string? email = null,
        bool emailConfirmed = false,
        bool phoneConfirmed = true,
        bool isDeleted = false,
        string? passwordHash = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            UserName = phone,
            NormalizedUserName = phone.ToUpperInvariant(),
            PhoneNumber = phone,
            PhoneNumberConfirmed = phoneConfirmed,
            Email = email,
            NormalizedEmail = email?.ToUpperInvariant(),
            EmailConfirmed = emailConfirmed,
            CreatedAt = DateTime.UtcNow.AddDays(-7),
            UpdatedAt = DateTime.UtcNow.AddDays(-1),
            IsDeleted = isDeleted,
            PasswordHash = passwordHash
        };

    public static ApplicationUser WithVerifiedEmail(string email = "naeem@example.com") =>
        Valid(email: email, emailConfirmed: true);

    public static ApplicationUser Deleted() => Valid(isDeleted: true);
}
