using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace PropertyApi.StagingSmokeTests;

public sealed class StagingSmokeJourneyTests
{
    private static readonly string[] MandatoryJourneys =
    [
        "Technical health",
        "Registration",
        "OTP",
        "Login",
        "Refresh token rotation",
        "Property creation",
        "Immediate property read",
        "Image upload",
        "Property listing",
        "Communication",
        "Logout",
        "Cleanup"
    ];

    [Fact(Timeout = 900_000)]
    public async Task Complete_staging_release_journey()
    {
        var run = new SmokeRun();
        Exception? failure = null;

        try
        {
            await run.ExecuteAsync();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            try
            {
                await run.CleanupAsync();
            }
            catch (Exception ex)
            {
                run.FailCleanup(ex.Message);
                failure ??= ex;
            }

            await run.WriteReportsAsync(failure);
        }

        Assert.True(failure is null, failure?.Message);
    }

    private sealed class SmokeRun
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly SmokeConfiguration _config = SmokeConfiguration.Load();
        private readonly HttpClient _client;
        private readonly Dictionary<string, JourneyResult> _journeys = MandatoryJourneys
            .ToDictionary(x => x, x => new JourneyResult(x));
        private readonly DateTime _startedAtUtc = DateTime.UtcNow;
        private readonly List<Guid> _userIds = [];
        private readonly List<Guid> _propertyIds = [];
        private readonly List<Guid> _imageIds = [];

        private Session? _owner;
        private Session? _visitor;
        private Guid _propertyId;
        private int _governorateId;
        private int _propertyTypeId;
        private string _imageUrl = string.Empty;
        private string _deployedCommitSha = string.Empty;
        private string _cleanupStatus = "skipped";
        private string? _cleanupMessage;
        private string? _csrfToken;

        public SmokeRun()
        {
            _client = new HttpClient
            {
                BaseAddress = _config.BaseUrl,
                Timeout = TimeSpan.FromSeconds(_config.HttpTimeoutSeconds)
            };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("PropertyApi-StagingSmoke/1.0");
        }

        public async Task ExecuteAsync()
        {
            // CookieCsrfProtectionMiddleware requires an X-XSRF-TOKEN header on every unsafe
            // request once ANY request has carried the refresh_token cookie -- not just on
            // /auth/refresh and /auth/logout, on any endpoint (see CookieCsrfOptions'
            // AuthenticationCookieName doc comment). _client keeps cookies across requests
            // (default HttpClientHandler), so as soon as Registration's/Login's response sets
            // that cookie, every later unsafe call -- including the very next request in the
            // *same* journey, e.g. Registration's own one-time-use OTP recheck -- needs this
            // header or the antiforgery check 403s before the endpoint's own logic ever runs.
            // Fetching the token once up front (matching the real frontend's CsrfInterceptor,
            // added for RELEASE-BLOCKERS-AR.md B-20) is harmless for the handful of early
            // calls made before any auth cookie exists: CookieCsrfProtectionMiddleware only
            // validates it once hasAuthCookie is true.
            await FetchCsrfTokenAsync();

            await Journey("Technical health", TechnicalHealthAsync);
            await Journey("Registration", RegisterUsersAsync);
            await Journey("OTP", VerifyOtpSecurityAsync);
            await Journey("Login", LoginAsync);
            await Journey("Refresh token rotation", RefreshAsync);
            await Journey("Property creation", CreatePropertyAsync);
            await Journey("Immediate property read", ImmediateReadAsync);
            await Journey("Image upload", ImageUploadAsync);
            await Journey("Property listing", ListingAsync);
            await Journey("Communication", CommunicationAsync);
            await Journey("Logout", LogoutAsync);
        }

        public async Task CleanupAsync()
        {
            if (_propertyIds.Count == 0 && _userIds.Count == 0)
            {
                Pass("Cleanup", 1);
                _cleanupStatus = "passed";
                return;
            }

            var response = await SendJsonAsync(
                HttpMethod.Post,
                "/api/staging-test-support/cleanup",
                new { _config.RunId, UserIds = _userIds, PropertyIds = _propertyIds },
                stagingSecret: _config.CleanupSecret);

            Expect(response.StatusCode == HttpStatusCode.OK,
                $"Cleanup expected HTTP 200, got {(int)response.StatusCode}.");

            using var body = await ReadJsonAsync(response);
            Expect(Integer(body.RootElement, "usersRemoved") == _userIds.Count,
                "Cleanup did not remove every test user.");
            Expect(Integer(body.RootElement, "propertiesRemoved") == _propertyIds.Count,
                "Cleanup did not remove every test property.");

            Pass("Cleanup", 3);
            _cleanupStatus = "passed";
        }

        public void FailCleanup(string message)
        {
            var result = _journeys["Cleanup"];
            result.Status = "failed";
            result.Error = message;
            _cleanupStatus = "failed";
            _cleanupMessage = message;
        }

