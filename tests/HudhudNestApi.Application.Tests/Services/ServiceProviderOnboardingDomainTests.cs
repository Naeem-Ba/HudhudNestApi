using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Services;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Tests.Services;

/// <summary>
/// The two state machines of provider onboarding, tested on the entities alone: the application
/// (Pending / NeedsInfo / Approved / Rejected / Withdrawn) and the provider it creates (Active /
/// Suspended / Revoked). They are separate on purpose — approving an application is not the same
/// as the provider staying active.
/// </summary>
public sealed class ServiceProviderOnboardingDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Admin = Guid.NewGuid();

    private static ServiceProviderApplication NewApplication(
        ServiceProviderApplicantType type = ServiceProviderApplicantType.Individual,
        string? company = null,
        string? email = "Sami@Example.com",
        string? phone = null,
        IEnumerable<ServiceCategory>? categories = null,
        IEnumerable<int>? governorates = null,
        bool terms = true) =>
        ServiceProviderApplication.Submit(
            Guid.NewGuid(), type, "  Cleaner Co ", "Sami", email, phone,
            categories ?? [ServiceCategory.Cleaning, ServiceCategory.Cleaning, ServiceCategory.Moving],
            governorates ?? [2, 1, 2], "5 years", company, null, terms, Now);

    [Fact]
    public void Submit_NormalisesInput_AndStartsPending()
    {
        var application = NewApplication();

        Assert.Equal(ServiceProviderApplicationStatus.Pending, application.Status);
        Assert.True(application.IsOpen);
        Assert.Equal("Cleaner Co", application.DisplayName);
        Assert.Equal("sami@example.com", application.ContactEmail);
        Assert.Contains("Cleaning", application.RequestedCategories);
        Assert.Contains("Moving", application.RequestedCategories);
        Assert.Equal(2, application.RequestedCategories.Length);
        Assert.Equal([1, 2], application.RequestedGovernorateIds);
    }

    [Fact]
    public void Submit_WithoutTerms_Fails() =>
        Assert.Throws<DomainException>(() => NewApplication(terms: false));

    [Fact]
    public void Submit_NeedsContactCategoryGovernorateAndCompanyName()
    {
        Assert.Throws<DomainException>(() => NewApplication(email: null, phone: null));
        Assert.Throws<DomainException>(() => NewApplication(categories: []));
        Assert.Throws<DomainException>(() => NewApplication(governorates: []));
        Assert.Throws<DomainException>(() => NewApplication(ServiceProviderApplicantType.Company, company: " "));
        Assert.NotNull(NewApplication(ServiceProviderApplicantType.Company, company: "Cleaner LLC"));
    }

    [Fact]
    public void RequestInfo_ThenRespond_ReturnsToPending_OnlyFromNeedsInfo()
    {
        var application = NewApplication();

        Assert.Throws<InvalidStateTransitionException>(() => application.Respond(
            ServiceProviderApplicantType.Individual, "x", "x", "x@y.z", null,
            [ServiceCategory.Cleaning], [1], "x", null, null, Now));

        application.RequestInfo("  send your licence ", Admin, Now);
        Assert.Equal(ServiceProviderApplicationStatus.NeedsInfo, application.Status);
        Assert.Equal("send your licence", application.InfoRequest);

        application.Respond(
            ServiceProviderApplicantType.Individual, "Cleaner Co", "Sami", "s@y.z", null,
            [ServiceCategory.Cleaning], [1], "updated", null, "L-1", Now.AddHours(1));
        Assert.Equal(ServiceProviderApplicationStatus.Pending, application.Status);
        Assert.Equal("L-1", application.LicenseNumber);
    }

    [Fact]
    public void RequestInfo_RequiresAMessage() =>
        Assert.Throws<DomainException>(() => NewApplication().RequestInfo("  ", Admin, Now));

    [Fact]
    public void Approve_RecordsDecision_AndClosesTheApplication()
    {
        var application = NewApplication();
        var providerId = Guid.NewGuid();

        application.Approve(providerId, Admin, Now);

        Assert.Equal(ServiceProviderApplicationStatus.Approved, application.Status);
        Assert.Equal(providerId, application.ServiceProviderId);
        Assert.Equal(Admin, application.ReviewedByUserId);
        Assert.False(application.IsOpen);
    }

    [Fact]
    public void ClosedApplication_RefusesEveryFurtherDecision()
    {
        var approved = NewApplication();
        approved.Approve(Guid.NewGuid(), Admin, Now);
        var rejected = NewApplication();
        rejected.Reject("incomplete", Admin, Now);
        var withdrawn = NewApplication();
        withdrawn.Withdraw(Now);

        foreach (var closed in new[] { approved, rejected, withdrawn })
        {
            Assert.Throws<InvalidStateTransitionException>(() => closed.Approve(Guid.NewGuid(), Admin, Now));
            Assert.Throws<InvalidStateTransitionException>(() => closed.Reject("again", Admin, Now));
            Assert.Throws<InvalidStateTransitionException>(() => closed.RequestInfo("more", Admin, Now));
            Assert.Throws<InvalidStateTransitionException>(() => closed.Withdraw(Now));
        }
    }

    [Fact]
    public void Reject_RequiresAReason_AndKeepsIt()
    {
        var application = NewApplication();
        Assert.Throws<DomainException>(() => application.Reject(" ", Admin, Now));

        application.Reject(" not enough detail ", Admin, Now);

        Assert.Equal(ServiceProviderApplicationStatus.Rejected, application.Status);
        Assert.Equal("not enough detail", application.DecisionReason);
    }

    private static ServiceProvider NewProvider()
    {
        var provider = ServiceProvider.Create(Guid.NewGuid(), null, "Cleaner Co", null, null, null, Now);
        provider.SetApprovedScope([ServiceCategory.Cleaning], [1], Now);
        provider.ActivateByApproval(Now);
        return provider;
    }

    [Fact]
    public void Provider_SuspendReactivate_RoundTrip_KeepsActiveFlagInSync()
    {
        var provider = NewProvider();
        Assert.True(provider.IsActive);

        provider.Suspend("complaints", Now);
        Assert.Equal(ServiceProviderStatus.Suspended, provider.Status);
        Assert.False(provider.IsActive);
        Assert.Equal("complaints", provider.StatusReason);

        provider.Reactivate(Now);
        Assert.Equal(ServiceProviderStatus.Active, provider.Status);
        Assert.True(provider.IsActive);
        Assert.Null(provider.StatusReason);
    }

    [Fact]
    public void Provider_Revoke_IsFinal_UntilANewApprovalReactivates()
    {
        var provider = NewProvider();
        provider.Revoke("fraud", Now);

        Assert.Equal(ServiceProviderStatus.Revoked, provider.Status);
        Assert.False(provider.IsActive);
        Assert.Throws<InvalidStateTransitionException>(() => provider.Reactivate(Now));
        Assert.Throws<InvalidStateTransitionException>(() => provider.Suspend("x", Now));
        Assert.Throws<InvalidStateTransitionException>(() => provider.Revoke("again", Now));

        provider.ActivateByApproval(Now);
        Assert.Equal(ServiceProviderStatus.Active, provider.Status);
        Assert.True(provider.IsActive);
    }

    [Fact]
    public void Provider_SuspendAndRevoke_RequireAReason()
    {
        Assert.Throws<DomainException>(() => NewProvider().Suspend(" ", Now));
        Assert.Throws<DomainException>(() => NewProvider().Revoke("", Now));
        var suspended = NewProvider();
        suspended.Suspend("x", Now);
        Assert.Throws<InvalidStateTransitionException>(() => suspended.Suspend("y", Now));
    }

    [Fact]
    public void ApprovedScope_NeedsCategoryAndGovernorate_AndDrivesCoverage()
    {
        var provider = NewProvider();
        Assert.Throws<DomainException>(() => provider.SetApprovedScope([], [1], Now));
        Assert.Throws<DomainException>(() => provider.SetApprovedScope([ServiceCategory.Cleaning], [], Now));

        Assert.True(provider.IsApprovedFor(ServiceCategory.Cleaning));
        Assert.False(provider.IsApprovedFor(ServiceCategory.Legal));
        Assert.True(provider.Covers(1));
        Assert.False(provider.Covers(2));
        Assert.True(provider.Covers(null));
    }

    [Fact]
    public void ServiceCategorySet_RoundTrips_AndIgnoresUnknownNames()
    {
        var names = ServiceCategorySet.ToNames([ServiceCategory.Moving, ServiceCategory.Verification, ServiceCategory.Moving]);

        Assert.Equal(["Verification", "Moving"], names);
        Assert.Equal([ServiceCategory.Verification, ServiceCategory.Moving], ServiceCategorySet.FromNames(names.Append("Nope")));
    }
}
