using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Services;

namespace PropertyApi.Auth.Tests;

/// <summary>
/// Comprehensive tests for password security validation.
/// Tests complexity requirements and breach password screening.
/// </summary>
public sealed class PasswordSecurityServiceTests
{
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly PwnedPasswordsCircuitBreaker _circuitBreaker;
    private readonly IPasswordSecurityService _service;

    public PasswordSecurityServiceTests()
    {
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpHandlerMock.Object);
        var loggerMock = new Mock<ILogger<PasswordSecurityService>>();

        // A fresh breaker per test: closed, with no failures carried over from a
        // neighbouring test.
        _circuitBreaker = new PwnedPasswordsCircuitBreaker(TimeProvider.System);

        _service = new PasswordSecurityService(
            _httpClient,
            loggerMock.Object,
            _circuitBreaker);
    }

    #region Length Tests

    [Fact]
    public async Task ValidatePasswordAsync_WithEmptyPassword_ReturnsFails()
    {
        // Act
        var result = await _service.ValidatePasswordAsync(string.Empty);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(PasswordErrorCodes.Required, result.Errors[0].Code);
    }

    [Theory]
    [InlineData("Pass1!")]  // 6 chars
    [InlineData("Pass1!2")] // 7 chars
    public async Task ValidatePasswordAsync_WithLessThan8Characters_ReturnsFails(string password)
    {
        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(PasswordErrorCodes.TooShort, result.Errors[0].Code);
    }

    #endregion

    #region Complexity Tests

    [Fact]
    public async Task ValidatePasswordAsync_WithoutUppercase_ReturnsFails()
    {
        // Arrange
        var password = "password123!";

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(PasswordErrorCodes.NoUppercase, result.Errors[0].Code);
    }

    [Fact]
    public async Task ValidatePasswordAsync_WithoutLowercase_ReturnsFails()
    {
        // Arrange
        var password = "PASSWORD123!";

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(PasswordErrorCodes.NoLowercase, result.Errors[0].Code);
    }

    [Fact]
    public async Task ValidatePasswordAsync_WithoutDigit_ReturnsFails()
    {
        // Arrange
        var password = "Password!";

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(PasswordErrorCodes.NoDigit, result.Errors[0].Code);
    }

    [Fact]
    public async Task ValidatePasswordAsync_WithoutSpecialCharacter_ReturnsFails()
    {
        // Arrange
        var password = "Password123";

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(PasswordErrorCodes.NoSpecialCharacter, result.Errors[0].Code);
    }

    [Fact]
    public async Task ValidatePasswordAsync_WithMultipleComplexityViolations_ReturnsAllErrors()
    {
        // Arrange
        // BUG FIX: the previous password here was "pass", which is entirely lowercase --
        // so the assertion that a *lowercase* error is reported could never hold, and the
        // test failed for a defect it had invented rather than one in the service.
        // "PASS" violates four rules at once (too short, no lowercase, no digit, no
        // special character), which is what this test exists to prove: every violation is
        // reported, not just the first one.
        var password = "PASS";

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 4, $"Expected at least 4 errors, got {result.Errors.Count}");
        Assert.Contains(result.Errors, e => e.Code == PasswordErrorCodes.TooShort);
        Assert.Contains(result.Errors, e => e.Code == PasswordErrorCodes.NoLowercase);
        Assert.Contains(result.Errors, e => e.Code == PasswordErrorCodes.NoDigit);
        Assert.Contains(result.Errors, e => e.Code == PasswordErrorCodes.NoSpecialCharacter);
    }

    #endregion

    #region Valid Password Tests

    [Theory]
    [InlineData("ValidPass1!")]
    [InlineData("SecureP@ssw0rd")]
    [InlineData("MyPassword123!")]
    [InlineData("Complex$Pass99")]
    public async Task ValidatePasswordAsync_WithValidComplexPassword_Succeeds(string password)
    {
        // Arrange
        MockSuccessfulApiResponse();

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidatePasswordAsync_WithAllSpecialCharacters_Succeeds()
    {
        // Arrange
        var specialChars = new[] { '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '_', '+', '-', '=', '[', ']', '{', '}', '|', ';', ':', '\'', ',', '.', '<', '>', '?', '/', '~', '`' };

        foreach (var specialChar in specialChars)
        {
            var password = $"ValidPass1{specialChar}";
            MockSuccessfulApiResponse();

            // Act
            var result = await _service.ValidatePasswordAsync(password);

            // Assert
            Assert.True(result.IsValid, $"Password with '{specialChar}' should be valid");
        }
    }

    #endregion

    #region Breached Password Tests

    [Fact]
    public async Task ValidatePasswordAsync_WithBreachedPassword_ReturnsFails()
    {
        // Arrange
        var password = "P@ssw0rd123";
        MockBreachedPasswordResponse(password);

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == PasswordErrorCodes.Breached);
    }

    [Fact]
    public async Task ValidatePasswordAsync_WithNonBreachedPassword_Succeeds()
    {
        // Arrange
        var password = "UniqueP@ssw0rdXyz789";
        MockSuccessfulApiResponse();

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePasswordAsync_WithApiTimeout_ContinuesWithRegistration()
    {
        // Arrange
        var password = "ValidPass1!";
        MockApiTimeout();

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        // Should not block registration on API errors
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePasswordAsync_WithApiFail_ContinuesWithRegistration()
    {
        // Arrange
        var password = "ValidPass1!";
        MockApiFailure();

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        // Should not block registration on API errors
        Assert.True(result.IsValid);
    }

    #endregion

    #region Common Passwords Tests

    // BUG FIX: these used to be one [Theory] asserting that four weak passwords are
    // rejected, with no HTTP mock at all. Three of them pass the character-class rules
    // and are only caught by breach screening -- and with an un-stubbed handler that
    // screening fails open, so the test asserted a rejection that never happened. The
    // two mechanisms are now separated so each one is actually exercised.

    [Theory]
    [InlineData("12345678")]   // digits only: no uppercase, no lowercase, no special
    [InlineData("abcdefgh")]   // letters only: no uppercase, no digit, no special
    public async Task ValidatePasswordAsync_WithLocallyWeakPatterns_FailsWithoutCallingBreachApi(string password)
    {
        // No API mock on purpose: character-class rules must reject these offline,
        // and ValidatePasswordAsync returns early without ever reaching the network.

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        Assert.False(result.IsValid);

        _httpHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    [Theory]
    [InlineData("Qwerty123!")]   // keyboard pattern as the base word
    [InlineData("Password1!")]   // common base word plus decoration
    [InlineData("PASSWORD!")]    // same word, different case
    [InlineData("Aa123456!")]    // six sequential digits
    [InlineData("Xy!aaaaa1")]    // five repeated characters
    public async Task ValidatePasswordAsync_WithCommonPatterns_RejectedLocallyWithoutCallingBreachApi(string password)
    {
        // These pass every character-class rule, so before the local pattern checks the
        // only thing standing between them and a new account was breach screening --
        // which fails open, and so accepted them whenever Have I Been Pwned was
        // unreachable. Rejecting them locally removes that external dependency, and
        // asserting the API was never called is what proves the check is local.

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        Assert.False(result.IsValid);

        _httpHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    [Theory]
    [InlineData("MyPassword123!")]        // a real word in front changes the guess space
    [InlineData("SecurePass123!")]
    [InlineData("StrongPass!123")]
    [InlineData("Complex$Pass99")]
    [InlineData("UniqueP@ssw0rdXyz789")]
    public async Task ValidatePasswordAsync_WithStrongPasswords_NotCaughtByPatternRules(string password)
    {
        // The guard against the rules above being too eager. Every value here is a
        // password a real user could reasonably pick, and several are fixtures other
        // suites register with -- if a pattern rule starts rejecting these, it is the
        // rule that is wrong.

        // Arrange
        MockSuccessfulApiResponse();

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        Assert.True(
            result.IsValid,
            $"'{password}' should be accepted but was rejected: {string.Join(" | ", result.Errors)}");
    }

    #endregion

    #region Breach-screening circuit breaker

    [Fact]
    public async Task ValidatePasswordAsync_AfterRepeatedApiFailures_StopsCallingTheApi()
    {
        // Arrange
        MockApiFailure();
        const string password = "UniqueP@ssw0rdXyz789";

        // Act: enough consecutive failures to trip the breaker, then more calls.
        for (var i = 0; i < PwnedPasswordsCircuitBreaker.FailureThreshold; i++)
        {
            var duringOutage = await _service.ValidatePasswordAsync(password);
            Assert.True(duringOutage.IsValid, "Screening must fail open, not block registration.");
        }

        var afterTripping = await _service.ValidatePasswordAsync(password);

        // Assert: still fails open, but no longer pays for a call that cannot succeed.
        Assert.True(afterTripping.IsValid);

        _httpHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Exactly(PwnedPasswordsCircuitBreaker.FailureThreshold),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ValidatePasswordAsync_WithHealthyApi_KeepsCircuitClosed()
    {
        // Arrange
        MockSuccessfulApiResponse();
        const string password = "UniqueP@ssw0rdXyz789";

        // Act
        for (var i = 0; i < PwnedPasswordsCircuitBreaker.FailureThreshold + 2; i++)
        {
            Assert.True((await _service.ValidatePasswordAsync(password)).IsValid);
        }

        // Assert: a healthy API is called every time.
        _httpHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Exactly(PwnedPasswordsCircuitBreaker.FailureThreshold + 2),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// The k-anonymity suffix the service looks for: SHA-1 of the password,
    /// uppercase hex, minus the 5-character prefix it sends to the API.
    /// </summary>
    private static string Sha1Suffix(string password)
    {
        var hash = System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(password));

        return Convert.ToHexString(hash)[5..];
    }

    private void MockSuccessfulApiResponse()
    {
        // Response with no matching hashes
        var content = new StringContent(
            "0001E4C9F3F0FD769557CF48C4D68D5E4D0:1\n" +
            "0005A671C18E6E6D51B6C6469B0FF6F9DBA:1\n",
            System.Text.Encoding.UTF8,
            "text/plain");

        var response = new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = content
        };

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private void MockBreachedPasswordResponse(string password)
    {
        // BUG FIX: this used to ignore its `password` argument and return two
        // hardcoded suffixes that could never match SHA-1(password), so the
        // "breached password is rejected" test was asserting against a response
        // that simulated a *clean* password. Build the range response the way
        // api.pwnedpasswords.com actually would: the real 35-character suffix of
        // the password's SHA-1, plus unrelated entries around it.
        var suffix = Sha1Suffix(password);

        var content = new StringContent(
            "0001E4C9F3F0FD769557CF48C4D68D5E4D0:1\n" +
            $"{suffix}:42\n" +
            "0005A671C18E6E6D51B6C6469B0FF6F9DBA:2\n",
            System.Text.Encoding.UTF8,
            "text/plain");

        var response = new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = content
        };

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private void MockApiTimeout()
    {
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Timeout"));
    }

    private void MockApiFailure()
    {
        var response = new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.ServiceUnavailable
        };

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    #endregion
}
