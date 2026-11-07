using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using HudhudNestApi.Application.Auth.Commands.Login;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Controllers;

namespace HudhudNestApi.Integration.Tests.Controllers;

/// <summary>
/// QA phase 19, P19-D07: while the database is down, e-mail sign-in answered the generic 500 "contact support"
/// because the controller's own catch-all swallowed the connection failure before the exception middleware
/// (which answers 503 + Retry-After) could see it. An outage must reach the middleware; a bug stays a 500.
/// </summary>
public sealed class LoginDatabaseOutageTests
{
    private static AuthController CreateController(Exception toThrow)
    {
        var sender = new Mock<ISender>();
        sender
            .Setup(s => s.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(toThrow);

        return new AuthController(sender.Object, Mock.Of<IJwtTokenSettings>(), NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact(DisplayName = "A database outage during e-mail sign-in is not turned into a 500 by the controller")]
    [Trait("Category", "Integration")]
    public async Task Outage_IsRethrownForTheMiddleware()
    {
        var outage = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.",
            new NpgsqlException("Failed to connect", new System.Net.Sockets.SocketException(111)));
        var controller = CreateController(outage);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.Login(new LoginRequest("a@b.co", "Xx123456!x"), CancellationToken.None));

        Assert.Same(outage, thrown);
    }

    [Fact(DisplayName = "Any other failure during e-mail sign-in still answers the generic 500")]
    [Trait("Category", "Integration")]
    public async Task OtherFailure_Stays500()
    {
        var controller = CreateController(new InvalidOperationException("a bug"));

        var result = await controller.Login(new LoginRequest("a@b.co", "Xx123456!x"), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }
}