        private async Task TechnicalHealthAsync()
        {
            await ExpectStatusWithTransientRetry("/health/live", HttpStatusCode.OK);
            await ExpectStatusWithTransientRetry("/health/ready", HttpStatusCode.OK);

            var enumResponse = await _client.GetAsync("/api/enums/PropertyStatus");
            Expect(enumResponse.StatusCode == HttpStatusCode.OK,
                "PropertyStatus enum endpoint failed.");
            using var enumJson = await ReadJsonAsync(enumResponse);
            Expect(enumJson.RootElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object,
                "Enum response is not a JSON object or array.");

            // RELEASE-BLOCKERS-AR.md B-8: build-info now requires the same Staging automation
            // secret the cleanup call already sends — it is no longer anonymous.
            var response = await SendAsync(
                HttpMethod.Get, "/api/operational/build-info", stagingSecret: _config.CleanupSecret);
            Expect(response.StatusCode == HttpStatusCode.OK,
                "Build-information endpoint failed.");
            using var body = await ReadJsonAsync(response);
            var root = body.RootElement;

            Expect(Text(root, "environment") == "Staging", "Deployed environment is not Staging.");
            _deployedCommitSha = Text(root, "commitSha");
            Expect(string.Equals(_deployedCommitSha, _config.ExpectedCommitSha,
                    StringComparison.OrdinalIgnoreCase),
                "Deployed commit does not match the intended release.");

            var migration = Property(root, "migration");
            Expect(Boolean(migration, "complete"), "Database has pending EF Core migrations.");
            Expect(Boolean(migration, "postGisAvailable"), "PostGIS is unavailable.");
            Expect(Integer(migration, "appliedCount") > 0, "No EF Core migrations are applied.");

            var isolation = Property(root, "isolation");
            Expect(!string.IsNullOrWhiteSpace(Text(isolation, "environmentId")),
                "Staging environment identity is missing.");
            Expect(!string.IsNullOrWhiteSpace(Text(isolation, "databaseMarker")),
                "Staging database isolation marker is missing.");
            Expect(!string.IsNullOrWhiteSpace(Text(isolation, "redisMarker")),
                "Staging Redis isolation marker is missing.");
            Expect(!string.IsNullOrWhiteSpace(Text(isolation, "storageMarker")),
                "Staging storage isolation marker is missing.");
            Expect(Boolean(isolation, "externalNotificationsDisabled"),
                "External notifications are not disabled in Staging.");
            Expect(Boolean(isolation, "testSupportEnabled"),
                "Staging test support is not enabled.");
            Expect(_config.AllowInMemoryMedia || !Boolean(isolation, "inMemoryMedia"),
                "A real Staging release may not use in-memory media storage.");
        }

        private async Task RegisterUsersAsync()
        {
            _owner = await RegisterPhoneUserAsync(_config.OwnerPhone, "OWNER");
            _visitor = await RegisterPhoneUserAsync(_config.VisitorPhone, "VISITOR");

            var duplicate = await SendJsonAsync(HttpMethod.Post,
                "/api/auth/phone/registration/send-otp",
                new { PhoneNumber = _config.OwnerPhone },
                stagingSecret: _config.CleanupSecret);
            Expect(duplicate.StatusCode == HttpStatusCode.OK,
                "Duplicate-registration privacy response must remain generic HTTP 200.");
            using var duplicateBody = await ReadJsonAsync(duplicate);
            Expect(!TryGuid(duplicateBody.RootElement, "ChallengeId", out _),
                "An already registered number received a new registration challenge.");
        }

        private Task VerifyOtpSecurityAsync()
        {
            // Wrong-code and one-time-use checks are executed during registration.
            Expect(_owner is not null && _visitor is not null, "OTP journey did not create both users.");
            return Task.CompletedTask;
        }

        private async Task LoginAsync()
        {
            var anonymous = await _client.GetAsync("/api/users/me");
            Expect(anonymous.StatusCode == HttpStatusCode.Unauthorized,
                "Protected endpoint accepted a missing token.");

            var invalid = await SendAsync(HttpMethod.Get, "/api/users/me", token: "invalid-token");
            Expect(invalid.StatusCode == HttpStatusCode.Unauthorized,
                "Protected endpoint accepted an invalid token.");

            var wrong = await SendJsonAsync(HttpMethod.Post, "/api/auth/phone/login",
                new { PhoneNumber = _config.OwnerPhone, Password = _config.Password + "x" });
            Expect(wrong.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized,
                "Incorrect password was not rejected.");

            _owner = await LoginPhoneAsync(_config.OwnerPhone);
            _visitor = await LoginPhoneAsync(_config.VisitorPhone);

            var me = await SendAsync(HttpMethod.Get, "/api/users/me", token: _owner.AccessToken);
            Expect(me.StatusCode == HttpStatusCode.OK, "Valid access token cannot call /api/users/me.");
            using var body = await ReadJsonAsync(me);
            var userId = Guid.Parse(Text(body.RootElement, "Id"));
            _owner = _owner with { UserId = userId };
            if (!_userIds.Contains(userId)) _userIds.Add(userId);

            var visitorMe = await SendAsync(HttpMethod.Get, "/api/users/me", token: _visitor.AccessToken);
            Expect(visitorMe.StatusCode == HttpStatusCode.OK, "Visitor access token is unusable.");
            using var visitorBody = await ReadJsonAsync(visitorMe);
            var visitorId = Guid.Parse(Text(visitorBody.RootElement, "Id"));
            _visitor = _visitor with { UserId = visitorId };
            if (!_userIds.Contains(visitorId)) _userIds.Add(visitorId);
        }

