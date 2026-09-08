using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Marketing.Entities;
using PropertyApi.Domain.Marketing.Enums;

namespace PropertyApi.Application.Tests.Marketing;

public sealed class LeadTests
{
    [Fact]
    public void Create_WithValidData_NormalizesUserTypeAndTrimsFields()
    {
        var lead = Lead.Create(
            fullName: "  أحمد الشمري  ",
            phone: " 0933123456 ",
            city: " دمشق ",
            userType: "AGENCY",
            source: "landing-page");

        Assert.Equal("أحمد الشمري", lead.FullName);
        Assert.Equal("0933123456", lead.Phone);
        Assert.Equal("دمشق", lead.City);
        Assert.Equal("agency", lead.UserType);
        Assert.Equal(LeadStatus.New, lead.Status);
        Assert.Null(lead.OfferId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("crm-admin")]
    [InlineData("agency-office")]
    public void Create_WithUnknownUserType_Throws(string userType)
    {
        Assert.Throws<DomainException>(() => Lead.Create(
            "الاسم", "0933123456", "دمشق", userType, "landing-page"));
    }

    [Fact]
    public void Create_WithoutFullName_Throws()
    {
        Assert.Throws<DomainException>(() => Lead.Create(
            "", "0933123456", "دمشق", "agency", "landing-page"));
    }

    [Fact]
    public void SetStatus_FromConvertedState_Throws()
    {
        var lead = Lead.Create("الاسم", "0933123456", "دمشق", "agency", "landing-page");
        lead.SetStatus(LeadStatus.Converted);

        Assert.Throws<DomainException>(() => lead.SetStatus(LeadStatus.Contacted));
    }

    [Fact]
    public void SetStatus_FromRejectedState_Throws()
    {
        var lead = Lead.Create("الاسم", "0933123456", "دمشق", "agency", "landing-page");
        lead.SetStatus(LeadStatus.Rejected);

        Assert.Throws<DomainException>(() => lead.SetStatus(LeadStatus.New));
    }

    [Fact]
    public void SetStatus_ThroughNonTerminalStates_Succeeds()
    {
        var lead = Lead.Create("الاسم", "0933123456", "دمشق", "agency", "landing-page");

        lead.SetStatus(LeadStatus.Contacted);
        Assert.Equal(LeadStatus.Contacted, lead.Status);

        lead.SetStatus(LeadStatus.Qualified);
        Assert.Equal(LeadStatus.Qualified, lead.Status);
    }
}
