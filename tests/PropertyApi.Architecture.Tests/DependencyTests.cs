using Xunit;

namespace PropertyApi.Architecture.Tests;

public sealed class DependencyTests
{
    [Fact]
    public void Domain_Should_Not_Reference_Application_Infrastructure_Or_Api()
    {
        var domainAssembly = typeof(PropertyApi.Domain.Common.Entities.BaseEntity).Assembly;

        var referencedAssemblies = domainAssembly
            .GetReferencedAssemblies()
            .Select(x => x.Name)
            .ToList();

        Assert.DoesNotContain("PropertyApi.Application", referencedAssemblies);
        Assert.DoesNotContain("PropertyApi.Infrastructure", referencedAssemblies);
        Assert.DoesNotContain("PropertyApi", referencedAssemblies);
    }

    [Fact]
    public void Application_Should_Not_Reference_Infrastructure_Or_Api()
    {
        var applicationAssembly = typeof(PropertyApi.Application.DependencyInjection).Assembly;

        var referencedAssemblies = applicationAssembly
            .GetReferencedAssemblies()
            .Select(x => x.Name)
            .ToList();

        Assert.DoesNotContain("PropertyApi.Infrastructure", referencedAssemblies);
        Assert.DoesNotContain("PropertyApi", referencedAssemblies);
    }
}