using Moq;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Users.Commands.WithdrawConsent;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Users.Entities;
using Xunit;

namespace HudhudNestApi.Application.Tests.Users;

public sealed class WithdrawConsentCommandHandlerTests
{
    [Fact]
    public async Task Handle_WithActiveConsents_WithdrawsAllOfThem_AndSaves()
    {
        var userId = Guid.NewGuid();
        var recordA = ConsentRecord.Create(
            userId, ConsentPolicyType.PrivacyPolicy, "1.0", ConsentSource.Web, DateTime.UtcNow.AddDays(-10));
        var recordB = ConsentRecord.Create(
            userId, ConsentPolicyType.PrivacyPolicy, "2.0", ConsentSource.Ios, DateTime.UtcNow.AddDays(-1));

        var consents = new Mock<IConsentRecordRepository>();
        consents
            .Setup(x => x.GetActiveByTypeAsync(userId, ConsentPolicyType.PrivacyPolicy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { recordA, recordB });

        var uow = new Mock<IUnitOfWork>();
        var sut = new WithdrawConsentCommandHandler(consents.Object, uow.Object, TimeProvider.System);

        var withdrawnCount = await sut.Handle(
            new WithdrawConsentCommand(userId, ConsentPolicyType.PrivacyPolicy),
            CancellationToken.None);

        Assert.Equal(2, withdrawnCount);
        Assert.False(recordA.IsActive);
        Assert.False(recordB.IsActive);
        Assert.NotNull(recordA.WithdrawnAtUtc);
        Assert.NotNull(recordB.WithdrawnAtUtc);

        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithNoActiveConsents_ReturnsZero_AndDoesNotSave()
    {
        var userId = Guid.NewGuid();
        var consents = new Mock<IConsentRecordRepository>();
        consents
            .Setup(x => x.GetActiveByTypeAsync(userId, ConsentPolicyType.TermsOfService, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ConsentRecord>());

        var uow = new Mock<IUnitOfWork>();
        var sut = new WithdrawConsentCommandHandler(consents.Object, uow.Object, TimeProvider.System);

        var withdrawnCount = await sut.Handle(
            new WithdrawConsentCommand(userId, ConsentPolicyType.TermsOfService),
            CancellationToken.None);

        Assert.Equal(0, withdrawnCount);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
