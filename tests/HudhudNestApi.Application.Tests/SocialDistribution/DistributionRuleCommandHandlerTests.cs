using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Commands.ActivateDistributionRule;
using HudhudNestApi.Application.SocialDistribution.Commands.ArchiveDistributionRule;
using HudhudNestApi.Application.SocialDistribution.Commands.CreateDistributionRule;
using HudhudNestApi.Application.SocialDistribution.Commands.DeactivateDistributionRule;
using HudhudNestApi.Application.SocialDistribution.Commands.UpdateDistributionRule;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class DistributionRuleCommandHandlerTests
{
    private static SocialAccount MakeAccount()
    {
        var channel = SocialChannel.Create(SocialPlatform.Facebook, "Facebook");
        return SocialAccount.Create(channel.Id, SocialPlatform.Facebook, "Page", "ext-1", SocialAccountType.Page);
    }

    [Fact]
    public async Task Create_HappyPath_PersistsAndReturnsDto()
    {
        var rules = new Mock<IDistributionRuleRepository>();
        var accounts = new Mock<ISocialAccountRepository>();
        var uow = new Mock<IUnitOfWork>();
        var account = MakeAccount();

        accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var handler = new CreateDistributionRuleCommandHandler(rules.Object, accounts.Object, uow.Object);
        var result = await handler.Handle(
            new CreateDistributionRuleCommand("شقق للبيع في طرطوس", null, 1, 2, ListingType.ForSale, account.Id, 100, null, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal("شقق للبيع في طرطوس", result.Name);
        Assert.True(result.IsActive);
        rules.Verify(x => x.AddAsync(It.IsAny<DistributionRule>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_AccountNotFound_ThrowsNotFound()
    {
        var rules = new Mock<IDistributionRuleRepository>();
        var accounts = new Mock<ISocialAccountRepository>();
        var uow = new Mock<IUnitOfWork>();

        accounts.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((SocialAccount?)null);

        var handler = new CreateDistributionRuleCommandHandler(rules.Object, accounts.Object, uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateDistributionRuleCommand("اسم", null, null, null, null, Guid.NewGuid(), 0, null, null, Guid.NewGuid()),
            CancellationToken.None));
    }

    [Fact]
    public async Task Update_HappyPath_ChangesFields()
    {
        var rules = new Mock<IDistributionRuleRepository>();
        var uow = new Mock<IUnitOfWork>();
        var rule = DistributionRule.Create("اسم قديم", null, null, null, null, Guid.NewGuid(), 10, null, null, null);

        rules.Setup(x => x.GetByIdAsync(rule.Id, It.IsAny<CancellationToken>())).ReturnsAsync(rule);

        var handler = new UpdateDistributionRuleCommandHandler(rules.Object, uow.Object);
        var result = await handler.Handle(
            new UpdateDistributionRuleCommand(rule.Id, "اسم جديد", null, 3, 4, ListingType.ForRent, 55, null, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal("اسم جديد", result.Name);
        Assert.Equal(55, result.Priority);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_RuleNotFound_ThrowsNotFound()
    {
        var rules = new Mock<IDistributionRuleRepository>();
        var uow = new Mock<IUnitOfWork>();
        rules.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((DistributionRule?)null);

        var handler = new UpdateDistributionRuleCommandHandler(rules.Object, uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UpdateDistributionRuleCommand(Guid.NewGuid(), "اسم", null, null, null, null, 0, null, null, Guid.NewGuid()),
            CancellationToken.None));
    }

    [Fact]
    public async Task Activate_And_Deactivate_RoundTrip()
    {
        var rules = new Mock<IDistributionRuleRepository>();
        var uow = new Mock<IUnitOfWork>();
        var rule = DistributionRule.Create("اسم", null, null, null, null, Guid.NewGuid(), 0, null, null, null);
        rule.Deactivate();

        rules.Setup(x => x.GetByIdAsync(rule.Id, It.IsAny<CancellationToken>())).ReturnsAsync(rule);

        var activateHandler = new ActivateDistributionRuleCommandHandler(rules.Object, uow.Object);
        var activated = await activateHandler.Handle(new ActivateDistributionRuleCommand(rule.Id), CancellationToken.None);
        Assert.True(activated.IsActive);

        var deactivateHandler = new DeactivateDistributionRuleCommandHandler(rules.Object, uow.Object);
        var deactivated = await deactivateHandler.Handle(new DeactivateDistributionRuleCommand(rule.Id), CancellationToken.None);
        Assert.False(deactivated.IsActive);
    }

    [Fact]
    public async Task Archive_MarksArchivedAndInactive()
    {
        var rules = new Mock<IDistributionRuleRepository>();
        var uow = new Mock<IUnitOfWork>();
        var rule = DistributionRule.Create("اسم", null, null, null, null, Guid.NewGuid(), 0, null, null, null);

        rules.Setup(x => x.GetByIdAsync(rule.Id, It.IsAny<CancellationToken>())).ReturnsAsync(rule);

        var handler = new ArchiveDistributionRuleCommandHandler(rules.Object, uow.Object);
        var result = await handler.Handle(new ArchiveDistributionRuleCommand(rule.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsArchived);
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task Archive_NotFound_ThrowsNotFound()
    {
        var rules = new Mock<IDistributionRuleRepository>();
        var uow = new Mock<IUnitOfWork>();
        rules.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((DistributionRule?)null);

        var handler = new ArchiveDistributionRuleCommandHandler(rules.Object, uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new ArchiveDistributionRuleCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
    }
}
