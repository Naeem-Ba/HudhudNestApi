using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PropertyApi.Application.Tests.Contact;

/// <summary>
/// اختبارات تكامل لـ ContactController.
/// تتحقق من:
///   1. إرسال رسالة تواصل بنجاح
///   2. رفض رسالة ناقصة البيانات
/// </summary>
public sealed class ContactControllerTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ContactControllerTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

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

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(body);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Submit_MissingName_ReturnsBadRequest()
    {
        var dto = new { Email = "test@test.com", Message = "test" };
        // Name مفقود

        var response = await _client.PostAsJsonAsync("/api/contact", dto);

        // يتوقع 400 من FluentValidation أو Model Binding
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetEnums_PropertyStatus_ReturnsValues()
    {
        var response = await _client.GetAsync("/api/enums/PropertyStatus");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetEnums_LegacyAlias_STATUS_ReturnsValues()
    {
        // التحقق من التوافق مع الأسماء القديمة
        var response = await _client.GetAsync("/api/enums/STATUS");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetEnums_InvalidName_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/enums/INVALIDNAME");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}