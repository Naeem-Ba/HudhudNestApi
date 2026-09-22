using System.Net;
using System.Net.Http.Json;
using HudhudNestApi.Integration.Tests.TestInfrastructure;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Marketing;

public sealed class SurveysControllerTests : IClassFixture<TestApplication>
{
    private readonly HttpClient _client;

    public SurveysControllerTests(TestApplication factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SubmitLanding_WithNoAnswersAtAll_Returns200()
    {
        // Every answer is optional by design — an empty submission must still succeed.
        var response = await _client.PostAsJsonAsync("/api/surveys/landing", new { });

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected OK but got {(int)response.StatusCode}. Body: {body}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SubmitLanding_WithFullAnswers_Returns200()
    {
        var dto = new
        {
            WillingnessToPay = "Yes",
            PreferredPaymentModel = "MonthlySubscription",
            ExpectedMonthlyPriceUsd = 49,
            AcceptableCommissionPercent = 2.5,
            MostImportantFeature = "إدارة الإعلانات",
            TeamSize = 5,
            PropertyCount = 80
        };

        var response = await _client.PostAsJsonAsync("/api/surveys/landing", dto);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected OK but got {(int)response.StatusCode}. Body: {body}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SubmitLanding_WithCommissionPercentOver100_ReturnsBadRequest()
    {
        var dto = new { AcceptableCommissionPercent = 150 };

        var response = await _client.PostAsJsonAsync("/api/surveys/landing", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetStats_WithoutAuthentication_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/surveys/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
