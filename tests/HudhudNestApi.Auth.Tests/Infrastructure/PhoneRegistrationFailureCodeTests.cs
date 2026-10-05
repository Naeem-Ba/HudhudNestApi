namespace HudhudNestApi.Auth.Tests.Infrastructure;

public sealed class PhoneRegistrationFailureCodeTests
{
    private static readonly IdentityErrorDescriber Describer = new();

    [Fact]
    public void Losing_A_Racing_Registration_Reports_The_Number_As_Already_Registered()
    {
        var result = IdentityResult.Failed(Describer.DuplicateUserName("+963900000001"));

        Assert.Equal("PHONE_ALREADY_REGISTERED", PhoneAuthenticationWorkflow.RegistrationFailureCode(result));
    }

    [Fact]
    public void Any_Other_Create_Failure_Keeps_The_Generic_Code()
    {
        var result = IdentityResult.Failed(Describer.DefaultError());

        Assert.Equal("USER_CREATE_FAILED", PhoneAuthenticationWorkflow.RegistrationFailureCode(result));
    }
}