        private async Task RefreshAsync()
        {
            RequireSessions();
            var previous = _owner!.RefreshToken;
            var response = await SendJsonAsync(HttpMethod.Post, "/api/auth/refresh",
                new { RefreshToken = previous });
            Expect(response.StatusCode == HttpStatusCode.OK, "Valid refresh token was rejected.");
            using var body = await ReadJsonAsync(response);
            var access = RequiredText(body.RootElement, "accessToken");

            // See RequiredCookie's doc comment: the rotated token is cookie-only now.
            var refresh = RequiredCookie(response, RefreshTokenCookieName);
            Expect(access != _owner.AccessToken, "Refresh did not issue a new access token.");
            Expect(refresh != previous, "Refresh-token rotation did not issue a new token.");
            _owner = _owner with { AccessToken = access, RefreshToken = refresh };

            // The rotated access token must work *before* the replay probe below: replaying a
            // rotated refresh token is treated as theft and (RefreshTokenReuseHandler) revokes
            // every active refresh token of the user and rotates the security stamp, which
            // deliberately invalidates the access token that was just issued. Probing first and
            // using the new token afterwards (the old order) made this journey fail on correct
            // behaviour.
            var protectedResponse = await SendAsync(
                HttpMethod.Get, "/api/users/me", token: _owner.AccessToken);
            Expect(protectedResponse.StatusCode == HttpStatusCode.OK,
                "Refreshed access token is unusable.");

            var invalid = await SendJsonAsync(HttpMethod.Post, "/api/auth/refresh",
                new { RefreshToken = "not-a-refresh-token" });
            Expect(invalid.StatusCode == HttpStatusCode.Unauthorized,
                "Invalid refresh token was accepted.");

            var reuse = await SendJsonAsync(HttpMethod.Post, "/api/auth/refresh",
                new { RefreshToken = previous });
            Expect(reuse.StatusCode == HttpStatusCode.Unauthorized,
                "Rotated refresh token was reusable.");

            // Reuse detection must have killed the whole session family, including the token
            // issued by the legitimate rotation.
            var afterReuse = await SendAsync(
                HttpMethod.Get, "/api/users/me", token: _owner.AccessToken);
            Expect(afterReuse.StatusCode == HttpStatusCode.Unauthorized,
                "Refresh-token reuse did not revoke the session family.");

            // Later journeys (property creation, upload, ...) need a live owner session.
            var ownerId = _owner.UserId;
            _owner = (await LoginPhoneAsync(_config.OwnerPhone)) with { UserId = ownerId };
            var relogin = await SendAsync(
                HttpMethod.Get, "/api/users/me", token: _owner.AccessToken);
            Expect(relogin.StatusCode == HttpStatusCode.OK,
                "Owner could not sign in again after refresh-token reuse revocation.");
        }

        private async Task CreatePropertyAsync()
        {
            RequireSessions();
            await LoadListingReferenceDataAsync();

            // Publishing is gated on an explicit plan choice (LISTING_PLAN_REQUIRED, see
            // CreatePropertyCommandHandler) -- "free" is not an implicit default -- so a fresh
            // account must pick one first, exactly like a real user does.
            var plan = await SendJsonAsync(HttpMethod.Post, "/api/users/me/plan",
                new { Tier = "free" }, _owner!.AccessToken);
            Expect(plan.StatusCode == HttpStatusCode.NoContent,
                $"Selecting the free plan expected HTTP 204, got {(int)plan.StatusCode}{await ApiErrorAsync(plan)}.");

            var payload = PropertyPayload(_config.PropertyTitle);

            var unauthorized = await SendJsonAsync(HttpMethod.Post, "/api/properties", payload);
            Expect(unauthorized.StatusCode == HttpStatusCode.Unauthorized,
                "Unauthenticated property creation was accepted.");

            var invalid = await SendJsonAsync(HttpMethod.Post, "/api/properties",
                PropertyPayload(string.Empty), _owner.AccessToken);
            Expect(invalid.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
                "Invalid property payload was accepted.");

            var response = await SendJsonAsync(
                HttpMethod.Post, "/api/properties", payload, _owner.AccessToken);
            Expect(response.StatusCode == HttpStatusCode.Created,
                $"Property creation expected HTTP 201, got {(int)response.StatusCode}{await ApiErrorAsync(response)}.");
            using var body = await ReadJsonAsync(response);
            Expect(TryGuid(body.RootElement, "id", out _propertyId),
                "Property creation response has no valid id.");
            Expect(response.Headers.Location is not null,
                "Property creation response has no Location header.");
            _propertyIds.Add(_propertyId);
        }

        private async Task ImmediateReadAsync()
        {
            RequireSessions();
            var response = await SendAsync(HttpMethod.Get,
                $"/api/properties/{_propertyId}/manage", token: _owner!.AccessToken);
            Expect(response.StatusCode == HttpStatusCode.OK,
                "POST succeeded but immediate owner read returned a non-success status.");
            using var body = await ReadJsonAsync(response);
            AssertProperty(body.RootElement);

            var publicDraft = await _client.GetAsync($"/api/properties/{_propertyId}");
            Expect(publicDraft.StatusCode == HttpStatusCode.NotFound,
                "An unpublished draft leaked through the public detail endpoint.");
        }

