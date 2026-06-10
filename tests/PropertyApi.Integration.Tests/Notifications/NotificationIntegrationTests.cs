using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.DTOs;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Notifications.Entities;
using PropertyApi.Domain.Notifications.Enums;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Integration.Tests.TestInfrastructure;
using Property = PropertyApi.Domain.Listings.Entities.Property;

namespace PropertyApi.Integration.Tests.Notifications;

/// <summary>
/// Integration tests for SignalR real-time notifications.
///
/// Covered scenarios:
/// - SendMessage creates a Notification row in the database.
/// - The persisted Notification has the correct recipient, type, property, related message id, and read state.
/// - GET /api/notifications/unread-count returns the correct count.
/// - SignalR /notificationHub pushes ReceiveNotification to the connected recipient.
/// - Failed SendMessage requests do not create notifications.
/// - Large notification sets are counted and paginated correctly.
/// </summary>
public sealed class NotificationIntegrationTests
    : IClassFixture<NotificationWebApplicationFactory>
{
    private readonly NotificationWebApplicationFactory _factory;

    public NotificationIntegrationTests(NotificationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task SendMessage_CreatesAndPersistsNotification_AndUnreadCountIsOne()
    {
        // Arrange
        var owner = await CreateUserAsync("owner");
        var visitor = await CreateUserAsync("visitor");
        var property = await CreatePropertyForOwnerAsync(owner.Id);
        var visitorClient = await CreateAuthenticatedClientAsync(visitor.Id);
        var ownerClient = await CreateAuthenticatedClientAsync(owner.Id);

        // Act
        var sendResponse = await visitorClient.PostAsJsonAsync(
            "/api/messages",
            new
            {
                PropertyId = property.Id,
                ReceiverId = (Guid?)null,
                Content = "Hello, I am interested in this property."
            });

        var sendBody = await sendResponse.Content.ReadAsStringAsync();

        // Assert: SendMessage succeeded.
        Assert.True(
            sendResponse.StatusCode == HttpStatusCode.Created,
            $"Expected 201 Created but got {(int)sendResponse.StatusCode} {sendResponse.StatusCode}. Body: {sendBody}");

        var messageId = GetGuidFromJson(sendBody, "Id");

        // Assert: Notification was stored in DB.
        var notification = await GetSingleNotificationForUserAsync(owner.Id);

        Assert.Equal(owner.Id, notification.RecipientId);
        Assert.Equal(NotificationType.NewMessage, notification.Type);
        Assert.Equal(property.Id, notification.PropertyId);
        Assert.Equal(messageId, notification.RelatedEntityId);
        Assert.False(notification.IsRead);
        Assert.Null(notification.ReadAt);
        Assert.Contains("رسالة", notification.Message, StringComparison.OrdinalIgnoreCase);

        // Assert: unread-count sees the stored unread notification.
        var countResponse = await ownerClient.GetAsync("/api/notifications/unread-count");
        var countBody = await countResponse.Content.ReadAsStringAsync();

        Assert.True(
            countResponse.StatusCode == HttpStatusCode.OK,
            $"Expected 200 OK but got {(int)countResponse.StatusCode} {countResponse.StatusCode}. Body: {countBody}");

        Assert.Equal(1, GetIntFromJson(countBody, "UnreadCount"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task MarkAsRead_ChangesNotificationState_AndUnreadCountBecomesZero()
    {
        // Arrange
        var owner = await CreateUserAsync("owner-read");
        var visitor = await CreateUserAsync("visitor-read");
        var property = await CreatePropertyForOwnerAsync(owner.Id);
        var visitorClient = await CreateAuthenticatedClientAsync(visitor.Id);
        var ownerClient = await CreateAuthenticatedClientAsync(owner.Id);

        await SendMessageAndAssertCreatedAsync(
            visitorClient,
            property.Id,
            "Please contact me about this listing.");

        var notification = await GetSingleNotificationForUserAsync(owner.Id);

        // Act
        var markResponse = await ownerClient.PatchAsync(
            $"/api/notifications/{notification.Id}/read",
            content: null);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, markResponse.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var updated = await db.Notifications
            .IgnoreQueryFilters()
            .SingleAsync(n => n.Id == notification.Id);

        Assert.True(updated.IsRead);
        Assert.NotNull(updated.ReadAt);

        var countResponse = await ownerClient.GetAsync("/api/notifications/unread-count");
        var countBody = await countResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, countResponse.StatusCode);
        Assert.Equal(0, GetIntFromJson(countBody, "UnreadCount"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task SignalR_ConnectedRecipient_ReceivesNotification_WhenMessageIsSent()
    {
        // Arrange
        var owner = await CreateUserAsync("owner-signalr");
        var visitor = await CreateUserAsync("visitor-signalr");
        var property = await CreatePropertyForOwnerAsync(owner.Id);

        var ownerToken = await CreateAccessTokenAsync(owner.Id);
        var visitorClient = await CreateAuthenticatedClientAsync(visitor.Id);

        var received = new TaskCompletionSource<NotificationDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = CreateNotificationHubConnection(ownerToken);

        connection.On<NotificationDto>("ReceiveNotification", notification =>
        {
            received.TrySetResult(notification);
        });

        await connection.StartAsync();

        try
        {
            // Act
            await SendMessageAndAssertCreatedAsync(
                visitorClient,
                property.Id,
                "Real-time notification test message.");

            var notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Assert: SignalR payload is correct.
            Assert.Equal(NotificationType.NewMessage, notification.Type);
            Assert.Equal(property.Id, notification.PropertyId);
            Assert.False(notification.IsRead);
            Assert.False(string.IsNullOrWhiteSpace(notification.Message));

            // Assert: the same notification is also persisted.
            var stored = await GetSingleNotificationForUserAsync(owner.Id);
            Assert.Equal(notification.Id, stored.Id);
            Assert.Equal(notification.RelatedEntityId, stored.RelatedEntityId);
        }
        finally
        {
            await connection.StopAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task SendMessage_OwnerReplyWithoutExistingConversation_ReturnsForbidden_AndDoesNotCreateNotification()
    {
        // Arrange
        var owner = await CreateUserAsync("owner-forbidden");
        var stranger = await CreateUserAsync("stranger-forbidden");
        var property = await CreatePropertyForOwnerAsync(owner.Id);
        var ownerClient = await CreateAuthenticatedClientAsync(owner.Id);

        var beforeCount = await CountNotificationsForUserAsync(stranger.Id);

        // Act: Owner cannot reply to a user unless a conversation already exists.
        var response = await ownerClient.PostAsJsonAsync(
            "/api/messages",
            new
            {
                PropertyId = property.Id,
                ReceiverId = stranger.Id,
                Content = "This reply should be forbidden because there is no prior conversation."
            });

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var afterCount = await CountNotificationsForUserAsync(stranger.Id);
        Assert.Equal(beforeCount, afterCount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task Notifications_LargeUnreadSet_ReturnsCorrectUnreadCount_AndPaginatesAtFifty()
    {
        // Arrange
        var user = await CreateUserAsync("bulk-notifications");
        var client = await CreateAuthenticatedClientAsync(user.Id);
        var total = 75;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            for (var i = 1; i <= total; i++)
            {
                db.Notifications.Add(new Notification
                {
                    RecipientId = user.Id,
                    Type = NotificationType.PropertyPriceChanged,
                    Message = $"Bulk notification {i:D2}",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow.AddSeconds(i),
                    UpdatedAt = DateTime.UtcNow.AddSeconds(i)
                });
            }

            await db.SaveChangesAsync();
        }

        // Act: unread-count
        var countResponse = await client.GetAsync("/api/notifications/unread-count");
        var countBody = await countResponse.Content.ReadAsStringAsync();

        // Assert: all 75 are counted.
        Assert.Equal(HttpStatusCode.OK, countResponse.StatusCode);
        Assert.Equal(total, GetIntFromJson(countBody, "UnreadCount"));

        // Act: pageSize is clamped to 50 in the controller.
        var listResponse = await client.GetAsync("/api/notifications?page=1&pageSize=1000");
        var listBody = await listResponse.Content.ReadAsStringAsync();

        // Assert: only 50 are returned on the first page.
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var json = JsonDocument.Parse(listBody);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(50, json.RootElement.GetArrayLength());
    }



    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task SoftDeleteNotification_HidesNotificationFromList_AndUnreadCountBecomesZero()
    {
        // Arrange
        var user = await CreateUserAsync("soft-delete-user");
        var client = await CreateAuthenticatedClientAsync(user.Id);
        var notification = await CreateStoredNotificationAsync(
            user.Id,
            NotificationType.PropertyPriceChanged,
            "Soft-delete test notification");

        // Act
        var response = await client.PatchAsync(
            $"/api/notifications/{notification.Id}/soft-delete",
            content: null);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Notifications
                .IgnoreQueryFilters()
                .SingleAsync(n => n.Id == notification.Id);

            Assert.True(stored.IsDeleted);
            Assert.NotNull(stored.DeletedAt);
        }

        var listResponse = await client.GetAsync("/api/notifications");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using (var document = JsonDocument.Parse(listBody))
        {
            Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
            Assert.Empty(document.RootElement.EnumerateArray());
        }

        var countResponse = await client.GetAsync("/api/notifications/unread-count");
        var countBody = await countResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, countResponse.StatusCode);
        Assert.Equal(0, GetIntFromJson(countBody, "UnreadCount"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task DeleteNotification_RemovesNotificationPhysically()
    {
        // Arrange
        var user = await CreateUserAsync("hard-delete-user");
        var client = await CreateAuthenticatedClientAsync(user.Id);
        var notification = await CreateStoredNotificationAsync(
            user.Id,
            NotificationType.PropertyStatusChanged,
            "Hard-delete test notification");

        // Act
        var response = await client.DeleteAsync($"/api/notifications/{notification.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exists = await db.Notifications
            .IgnoreQueryFilters()
            .AnyAsync(n => n.Id == notification.Id);

        Assert.False(exists);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task UpdateProperty_StatusChange_CreatesPropertyStatusChangedNotification()
    {
        // Arrange
        var owner = await CreateUserAsync("property-status-owner");
        var property = await CreatePropertyForOwnerAsync(owner.Id);
        var ownerClient = await CreateAuthenticatedClientAsync(owner.Id);

        // Act
        var response = await ownerClient.PutAsJsonAsync(
            $"/api/properties/{property.Id}",
            CreateUpdatePropertyPayload(
                property.Id,
                owner.Id,
                status: PropertyStatus.Reserved));

        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"Expected 204 NoContent but got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");

        var notification = await GetSingleNotificationForUserAsync(owner.Id);

        Assert.Equal(NotificationType.PropertyStatusChanged, notification.Type);
        Assert.Equal(property.Id, notification.PropertyId);
        Assert.False(notification.IsRead);
        Assert.Contains("Reserved", notification.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task UpdateProperty_ColdRentChange_CreatesPropertyPriceChangedNotification()
    {
        // Arrange
        var owner = await CreateUserAsync("property-price-owner");
        var property = await CreatePropertyForOwnerAsync(owner.Id);
        var ownerClient = await CreateAuthenticatedClientAsync(owner.Id);
        var newColdRent = property.ColdRent!.Value + 250;

        // Act
        var response = await ownerClient.PutAsJsonAsync(
            $"/api/properties/{property.Id}",
            CreateUpdatePropertyPayload(
                property.Id,
                owner.Id,
                coldRent: newColdRent));

        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"Expected 204 NoContent but got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");

        var notification = await GetSingleNotificationForUserAsync(owner.Id);

        Assert.Equal(NotificationType.PropertyPriceChanged, notification.Type);
        Assert.Equal(property.Id, notification.PropertyId);
        Assert.False(notification.IsRead);
        Assert.Contains("Cold rent", notification.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(newColdRent.ToString("0.##"), notification.Message, StringComparison.OrdinalIgnoreCase);
    }


    private async Task<Notification> CreateStoredNotificationAsync(
        Guid recipientId,
        NotificationType type,
        string message)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var notification = new Notification
        {
            RecipientId = recipientId,
            Type = type,
            Message = message,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        return notification;
    }

    private static object CreateUpdatePropertyPayload(
        Guid propertyId,
        Guid requestingUserId,
        PropertyStatus? status = null,
        decimal? coldRent = null,
        decimal? warmRent = null,
        decimal? purchasePrice = null,
        bool? isPublished = null)
    {
        return new
        {
            PropertyId = propertyId,
            RequestingUserId = requestingUserId,
            Title = (string?)null,
            Description = (string?)null,
            Street = (string?)null,
            City = (string?)null,
            Region = (string?)null,
            CountryCode = (string?)null,
            PostalCode = (string?)null,
            Latitude = (decimal?)null,
            Longitude = (decimal?)null,
            ColdRent = coldRent,
            WarmRent = warmRent,
            PurchasePrice = purchasePrice,
            Deposit = (decimal?)null,
            AdditionalCosts = (decimal?)null,
            CurrencyCode = (string?)null,
            Rooms = (int?)null,
            Area = (decimal?)null,
            Floor = (int?)null,
            TotalFloors = (int?)null,
            HasBalcony = (bool?)null,
            HasElevator = (bool?)null,
            HasParkingSpace = (bool?)null,
            HeatingType = (HeatingType?)null,
            Status = status,
            Condition = (PropertyCondition?)null,
            EnergyEfficiency = (EnergyEfficiencyType?)null,
            AvailableFrom = (DateTime?)null,
            ExpiresAt = (DateTime?)null,
            IsPublished = isPublished
        };
    }

    private async Task<HttpResponseMessage> SendMessageAndAssertCreatedAsync(
        HttpClient client,
        Guid propertyId,
        string content)
    {
        var response = await client.PostAsJsonAsync(
            "/api/messages",
            new
            {
                PropertyId = propertyId,
                ReceiverId = (Guid?)null,
                Content = content
            });

        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected 201 Created but got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");

        return response;
    }

    private HubConnection CreateNotificationHubConnection(string accessToken)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/notificationHub"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(Guid userId)
    {
        var token = await CreateAccessTokenAsync(userId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private async Task<string> CreateAccessTokenAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);

        var roles = await userManager.GetRolesAsync(user!);
        return tokenService.GenerateAccessToken(user!, roles.ToArray());
    }

    private async Task<User> CreateUserAsync(string prefix)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var unique = Guid.NewGuid().ToString("N");
        var email = $"{prefix}.{unique}@tests.local";

        var user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = prefix,
            LastName = "Tester",
            DisplayName = $"{prefix} Tester",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, "Password123");

        Assert.True(
            result.Succeeded,
            "Failed to create test user: " + string.Join(" | ", result.Errors.Select(e => e.Description)));

        return user;
    }

    private async Task<Property> CreatePropertyForOwnerAsync(Guid ownerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var property = Property.Create(
            title: "Integration Test Apartment",
            description: "A property created by notification integration tests.",
            ownerId: ownerId,
            listingType: ListingType.ForRent,
            countryCode: "DE",
            currencyCode: "EUR");

        property.Street = "Teststraße 1";
        property.City = "Düsseldorf";
        property.ColdRent = 900;
        property.WarmRent = 1100;
        property.Rooms = 3;
        property.Area = 80;
        property.HasBalcony = true;
        property.HasElevator = false;
        property.HasParkingSpace = false;
        property.HeatingType = HeatingType.Gas;

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        return property;
    }

    private async Task<Notification> GetSingleNotificationForUserAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var notifications = await db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.RecipientId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        Assert.Single(notifications);
        return notifications[0];
    }

    private async Task<int> CountNotificationsForUserAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Notifications
            .IgnoreQueryFilters()
            .CountAsync(n => n.RecipientId == userId);
    }

    private static Guid GetGuidFromJson(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        var value = GetJsonProperty(document.RootElement, propertyName).GetString();

        Assert.False(string.IsNullOrWhiteSpace(value), $"JSON property '{propertyName}' was empty. Payload: {json}");
        return Guid.Parse(value!);
    }

    private static int GetIntFromJson(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        return GetJsonProperty(document.RootElement, propertyName).GetInt32();
    }

    private static JsonElement GetJsonProperty(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        throw new InvalidOperationException(
            $"JSON property '{propertyName}' was not found. Payload: {element}");
    }
}
