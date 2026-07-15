using System.Net;
using System.Net.Http.Json;
using PropertyApi.Integration.Tests.TestInfrastructure;
using Xunit;

namespace PropertyApi.Application.Tests.Contact;

/// <summary>
/// اختبارات تكامل لـ ContactController.
/// تتحقق من:
///   1. إرسال رسالة تواصل بنجاح
///   2. رفض رسالة ناقصة البيانات
///   3. قراءة Enums عبر API
/// </summary>
public sealed class ContactControllerTests
    : IClassFixture<TestApplication>
{
    private readonly HttpClient _client;

    public ContactControllerTests(TestApplication factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Submit_ValidContact_Returns200()
    {
        // Arrange
        var dto = new
        {
            Name = "أحمد الشمري",
            Email = "ahmed@test.com",
            Subject = "استفسار عن شقة",
            Message = "أريد معرفة المزيد عن الشقق المتاحة في برلين"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/contact", dto);
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected OK but got {(int)response.StatusCode} {response.StatusCode}. Body: {responseBody}");

        Assert.False(
            string.IsNullOrWhiteSpace(responseBody),
            "Expected a non-empty response body.");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Submit_MissingName_ReturnsBadRequest()
    {
        // Arrange
        var dto = new
        {
            Email = "test@test.com",
            Message = "test"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/contact", dto);
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"Expected BadRequest or UnprocessableEntity but got {(int)response.StatusCode} {response.StatusCode}. Body: {responseBody}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetEnums_PropertyStatus_ReturnsValues()
    {
        // Act
        var response = await _client.GetAsync("/api/enums/PropertyStatus");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected OK but got {(int)response.StatusCode} {response.StatusCode}. Body: {responseBody}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetEnums_LegacyAlias_STATUS_ReturnsValues()
    {
        // Act
        var response = await _client.GetAsync("/api/enums/STATUS");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected OK but got {(int)response.StatusCode} {response.StatusCode}. Body: {responseBody}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetEnums_InvalidName_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/enums/INVALIDNAME");
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected BadRequest but got {(int)response.StatusCode} {response.StatusCode}. Body: {responseBody}");
    }
}