        private async Task ImageUploadAsync()
        {
            RequireSessions();
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z0mQAAAAASUVORK5CYII=");

            var upload = await UploadAsync(_propertyId, png, "smoke.png", "image/png", _owner!.AccessToken);
            Expect(upload.StatusCode == HttpStatusCode.OK,
                $"Valid image upload failed with HTTP {(int)upload.StatusCode}.");
            using var body = await ReadJsonAsync(upload);
            Expect(body.RootElement.ValueKind == JsonValueKind.Array &&
                body.RootElement.GetArrayLength() == 1, "Image upload response contract is invalid.");
            var image = body.RootElement[0];
            Expect(TryGuid(image, "Id", out var imageId), "Uploaded image id is invalid.");
            _imageIds.Add(imageId);
            _imageUrl = RequiredText(image, "Url");

            var invalid = await UploadAsync(_propertyId,
                Encoding.UTF8.GetBytes("not-an-image"), "invalid.txt", "text/plain", _owner.AccessToken);
            Expect(invalid.StatusCode == HttpStatusCode.BadRequest,
                "Unsupported image type was accepted.");

            var missing = await UploadAsync(Guid.NewGuid(), png, "smoke.png", "image/png", _owner.AccessToken);
            Expect(missing.StatusCode == HttpStatusCode.NotFound,
                "Upload to a missing property was not rejected.");

            var unauthenticated = await UploadAsync(_propertyId, png, "smoke.png", "image/png", null);
            Expect(unauthenticated.StatusCode == HttpStatusCode.Unauthorized,
                "Unauthenticated image upload was accepted.");

            var otherUser = await UploadAsync(_propertyId, png, "smoke.png", "image/png", _visitor!.AccessToken);
            Expect(otherUser.StatusCode == HttpStatusCode.Forbidden,
                "A non-owner uploaded an image.");

            var images = await _client.GetAsync($"/api/properties/{_propertyId}/images");
            Expect(images.StatusCode == HttpStatusCode.OK, "Persisted images cannot be read.");
            using var imagesBody = await ReadJsonAsync(images);
            Expect(imagesBody.RootElement.EnumerateArray().Any(x =>
                    string.Equals(Text(x, "Url"), _imageUrl, StringComparison.Ordinal)),
                "Uploaded image is absent from the image endpoint.");

            var publish = await SendAsync(HttpMethod.Post,
                $"/api/properties/{_propertyId}/publish", token: _owner.AccessToken);
            Expect(publish.StatusCode == HttpStatusCode.NoContent,
                "Property could not be published after image upload.");

            var publicRead = await _client.GetAsync($"/api/properties/{_propertyId}");
            Expect(publicRead.StatusCode == HttpStatusCode.OK,
                "Published property cannot be read publicly.");
            using var publicBody = await ReadJsonAsync(publicRead);
            AssertProperty(publicBody.RootElement);
            Expect(ArrayContainsText(publicBody.RootElement, "ImageUrls", _imageUrl),
                "Public property response does not contain the uploaded image.");

            var imageResponse = await _client.GetAsync(_imageUrl);
            Expect(imageResponse.StatusCode == HttpStatusCode.OK,
                "Stored image URL is not usable.");
        }

        private async Task ListingAsync()
        {
            RequireSessions();
            var response = await _client.GetAsync(
                $"/api/properties?OwnerId={_owner!.UserId}&Page=1&PageSize=20");
            Expect(response.StatusCode == HttpStatusCode.OK, "Property listing failed.");
            using var body = await ReadJsonAsync(response);
            var items = Property(body.RootElement, "Items");
            Expect(items.ValueKind == JsonValueKind.Array, "Property list Items is not an array.");
            Expect(items.EnumerateArray().Any(x =>
                    Guid.TryParse(Text(x, "Id"), out var id) && id == _propertyId),
                "Created property is not discoverable in the public listing.");
            Expect(Integer(body.RootElement, "Page") == 1 && Integer(body.RootElement, "PageSize") == 20,
                "Pagination metadata is invalid.");
        }

        private async Task CommunicationAsync()
        {
            RequireSessions();
            var marker = $"E2E-SMOKE-MESSAGE-{_config.RunId}";
            var response = await SendJsonAsync(HttpMethod.Post, "/api/messages",
                new { PropertyId = _propertyId, ReceiverId = (Guid?)null, Content = marker },
                _visitor!.AccessToken);
            Expect(response.StatusCode == HttpStatusCode.Created,
                "Visitor-to-owner message creation failed.");
            using var body = await ReadJsonAsync(response);
            Expect(Text(body.RootElement, "Content") == marker, "Persisted message content differs.");
            Expect(Guid.Parse(Text(body.RootElement, "ReceiverId")) == _owner!.UserId,
                "Message receiver is not the property owner.");

            var ownerRead = await SendAsync(HttpMethod.Get,
                $"/api/messages?PropertyId={_propertyId}", token: _owner.AccessToken);
            Expect(ownerRead.StatusCode == HttpStatusCode.OK, "Owner cannot read property messages.");
            using var ownerBody = await ReadJsonAsync(ownerRead);
            Expect(Property(ownerBody.RootElement, "Items").EnumerateArray()
                .Any(x => Text(x, "Content") == marker), "Created message is not persisted.");

            var unauthorizedRead = await SendAsync(HttpMethod.Get,
                $"/api/messages?PropertyId={_propertyId}", token: _visitor.AccessToken);
            Expect(unauthorizedRead.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                "Visitor read another user's private property inbox.");

            var badProperty = await SendJsonAsync(HttpMethod.Post, "/api/messages",
                new { PropertyId = Guid.NewGuid(), ReceiverId = (Guid?)null, Content = marker },
                _visitor.AccessToken);
            Expect(badProperty.StatusCode == HttpStatusCode.NotFound,
                "Message with an invalid property reference was accepted.");

            var invalidContent = await SendJsonAsync(HttpMethod.Post, "/api/messages",
                new { PropertyId = _propertyId, ReceiverId = (Guid?)null, Content = "" },
                _visitor.AccessToken);
            Expect(invalidContent.StatusCode == HttpStatusCode.BadRequest,
                "Empty message content was accepted.");
        }

