using System.Reflection;
using Xunit;

namespace HudhudNestApi.Architecture.Tests;

public sealed class DependencyTests
{
    private static readonly Assembly DomainAssembly =
        typeof(HudhudNestApi.Domain.Common.Entities.BaseEntity).Assembly;

    private static readonly Assembly ApplicationAssembly =
        typeof(HudhudNestApi.Application.DependencyInjection).Assembly;

    private static readonly Assembly InfrastructureAssembly =
        typeof(HudhudNestApi.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly ApiAssembly =
        typeof(Program).Assembly;

    [Fact]
    public void Domain_Should_Not_Reference_Application_Infrastructure_Or_Api()
    {
        var referencedAssemblies = GetReferencedAssemblyNames(DomainAssembly);

        Assert.DoesNotContain("HudhudNestApi.Application", referencedAssemblies);
        Assert.DoesNotContain("HudhudNestApi.Infrastructure", referencedAssemblies);
        Assert.DoesNotContain("HudhudNestApi", referencedAssemblies);
    }

    [Fact]
    public void Application_Should_Not_Reference_Infrastructure_Or_Api()
    {
        var referencedAssemblies = GetReferencedAssemblyNames(ApplicationAssembly);

        Assert.DoesNotContain("HudhudNestApi.Infrastructure", referencedAssemblies);
        Assert.DoesNotContain("HudhudNestApi", referencedAssemblies);
    }

    [Fact]
    public void Infrastructure_Should_Not_Reference_Api()
    {
        var referencedAssemblies = GetReferencedAssemblyNames(InfrastructureAssembly);

        Assert.DoesNotContain("HudhudNestApi", referencedAssemblies);
    }

    [Fact]
    public void Api_Should_Reference_Application_And_Infrastructure()
    {
        var referencedAssemblies = GetReferencedAssemblyNames(ApiAssembly);

        Assert.Contains("HudhudNestApi.Application", referencedAssemblies);
        Assert.Contains("HudhudNestApi.Infrastructure", referencedAssemblies);
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