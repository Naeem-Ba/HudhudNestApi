using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Identity.Services;


namespace PropertyApi.Integration.Tests.Auth;

internal sealed class AuthFaultPlan
{
    public bool FailCreateAsync { get; init; }

    public bool FailAddLoginAsync { get; init; }

    public bool FailAddToRoleAsync { get; init; }

    public bool FailRefreshTokenAddAsync { get; init; }
}

internal sealed class FaultingPureIdentityService
    : IIdentityCapabilityAdapter
{
    private readonly IIdentityCapabilityAdapter _inner;

    private readonly AuthFaultPlan _faults;

    public FaultingPureIdentityService(
        IIdentityCapabilityAdapter inner,
        AuthFaultPlan faults)
    {
        _inner = inner;
        _faults = faults;
    }
    public Task<IdentityAccountSnapshot?>
    FindByPhoneNumberAsync(
        string phoneNumber,
        CancellationToken ct = default)
    => ((IPhoneOtpIdentityService)_inner).FindByPhoneNumberAsync(
        phoneNumber,
        ct);
    public Task<IdentityOperationResult>
    ConfirmPhoneNumberAsync(
        Guid identityId,
        DateTime confirmedAtUtc,
        CancellationToken ct = default)
    => ((IPhoneOtpIdentityService)_inner).ConfirmPhoneNumberAsync(
        identityId,
        confirmedAtUtc,
        ct);

    public Task<IdentityOperationResult>
    SoftDeleteAsync(
        Guid identityId,
        DateTime deletedAtUtc,
        CancellationToken ct = default)
    => ((IDeleteUserIdentityService)_inner).SoftDeleteAsync(
        identityId,
        deletedAtUtc,
        ct);

    public Task<IdentityOperationResult>
    SetPhoneNumberAsync(
        Guid identityId,
        string? phoneNumber,
        DateTime changedAtUtc,
        CancellationToken ct = default)
    => ((IUpdateUserIdentityService)_inner).SetPhoneNumberAsync(
        identityId,
        phoneNumber,
        changedAtUtc,
        ct);

    public Task<IdentityAccountSnapshot?>
    FindByLoginAsync(
        string loginProvider,
        string providerKey,
        CancellationToken ct = default)
    => ((ISocialLoginIdentityService)_inner).FindByLoginAsync(
        loginProvider,
        providerKey,
        ct);
    public Task<IdentityAccountSnapshot?> FindByIdAsync(
        Guid identityId,
        CancellationToken ct = default)
        => ((IUserIdentityReadService)_inner).FindByIdAsync(
            identityId,
            ct);

    public Task<IdentityAccountSnapshot?> FindByEmailAsync(
        string email,
        CancellationToken ct = default)
        => ((IAddEmailIdentityService)_inner).FindByEmailAsync(
            email,
            ct);
    public Task<IdentityOperationResult>
    AddLoginAsync(
        Guid identityId,
        string loginProvider,
        string providerKey,
        string providerDisplayName,
        CancellationToken ct = default)
    => _faults.FailAddLoginAsync
        ? Task.FromResult(
            IdentityOperationResult.Failed(
                new[]
                {
                    "Injected AddLoginAsync failure."
                }))
        : ((ISocialLoginIdentityService)_inner).AddLoginAsync(
            identityId,
            loginProvider,
            providerKey,
            providerDisplayName,
            ct);
    public Task<IdentityOperationResult> CreateAsync(
        CreateIdentityAccount request,
        CancellationToken ct = default)
        => _faults.FailCreateAsync
            ? Task.FromResult(
                IdentityOperationResult.Failed(
                    new[]
                    {
                        "Injected CreateAsync failure."
                    }))
            : ((IRegisterIdentityService)_inner).CreateAsync(
                request,
                ct);

    public Task<bool> CheckPasswordAsync(
        Guid identityId,
        string password,
        CancellationToken ct = default)
        => ((ILoginIdentityService)_inner).CheckPasswordAsync(
            identityId,
            password,
            ct);

    public Task<IReadOnlyList<string>> GetRolesAsync(
        Guid identityId,
        CancellationToken ct = default)
        => ((IUserIdentityReadService)_inner).GetRolesAsync(
            identityId,
            ct);

    public Task<IdentityOperationResult> AddToRoleAsync(
        Guid identityId,
        string role,
        CancellationToken ct = default)
        => _faults.FailAddToRoleAsync
            ? Task.FromResult(
                IdentityOperationResult.Failed(
                    new[]
                    {
                        "Injected AddToRoleAsync failure."
                    }))
            : ((IRegisterIdentityService)_inner).AddToRoleAsync(
                identityId,
                role,
                ct);

    public Task<IdentityOperationResult> UpdateSecurityStampAsync(
        Guid identityId,
        CancellationToken ct = default)
        => ((ILogoutIdentityService)_inner).UpdateSecurityStampAsync(
            identityId,
            ct);

    public Task<IdentityOperationResult> RecordSuccessfulLoginAsync(
        Guid identityId,
        DateTime loginAtUtc,
        CancellationToken ct = default)
        => ((ILoginIdentityService)_inner).RecordSuccessfulLoginAsync(
            identityId,
            loginAtUtc,
            ct);

    public Task<string> GeneratePasswordResetTokenAsync(
        Guid identityId,
        CancellationToken ct = default)
        => ((IForgotPasswordIdentityService)_inner).GeneratePasswordResetTokenAsync(
            identityId,
            ct);

    public Task<IdentityOperationResult> ResetPasswordAsync(
        Guid identityId,
        string token,
        string newPassword,
        CancellationToken ct = default)
        => ((IResetPasswordIdentityService)_inner).ResetPasswordAsync(
            identityId,
            token,
            newPassword,
            ct);

    public Task<IdentityOperationResult> ChangePasswordAsync(
        Guid identityId,
        string currentPassword,
        string newPassword,
        CancellationToken ct = default)
        => ((IChangePasswordIdentityService)_inner).ChangePasswordAsync(
            identityId,
            currentPassword,
            newPassword,
            ct);

    public Task<IdentityOperationResult> RecordCredentialChangeAsync(
        Guid identityId,
        DateTime changedAtUtc,
        CancellationToken ct = default)
        => ((IChangePasswordIdentityService)_inner).RecordCredentialChangeAsync(
            identityId,
            changedAtUtc,
            ct);

    public Task<IdentityOperationResult> SetEmailAsync(
        Guid identityId,
        string email,
        CancellationToken ct = default)
        => ((IAddEmailIdentityService)_inner).SetEmailAsync(
            identityId,
            email,
            ct);

    public Task<string> GenerateEmailConfirmationTokenAsync(
        Guid identityId,
        CancellationToken ct = default)
        => ((IAddEmailIdentityService)_inner).GenerateEmailConfirmationTokenAsync(
            identityId,
            ct);

    public Task<IdentityOperationResult> ConfirmEmailAsync(
        Guid identityId,
        string token,
        CancellationToken ct = default)
        => ((IVerifyEmailIdentityService)_inner).ConfirmEmailAsync(
            identityId,
            token,
            ct);
}