        private async Task LogoutAsync()
        {
            RequireSessions();
            var refresh = _owner!.RefreshToken;
            var response = await SendJsonAsync(HttpMethod.Post, "/api/auth/logout",
                new { RefreshToken = refresh }, _owner.AccessToken);
            Expect(response.StatusCode == HttpStatusCode.NoContent, "Logout failed.");

            var reuse = await SendJsonAsync(HttpMethod.Post, "/api/auth/refresh",
                new { RefreshToken = refresh });
            Expect(reuse.StatusCode == HttpStatusCode.Unauthorized,
                "Refresh token remained active after logout.");

            var statelessAccess = await SendAsync(HttpMethod.Get,
                "/api/users/me", token: _owner.AccessToken);
            Expect(statelessAccess.StatusCode == HttpStatusCode.OK,
                "Logout unexpectedly revoked a stateless access token contrary to the current model.");
        }

        private const string RefreshTokenCookieName = "refresh_token";

        private async Task<Session> RegisterPhoneUserAsync(string phone, string roleMarker)
        {
            // stagingSecret is passed on every OTP call here (not just Cleanup/build-info) so
            // the server can recognize this as mandatory smoke-suite traffic and exempt it from
            // verify-otp's/send-otp's shared per-IP rate limit -- see the doc comment on
            // "verify-otp" in RateLimitingRegistration.AddPropertyApiRateLimiting for why an
            // ordinary caller's limit alone can't accommodate this journey's required call
            // pattern (wrong-code, correct-code, one-time-use recheck) run twice per journey
            // (owner + visitor) while Staging collapses every caller to one shared IP bucket.
            var send = await SendJsonAsync(HttpMethod.Post,
                "/api/auth/phone/registration/send-otp", new { PhoneNumber = phone },
                stagingSecret: _config.CleanupSecret);
            Expect(send.StatusCode == HttpStatusCode.OK, "OTP request failed.");
            using var sendBody = await ReadJsonAsync(send);
            Expect(TryGuid(sendBody.RootElement, "ChallengeId", out var challengeId),
                "OTP response has no challenge id.");

            var wrong = await SendJsonAsync(HttpMethod.Post,
                "/api/auth/phone/registration/verify",
                new
                {
                    ChallengeId = challengeId,
                    Code = _config.FixedOtp == "000000" ? "999999" : "000000",
                    Password = _config.Password,
                    FirstName = "E2E",
                    LastName = $"{_config.RunId}-{roleMarker}"
                },
                stagingSecret: _config.CleanupSecret);
            Expect(wrong.StatusCode == HttpStatusCode.BadRequest, "Incorrect OTP was accepted.");

            var verify = await SendJsonAsync(HttpMethod.Post,
                "/api/auth/phone/registration/verify",
                new
                {
                    ChallengeId = challengeId,
                    Code = _config.FixedOtp,
                    Password = _config.Password,
                    FirstName = "E2E",
                    LastName = $"{_config.RunId}-{roleMarker}"
                },
                stagingSecret: _config.CleanupSecret);
            Expect(verify.StatusCode == HttpStatusCode.OK, "Correct OTP did not register the user.");
            using var verifyBody = await ReadJsonAsync(verify);
            var session = SessionFrom(verify, verifyBody.RootElement);

            var me = await SendAsync(HttpMethod.Get, "/api/users/me", token: session.AccessToken);
            Expect(me.StatusCode == HttpStatusCode.OK,
                "Newly registered access token cannot read the user profile.");
            using var meBody = await ReadJsonAsync(me);
            var userId = Guid.Parse(Text(meBody.RootElement, "Id"));
            session = session with { UserId = userId };
            if (!_userIds.Contains(userId)) _userIds.Add(userId);

            var reuse = await SendJsonAsync(HttpMethod.Post,
                "/api/auth/phone/registration/verify",
                new
                {
                    ChallengeId = challengeId,
                    Code = _config.FixedOtp,
                    Password = _config.Password,
                    FirstName = "E2E",
                    LastName = $"{_config.RunId}-{roleMarker}"
                },
                stagingSecret: _config.CleanupSecret);
            Expect(reuse.StatusCode == HttpStatusCode.BadRequest, "Consumed OTP was reusable.");
            return session;
        }

        private async Task<Session> LoginPhoneAsync(string phone)
        {
            var response = await SendJsonAsync(HttpMethod.Post, "/api/auth/phone/login",
                new { PhoneNumber = phone, Password = _config.Password });
            Expect(response.StatusCode == HttpStatusCode.OK, "Correct phone credentials were rejected.");
            using var body = await ReadJsonAsync(response);
            return SessionFrom(response, body.RootElement);
        }

        private Session SessionFrom(HttpResponseMessage response, JsonElement body)
        {
            var access = RequiredText(body, "AccessToken");

            // See RequiredCookie's doc comment: the refresh token is cookie-only now.
            var refresh = RequiredCookie(response, RefreshTokenCookieName);
            Expect(access.Split('.').Length == 3, "Access token is not a JWT.");
            Expect(refresh.Length >= 40, "Refresh token contract is invalid.");
            Expect(!body.EnumerateObject().Any(x =>
                    x.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                    x.Name.Contains("hash", StringComparison.OrdinalIgnoreCase)),
                "Authentication response exposes a sensitive field.");
            return new Session(Guid.Empty, access, refresh);
        }

