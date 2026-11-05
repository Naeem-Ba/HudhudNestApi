using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Auth.Services;

namespace HudhudNestApi.Auth.Tests;

/// <summary>
/// "The password must not contain who you are": the account holder's name, the name part of their e-mail
/// address, or six consecutive digits of their phone number. The same vectors exist in the Angular client's
/// password-policy.spec.ts; the server is the reference.
/// </summary>
public sealed class PasswordPersonalInformationTests
{
    private static readonly PasswordCheckContext Holder = new(
        FirstName: "Mohammad",
        LastName: "Al-Khatib",
        Email: "m.alkhatib@example.com",
        PhoneNumber: "+963944123456");

    private static string[] Codes(string password, PasswordCheckContext? context = null) =>
        PasswordPolicy.Evaluate(password, context ?? Holder).Select(e => e.Code).ToArray();

    [Theory]
    [InlineData("Mohammad#2024x")]        // first name
    [InlineData("MOHAMMAD!secure9")]      // upper case
    [InlineData("M0hammad!Secure")]       // look-alike digit for 'o'
    [InlineData("Khatib#Strong9")]        // part of the last name
    [InlineData("Alkhatib!2025x")]        // name part of the e-mail address
    [InlineData("Abc!944123Zq")]          // six digits of the phone number
    [InlineData("Zz9!٩٤٤١٢٣Qq")]          // the same digits written in Arabic-Indic numerals
    public void PasswordContainingThePersonsDetails_IsRejected(string password)
    {
        Assert.Contains(PasswordErrorCodes.PersonalInfo, Codes(password));
    }

    [Fact]
    public void ArabicName_IsMatchedIgnoringDiacriticsAndAlefForms()
    {
        var context = new PasswordCheckContext(FirstName: "أحمد", LastName: "الخطيب");

        Assert.Contains(PasswordErrorCodes.PersonalInfo, Codes("احمد#Abc12xZ", context));
        Assert.Contains(PasswordErrorCodes.PersonalInfo, Codes("Qw!الخَطِيب9xk", context));
    }

    [Fact]
    public void AccentedName_IsMatchedWithoutTheAccent()
    {
        var context = new PasswordCheckContext(FirstName: "José");

        Assert.Contains(PasswordErrorCodes.PersonalInfo, Codes("JOSE#Fox91zz", context));
    }

    [Fact]
    public void ThreeLetterName_IsOnlyRejectedWhenItIsTheWholePassword()
    {
        var context = new PasswordCheckContext(FirstName: "Ali");

        Assert.Contains(PasswordErrorCodes.PersonalInfo, Codes("Ali@2468@2468", context));
        Assert.DoesNotContain(PasswordErrorCodes.PersonalInfo, Codes("Channali#Fox91", context));
        Assert.DoesNotContain(PasswordErrorCodes.PersonalInfo, Codes("Ali#Fox91zzKl", context));
    }

    [Theory]
    [InlineData("Tr!ckyHorse1x")]         // nothing to do with the holder
    [InlineData("Abc!9441x2Z")]           // only four digits of the phone number
    [InlineData("Mo#hammZ9xKq")]          // broken up name
    public void UnrelatedPasswords_AreNotRejected(string password)
    {
        Assert.DoesNotContain(PasswordErrorCodes.PersonalInfo, Codes(password));
    }

    [Fact]
    public void WithoutContext_NothingPersonalIsChecked()
    {
        Assert.Empty(PasswordPolicy.Evaluate("Mohammad#2024x", null));
        Assert.Empty(PasswordPolicy.Evaluate("Mohammad#2024x", PasswordCheckContext.None));
    }

    [Fact]
    public void ShortNameFragments_AreIgnored()
    {
        // "Al" and "m" are far too short to be searched for.
        Assert.DoesNotContain(PasswordErrorCodes.PersonalInfo, Codes("Alpha#Mango91x"));
    }

    [Fact]
    public void BasicRulesStillComeFirst()
    {
        // A weak password is told how to be strong first; the personal-information rule is added once the basics pass.
        var codes = Codes("mohammad");

        Assert.Contains(PasswordErrorCodes.NoUppercase, codes);
        Assert.DoesNotContain(PasswordErrorCodes.PersonalInfo, codes);
    }

    [Fact]
    public async Task Service_RejectsPersonalInformation_WithoutAskingTheBreachDatabase()
    {
        var handler = new ThrowingHandler();
        var service = new PasswordSecurityService(
            new HttpClient(handler),
            NullLogger<PasswordSecurityService>.Instance,
            new PwnedPasswordsCircuitBreaker(TimeProvider.System));

        var result = await service.ValidatePasswordAsync("Mohammad#2024x", Holder);

        Assert.False(result.IsValid);
        Assert.Equal(PasswordErrorCodes.PersonalInfo, Assert.Single(result.Errors).Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void ErrorCodeContract_IsPinned()
    {
        // Renaming one silently turns a translated message back into English in every client.
        Assert.Equal("PASSWORD_CONTAINS_PERSONAL_INFO", PasswordErrorCodes.PersonalInfo);
        Assert.Equal("PASSWORD_REQUIRED", PasswordErrorCodes.Required);
        Assert.Equal("PASSWORD_TOO_SHORT", PasswordErrorCodes.TooShort);
        Assert.Equal("PASSWORD_NO_UPPERCASE", PasswordErrorCodes.NoUppercase);
        Assert.Equal("PASSWORD_NO_LOWERCASE", PasswordErrorCodes.NoLowercase);
        Assert.Equal("PASSWORD_NO_DIGIT", PasswordErrorCodes.NoDigit);
        Assert.Equal("PASSWORD_NO_SPECIAL", PasswordErrorCodes.NoSpecialCharacter);
        Assert.Equal("PASSWORD_COMMON_WORD", PasswordErrorCodes.CommonWord);
        Assert.Equal("PASSWORD_LONG_RUN", PasswordErrorCodes.LongRun);
        Assert.Equal("PASSWORD_BREACHED", PasswordErrorCodes.Breached);

        Assert.Equal("FIRST_NAME_REQUIRED", AuthValidationCodes.FirstNameRequired);
        Assert.Equal("FIRST_NAME_TOO_LONG", AuthValidationCodes.FirstNameTooLong);
        Assert.Equal("LAST_NAME_REQUIRED", AuthValidationCodes.LastNameRequired);
        Assert.Equal("LAST_NAME_TOO_LONG", AuthValidationCodes.LastNameTooLong);
        Assert.Equal("EMAIL_REQUIRED", AuthValidationCodes.EmailRequired);
        Assert.Equal("EMAIL_TOO_LONG", AuthValidationCodes.EmailTooLong);
        Assert.Equal("EMAIL_INVALID", AuthValidationCodes.EmailInvalid);
        Assert.Equal("EMAIL_HAS_WHITESPACE", AuthValidationCodes.EmailHasWhitespace);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("The breach database must not be asked for a locally refused password.");
        }
    }
}
