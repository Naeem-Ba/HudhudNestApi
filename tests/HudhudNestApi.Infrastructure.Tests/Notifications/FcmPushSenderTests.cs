using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Notifications.Push;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Infrastructure.Notifications.Push;

namespace HudhudNestApi.Infrastructure.Tests.Notifications;

public sealed class FcmPushSenderTests
{
    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("must not be called");
    }

    private static PushPayload Payload(Guid? property = null) =>
        new("HudhudNest", "Hallo", NotificationType.NewMessage, Guid.NewGuid(), property, null);

    [Fact]
    public async Task WithoutCredentials_TheSender_IsDisabled_AndNeverTouchesTheNetwork()
    {
        var sender = new FcmPushSender(
            new NoHttp(),
            Options.Create(new PushOptions { Enabled = true, ProjectId = "p" /* no key */ }),
            NullLogger<FcmPushSender>.Instance);

        Assert.Equal(PushSendStatus.Disabled, await sender.SendAsync("token", Payload()));
    }

    [Fact]
    public async Task NullSender_ReportsDisabled()
    {
        Assert.Equal(PushSendStatus.Disabled, await new NullPushSender().SendAsync("t", Payload()));
    }

    [Fact]
    public void TheMessage_CarriesLocalizedBody_AndRoutingData_ButNoRecipientIdentity()
    {
        var propertyId = Guid.NewGuid();
        var json = FcmPushSender.BuildMessage("device-1", Payload(propertyId));

        using var doc = JsonDocument.Parse(json);
        var message = doc.RootElement.GetProperty("message");
        Assert.Equal("device-1", message.GetProperty("token").GetString());
        Assert.Equal("Hallo", message.GetProperty("notification").GetProperty("body").GetString());
        var data = message.GetProperty("data");
        Assert.Equal("NewMessage", data.GetProperty("type").GetString());
        Assert.Equal(propertyId.ToString(), data.GetProperty("propertyId").GetString());
        Assert.False(data.TryGetProperty("userId", out _));
        Assert.False(data.TryGetProperty("relatedEntityId", out _));
    }

    [Theory]
    [InlineData(false, "p", "{}")]
    [InlineData(true, null, "{}")]
    [InlineData(true, "p", null)]
    public void OptionsAreOnlyConfigured_WhenEnabledAndProjectAndKeyArePresent(bool enabled, string? project, string? key)
    {
        Assert.False(new PushOptions { Enabled = enabled, ProjectId = project, ServiceAccountJson = key }.IsConfigured);
        Assert.True(new PushOptions { Enabled = true, ProjectId = "p", ServiceAccountJson = "{}" }.IsConfigured);
    }
}