        // Structured location and property type are mandatory for a listing (see
        // CreatePropertyCommandValidator); the ids belong to seeded reference data and are looked
        // up from the public lookup endpoints instead of being hard-coded.
        private async Task LoadListingReferenceDataAsync()
        {
            if (_governorateId > 0 && _propertyTypeId > 0) return;

            var governorates = await SendAsync(HttpMethod.Get, "/api/lookups/governorates?countryCode=SY");
            Expect(governorates.StatusCode == HttpStatusCode.OK, "Governorate lookup failed.");
            using var governorateBody = await ReadJsonAsync(governorates);
            var governorate = governorateBody.RootElement.EnumerateArray()
                .FirstOrDefault(item => item.TryGetProperty("nameEn", out var name) && name.GetString() == "Damascus");
            Expect(governorate.ValueKind == JsonValueKind.Object, "Seeded governorate 'Damascus' was not found.");
            _governorateId = Integer(governorate, "id");

            var types = await SendAsync(HttpMethod.Get, "/api/lookups/property-type-catalog");
            Expect(types.StatusCode == HttpStatusCode.OK, "Property type catalog lookup failed.");
            using var typeBody = await ReadJsonAsync(types);
            var apartment = typeBody.RootElement.EnumerateArray()
                .FirstOrDefault(item => item.TryGetProperty("code", out var code) && code.GetString() == "apartment");
            Expect(apartment.ValueKind == JsonValueKind.Object, "Seeded property type 'apartment' was not found.");
            _propertyTypeId = Integer(apartment, "id");
        }

