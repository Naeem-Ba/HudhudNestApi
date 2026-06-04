using System.Reflection;
using Xunit;

namespace PropertyApi.Architecture.Tests;

public sealed class DependencyTests
{
    private static readonly Assembly DomainAssembly =
        typeof(PropertyApi.Domain.Common.Entities.BaseEntity).Assembly;

    private static readonly Assembly ApplicationAssembly =
        typeof(PropertyApi.Application.DependencyInjection).Assembly;

    private static readonly Assembly InfrastructureAssembly =
        typeof(PropertyApi.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly ApiAssembly =
        typeof(Program).Assembly;

    [Fact]
    public void Domain_Should_Not_Reference_Application_Infrastructure_Or_Api()
    {
        var referencedAssemblies = GetReferencedAssemblyNames(DomainAssembly);

        Assert.DoesNotContain("PropertyApi.Application", referencedAssemblies);
        Assert.DoesNotContain("PropertyApi.Infrastructure", referencedAssemblies);
        Assert.DoesNotContain("PropertyApi", referencedAssemblies);
    }

    [Fact]
    public void Application_Should_Not_Reference_Infrastructure_Or_Api()
    {
        var referencedAssemblies = GetReferencedAssemblyNames(ApplicationAssembly);

        Assert.DoesNotContain("PropertyApi.Infrastructure", referencedAssemblies);
        Assert.DoesNotContain("PropertyApi", referencedAssemblies);
    }

    [Fact]
    public void Infrastructure_Should_Not_Reference_Api()
    {
        var referencedAssemblies = GetReferencedAssemblyNames(InfrastructureAssembly);

        Assert.DoesNotContain("PropertyApi", referencedAssemblies);
    }

    [Fact]
    public void Api_Should_Reference_Application_And_Infrastructure()
    {
        var referencedAssemblies = GetReferencedAssemblyNames(ApiAssembly);

        Assert.Contains("PropertyApi.Application", referencedAssemblies);
        Assert.Contains("PropertyApi.Infrastructure", referencedAssemblies);
    }

    private static IReadOnlyCollection<string> GetReferencedAssemblyNames(Assembly assembly)
    {
        return assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToArray();
    }
}