using System.Net;
using System.Net.Http.Json;
using HudhudNestApi.Integration.Tests.TestInfrastructure;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Marketing;

/// <summary>
/// Structured lead capture — replaces the previous approach of posting this data into
/// ContactController.Submit's free-text Body field.
/// </summary>
public sealed class LeadsControllerTests : IClassFixture<TestApplication>
{
    private readonly HttpClient _client;

    public LeadsControllerTests(TestApplication factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Submit_ValidLead_Returns200WithNoOfferApplied()
    {
        var dto = new
        {
            FullName = "أحمد الشمري",
            Phone = "0933123456",
            City = "دمشق",
            UserType = "agency",
            Notes = "مكتب عقاري في دمشق"
        };

        var response = await _client.PostAsJsonAsync("/api/leads", dto);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected OK but got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        Assert.Contains("leadId", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"offerRequested\":false", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Submit_MissingFullName_ReturnsBadRequest()
    {
        var dto = new
        {
            Phone = "0933123456",
            City = "دمشق",
            UserType = "agency"
        };

        var response = await _client.PostAsJsonAsync("/api/leads", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("crm-admin")]
    [InlineData("office")]
    [InlineData("")]
    [Trait("Category", "Integration")]
    public async Task Submit_UnknownUserType_ReturnsBadRequest(string userType)
    {
        var dto = new
        {
            FullName = "أحمد الشمري",
            Phone = "0933123456",
            City = "دمشق",
            UserType = userType
        };

        var response = await _client.PostAsJsonAsync("/api/leads", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAll_WithoutAuthentication_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/leads");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