        private object PropertyPayload(string title) => new
        {
            OwnerId = Guid.Empty,
            Title = title,
            Description = $"Staging smoke property {_config.RunId}",
            ListingType = "ForRent",
            Street = "Smoke Test Street 1",
            City = "Damascus",
            Region = "Damascus",
            CountryCode = "SY",
            PostalCode = "10115",
            GovernorateId = _governorateId,
            DistrictText = "Smoke Test District",
            PropertyTypeId = _propertyTypeId,
            Latitude = 33.5138m,
            Longitude = 36.2765m,
            ColdRent = 1250.50m,
            WarmRent = 1450.50m,
            PurchasePrice = (decimal?)null,
            Deposit = 2500m,
            AdditionalCosts = 200m,
            CurrencyCode = "EUR",
            Rooms = 3,
            Area = 82.5m,
            Floor = 2,
            HasBalcony = true,
            HasElevator = true,
            HasParkingSpace = false,
            HeatingType = "Gas",
            AvailableFrom = DateTime.UtcNow.AddDays(14),
            RentalStartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            RentalEndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14 + 365)),
            RentalDurationType = "OneYear",
            AmenityIds = Array.Empty<Guid>()
        };

        private void AssertProperty(JsonElement body)
        {
            Expect(Guid.Parse(Text(body, "Id")) == _propertyId, "Read property id differs.");
            Expect(Text(body, "Title") == _config.PropertyTitle, "Read property title differs.");
            Expect(Text(body, "ListingType") == "ForRent", "Listing type differs.");
            Expect(decimal.Parse(Text(body, "ColdRent"), System.Globalization.CultureInfo.InvariantCulture)
                == 1250.50m, "Cold rent differs.");
            Expect(Text(body, "City") == "Damascus", "City differs.");
            Expect(Integer(body, "Rooms") == 3, "Room count differs.");
            Expect(DateTime.TryParse(Text(body, "CreatedAt"), out _), "Creation timestamp is invalid.");
        }

        private async Task<HttpResponseMessage> UploadAsync(
            Guid propertyId, byte[] bytes, string fileName, string contentType, string? token)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(file, "files", fileName);
            return await SendAsync(HttpMethod.Post,
                $"/api/properties/{propertyId}/images", form, token);
        }

        private async Task<HttpResponseMessage> SendJsonAsync(
            HttpMethod method, string path, object payload, string? token = null,
            string? stagingSecret = null)
        {
            var content = JsonContent.Create(payload, options: JsonOptions);
            return await SendAsync(method, path, content, token, stagingSecret);
        }

        private static readonly HashSet<string> CsrfSafeMethods = new(StringComparer.OrdinalIgnoreCase)
        {
            HttpMethod.Get.Method, HttpMethod.Head.Method, HttpMethod.Options.Method, HttpMethod.Trace.Method
        };

        private async Task<HttpResponseMessage> SendAsync(
            HttpMethod method, string path, HttpContent? content = null, string? token = null,
            string? stagingSecret = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = content };
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrWhiteSpace(stagingSecret))
                request.Headers.TryAddWithoutValidation("X-Staging-Smoke-Secret", stagingSecret);
            if (!string.IsNullOrWhiteSpace(_csrfToken) && !CsrfSafeMethods.Contains(method.Method))
                request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", _csrfToken);
            return await _client.SendAsync(request);
        }

        private async Task FetchCsrfTokenAsync()
        {
            var response = await _client.GetAsync("/api/security/csrf-token");
            Expect(response.StatusCode == HttpStatusCode.OK, "Could not obtain a CSRF token.");
            using var body = await ReadJsonAsync(response);
            _csrfToken = Text(body.RootElement, "csrfToken");
            Expect(!string.IsNullOrWhiteSpace(_csrfToken), "CSRF token endpoint returned an empty token.");
        }

        private async Task ExpectStatusWithTransientRetry(string path, HttpStatusCode expected)
        {
            for (var attempt = 1; attempt <= _config.MaxRetries; attempt++)
            {
                try
                {
                    var response = await _client.GetAsync(path);
                    if (response.StatusCode == expected) return;
                    if (response.StatusCode is not (HttpStatusCode.BadGateway or
                        HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout))
                        throw new InvalidOperationException(
                            $"{path} returned HTTP {(int)response.StatusCode}.");
                }
                catch (HttpRequestException) when (attempt < _config.MaxRetries)
                {
                }

                if (attempt < _config.MaxRetries)
                    await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds));
            }

            throw new InvalidOperationException($"{path} did not become healthy within the retry budget.");
        }

        private async Task Journey(string name, Func<Task> action)
        {
            var result = _journeys[name];
            var stopwatch = Stopwatch.StartNew();
            result.Status = "running";
            try
            {
                var before = result.Assertions;
                await action();
                result.Status = "passed";
                if (result.Assertions == before) result.Assertions++;
            }
            catch (Exception ex)
            {
                result.Status = "failed";
                result.Error = ex.Message;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                result.DurationMilliseconds = stopwatch.ElapsedMilliseconds;
            }
        }

        private void Expect(bool condition, string message)
        {
            var active = _journeys.Values.FirstOrDefault(x => x.Status == "running");
            if (active is not null) active.Assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private void Pass(string journey, int assertions)
        {
            var result = _journeys[journey];
            result.Status = "passed";
            result.Assertions += assertions;
        }

        private void RequireSessions()
        {
            Expect(_owner is not null && _visitor is not null, "Required authenticated sessions are missing.");
        }

        public async Task WriteReportsAsync(Exception? failure)
        {
            Directory.CreateDirectory(_config.ArtifactsDirectory);
            var completed = DateTime.UtcNow;
            var skipped = _journeys.Values.Any(x => x.Status is "skipped" or "running");
            var passed = failure is null && _cleanupStatus == "passed" && !skipped &&
                _journeys.Values.All(x => x.Status == "passed");

            var report = new
            {
                runId = _config.RunId,
                startedAtUtc = _startedAtUtc,
                completedAtUtc = completed,
                baseUrlSanitized = _config.BaseUrl.GetLeftPart(UriPartial.Authority),
                expectedCommitSha = _config.ExpectedCommitSha,
                deployedCommitSha = _deployedCommitSha,
                environment = "Staging",
                journeys = _journeys.Values,
                cleanup = new { status = _cleanupStatus, message = _cleanupMessage },
                totalDurationSeconds = Math.Round((completed - _startedAtUtc).TotalSeconds, 3),
                result = passed ? "passed" : "failed"
            };

            var jsonPath = Path.Combine(_config.ArtifactsDirectory, "staging-smoke-report.json");
            await File.WriteAllTextAsync(jsonPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

            var summary = new StringBuilder()
                .AppendLine("# Staging E2E smoke report")
                .AppendLine()
                .AppendLine($"- Run: `{_config.RunId}`")
                .AppendLine($"- Expected commit: `{_config.ExpectedCommitSha}`")
                .AppendLine($"- Deployed commit: `{_deployedCommitSha}`")
                .AppendLine($"- Result: **{(passed ? "Passed" : "Failed")}**")
                .AppendLine()
                .AppendLine("| Journey | Status | Assertions | Duration (ms) |")
                .AppendLine("|---|---:|---:|---:|");
            foreach (var journey in _journeys.Values)
                summary.AppendLine($"| {journey.Name} | {journey.Status} | {journey.Assertions} | {journey.DurationMilliseconds} |");
            if (failure is not null)
                summary.AppendLine().AppendLine($"Failure: `{Sanitize(failure.Message)}`");
            await File.WriteAllTextAsync(
                Path.Combine(_config.ArtifactsDirectory, "staging-smoke-summary.md"), summary.ToString());

            var suite = new XElement("testsuite",
                new XAttribute("name", "PropertyApi.StagingSmoke"),
                new XAttribute("tests", _journeys.Count),
                new XAttribute("failures", _journeys.Values.Count(x => x.Status == "failed")),
                new XAttribute("skipped", _journeys.Values.Count(x => x.Status == "skipped")),
                new XAttribute("time", (completed - _startedAtUtc).TotalSeconds.ToString("0.000",
                    System.Globalization.CultureInfo.InvariantCulture)));
            foreach (var journey in _journeys.Values)
            {
                var test = new XElement("testcase",
                    new XAttribute("name", journey.Name),
                    new XAttribute("time", (journey.DurationMilliseconds / 1000d).ToString("0.000",
                        System.Globalization.CultureInfo.InvariantCulture)));
                if (journey.Status == "failed") test.Add(new XElement("failure", Sanitize(journey.Error ?? "failed")));
                if (journey.Status == "skipped") test.Add(new XElement("skipped"));
                suite.Add(test);
            }
            await File.WriteAllTextAsync(
                Path.Combine(_config.ArtifactsDirectory, "staging-smoke-junit.xml"),
                new XDocument(new XElement("testsuites", suite)).ToString());
        }

        // Non-secret diagnostics for a failed call: the API's machine-readable code and title
        // (e.g. " (LISTING_PLAN_REQUIRED: Forbidden)"), so a red journey names its cause.
        private static async Task<string> ApiErrorAsync(HttpResponseMessage response)
        {
            try
            {
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var parts = new[] { "code", "title" }
                    .Select(name => body.RootElement.TryGetProperty(name, out var value) &&
                                    value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                    .Where(value => !string.IsNullOrWhiteSpace(value));
                var text = string.Join(": ", parts);
                return text.Length == 0 ? string.Empty : $" ({text})";
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            try { return JsonDocument.Parse(text); }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"HTTP {(int)response.StatusCode} response is not valid JSON.", ex);
            }
        }

        private static JsonElement Property(JsonElement element, string name) =>
            element.EnumerateObject().First(x =>
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

        private static string Text(JsonElement element, string name)
        {
            var value = Property(element, name);
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
        }

        private static string RequiredText(JsonElement element, string name)
        {
            var value = Text(element, name);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Required response property '{name}' is missing.");
            return value;
        }

        // RELEASE-BLOCKERS-AR.md B-13: the refresh token no longer travels in the JSON body
        // at all — every issuing endpoint (login, phone login/registration, refresh) now sets
        // it only via the refresh_token cookie. This client's default HttpClientHandler
        // already tracks cookies automatically (UseCookies defaults to true), which is why
        // the rest of this file needs no other change: RefreshAsync/LogoutAsync still send an
        // explicit RefreshToken in the body for the negative-path assertions
        // (stale/invalid/logged-out token rejection), and RefreshTokenCookie.Read on the
        // server prefers that explicit value over the ambient cookie, so those assertions
        // keep testing exactly the token value each one intends to.
        private static string RequiredCookie(HttpResponseMessage response, string cookieName)
        {
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    var prefix = cookieName + "=";
                    if (!cookie.StartsWith(prefix, StringComparison.Ordinal))
                        continue;

                    var end = cookie.IndexOf(';');
                    var raw = end >= 0 ? cookie[prefix.Length..end] : cookie[prefix.Length..];
                    var value = Uri.UnescapeDataString(raw);

                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            throw new InvalidOperationException(
                $"Response did not set the '{cookieName}' cookie.");
        }

        private static bool Boolean(JsonElement element, string name) => Property(element, name).GetBoolean();
        private static int Integer(JsonElement element, string name) => Property(element, name).GetInt32();
        private static bool TryGuid(JsonElement element, string name, out Guid value) =>
            Guid.TryParse(Text(element, name), out value);

        private static bool ArrayContainsText(JsonElement element, string name, string expected) =>
            Property(element, name).EnumerateArray().Any(x =>
                string.Equals(x.GetString(), expected, StringComparison.Ordinal));

        private static string Sanitize(string value) =>
            value.Replace('\r', ' ').Replace('\n', ' ').Replace("`", "'");
    }

    private sealed class SmokeConfiguration
    {
        public required Uri BaseUrl { get; init; }
        public required string ExpectedCommitSha { get; init; }
        public required string Password { get; init; }
        public required string FixedOtp { get; init; }
        public required string CleanupSecret { get; init; }
        public required string RunId { get; init; }
        public required string OwnerPhone { get; init; }
        public required string VisitorPhone { get; init; }
        public required string PropertyTitle { get; init; }
        public required string ArtifactsDirectory { get; init; }
        public required int HttpTimeoutSeconds { get; init; }
        public required int MaxRetries { get; init; }
        public required int RetryDelaySeconds { get; init; }
        public required bool AllowInMemoryMedia { get; init; }

        public static SmokeConfiguration Load()
        {
            var baseUrlText = Required("STAGING_BASE_URL").TrimEnd('/');
            var productionUrlText = Required("PRODUCTION_BASE_URL").TrimEnd('/');
            if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl) ||
                baseUrl.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("STAGING_BASE_URL must be an absolute HTTPS URL.");
            if (!Uri.TryCreate(productionUrlText, UriKind.Absolute, out var productionUrl))
                throw new InvalidOperationException("PRODUCTION_BASE_URL must be an absolute URL.");
            if (string.Equals(baseUrl.Host, productionUrl.Host, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to run smoke tests against Production.");

            var expectedSha = Required("EXPECTED_COMMIT_SHA");
            var shortSha = expectedSha[..Math.Min(7, expectedSha.Length)];
            var generatedRunId = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{shortSha}-{Guid.NewGuid():N}";
            var runId = Environment.GetEnvironmentVariable("SMOKE_RUN_ID")
                ?? generatedRunId[..Math.Min(48, generatedRunId.Length)];
            var prefix = Required("STAGING_SMOKE_PHONE_PREFIX");
            var suffixSeed = Math.Abs(runId.GetHashCode(StringComparison.Ordinal));
            var ownerSuffix = (suffixSeed % 1_000_000).ToString("D6");
            var visitorSuffix = ((suffixSeed + 1) % 1_000_000).ToString("D6");

            return new SmokeConfiguration
            {
                BaseUrl = baseUrl,
                ExpectedCommitSha = expectedSha,
                Password = Required("STAGING_SMOKE_PASSWORD"),
                FixedOtp = Required("STAGING_SMOKE_FIXED_OTP"),
                CleanupSecret = Required("STAGING_SMOKE_CLEANUP_SECRET"),
                RunId = runId,
                OwnerPhone = prefix + ownerSuffix,
                VisitorPhone = prefix + visitorSuffix,
                PropertyTitle = $"E2E-SMOKE-{runId}",
                ArtifactsDirectory = Environment.GetEnvironmentVariable("SMOKE_ARTIFACTS_DIR")
                    ?? Path.GetFullPath("artifacts/staging-smoke"),
                HttpTimeoutSeconds = IntegerEnvironment("SMOKE_HTTP_TIMEOUT_SECONDS", 20),
                MaxRetries = IntegerEnvironment("SMOKE_MAX_RETRIES", 5),
                RetryDelaySeconds = IntegerEnvironment("SMOKE_RETRY_DELAY_SECONDS", 3),
                AllowInMemoryMedia = string.Equals(
                    Environment.GetEnvironmentVariable("SMOKE_ALLOW_IN_MEMORY_MEDIA"), "true",
                    StringComparison.OrdinalIgnoreCase)
            };
        }

        private static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"{name} is required.");

        private static int IntegerEnvironment(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0
                ? value
                : fallback;
    }

    private sealed class JourneyResult(string name)
    {
        public string Name { get; } = name;
        public string Status { get; set; } = "skipped";
        public long DurationMilliseconds { get; set; }
        public int Assertions { get; set; }
        public string? Error { get; set; }
        public IReadOnlyList<string> ResourceIdsSanitized { get; } = [];
    }

    private sealed record Session(Guid UserId, string AccessToken, string RefreshToken);
}
