using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Security;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Configuration;

public static class JwtAuthenticationRegistration
{
    public static IServiceCollection AddHudhudNestApiJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var jwtKey = jwtSection["Key"]
            ?? throw new InvalidOperationException(
                "Jwt:Key is missing. Set it via User Secrets in Development or as an environment variable in Production.");

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.SaveToken = true;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSection["Issuer"],
                    ValidAudience = jwtSection["Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var userIdText = context.Principal?
                            .FindFirstValue(ClaimTypes.NameIdentifier);

                        var tokenSecurityStamp = context.Principal?
                            .FindFirstValue(CustomClaimTypes.SecurityStamp);

                        if (!Guid.TryParse(userIdText, out var userId) ||
                            string.IsNullOrWhiteSpace(tokenSecurityStamp))
                        {
                            context.Fail("The token does not contain valid user data.");
                            return;
                        }

                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("JwtSecurityStampValidation");

                        try
                        {
                            var securityStampValidator = context.HttpContext.RequestServices
                                .GetRequiredService<IUserSecurityStampValidator>();

                            var validationResult = await securityStampValidator.ValidateAsync(
                                userId,
                                tokenSecurityStamp,
                                context.HttpContext.RequestAborted);

                            if (!validationResult.IsValid)
                            {
                                context.Fail(validationResult.FailureMessage ?? "The token is no longer valid.");
                            }
                        }
                        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            // Do not let authentication infrastructure convert Redis/cache failures into HTTP 500.
                            // The validator itself should fall back to DB; this is the final safety net.
                            logger.LogError(ex, "Security stamp validation failed during JWT authentication.");
                            context.Fail("Token validation failed.");
                        }
                    },

                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("JwtAuthentication");

                        logger.LogWarning(context.Exception, "JWT authentication failed.");
                        return Task.CompletedTask;
                    },

                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        if (!string.IsNullOrWhiteSpace(accessToken) &&
                            path.StartsWithSegments("/notificationHub"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            // SECURITY FIX: default-deny.
            //
            // Without a fallback policy, ASP.NET Core leaves an endpoint that carries no
            // [Authorize] and no [AllowAnonymous] open to anonymous callers. Being public was
            // therefore the result of forgetting an attribute, not of deciding anything --
            // one missed attribute in a future review would silently publish an endpoint, and
            // nothing in the build or the test suite would notice.
            //
            // With this policy the default is reversed: an unannotated endpoint returns 401,
            // and every public endpoint has to say [AllowAnonymous] out loud. All 107 existing
            // endpoints were audited before this was switched on; the 13 that were public by
            // omission now carry the attribute explicitly, and PublicEndpointPolicyTests fails
            // the build if a new endpoint is added without an explicit decision either way.
            //
            // Note this only governs endpoint routing. Health checks call .AllowAnonymous()
            // themselves, and the Swagger middleware runs before UseAuthorization, so neither
            // is affected.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(RoleNames.Agent, policy =>
                policy.RequireRole(RoleNames.Agent));
        });

        return services;
    }
}
