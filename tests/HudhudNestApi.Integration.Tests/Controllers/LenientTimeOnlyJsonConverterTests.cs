using System.Text.Json;
using HudhudNestApi.Configuration;
using HudhudNestApi.Controllers;

namespace HudhudNestApi.Integration.Tests.Controllers;

/// <summary>
/// Regression: the short-stay listing form posts check-in/out times as "HH:mm" (browser
/// &lt;input type="time"&gt;), which System.Text.Json's stock TimeOnly converter rejects — the user saw
/// "The Dto field is required. The Check in time field is empty or invalid."
/// </summary>
[Trait("Feature", "ShortStay")]
public sealed class LenientTimeOnlyJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new LenientTimeOnlyJsonConverter() },
    };

    [Theory]
    [InlineData("\"14:00\"", 14, 0, 0)]
    [InlineData("\"14:00:00\"", 14, 0, 0)]
    [InlineData("\"09:05:30\"", 9, 5, 30)]
    [InlineData("\" 23:59 \"", 23, 59, 0)]
    public void Read_accepts_HHmm_and_HHmmss(string json, int h, int m, int s)
        => Assert.Equal(new TimeOnly(h, m, s), JsonSerializer.Deserialize<TimeOnly>(json, Options));

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"25:00\"")]
    [InlineData("\"noon\"")]
    [InlineData("1400")]
    public void Read_rejects_garbage(string json)
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TimeOnly>(json, Options));

    [Fact]
    public void Write_emits_canonical_HHmmss()
        => Assert.Equal("\"14:00:00\"", JsonSerializer.Serialize(new TimeOnly(14, 0), Options));

    [Fact]
    public void Nullable_time_round_trips_null_and_short_form()
    {
        Assert.Null(JsonSerializer.Deserialize<TimeOnly?>("null", Options));
        Assert.Equal(new TimeOnly(22, 30), JsonSerializer.Deserialize<TimeOnly?>("\"22:30\"", Options));
    }

    [Fact]
    public void Create_request_from_the_form_payload_binds()
    {
        const string body = """
            {"accommodationTypeId":1,"title":"Sea view flat","description":"Nice","capacity":4,"bedrooms":2,
             "bathrooms":1,"checkInTime":"14:00","checkOutTime":"11:00","latitude":33.5,"longitude":36.3,
             "defaultBasePricePerNight":50,"propertyId":null}
            """;

        var dto = JsonSerializer.Deserialize<CreateShortStayListingRequest>(body, Options);

        Assert.NotNull(dto);
        Assert.Equal(new TimeOnly(14, 0), dto!.CheckInTime);
        Assert.Equal(new TimeOnly(11, 0), dto.CheckOutTime);
    }
}
