using HudhudNestApi.Application.Auth.Interfaces;

namespace HudhudNestApi.Infrastructure.Identity.Services;

/// <summary>
/// Infrastructure composition adapter that groups the Identity capabilities
/// implemented by <see cref="PureIdentityService" />.
/// </summary>
public interface IIdentityCapabilityAdapter
    : ISocialLoginIdentityService,
        IAddEmailIdentityService,
        IForgotPasswordIdentityService,
        IRefreshTokenIdentityService,
        IPhoneOtpIdentityService,
        IRegisterIdentityService,
        IResendConfirmationIdentityService,
        ILoginIdentityService,
        IResetPasswordIdentityService,
        ILogoutIdentityService,
        IVerifyEmailIdentityService,
        IChangePasswordIdentityService,
        IUserIdentityReadService,
        IUpdateUserIdentityService,
        IDeleteUserIdentityService
{
}
