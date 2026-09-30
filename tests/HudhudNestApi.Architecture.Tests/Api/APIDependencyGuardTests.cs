using System.Reflection;
using MediatR;
using HudhudNestApi.Controllers;

namespace HudhudNestApi.Architecture.Tests.Api;

public sealed class APIDependencyGuardTests
{
    private static readonly string[] TargetControllerNames =
    [
        nameof(PropertyImagesController),
        nameof(FavoritesController),
        nameof(ContactController),
        nameof(AmenitiesController),
        nameof(UsersController)
    ];

    [Fact(DisplayName = "Critical controllers must depend on ISender only")]
    public void CriticalControllers_Should_Depend_On_ISender_Only()
    {
        var controllerTypes = GetTargetControllerTypes();

        foreach (var controllerType in controllerTypes)
        {
            var constructor = Assert.Single(controllerType.GetConstructors());
            var parameter = Assert.Single(constructor.GetParameters());

            Assert.Equal(typeof(ISender), parameter.ParameterType);
        }
    }

    [Fact(DisplayName = "Critical controllers must not reference Infrastructure, EF Core, or AppDbContext")]
    public void CriticalControllers_Should_Not_Reference_Infrastructure_Or_EFCore()
    {
        var failures = new List<string>();

        foreach (var controllerType in GetTargetControllerTypes())
        {
            InspectTypeReferences(controllerType, failures);
            InspectSourceFile(controllerType.Name, failures);
        }

        Assert.True(
            failures.Count == 0,
            string.Join(Environment.NewLine, failures));
    }

    private static IReadOnlyList<Type> GetTargetControllerTypes()
    {
        var apiAssembly = typeof(PropertyImagesController).Assembly;

        return apiAssembly
            .GetTypes()
            .Where(type => TargetControllerNames.Contains(type.Name))
            .OrderBy(type => type.Name)
            .ToList();
    }

    private static void InspectTypeReferences(Type controllerType, ICollection<string> failures)
    {
        const BindingFlags flags = BindingFlags.Instance |
                                   BindingFlags.Static |
                                   BindingFlags.Public |
                                   BindingFlags.NonPublic |
                                   BindingFlags.DeclaredOnly;

        foreach (var field in controllerType.GetFields(flags))
            CheckForbidden(controllerType, field.FieldType, $"field '{field.Name}'", failures);

        foreach (var constructor in controllerType.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
                CheckForbidden(controllerType, parameter.ParameterType, $"constructor parameter '{parameter.Name}'", failures);
        }

        foreach (var method in controllerType.GetMethods(flags))
        {
            CheckForbidden(controllerType, method.ReturnType, $"method '{method.Name}' return type", failures);

            foreach (var parameter in method.GetParameters())
                CheckForbidden(controllerType, parameter.ParameterType, $"method '{method.Name}' parameter '{parameter.Name}'", failures);
        }
    }

    private static void CheckForbidden(
        Type controllerType,
        Type referencedType,
        string usage,
        ICollection<string> failures)
    {
        foreach (var candidate in Flatten(referencedType))
        {
            var fullName = candidate.FullName ?? candidate.Name;
            var ns = candidate.Namespace ?? string.Empty;

            if (ns.StartsWith("HudhudNestApi.Infrastructure", StringComparison.Ordinal) ||
                ns.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                ns.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal) ||
                candidate.Name.StartsWith("UserManager", StringComparison.Ordinal) ||
                candidate.Name.StartsWith("RoleManager", StringComparison.Ordinal) ||
                candidate.Name.Equals("AppDbContext", StringComparison.Ordinal))
            {
                failures.Add($"{controllerType.Name} uses forbidden type '{fullName}' via {usage}.");
            }
        }
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var child in Flatten(elementType))
                yield return child;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var child in Flatten(argument))
                    yield return child;
            }
        }
    }

    private static void InspectSourceFile(string controllerName, ICollection<string> failures)
    {
        var repoRoot = FindRepositoryRoot();
        var file = Path.Combine(repoRoot, "HudhudNestApi", "Controllers", $"{controllerName}.cs");

        if (!File.Exists(file))
        {
            failures.Add($"Could not find source file for {controllerName}.");
            return;
        }

        var source = File.ReadAllText(file);
        var forbiddenTokens = new[]
        {
            "HudhudNestApi.Infrastructure",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore.Identity",
            "UserManager<",
            "RoleManager<",
            "AppDbContext",
            "DbSet<",
            ".SaveChangesAsync(",
            ".Include(",
            ".ToListAsync(",
            ".FirstOrDefaultAsync(",
            ".AnyAsync(",
            ".CountAsync("
        };

        foreach (var token in forbiddenTokens)
        {
            if (source.Contains(token, StringComparison.Ordinal))
                failures.Add($"{controllerName}.cs contains forbidden source token '{token}'.");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HudhudNestApi.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing HudhudNestApi.sln.");
    }
}
