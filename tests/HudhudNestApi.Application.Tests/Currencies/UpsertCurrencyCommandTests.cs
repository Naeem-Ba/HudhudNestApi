using FluentValidation.TestHelper;
using Moq;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Currencies.Commands.UpsertCurrency;
using HudhudNestApi.Application.Currencies.Interfaces;
using HudhudNestApi.Application.Currencies.Queries.GetCurrencies;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Lookups.Entities;

namespace HudhudNestApi.Application.Tests.Currencies;

public sealed class UpsertCurrencyCommandTests
{
    private static UpsertCurrencyCommand Command(string code = "TRY", decimal rate = 40m, bool active = true) =>
        new(code, "Turkish Lira", "الليرة التركية", "₺", 2, rate, active, Guid.NewGuid(), "127.0.0.1");

    [Theory]
    [InlineData("US")]
    [InlineData("USDX")]
    [InlineData("12A")]
    public void Validator_RejectsBadCodes(string code)
    {
        new UpsertCurrencyCommandValidator().TestValidate(Command(code)).ShouldHaveValidationErrorFor(x => x.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validator_RejectsNonPositiveRates(decimal rate)
    {
        new UpsertCurrencyCommandValidator().TestValidate(Command(rate: rate)).ShouldHaveValidationErrorFor(x => x.ExchangeRateToUsd);
    }

    [Fact(DisplayName = "An existing currency gets its rate updated, and the change is audited with old and new values")]
    public async Task Existing_IsUpdated_AndAudited()
    {
        var existing = Currency.Create("TRY", "Turkish Lira", "الليرة التركية", "₺", 32.5m);
        var repo = new Mock<ICurrencyRepository>();
        repo.Setup(r => r.GetByCodeAsync("TRY", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var audit = new Mock<IAuditLogService>();
        var uow = new Mock<IUnitOfWork>();

        var result = await new UpsertCurrencyCommandHandler(repo.Object, uow.Object, audit.Object)
            .Handle(Command(rate: 41.25m), CancellationToken.None);

        Assert.Equal(41.25m, existing.ExchangeRateToUSD);
        Assert.Equal(41.25m, result.ExchangeRateToUsd);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.LogAsync(
            It.IsAny<Guid>(), AuditActions.CurrencyRateUpdatedByAdmin, "127.0.0.1",
            It.Is<string?>(old => old!.Contains("32.5")), It.Is<string?>(now => now!.Contains("41.25")),
            It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.Add(It.IsAny<Currency>()), Times.Never);
    }

    [Fact]
    public async Task Unknown_IsCreated()
    {
        var repo = new Mock<ICurrencyRepository>();
        repo.Setup(r => r.GetByCodeAsync("GBP", It.IsAny<CancellationToken>())).ReturnsAsync((Currency?)null);

        var result = await new UpsertCurrencyCommandHandler(repo.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IAuditLogService>())
            .Handle(Command("gbp", 0.78m), CancellationToken.None);

        Assert.Equal("GBP", result.Code);
        repo.Verify(r => r.Add(It.Is<Currency>(c => c.Code == "GBP" && c.ExchangeRateToUSD == 0.78m && !c.IsBaseCurrency)), Times.Once);
    }

    [Fact(DisplayName = "The base currency keeps rate 1 and cannot be deactivated")]
    public void BaseCurrency_IsProtected()
    {
        var usd = Currency.Create("USD", "US Dollar", "الدولار", "$", 1m, isBaseCurrency: true);

        Assert.Throws<DomainException>(() => usd.UpdateExchangeRate(2m));
        Assert.Throws<DomainException>(() => usd.UpdateDetails("US Dollar", "الدولار", "$", 2, isActive: false));
        usd.UpdateExchangeRate(1m);
    }

    [Fact]
    public void NonPositiveRate_IsRefusedByTheEntity()
    {
        var eur = Currency.Create("EUR", "Euro", "اليورو", "€", 0.9m);

        Assert.Throws<DomainException>(() => eur.UpdateExchangeRate(0m));
        Assert.Equal(0.9m, eur.ExchangeRateToUSD);
    }

    [Fact(DisplayName = "The public list trims the padded codes and puts the base currency first")]
    public async Task Query_TrimsCodes_AndOrdersBaseFirst()
    {
        var repo = new Mock<ICurrencyRepository>();
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            Currency.Create("SYP  ", "Syrian Pound", "الليرة", "ل.س", 14000m),
            Currency.Create("USD", "US Dollar", "الدولار", "$", 1m, isBaseCurrency: true),
            Currency.Create("EUR", "Euro", "اليورو", "€", 0.9m)
        });

        var list = await new GetCurrenciesQueryHandler(repo.Object).Handle(new GetCurrenciesQuery(), CancellationToken.None);

        Assert.Equal(["USD", "EUR", "SYP"], list.Select(c => c.Code));
        Assert.True(list[0].IsBase);
    }
}