internal class RefreshTokenRepositoryFaultProxy
    : DispatchProxy
{
    public IRefreshTokenRepository Inner { get; set; }
        = default!;

    public AuthFaultPlan Faults { get; set; }
        = default!;

    protected override object? Invoke(
        MethodInfo? targetMethod,
        object?[]? args)
    {
        if (targetMethod is null)
        {
            throw new InvalidOperationException(
                "Refresh token repository proxy received a null method.");
        }

        if (Faults.FailRefreshTokenAddAsync &&
            targetMethod.Name.Equals(
                "AddAsync",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Injected refresh-token repository persistence failure.");
        }

        try
        {
            return targetMethod.Invoke(
                Inner,
                args);
        }
        catch (TargetInvocationException ex)
            when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    public static IRefreshTokenRepository Create(
        IRefreshTokenRepository inner,
        AuthFaultPlan faults)
    {
        var proxy =
            DispatchProxy.Create<
                IRefreshTokenRepository,
                RefreshTokenRepositoryFaultProxy>();

        var typed =
            (RefreshTokenRepositoryFaultProxy)
            (object)proxy;

        typed.Inner = inner;
        typed.Faults = faults;

        return proxy;
    }
}

internal class RefreshTokenStoreFaultProxy
    : DispatchProxy
{
    public IRefreshTokenStore Inner { get; set; }
        = default!;

    public AuthFaultPlan Faults { get; set; }
        = default!;

    protected override object? Invoke(
        MethodInfo? targetMethod,
        object?[]? args)
    {
        if (targetMethod is null)
        {
            throw new InvalidOperationException(
                "Refresh token store proxy received a null method.");
        }

        if (Faults.FailRefreshTokenAddAsync &&
            targetMethod.Name.Equals(
                "StoreAsync",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Injected refresh-token store persistence failure.");
        }

        try
        {
            return targetMethod.Invoke(
                Inner,
                args);
        }
        catch (TargetInvocationException ex)
            when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    public static IRefreshTokenStore Create(
        IRefreshTokenStore inner,
        AuthFaultPlan faults)
    {
        var proxy =
            DispatchProxy.Create<
                IRefreshTokenStore,
                RefreshTokenStoreFaultProxy>();

        var typed =
            (RefreshTokenStoreFaultProxy)
            (object)proxy;

        typed.Inner = inner;
        typed.Faults = faults;

        return proxy;
    }
}

internal static class AuthFaultServiceCollectionExtensions
{
    public static void DecorateAuthServices(
        this IServiceCollection services,
        AuthFaultPlan faults)
    {
        DecorateIdentityCapabilityAdapter(
            services,
            faults);

        DecorateRefreshTokenRepository(
            services,
            faults);

        DecorateRefreshTokenStore(
            services,
            faults);
    }

    private static void DecorateIdentityCapabilityAdapter(
        IServiceCollection services,
        AuthFaultPlan faults)
    {
        var descriptor =
            services.LastOrDefault(
                service =>
                    service.ServiceType ==
                    typeof(IIdentityCapabilityAdapter))
            ?? throw new InvalidOperationException(
                "IIdentityCapabilityAdapter is not registered.");

        services.Remove(
            descriptor);

        services.Add(
            ServiceDescriptor.Describe(
                typeof(IIdentityCapabilityAdapter),

                serviceProvider =>
                    new FaultingPureIdentityService(
                        ResolveOriginal<
                            IIdentityCapabilityAdapter>(
                            serviceProvider,
                            descriptor),
                        faults),

                descriptor.Lifetime));
    }

    private static void DecorateRefreshTokenRepository(
        IServiceCollection services,
        AuthFaultPlan faults)
    {
        var descriptor =
            services.LastOrDefault(
                service =>
                    service.ServiceType ==
                    typeof(IRefreshTokenRepository))
            ?? throw new InvalidOperationException(
                "IRefreshTokenRepository is not registered.");

        services.Remove(
            descriptor);

        services.Add(
            ServiceDescriptor.Describe(
                typeof(IRefreshTokenRepository),

                serviceProvider =>
                    RefreshTokenRepositoryFaultProxy.Create(
                        ResolveOriginal<
                            IRefreshTokenRepository>(
                            serviceProvider,
                            descriptor),
                        faults),

                descriptor.Lifetime));
    }

    private static void DecorateRefreshTokenStore(
        IServiceCollection services,
        AuthFaultPlan faults)
    {
        var descriptor =
            services.LastOrDefault(
                service =>
                    service.ServiceType ==
                    typeof(IRefreshTokenStore))
            ?? throw new InvalidOperationException(
                "IRefreshTokenStore is not registered.");

        services.Remove(
            descriptor);

        services.Add(
            ServiceDescriptor.Describe(
                typeof(IRefreshTokenStore),

                serviceProvider =>
                    RefreshTokenStoreFaultProxy.Create(
                        ResolveOriginal<
                            IRefreshTokenStore>(
                            serviceProvider,
                            descriptor),
                        faults),

                descriptor.Lifetime));
    }

    private static T ResolveOriginal<T>(
        IServiceProvider services,
        ServiceDescriptor descriptor)
        where T : class
    {
        if (descriptor.ImplementationInstance is T instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (T)descriptor
                .ImplementationFactory(
                    services);
        }

        if (descriptor.ImplementationType is not null)
        {
            return (T)ActivatorUtilities.CreateInstance(
                services,
                descriptor.ImplementationType);
        }

        throw new InvalidOperationException(
            $"Cannot resolve original registration for {typeof(T).FullName}.");
    }
}
