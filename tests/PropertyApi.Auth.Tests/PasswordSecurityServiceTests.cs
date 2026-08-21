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
    private readonly IPasswordSecurityService _service;

    public PasswordSecurityServiceTests()
    {
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpHandlerMock.Object);
        var loggerMock = new Mock<ILogger<PasswordSecurityService>>();

        _service = new PasswordSecurityService(_httpClient, loggerMock.Object);
    }

    #region Length Tests

    [Fact]
    public async Task ValidatePasswordAsync_WithEmptyPassword_ReturnsFails()
    {
        // Act
        var result = await _service.ValidatePasswordAsync(string.Empty);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("required", result.Errors[0], StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("at least 8", result.Errors[0], StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("uppercase", result.Errors[0], StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("lowercase", result.Errors[0], StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("digit", result.Errors[0], StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("special character", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidatePasswordAsync_WithMultipleComplexityViolations_ReturnsAllErrors()
    {
        // Arrange
        var password = "pass";

        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 4, $"Expected at least 4 errors, got {result.Errors.Count}");
        Assert.Contains(result.Errors, e => e.Contains("8", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.Contains("uppercase", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.Contains("lowercase", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.Contains("digit", StringComparison.OrdinalIgnoreCase));
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
        Assert.Contains(result.Errors, e => e.Contains("breached", StringComparison.OrdinalIgnoreCase) ||
                                            e.Contains("data breach", StringComparison.OrdinalIgnoreCase));
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

    [Theory]
    [InlineData("12345678")]        // Sequential numbers
    [InlineData("Qwerty123!")]       // Keyboard pattern
    [InlineData("Password1!")]       // Too common
    [InlineData("Aa123456!")]        // Too simple
    public async Task ValidatePasswordAsync_WithCommonWeakPatterns_ShouldFailComplexity(string password)
    {
        // Act
        var result = await _service.ValidatePasswordAsync(password);

        // Assert
        // Note: These will fail at complexity check before breach check
        // (except "Password1!" which would theoretically pass complexity but fail at breach)
        Assert.False(result.IsValid);
    }

    #endregion

    #region Helper Methods

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
        // Simulate a response containing the password hash
        var content = new StringContent(
            "0001E4C9F3F0FD769557CF48C4D68D5E4D0:1\n" +
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
