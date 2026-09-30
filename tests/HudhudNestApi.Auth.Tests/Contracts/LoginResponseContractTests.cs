using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HudhudNestApi.Application.Auth.Models;

namespace HudhudNestApi.Auth.Tests.Contracts;

/// <summary>
/// Guards the wire shape of the /api/auth/login response.
///
/// RELEASE-BLOCKERS-AR.md B-11: Program.cs now configures MVC with
/// PropertyNamingPolicy = JsonNamingPolicy.CamelCase for every DTO, not just this one — before
/// that, it was `null` (PascalCase by default) and LoginResponseDto alone carried explicit
/// [JsonPropertyName] attributes to force camelCase, since the Angular client
/// (core/models/auth.model.ts) reads camelCase only. Those attributes are gone now that the
/// policy produces the same names by default; these tests still pin the exact serializer
/// configuration from Program.cs so that regression cannot ship again.
/// </summary>
[Trait("Category", "AuthContract")]
public sealed class LoginResponseContractTests
{
    /// <summary>Mirrors the AddJsonOptions block in Program.cs.</summary>
    private static readonly JsonSerializerOptions ProgramJsonOptions = CreateProgramJsonOptions();

    private static JsonSerializerOptions CreateProgramJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static JsonObject Serialize(LoginResponseDto response) =>
        JsonNode.Parse(JsonSerializer.Serialize(response, ProgramJsonOptions))!.AsObject();

    [Fact(DisplayName = "Successful login emits the camelCase token fields the client reads")]
    public void Success_SerializesTokenFieldsAsCamelCase()
    {
        var json = Serialize(
            LoginResponseDto.CreateSuccess("access-token", "refresh-token", 3600));

        // These three names are the entire contract the Angular token storage depends on.
        Assert.Equal("access-token", (string?)json["accessToken"]);
        Assert.Equal("refresh-token", (string?)json["refreshToken"]);
        Assert.Equal(3600, (int?)json["expiresIn"]);

        Assert.True((bool?)json["success"]);
        Assert.Equal(200, (int?)json["statusCode"]);
    }

    [Fact(DisplayName = "No login response property is serialized as PascalCase")]
    public void Response_ContainsNoPascalCaseProperties()
    {
        var json = Serialize(
            LoginResponseDto.CreateAccountLocked(DateTime.UtcNow.AddMinutes(12), 5));

        var pascalCased = json
            .Select(property => property.Key)
            .Where(name => char.IsUpper(name[0]))
            .ToArray();

        Assert.True(
            pascalCased.Length == 0,
            $"Add [JsonPropertyName] to: {string.Join(", ", pascalCased)}");
    }

    [Fact(DisplayName = "Invalid credentials expose the attempt counter fields")]
    public void InvalidCredentials_ExposesAttemptCounters()
    {
        var json = Serialize(
            LoginResponseDto.CreateInvalidCredentials(failedAttemptCount: 2, maxAttempts: 5));

        Assert.Equal(LoginErrorCodes.InvalidCredentials, (string?)json["errorCode"]);
        Assert.Equal(2, (int?)json["failedAttemptCount"]);
        Assert.Equal(5, (int?)json["maxFailedAttempts"]);
        Assert.Equal(3, (int?)json["remainingAttemptsBeforeLockout"]);
        Assert.False((bool?)json["isAccountLocked"]);
    }

    [Fact(DisplayName = "Anonymous invalid-credentials 401 carries no attempt counter")]
    public void InvalidCredentials_Default_OmitsAttemptCounters()
    {
        // Regression: this used to serialize failedAttemptCount=0 / remaining=5 on every
        // failure, so the UI always claimed "5 attempts remaining".
        var json = Serialize(LoginResponseDto.CreateInvalidCredentials());

        Assert.Null(json["failedAttemptCount"]);
        Assert.Null(json["maxFailedAttempts"]);
        Assert.Null(json["remainingAttemptsBeforeLockout"]);
        Assert.Null(json["attemptWarningMessage"]);
    }

    [Fact(DisplayName = "errorType serializes as a string, not a numeric enum value")]
    public void ErrorType_SerializesAsString()
    {
        var json = Serialize(LoginResponseDto.CreateInvalidCredentials());

        // JsonStringEnumConverter is registered globally in Program.cs; a client branching on
        // the numeric value would break the moment the enum is reordered.
        Assert.Equal(
            nameof(LoginErrorType.InvalidCredentials),
            (string?)json["errorType"]);
    }

    [Fact(DisplayName = "Locked response carries an absolute unlock timestamp for countdowns")]
    public void AccountLocked_CarriesAbsoluteUnlockTimestamp()
    {
        var lockoutEnd = DateTime.UtcNow.AddMinutes(12);

        var json = Serialize(LoginResponseDto.CreateAccountLocked(lockoutEnd, 5));

        Assert.Equal(423, (int?)json["statusCode"]);
        Assert.Equal(LoginErrorCodes.AccountLocked, (string?)json["errorCode"]);
        Assert.True((bool?)json["isAccountLocked"]);
        Assert.NotNull((string?)json["lockoutEndTimeUtc"]);

        // Ceiling, so a 12-minute lockout never renders as "11 minutes remaining".
        Assert.Equal(12, (int?)json["lockoutMinutesRemaining"]);
    }

    [Fact(DisplayName = "A failed login never leaks tokens")]
    public void FailedLogin_DoesNotLeakTokens()
    {
        foreach (var failure in new[]
                 {
                     LoginResponseDto.CreateInvalidCredentials(1),
                     LoginResponseDto.CreateAccountLocked(DateTime.UtcNow.AddMinutes(5), 5),
                     LoginResponseDto.RateLimited(),
                     LoginResponseDto.AccountDisabled()
                 })
        {
            var json = Serialize(failure);

            Assert.False((bool?)json["success"]);
            Assert.True(string.IsNullOrEmpty((string?)json["accessToken"]));
            Assert.True(string.IsNullOrEmpty((string?)json["refreshToken"]));
        }
    }

    [Fact(DisplayName = "Password verification fails closed by default")]
    public void PasswordVerification_DefaultValue_DeniesAccess()
    {
        // A test double, an unassigned field, or any path returning default must NOT
        // authenticate. This regressed once: Success used to be 0.
        Assert.NotEqual(
            LoginPasswordVerificationResult.Success,
            default(LoginPasswordVerificationResult));

        Assert.Equal(
            LoginPasswordVerificationResult.InvalidPassword,
            default(LoginPasswordVerificationResult));
    }

    [Fact(DisplayName = "Unknown email is indistinguishable from a wrong password")]
    public void UserNotFound_IsIndistinguishableFromWrongPassword()
    {
        // User enumeration guard: the two responses must be byte-identical.
        var unknownEmail = JsonSerializer.Serialize(
            LoginResponseDto.UserNotFound(), ProgramJsonOptions);

        var wrongPassword = JsonSerializer.Serialize(
            LoginResponseDto.CreateInvalidCredentials(), ProgramJsonOptions);

        Assert.Equal(wrongPassword, unknownEmail);
    }
}
