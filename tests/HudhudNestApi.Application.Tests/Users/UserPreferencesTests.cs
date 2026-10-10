using HudhudNestApi.Application.Users.Commands.UpdateUser;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Tests.Users;

public sealed class UserPreferencesTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static UserAccount NewAccount() => UserAccount.Create(Guid.NewGuid(), "Ada", "Lovelace", Now);

    [Fact]
    public void ANewAccount_HasNotChosenAnything()
    {
        var account = NewAccount();

        Assert.Null(account.PreferredLanguageSetAt);
        Assert.Null(account.PreferredCurrencySetAt);
    }

    [Fact]
    public void ChoosingOnlyTheLanguage_DoesNotMarkTheCurrencyAsChosen_AndViceVersa()
    {
        var account = NewAccount();

        account.UpdatePreferences("de", account.PreferredCurrency, null, Now, languageChosen: true);

        Assert.Equal("de", account.PreferredLanguage);
        Assert.Equal(Now, account.PreferredLanguageSetAt);
        Assert.Null(account.PreferredCurrencySetAt);
        Assert.Equal("EUR", account.PreferredCurrency);

        var later = Now.AddMinutes(5);
        account.UpdatePreferences(account.PreferredLanguage, "usd", null, later, currencyChosen: true);

        Assert.Equal("USD", account.PreferredCurrency);
        Assert.Equal(later, account.PreferredCurrencySetAt);
        Assert.Equal(Now, account.PreferredLanguageSetAt); // changing currency never touches the language
        Assert.Equal("de", account.PreferredLanguage);
    }

    [Fact]
    public void EchoingTheStoredValues_IsNotAChoice()
    {
        var account = NewAccount();

        account.UpdatePreferences(account.PreferredLanguage, account.PreferredCurrency, "DE", Now);

        Assert.Null(account.PreferredLanguageSetAt);
        Assert.Null(account.PreferredCurrencySetAt);
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("fr")]
    [InlineData("")]
    public void AnUnsupportedLanguage_IsRejected_ByTheDomain(string language)
    {
        var account = NewAccount();

        Assert.ThrowsAny<ArgumentException>(
            () => account.UpdatePreferences(language, "EUR", null, Now, languageChosen: true));
    }

    [Fact]
    public void AnUnsupportedCurrency_IsRejected_ByTheDomain()
    {
        var account = NewAccount();

        Assert.Throws<ArgumentException>(
            () => account.UpdatePreferences("ar", "ZZZ", null, Now, currencyChosen: true));
    }

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("AR", "ar")]
    [InlineData(" en_US ", "en")]
    public void ChooseInitialLanguage_AcceptsRegionalTags(string tag, string expected)
    {
        var account = NewAccount();

        account.ChooseInitialLanguage(tag, Now);

        Assert.Equal(expected, account.PreferredLanguage);
        Assert.Equal(Now, account.PreferredLanguageSetAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    public void ChooseInitialLanguage_IgnoresWhatTheProductDoesNotTranslate(string? tag)
    {
        var account = NewAccount();

        account.ChooseInitialLanguage(tag, Now);

        Assert.Null(account.PreferredLanguageSetAt);
    }

    [Theory]
    [InlineData("ar", "EUR", true)]
    [InlineData("de", "USD", true)]
    [InlineData("EN", "syp", true)]
    [InlineData("fr", "EUR", false)]
    [InlineData("ar", "JPY", false)]
    [InlineData("ar", "EU", false)]
    public void TheValidator_AcceptsOnlySupportedCodes(string language, string currency, bool valid)
    {
        var command = new UpdateUserCommand(
            Guid.NewGuid(), null, null, null, null, null, language, currency, null);

        Assert.Equal(valid, new UpdateUserCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public void TheValidator_LeavesUntouchedPreferences_Alone()
    {
        var command = new UpdateUserCommand(Guid.NewGuid(), "Ada", null, null, null, null, null, null, null);

        Assert.True(new UpdateUserCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public void TheSupportedLists_AreWhatTheProductTranslatesAndPrices()
    {
        Assert.Equal(new[] { "ar", "en", "de" }, SupportedPreferences.Languages);
        Assert.Contains("EUR", SupportedPreferences.Currencies);
        Assert.Equal("ar", SupportedPreferences.FallbackLanguage);
    }
}
