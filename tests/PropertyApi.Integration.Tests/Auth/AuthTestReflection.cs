using System.Reflection;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

using AuthRegisterCommand =
    PropertyApi.Application.Auth.Commands.Register.RegisterCommand;


namespace PropertyApi.Integration.Tests.Auth;

internal static class AuthTestReflection
{
    /// <summary>
    /// Creates the exact Auth registration command.
    ///
    /// Do not use name-based reflection here because the application contains
    /// both RegisterCommand and RegisterUserCommand, which represent different
    /// application flows and return different result types.
    /// </summary>
    public static object RegisterCommand(
        string email,
        string password,
        string firstName = "Atomic",
        string lastName = "Test",
        string? phoneNumber = null,
        string? ipAddress = "127.0.0.1")
    {
        // These parameters are preserved for compatibility with existing
        // test call sites. The Auth RegisterCommand currently does not use them.
        _ = phoneNumber;
        _ = ipAddress;

        return new AuthRegisterCommand(
            FirstName: firstName,
            LastName: lastName,
            Email: email,
            Password: password);
    }

    public static async Task<object?> SendAsync(
        IServiceProvider services,
        object command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(command);

        var sender = services.GetRequiredService<ISender>();

        return await sender.Send(command, ct);
    }

    public static bool IsSucceeded(
        object? result)
    {
        if (result is null)
        {
            return false;
        }

        foreach (var name in new[]
                 {
                     "Succeeded",
                     "Success",
                     "IsSuccess"
                 })
        {
            var property = result
                .GetType()
                .GetProperty(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.IgnoreCase);

            if (property?.PropertyType == typeof(bool))
            {
                return (bool)(
                    property.GetValue(result)
                    ?? false);
            }
        }

        throw new InvalidOperationException(
            $"Could not find a success flag on result type " +
            $"'{result.GetType().FullName}'.");
    }

    public static T Create<T>(
        IReadOnlyDictionary<string, object?> values)
        where T : class
        => (T)Create(typeof(T), values);

    public static object Create(
        Type type,
        IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(values);

        foreach (var constructor in type
                     .GetConstructors(
                         BindingFlags.Instance |
                         BindingFlags.Public |
                         BindingFlags.NonPublic)
                     .OrderByDescending(
                         constructor =>
                             constructor.GetParameters().Length))
        {
            var parameters = constructor.GetParameters();
            var args = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];

                if (parameter.Name is not null &&
                    TryGetValue(
                        values,
                        parameter.Name,
                        parameter.ParameterType,
                        out var converted))
                {
                    args[i] = converted;
                    continue;
                }

                if (parameter.HasDefaultValue)
                {
                    args[i] = parameter.DefaultValue;
                    continue;
                }

                args[i] = DefaultValue(
                    parameter.ParameterType);
            }

            try
            {
                var instance = constructor.Invoke(args);

                ApplyWritableProperties(
                    instance,
                    values);

                return instance;
            }
            catch (TargetInvocationException)
            {
                // Try another constructor.
            }
            catch (ArgumentException)
            {
                // Try another constructor.
            }
        }

        throw new InvalidOperationException(
            $"Could not construct '{type.FullName}'.");
    }

    private static void ApplyWritableProperties(
        object instance,
        IReadOnlyDictionary<string, object?> values)
    {
        foreach (var property in instance
                     .GetType()
                     .GetProperties(
                         BindingFlags.Instance |
                         BindingFlags.Public))
        {
            if (!property.CanWrite ||
                property.SetMethod is null)
            {
                continue;
            }

            if (TryGetValue(
                    values,
                    property.Name,
                    property.PropertyType,
                    out var converted))
            {
                property.SetValue(
                    instance,
                    converted);
            }
        }
    }

    private static bool TryGetValue(
        IReadOnlyDictionary<string, object?> values,
        string name,
        Type targetType,
        out object? converted)
    {
        if (!values.TryGetValue(
                name,
                out var value))
        {
            converted = null;
            return false;
        }

        converted = ConvertValue(
            value,
            targetType);

        return true;
    }

    private static object? ConvertValue(
        object? value,
        Type targetType)
    {
        if (value is null)
        {
            return DefaultValue(targetType);
        }

        var nullableType =
            Nullable.GetUnderlyingType(targetType);

        var effectiveType =
            nullableType ?? targetType;

        if (effectiveType.IsInstanceOfType(value))
        {
            return value;
        }

        if (effectiveType.IsEnum)
        {
            if (value is string text)
            {
                return Enum.Parse(
                    effectiveType,
                    text,
                    ignoreCase: true);
            }

            return Enum.ToObject(
                effectiveType,
                value);
        }

        if (effectiveType == typeof(Guid) &&
            value is string guidText)
        {
            return Guid.Parse(guidText);
        }

        return Convert.ChangeType(
            value,
            effectiveType);
    }

    private static object? DefaultValue(
        Type type)
    {
        if (!type.IsValueType ||
            Nullable.GetUnderlyingType(type) is not null)
        {
            return null;
        }

        return Activator.CreateInstance(type);
    }
}
