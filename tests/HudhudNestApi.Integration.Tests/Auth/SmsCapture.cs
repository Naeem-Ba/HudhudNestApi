using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace HudhudNestApi.Integration.Tests.Auth;

internal sealed class SmsCaptureSink
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _codesByPhone = new(StringComparer.Ordinal);

    public void Capture(object?[]? args)
    {
        if (args is null)
        {
            return;
        }

        var strings = args.OfType<string>().ToArray();
        var code = strings
            .SelectMany(s => Regex.Matches(s, @"(?<!\d)\d{6}(?!\d)").Select(m => m.Value))
            .FirstOrDefault();

        if (code is null)
        {
            return;
        }

        var phone = strings.FirstOrDefault(s =>
            s.Any(char.IsDigit) &&
            !string.Equals(s, code, StringComparison.Ordinal) &&
            s.Length >= 7);

        if (phone is null)
        {
            phone = "*";
        }

        lock (_gate)
        {
            _codesByPhone[phone] = code;
            _codesByPhone["*"] = code;
        }
    }

    public string GetCode(string phoneNumber)
    {
        lock (_gate)
        {
            if (_codesByPhone.TryGetValue(phoneNumber, out var code) ||
                _codesByPhone.TryGetValue("*", out code))
            {
                return code;
            }
        }

        throw new InvalidOperationException(
            "No OTP code was captured. Ensure the outbound SMS/message sender is registered through an interface recognized by SmsCaptureRegistration.");
    }

    public void Clear()
    {
        lock (_gate)
        {
            _codesByPhone.Clear();
        }
    }
}

internal class SmsCaptureProxy : DispatchProxy
{
    public SmsCaptureSink Sink { get; set; } = default!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Sink.Capture(args);

        if (targetMethod is null)
        {
            return null;
        }

        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(void))
        {
            return null;
        }

        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (returnType == typeof(ValueTask))
        {
            return ValueTask.CompletedTask;
        }

        if (returnType.IsGenericType &&
            returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var result = SuccessLikeDefault(resultType);
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, new[] { result });
        }

        if (returnType.IsGenericType &&
            returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var result = SuccessLikeDefault(resultType);
            return Activator.CreateInstance(returnType, result);
        }

        return SuccessLikeDefault(returnType);
    }

    private static object? SuccessLikeDefault(Type type)
    {
        if (type == typeof(bool))
        {
            return true;
        }

        if (!type.IsValueType)
        {
            return null;
        }

        return Activator.CreateInstance(type);
    }

    public static object Create(Type serviceType, SmsCaptureSink sink)
    {
        var createMethod = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(DispatchProxy.Create) && m.IsGenericMethodDefinition)
            .MakeGenericMethod(serviceType, typeof(SmsCaptureProxy));

        var proxy = createMethod.Invoke(null, null)
            ?? throw new InvalidOperationException($"Could not create SMS proxy for {serviceType.FullName}.");

        ((SmsCaptureProxy)proxy).Sink = sink;
        return proxy;
    }
}

internal static class SmsCaptureRegistration
{
    public static void ReplaceSmsAbstractions(
        IServiceCollection services,
        SmsCaptureSink sink)
    {
        var candidates = services
            .Where(d =>
                d.ServiceType.IsInterface &&
                IsMessageSenderAbstraction(d.ServiceType.Name))
            .ToArray();

        foreach (var descriptor in candidates)
        {
            services.Remove(descriptor);
            services.Add(ServiceDescriptor.Singleton(
                descriptor.ServiceType,
                _ => SmsCaptureProxy.Create(descriptor.ServiceType, sink)));
        }
    }

    private static bool IsMessageSenderAbstraction(string typeName)
        => typeName.Contains("Sms", StringComparison.OrdinalIgnoreCase) ||
           typeName.Contains("OtpSender", StringComparison.OrdinalIgnoreCase) ||
           typeName.Contains("MessageSender", StringComparison.OrdinalIgnoreCase) ||
           typeName.Contains("TextMessage", StringComparison.OrdinalIgnoreCase);
}
