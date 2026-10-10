using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// The profile form is filled from GET /api/users/me and saves everything it shows. The response left out
/// the saved bio and contact line (the DTO has the fields, the handler never mapped them), so the form showed
/// both empty and the next save sent empty strings, which PUT /api/users/me reads as "clear the field".
/// Anyone who had written a bio lost it the next time they changed their name.
/// </summary>
public sealed class ProfileAboutInfoRoundTripTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private const string Password = "SecurePass9!";

    private readonly HttpClient _client;

    public ProfileAboutInfoRoundTripTests(PhoneAuthWebApplicationFactory factory)
        => _client = factory.CreateClient();

    [Fact(DisplayName = "GET /users/me returns the bio and contact line that PUT /users/me saved")]
    [Trait("Category", "Security")]
    public async Task Me_ReturnsTheSavedBioAndContactInfo()
    {
        var access = await RegisterByEmailAsync();

        var save = await SendAsync(HttpMethod.Put, "/api/users/me", access,
            new { bio = "Letting flats in Damascus since 2019.", contactInfo = "WhatsApp +963911234567" });
        await AssertStatusAsync(HttpStatusCode.NoContent, save);

        var me = await Json(await SendAsync(HttpMethod.Get, "/api/users/me", access));
        Assert.Equal("Letting flats in Damascus since 2019.", me.GetProperty("bio").GetString());
        Assert.Equal("WhatsApp +963911234567", me.GetProperty("contactInfo").GetString());
    }

    [Fact(DisplayName = "Saving only the name keeps the bio and contact line")]
    [Trait("Category", "Security")]
    public async Task Update_WithoutBioFields_KeepsTheSavedBioAndContactInfo()
    {
        var access = await RegisterByEmailAsync();
        await AssertStatusAsync(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Put, "/api/users/me", access,
            new { bio = "About me", contactInfo = "Call after 6pm" }));

        await AssertStatusAsync(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Put, "/api/users/me", access,
            new { displayName = "New Name" }));

        var me = await Json(await SendAsync(HttpMethod.Get, "/api/users/me", access));
        Assert.Equal("New Name", me.GetProperty("displayName").GetString());
        Assert.Equal("About me", me.GetProperty("bio").GetString());
        Assert.Equal("Call after 6pm", me.GetProperty("contactInfo").GetString());
    }

    private async Task<string> RegisterByEmailAsync()
    {
        var email = $"about-info-{Guid.NewGuid():N}@example.com";
        var registration = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Mail",
            lastName = "Account",
            email,
            password = Password,
            privacyPolicyAccepted = true,
            privacyPolicyVersion = "2026-10-01",
            termsAccepted = true,
            termsVersion = "2026-10-01",
            consentSource = "Web"
        });
        await AssertStatusAsync(HttpStatusCode.Created, registration);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        await AssertStatusAsync(HttpStatusCode.OK, login);
        return (await Json(login)).GetProperty("accessToken").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
