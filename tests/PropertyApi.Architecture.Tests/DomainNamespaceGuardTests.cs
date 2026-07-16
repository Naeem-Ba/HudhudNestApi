using System.Reflection;
using PropertyApi.Domain.Common.Entities;
using Xunit;

namespace PropertyApi.Architecture.Tests;

public sealed class DomainNamespaceGuardTests
{
    [Fact]
    public void Domain_types_must_use_domain_namespace()
    {
        var domainAssembly = typeof(BaseEntity).Assembly;

        var invalidTypes = GetLoadableTypes(domainAssembly)
            .Where(type => !IsCoverageInstrumentationType(type))
            .Where(type =>
            {
                var namespaceName = type.Namespace;

                return !string.IsNullOrWhiteSpace(namespaceName) &&
                       !namespaceName.StartsWith(
                           "PropertyApi.Domain",
                           StringComparison.Ordinal);
            })
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            invalidTypes.Length == 0,
            "Types compiled into PropertyApi.Domain must use the " +
            "PropertyApi.Domain namespace. Invalid types:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, invalidTypes));
    }

    [Fact]
    public void Domain_must_not_reference_outer_layers_or_web_frameworks()
    {
        var domainAssembly = typeof(BaseEntity).Assembly;

        var forbiddenPrefixes = new[]
        {
            "PropertyApi.Application",
            "PropertyApi.Infrastructure",
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "MediatR"
        };

        var invalidReferences = domainAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => forbiddenPrefixes.Any(prefix =>
                name.StartsWith(
                    prefix,
                    StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            invalidReferences.Length == 0,
            "PropertyApi.Domain contains forbidden assembly references:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, invalidReferences));
    }

    private static IEnumerable<Type> GetLoadableTypes(
        Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private static bool IsCoverageInstrumentationType(Type type)
    {
        return type.FullName?.StartsWith(
            "Coverlet.Core.Instrumentation.Tracker.",
            StringComparison.Ordinal) == true;
    }
}
