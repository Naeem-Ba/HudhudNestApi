using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;
using HudhudNestApi.Configuration;

namespace HudhudNestApi.Architecture.Tests.Api;

/// <summary>
/// /swagger/v1/swagger.json is one document: a single action Swashbuckle cannot describe makes the
/// whole file answer 500. That is how <c>AdminInvestmentsController.AddDocument</c>
/// ([FromForm] IFormFile) broke Swagger for every endpoint. Generating the document here, with the
/// real registration and every controller, fails the build on the first such action.
/// </summary>
public sealed class SwaggerDocumentTests
{
    [Fact(DisplayName = "The v1 OpenAPI document can be generated for every controller action")]
    public void SwaggerDocument_Should_Generate_For_All_Controllers()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Services.AddControllers()
            .PartManager.ApplicationParts.Add(new AssemblyPart(typeof(Program).Assembly));
        builder.Services.AddHudhudNestApiSwagger();

        // Built, never started: no port is opened and no controller is constructed.
        using var app = builder.Build();

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.NotEmpty(document.Paths);
    }
}
