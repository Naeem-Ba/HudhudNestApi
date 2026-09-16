using System.Reflection;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Architecture.Tests;

public sealed class RepositoryWorkflowBoundaryTests
{
    private static readonly Assembly InfrastructureAssembly =
        typeof(AppDbContext).Assembly;

    private static readonly string[] ForbiddenDependencyTypeNames =
    [
        "IIdentityCapabilityAdapter",
        "ITokenService",
        "IOtpService",
        "IApplicationEmailSender",
        "IEmailVerificationService",
        "ISender",
        "IMediator",
        "UserManager`1",
        "SignInManager`1"
    ];

    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "MediatR",
        "Microsoft.AspNetCore.Identity"
    ];

    [Fact]
    public void Infrastructure_repositories_must_not_orchestrate_application_workflows()
    {
        var failures =
            GetLoadableTypes(InfrastructureAssembly)
                .Where(type =>
                    type is { IsClass: true, IsAbstract: false } &&
                    type.Name.EndsWith(
                        "Repository",
                        StringComparison.Ordinal))
                .SelectMany(InspectRepositoryDependencies)
                .OrderBy(failure => failure, StringComparer.Ordinal)
                .ToArray();

        Assert.True(
            failures.Length == 0,
            "Repositories must remain persistence adapters. " +
            "Application workflow orchestration belongs in handlers/services." +
            Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    private static IEnumerable<string> InspectRepositoryDependencies(
        Type repositoryType)
    {
        const BindingFlags flags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        foreach (var field in repositoryType.GetFields(flags))
        {
            foreach (var failure in CheckDependency(
                         repositoryType,
                         field.FieldType,
                         $"field '{field.Name}'"))
            {
                yield return failure;
            }
        }

        foreach (var constructor in repositoryType.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                foreach (var failure in CheckDependency(
                             repositoryType,
                             parameter.ParameterType,
                             $"constructor parameter '{parameter.Name}'"))
                {
                    yield return failure;
                }
            }
        }
    }

    private static IEnumerable<string> CheckDependency(
        Type repositoryType,
        Type dependencyType,
        string usage)
    {
        foreach (var candidate in FlattenType(dependencyType))
        {
            var namespaceName =
                candidate.Namespace ?? string.Empty;

            var fullName =
                candidate.FullName ?? candidate.Name;

            if (ForbiddenNamespacePrefixes.Any(prefix =>
                    namespaceName.StartsWith(
                        prefix,
                        StringComparison.Ordinal)))
            {
                yield return
                    $"{repositoryType.FullName} depends on forbidden namespace " +
                    $"'{namespaceName}' through {usage}: {fullName}.";
            }

            if (ForbiddenDependencyTypeNames.Contains(
                    candidate.Name,
                    StringComparer.Ordinal))
            {
                yield return
                    $"{repositoryType.FullName} depends on workflow service " +
                    $"'{fullName}' through {usage}.";
            }

            if (candidate.Name.EndsWith(
                    "IdentityService",
                    StringComparison.Ordinal) &&
                namespaceName.StartsWith(
                    "PropertyApi.Application",
                    StringComparison.Ordinal))
            {
                yield return
                    $"{repositoryType.FullName} depends on Application identity " +
                    $"capability '{fullName}' through {usage}.";
            }
        }
    }

    private static IEnumerable<Type> FlattenType(Type type)
    {
        yield return type;

        if (type.HasElementType &&
            type.GetElementType() is { } elementType)
        {
            foreach (var nested in FlattenType(elementType))
            {
                yield return nested;
            }
        }

        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (var genericArgument in type.GetGenericArguments())
        {
            foreach (var nested in FlattenType(genericArgument))
            {
                yield return nested;
            }
        }
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
}
