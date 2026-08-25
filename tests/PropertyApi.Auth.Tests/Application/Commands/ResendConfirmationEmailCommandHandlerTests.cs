using Microsoft.Extensions.Logging.Abstractions;
using PropertyApi.Application.Auth.Commands.ResendConfirmationEmail;
using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Auth.Tests.Application.Commands;

[Trait("Category", "AuthCQRS")]
public sealed class ResendConfirmationEmailCommandHandlerTests
{
    private const string Email = "naeem@example.com";

    [Fact(DisplayName = "Unknown address gets the same answer as a real one, and no email")]
    public async Task UnknownEmail_ReturnsGenericSuccess_AndSendsNothing()
    {
        var identity = new Mock<IResendConfirmationIdentityService>();

        identity
            .Setup(x => x.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityAccountSnapshot?)null);

        var verification = new Mock<IEmailVerificationService>();

        var result = await CreateHandler(identity, verification)
            .Handle(new ResendConfirmationEmailCommand(Email), CancellationToken.None);

        Assert.True(result.Success);
        VerifyNothingSent(verification);
    }

    [Fact(DisplayName = "An already-confirmed address is not mailed again")]
    public async Task ConfirmedEmail_SendsNothing()
    {
        var identity = new Mock<IResendConfirmationIdentityService>();

        identity
            .Setup(x => x.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(emailConfirmed: true));

        var verification = new Mock<IEmailVerificationService>();

        var result = await CreateHandler(identity, verification)
            .Handle(new ResendConfirmationEmailCommand(Email), CancellationToken.None);

        Assert.True(result.Success);
        VerifyNothingSent(verification);
    }

    [Fact(DisplayName = "A deleted account is not mailed")]
    public async Task DeletedIdentity_SendsNothing()
    {
        var identity = new Mock<IResendConfirmationIdentityService>();

        identity
            .Setup(x => x.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(emailConfirmed: false, isDeleted: true));

        var verification = new Mock<IEmailVerificationService>();

        var result = await CreateHandler(identity, verification)
            .Handle(new ResendConfirmationEmailCommand(Email), CancellationToken.None);

        Assert.True(result.Success);
        VerifyNothingSent(verification);
    }

    [Fact(DisplayName = "An unconfirmed account gets a freshly minted link")]
    public async Task UnconfirmedIdentity_SendsTheLink()
    {
        var snapshot = Snapshot(emailConfirmed: false);
        var identity = new Mock<IResendConfirmationIdentityService>();

        identity
            .Setup(x => x.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        identity
            .Setup(x => x.GenerateEmailConfirmationTokenAsync(
                snapshot.IdentityId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("fresh-token");

        var verification = new Mock<IEmailVerificationService>();

        var result = await CreateHandler(identity, verification)
            .Handle(new ResendConfirmationEmailCommand(Email), CancellationToken.None);

        Assert.True(result.Success);

        verification.Verify(
            x => x.SendVerificationLinkAsync(
                Email,
                "fresh-token",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "The address is normalised before it is looked up")]
    public async Task MixedCaseEmail_IsNormalisedBeforeLookup()
    {
        var identity = new Mock<IResendConfirmationIdentityService>();

        identity
            .Setup(x => x.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityAccountSnapshot?)null);

        await CreateHandler(identity, new Mock<IEmailVerificationService>())
            .Handle(
                new ResendConfirmationEmailCommand("  Naeem@Example.COM  "),
                CancellationToken.None);

        identity.Verify(
            x => x.FindByEmailAsync(Email, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "A provider outage still returns the generic answer")]
    public async Task SendFailure_StillReturnsGenericSuccess()
    {
        // If a failed send surfaced as a 500 while an unknown address returned 200, the
        // status code would answer the one question the identical responses exist to
        // refuse: does this account exist?
        var snapshot = Snapshot(emailConfirmed: false);
        var identity = new Mock<IResendConfirmationIdentityService>();

        identity
            .Setup(x => x.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        identity
            .Setup(x => x.GenerateEmailConfirmationTokenAsync(
                snapshot.IdentityId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("fresh-token");

        var verification = new Mock<IEmailVerificationService>();

        verification
            .Setup(x => x.SendVerificationLinkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Resend returned 422"));

        var result = await CreateHandler(identity, verification)
            .Handle(new ResendConfirmationEmailCommand(Email), CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact(DisplayName = "The same message comes back whatever the account state")]
    public async Task AllOutcomes_ShareOneMessage()
    {
        var missing = new Mock<IResendConfirmationIdentityService>();

        missing
            .Setup(x => x.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityAccountSnapshot?)null);

        var existing = new Mock<IResendConfirmationIdentityService>();

        existing
            .Setup(x => x.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(emailConfirmed: false));

        existing
            .Setup(x => x.GenerateEmailConfirmationTokenAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("token");

        var missingResult = await CreateHandler(missing, new Mock<IEmailVerificationService>())
            .Handle(new ResendConfirmationEmailCommand(Email), CancellationToken.None);

        var existingResult = await CreateHandler(existing, new Mock<IEmailVerificationService>())
            .Handle(new ResendConfirmationEmailCommand(Email), CancellationToken.None);

        Assert.Equal(missingResult.Message, existingResult.Message);
    }

    private static ResendConfirmationEmailCommandHandler CreateHandler(
        Mock<IResendConfirmationIdentityService> identity,
        Mock<IEmailVerificationService> verification)
        => new(
            identity.Object,
            verification.Object,
            NullLogger<ResendConfirmationEmailCommandHandler>.Instance);

    private static void VerifyNothingSent(
        Mock<IEmailVerificationService> verification)
        => verification.Verify(
            x => x.SendVerificationLinkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

    private static IdentityAccountSnapshot Snapshot(
        bool emailConfirmed,
        bool isDeleted = false)
    {
        var id = Guid.NewGuid();

        return new IdentityAccountSnapshot(
            IdentityId: id,
            UserAccountId: id,
            Email: Email,
            PhoneNumber: null,
            EmailConfirmed: emailConfirmed,
            PhoneConfirmed: false,
            HasPassword: true,
            IsDeleted: isDeleted,
            UserName: Email,
            SecurityStamp: "stamp");
    }
}
