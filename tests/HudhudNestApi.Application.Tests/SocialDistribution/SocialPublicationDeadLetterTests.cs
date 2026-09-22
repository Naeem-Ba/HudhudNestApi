using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class SocialPublicationDeadLetterTests
{
    [Fact]
    public void Create_ValidInput_StartsUnresolved()
    {
        var deadLetter = SocialPublicationDeadLetter.Create(
            Guid.NewGuid(), Guid.NewGuid(), SocialPlatform.Facebook,
            SocialPublicationErrorCode.RateLimited, "تم تجاوز حد المعدل.", 5, DateTime.UtcNow);

        Assert.False(deadLetter.IsResolved);
        Assert.Null(deadLetter.ResolvedAt);
    }

    [Fact]
    public void Create_EmptyPublicationId_Throws() =>
        Assert.Throws<DomainException>(() => SocialPublicationDeadLetter.Create(
            Guid.Empty, Guid.NewGuid(), SocialPlatform.Facebook, SocialPublicationErrorCode.RateLimited, "msg", 1, DateTime.UtcNow));

    [Fact]
    public void Resolve_MarksResolvedWithNoteAndUser()
    {
        var deadLetter = SocialPublicationDeadLetter.Create(
            Guid.NewGuid(), Guid.NewGuid(), SocialPlatform.Facebook, SocialPublicationErrorCode.PermissionDenied, "msg", 5, DateTime.UtcNow);
        var userId = Guid.NewGuid();

        deadLetter.Resolve(userId, "تم إصلاح صلاحيات الحساب.", DateTime.UtcNow);

        Assert.True(deadLetter.IsResolved);
        Assert.Equal(userId, deadLetter.ResolvedByUserId);
        Assert.Equal("تم إصلاح صلاحيات الحساب.", deadLetter.ResolutionNote);
    }

    [Fact]
    public void Resolve_Twice_Throws()
    {
        var deadLetter = SocialPublicationDeadLetter.Create(
            Guid.NewGuid(), Guid.NewGuid(), SocialPlatform.Facebook, SocialPublicationErrorCode.PermissionDenied, "msg", 5, DateTime.UtcNow);
        deadLetter.Resolve(Guid.NewGuid(), null, DateTime.UtcNow);

        Assert.Throws<InvalidStateTransitionException>(() => deadLetter.Resolve(Guid.NewGuid(), null, DateTime.UtcNow));
    }
}
