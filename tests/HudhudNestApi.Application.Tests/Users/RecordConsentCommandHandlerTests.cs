using Moq;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Users.Commands.RecordConsent;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Users.Entities;
using Xunit;

namespace HudhudNestApi.Application.Tests.Users;

/// <summary>
/// Covers the consent-recording feature added for docs/privacy/privacy-gaps.md (P1): before
/// this, registration recorded no acknowledgement of the Privacy Policy or Terms of Service
/// at all, so there was nothing to show as proof a user had agreed.
/// </summary>
public sealed class RecordConsentCommandHandlerTests
{
    [Fact]
    public async Task Handle_WithNoExistingActiveConsent_CreatesANewRecord_AndSaves()
    {
        var userId = Guid.NewGuid();
        var consents = new Mock<IConsentRecordRepository>();
        consents
            .Setup(x => x.GetActiveAsync(
                userId,
                ConsentPolicyType.PrivacyPolicy,
                "2026-09-04",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsentRecord?)null);

        var uow = new Mock<IUnitOfWork>();
        var clock = new FakeTimeProvider(new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc));

        var sut = new RecordConsentCommandHandler(consents.Object, uow.Object, clock);

        var dto = await sut.Handle(
            new RecordConsentCommand(
                userId,
                ConsentPolicyType.PrivacyPolicy,
                "2026-09-04",
                ConsentSource.Web),
            CancellationToken.None);

        Assert.Equal("PrivacyPolicy", dto.PolicyType);
        Assert.Equal("2026-09-04", dto.PolicyVersion);
        Assert.Equal("Web", dto.Source);
        Assert.True(dto.IsActive);
        Assert.Null(dto.WithdrawnAtUtc);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, dto.ConsentedAtUtc);

        consents.Verify(
            x => x.Add(It.Is<ConsentRecord>(r =>
                r.UserId == userId &&
                r.PolicyType == ConsentPolicyType.PrivacyPolicy &&
                r.PolicyVersion == "2026-09-04")),
            Times.Once);

        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAnActiveConsentForTheSameVersionAlreadyExists_ReturnsItWithoutCreatingADuplicate()
    {
        // A retried/duplicate "I agree" submission (double-click, flaky network) must not
        // create a second row for the exact same version the user already actively agreed
        // to.
        var userId = Guid.NewGuid();
        var existing = ConsentRecord.Create(
            userId,
            ConsentPolicyType.PrivacyPolicy,
            "2026-09-04",
            ConsentSource.Web,
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        var consents = new Mock<IConsentRecordRepository>();
        consents
            .Setup(x => x.GetActiveAsync(
                userId,
                ConsentPolicyType.PrivacyPolicy,
                "2026-09-04",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var uow = new Mock<IUnitOfWork>();
        var sut = new RecordConsentCommandHandler(
            consents.Object,
            uow.Object,
            new FakeTimeProvider(DateTime.UtcNow));

        var dto = await sut.Handle(
            new RecordConsentCommand(
                userId,
                ConsentPolicyType.PrivacyPolicy,
                "2026-09-04",
                ConsentSource.Web),
            CancellationToken.None);

        Assert.Equal(existing.Id, dto.Id);

        consents.Verify(x => x.Add(It.IsAny<ConsentRecord>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeTimeProvider(DateTime now)
            => _now = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
