using System.Reflection;
using System.Text;
using Xunit;

namespace HudhudNestApi.Architecture.Tests;

public sealed class ApplicationDependencyGuardTests
{
    private const string ApplicationAssemblyName = "HudhudNestApi.Application";

    private static readonly string[] ForbiddenAssemblyNames =
    [
        "HudhudNestApi.Infrastructure",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore.Mvc",
        "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Identity",
        "Microsoft.AspNetCore.Identity.UI"
    ];

    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "HudhudNestApi.Infrastructure",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore.Mvc",
        "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Identity",
        "Microsoft.AspNetCore.Identity.UI"
    ];

    private static readonly string[] ForbiddenTypeNames =
    [
        "AppDbContext",
        "DbContext",
        "DbSet",
        "ControllerBase",
        "IHttpContextAccessor",
        "UserManager`1",
        "RoleManager`1"
    ];

    private static readonly string[] ForbiddenSourceTokens =
    [
        "using HudhudNestApi.Infrastructure",
        "HudhudNestApi.Infrastructure",
        "using Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore",
        "AppDbContext",
        "DbContext",
        "DbSet",
        "using Microsoft.AspNetCore.Identity",
        "Microsoft.AspNetCore.Identity",
        "UserManager<",
        "RoleManager<",
        "using Microsoft.AspNetCore.Identity.UI.Services",
        "Microsoft.AspNetCore.Identity.UI.Services",
        "using Microsoft.AspNetCore.Mvc",
        "Microsoft.AspNetCore.Mvc",
        "using Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Http",
        "ControllerBase",
        "IHttpContextAccessor",
        "UserManager`1",
        "RoleManager`1"
    ];

    [Fact(DisplayName = "Application must not depend on Infrastructure, EF Core, DbContext, or ASP.NET UI/Web services")]
    public void ArchitectureTests_ApplicationShouldNotDependOnInfrastructure_AndAspNetCoreServices()
    {
        var applicationAssembly = Assembly.Load(ApplicationAssemblyName);
        var failures = new List<string>();

        AssertForbiddenAssemblyReferences(applicationAssembly, failures);
        AssertForbiddenTypeReferences(applicationAssembly, failures);
        AssertForbiddenSourceTokens(failures);

        Assert.True(
            failures.Count == 0,
            BuildFailureMessage(failures));
    }

    private static void AssertForbiddenAssemblyReferences(
        Assembly applicationAssembly,
        ICollection<string> failures)
    {
        foreach (var reference in applicationAssembly.GetReferencedAssemblies())
        {
            if (ForbiddenAssemblyNames.Any(forbidden =>
                    reference.Name is not null &&
                    reference.Name.Equals(forbidden, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add(
                    $"Assembly reference violation: {ApplicationAssemblyName} references forbidden assembly '{reference.Name}'.");
            }
        }
    }

    private static void AssertForbiddenTypeReferences(
        Assembly applicationAssembly,
        ICollection<string> failures)
    {
        foreach (var type in SafeGetTypes(applicationAssembly))
        {
            InspectType(type, failures);
        }
    }

    private static void InspectType(Type type, ICollection<string> failures)
    {
        CheckTypeReference(type, type, "type declaration", failures);
        CheckTypeReference(type, type.BaseType, "base type", failures);

        foreach (var interfaceType in type.GetInterfaces())
            CheckTypeReference(type, interfaceType, "implemented interface", failures);

        foreach (var attribute in type.GetCustomAttributesData())
            CheckTypeReference(type, attribute.AttributeType, "attribute", failures);

        const BindingFlags flags = BindingFlags.Instance |
                                   BindingFlags.Static |
                                   BindingFlags.Public |
                                   BindingFlags.NonPublic |
                                   BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(flags))
            CheckTypeReference(type, field.FieldType, $"field '{field.Name}'", failures);

        foreach (var property in type.GetProperties(flags))
            CheckTypeReference(type, property.PropertyType, $"property '{property.Name}'", failures);

        foreach (var constructor in type.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
                CheckTypeReference(type, parameter.ParameterType, $"constructor parameter '{parameter.Name}'", failures);
        }

        foreach (var method in type.GetMethods(flags))
        {
            CheckTypeReference(type, method.ReturnType, $"method '{method.Name}' return type", failures);

            foreach (var parameter in method.GetParameters())
                CheckTypeReference(type, parameter.ParameterType, $"method '{method.Name}' parameter '{parameter.Name}'", failures);

            foreach (var genericArgument in method.GetGenericArguments())
                CheckTypeReference(type, genericArgument, $"method '{method.Name}' generic argument", failures);
        }
    }

    private static void CheckTypeReference(
        Type inspectedApplicationType,
        Type? referencedType,
        string usage,
        ICollection<string> failures)
    {
        if (referencedType is null)
            return;

        foreach (var candidate in FlattenType(referencedType))
        {
            var candidateNamespace = candidate.Namespace ?? string.Empty;
            var candidateName = candidate.Name;
            var candidateFullName = candidate.FullName ?? candidateName;

            if (ForbiddenNamespacePrefixes.Any(prefix =>
                    candidateNamespace.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add(
                    $"Type reference violation: {inspectedApplicationType.FullName} uses forbidden namespace '{candidateNamespace}' via {usage}: {candidateFullName}.");
            }

            if (ForbiddenTypeNames.Any(forbidden =>
                    candidateName.Equals(forbidden, StringComparison.OrdinalIgnoreCase) ||
                    candidateFullName.EndsWith($".{forbidden}", StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add(
                    $"Type reference violation: {inspectedApplicationType.FullName} uses forbidden type '{candidateFullName}' via {usage}.");
            }
        }
    }

    private static IEnumerable<Type> FlattenType(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var nested in FlattenType(elementType))
                yield return nested;
        }

        if (type.IsGenericType)
        {
            foreach (var genericArgument in type.GetGenericArguments())
            {
                foreach (var nested in FlattenType(genericArgument))
                    yield return nested;
            }
        }
    }

    private static void AssertForbiddenSourceTokens(ICollection<string> failures)
    {
        var applicationProjectDirectory = FindApplicationProjectDirectory();

        if (applicationProjectDirectory is null)
        {
            failures.Add(
                "Source scan could not locate HudhudNestApi.Application project directory. " +
                "Reflection checks still ran, but line-number checks could not run.");

            return;
        }

        var sourceFiles = Directory.EnumerateFiles(
            applicationProjectDirectory,
            "*.cs",
            SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        foreach (var file in sourceFiles)
        {
            var lines = File.ReadAllLines(file);

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var trimmed = line.TrimStart();

                // Avoid false positives from normal comments/XML docs.
                if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("///", StringComparison.Ordinal) ||
                    trimmed.StartsWith("/*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("*", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var token in ForbiddenSourceTokens)
                {
                    if (line.Contains(token, StringComparison.Ordinal))
                    {
                        failures.Add(
                            $"Source dependency violation: {Path.GetRelativePath(applicationProjectDirectory, file)}:{index + 1} contains forbidden token '{token}'.");
                    }
                }
            }
        }
    }

    private static string? FindApplicationProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "HudhudNestApi.Application",
                "HudhudNestApi.Application.csproj");

            if (File.Exists(candidate))
                return Path.GetDirectoryName(candidate);

            current = current.Parent;
        }

        return null;
    }

    private static IReadOnlyList<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }

    private static string BuildFailureMessage(IReadOnlyCollection<string> failures)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Application layer dependency rules were violated.");
        builder.AppendLine();
        builder.AppendLine("Forbidden dependencies:");
        builder.AppendLine("- HudhudNestApi.Infrastructure");
        builder.AppendLine("- Microsoft.EntityFrameworkCore");
        builder.AppendLine("- AppDbContext / DbContext / DbSet");
        builder.AppendLine("- Microsoft.AspNetCore.Identity / UserManager / RoleManager");
        builder.AppendLine("- Microsoft.AspNetCore.Identity.UI.Services");
        builder.AppendLine("- Microsoft.AspNetCore.Mvc / ControllerBase");
        builder.AppendLine("- Microsoft.AspNetCore.Http / IHttpContextAccessor");
        builder.AppendLine();
        builder.AppendLine("Violations:");

        foreach (var failure in failures.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            builder.AppendLine("- " + failure);

        return builder.ToString();
    }
}
