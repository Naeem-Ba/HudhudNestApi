namespace HudhudNestApi.Domain.SocialDistribution.Enums;

/// <summary>What kind of presence a <see cref="Entities.SocialAccount"/> represents on its platform.</summary>
public enum SocialAccountType
{
    Page = 1,
    Profile = 2,
    Channel = 3,
    BusinessAccount = 4,
    Group = 5,
    Organization = 6,
    Other = 7,
}
